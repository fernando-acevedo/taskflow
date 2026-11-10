# 19. Serializable board moves, a move response with the reordered tasks, and live/ready health probes

- Status: Accepted
- Date: 2026-11-10

## Context

- A Kanban move renumbers the columns it left and entered. Two concurrent moves into one column read the same
  snapshot and can both write the same positions, leaving duplicates.
- After a move the frontend reloaded the whole project's tasks, because the API renumbers (and re-versions) the
  neighbors.
- `/health` checks the database. Used as a liveness probe it restarts the app during a database outage, which does
  not help.

## Decision

- `MoveTask` runs inside `TransactionRunner.RunSerializableAsync`, like `UpsertTask` and `SyncTasks`: loading the
  task, comparing the row version, renumbering and saving are one serializable transaction, retried by the
  execution strategy after a deadlock. The losing request of two racing moves may answer 409 `Concurrency.Stale`,
  which is valid.
- LexoRank (fractional ranks that avoid renumbering) is discarded for now: it needs a rank column, rebalancing when
  ranks run out, and a changed contract for the frontend and the Gantt sync. Columns are small, so renumbering in a
  transaction is cheap enough.
- `PUT /api/projects/{id}/tasks/{taskId}/position` answers 200 with `MoveTaskResultDto { task, reordered }`, where
  `reordered` lists the other tasks whose position changed, as `{ id, status, position, rowVersion }` with the row
  version saved. The client patches its store and no longer reloads. This breaks the previous contract (a bare task);
  it is accepted because the API is not versioned yet.
- Health: `GET /health/live` runs no checks (the process answers); `GET /health/ready` runs the checks tagged
  `ready` (the database). `GET /health` stays as an alias of ready so existing callers (workflows, `dev.ps1`)
  keep working. The App Service health check uses `/health/ready`.

## Consequences

- No duplicate positions under concurrency, at the cost of some 409s on contended columns and a longer transaction.
- Fewer requests after each drag and drop.
- Orchestrators can separate "restart me" (live) from "do not send traffic yet" (ready).

## Known limits (review of T-102)

- **"Save anyway" overwrites.** After a 409 `Concurrency.Stale` on a task edit, the frontend's conflict notice offers
  "Save anyway", which resends the whole form with the new row version. It therefore overwrites the fields the other
  user changed, not only the ones this user edited. This is an accepted product decision. A future alternative is to
  send only the modified fields.
- **Creating a task is outside the serializable transaction.** `TaskMoves` (renumbering) is only used by `MoveTask`,
  `UpsertTask` and `SyncTasks`, all inside `RunSerializableAsync`. `UpdateTask` does not change status or position, and
  `CreateTask` does not call `TaskMoves`: it appends to the To Do column with `MAX(position) + 1` in a plain
  `SaveChanges`. Two concurrent creations can get the same position in To Do (and a create racing a serialized move
  can too). Positions are only ordered, not unique-constrained, and the next move of that column renumbers it, so the
  effect is a transient tie in the order. This is left as a known limit.
