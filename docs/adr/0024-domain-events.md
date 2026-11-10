# 24. Domain events dispatched around SaveChanges

- Status: Accepted
- Date: 2026-11-10

## Context

Activity entries, notifications, outbox messages and real-time updates were each written by the handler that made the
change. Every new side effect meant touching every handler, and real-time updates must not fire before the commit.

## Decision

- Entities that raise events implement `IHasDomainEvents` and accumulate them as the domain methods run.
- A SaveChanges interceptor collects the events of the tracked entities.
- **Before-commit handlers** run in the same transaction, so their writes commit or roll back with the change:
  activity, notifications and outbox messages.
- **After-commit handlers** run once the save has succeeded: real-time publishing (ADR 0023). They are best effort; a
  failure is logged and never fails the request.
- No mediator library. Handlers are registered by convention, like the use case handlers (ADR 0001): the application
  scans for the event handler interfaces.

## Consequences

- A new side effect is a new handler, not a change to each use case.
- Before-commit handlers must stay quick and database-only; slow or external work goes after the commit or into the
  outbox (ADR 0018).
- Events are lost if the process dies between the commit and the after-commit handlers; that is acceptable for
  real-time updates, and why durable effects go through the outbox.

## Implementation notes

- **Failed saves.** The interceptor keeps the events of every SaveChanges of a transaction. When one SaveChanges fails, only the events it took are set aside (not those of earlier saves of the same transaction, which a handled failure still commits); they come back if the same context saves again (the entities no longer hold them, but their changes are still tracked), and `TransactionRunner` discards everything when it starts an attempt, so a retried transaction neither loses nor duplicates events. The SQL Server retrying strategy retries inside SaveChanges without raising the events again.
- **Batch after-commit handlers.** `IAfterCommitBatchHandler` receives all the events of a save at once; real-time publishing uses it to read all the changed tasks with one query per project (a sync of 200 tasks is one read, not 200) and to publish a task touched several times once. A failure to read is logged at Warning.
- **Events left behind.** If the scope ends with events still pending (a transaction opened outside `ITransactionRunner` and never published), the interceptor logs a Warning with their number. Use the runner for any explicit transaction. A failure while publishing after a commit is caught and logged by the runner: the change is already committed.
- **Not published.** The demo seeder takes the events of what it creates, so seeded history produces no activity, notifications or real-time messages.
