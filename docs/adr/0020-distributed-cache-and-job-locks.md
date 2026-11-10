# 20. Distributed cache with tag invalidation, and database locks for background jobs

- Status: Accepted
- Date: 2026-11-10

## Context

- `SessionValidator` (security stamp), `TenantAccess` (membership) and the API key handler (is the owner active)
  cached in `IMemoryCache` for 30 s. A change was forgotten at once only by the instance that made it; a second
  instance kept answering with the old value for up to 30 s (a deactivated user still got in).
- Background jobs (due-soon notifications, the outbox clean-up, the demo reset) ran on every instance. Some are
  idempotent, but running them in parallel is wasted work and a race waiting to happen.
- The deployment is App Service on the free F1 plan: nothing may cost money by default.

## Decision

- The three caches use `HybridCache` (`Microsoft.Extensions.Caching.Hybrid`). Entries carry tags: `user:{id}` (stamp,
  owner active), `membership:{id}` (tenant access, default tenant), `tenant:{id}` and `apikey:{id}`. Whatever changes
  a stamp, a role, a membership or an owner's active flag calls `RemoveByTagAsync` (`SessionValidator.ForgetAsync`,
  `TenantAccess.ForgetAsync`). The old synchronous `Forget` methods are gone.
- Redis is optional: when `ConnectionStrings:Redis` is set, `AddStackExchangeRedisCache` provides the L2 that shares
  entries and tag invalidation between instances. Without it only the per-instance L1 exists.
- Things that are fine per instance stay in `IMemoryCache`: the 5 s memory of unknown API secrets and the
  "support access recorded" window (the worst case is one extra audit row per instance and window).
- Jobs take an application lock with `sp_getapplock` (session owner, timeout 0) through `JobLock`, on a dedicated
  connection: a pass runs only if it gets the lock, otherwise this instance skips it. A crashed instance releases the
  lock by losing its connection. No new infrastructure. The demo reset already locked inside its transaction and
  reads its persisted `DemoResetLog` under the lock; the due-soon job is idempotent, so keeping its last run in
  memory is enough (a restart repeats a pass that creates nothing); the outbox clean-up takes the lock (claiming
  messages was already safe between instances, ADR 0018).

## Operating Redis and the job lock

- **Redis security.** Use TLS (`rediss://` / `ssl=true` in the connection string) and AUTH (a password or Entra token);
  never expose it publicly. The cache holds no secrets in the clear: the session entry stores a SHA-256 of the
  security stamp, not the stamp.
- **Job lock and a dropped connection.** `sp_getapplock` is owned by the session, so if the connection drops the lock is
  released at once and another instance may start the pass while the first one is still finishing. That is acceptable
  because every job is idempotent (due-soon, outbox clean-up, demo reset), at worst one pass is repeated.
- **Cache tags.** Only tags that something invalidates exist (`user`, `membership`, `apikey`); a tag without a caller was removed.

## Consequences

- With one instance and no Redis the behavior is the same as before. **To scale to more than one instance, configure
  Redis**; without it the other instances keep the 30 s window of the old design.
- Tag removal is best effort across instances: L1 entries elsewhere are dropped when they next consult the shared
  invalidation state, and expire after 30 s in any case.
- A job lock costs one extra connection per pass for the time the pass runs.
- Locks held by the database need no leader election or lease table, but tie job coordination to SQL Server
  availability, which the jobs need anyway.
