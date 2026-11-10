# 18. Emails go through a transactional outbox

- Status: Accepted
- Date: 2026-11-10

## Context

Emails (notifications, invitations and the ones Identity sends) are sent inside the request, after `SaveChanges`
(`Notifier.SendEmailsAsync`, `InvitationSender`, `NotificationEmailSender`, `IdentityEmailSender`). If the SMTP server
fails or is slow, the email is lost (the data is already saved, nothing retries) and the request waits for SMTP.

## Decision

Transactional Outbox.

- A table `OutboxMessages` (id, type, JSON payload, created at, attempts, next attempt at, last error, processed at,
  lease owner and lease expiry). It is not tenant filtered: the worker has no tenant, and the payload carries what it
  needs.
- The code that today sends an email adds an outbox row in the same `SaveChanges` as the change that caused it, so
  either both are saved or neither.
- A `BackgroundService` polls for due rows, sends them through `IEmailSender` and marks them processed. A failure
  schedules a retry with exponential backoff (with jitter) and a maximum number of attempts; after that the row is
  left as failed, with its last error, for inspection.
- Claiming is safe with several instances: the worker claims a batch with `UPDLOCK, READPAST` (or a lease owner and
  expiry column that it sets atomically), so two instances never send the same row. A crashed worker's lease expires
  and the row is claimed again.
- Delivery is at least once: a crash between sending and marking can repeat an email. This is accepted for these
  messages.
- Implementation is a separate task (T-95); this ADR records the decision.

## Consequences

- A SMTP outage no longer loses emails or slows requests; emails arrive a few seconds later instead of inline.
- One new table and a migration; tests read the outbox or run the worker once instead of reading `TestEmailSender`
  directly after the request.
- Old rows need a cleanup job (delete processed rows after some days).

## Details of the implementation (review of T-102)

- **At-least-once.** A crash between sending and marking, or a send that outlasts the lease, can deliver an email
  twice. `Outbox:SendTimeout` (30 s) cancels a send and counts it as a failed attempt (with backoff); startup
  validation (`ValidateOnStart`) requires it to be shorter than `Outbox:LeaseDuration` (2 min), so a worker normally
  gives up before another can claim the row. A worker whose lease expired cannot overwrite the next claim's result
  (the update matches the lock token).
- **Secrets at rest.** The payload holds links (password reset, invitation). It is replaced by `{}` as soon as the
  message is processed, and also when it is left as failed (`LastError` is enough to diagnose; a failed message is not
  re-sent from the stored payload). The column stays required, so no migration is needed.
- **Retention.** Processed rows are deleted after `Outbox:RetentionDays` (7), failed rows after
  `Outbox:FailedRetentionDays` (14) counted from `FailedAt`.
- **Not atomic with user creation.** Notifications are queued in the same `SaveChanges` as their change.
  Invitations and Identity emails (`InvitationSender`, `IdentityEmailSender`) are queued in their own `SaveChanges`
  after the user is created, so a failure to queue leaves the user without the email; the caller reports "not sent"
  and the admin can resend.
- **PII.** `LastError` can contain the recipient's address (the SMTP error often echoes it). It holds the exception
  type and message only, never the email's content, and is truncated to 600 characters.
