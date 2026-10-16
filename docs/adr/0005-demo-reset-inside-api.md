# 5. Reset the public demo from inside the API

- Status: Accepted
- Date: 2026-10-16

## Context

The public demo runs on Azure App Service F1, which has no Always On: the app is unloaded after about
20 minutes without traffic, so a timer inside the app does not fire while it sleeps. Demo data must still be
reset on a schedule that is set in configuration.

## Decision

- Configuration under `Demo`: `Enabled`, `ResetCron` (default `0 3 * * *`), `TimeZone`,
  `CheckIntervalMinutes` and the demo account emails. Demo passwords are app settings marked as secrets.
- A `BackgroundService` runs at startup and every `CheckIntervalMinutes`. It computes the most recent cron
  occurrence with Cronos; if the last successful reset in `DemoResetLog` is older, it resets. A reset missed
  while the app slept therefore runs on the next start.
- `sp_getapplock` prevents two resets from running at the same time.
- A reset deletes business data and non-demo users in a transaction, recreates the demo accounts, seeds
  clients, projects and tasks with due dates relative to the reset day, and rotates security stamps to end
  open sessions.
- `GET /api/demo/status` exposes `enabled` and `nextResetAt` so the login page can show them.
- Optional: a scheduled GitHub Actions workflow calls `/health` just after the reset time to wake the app.

## Consequences

- No extra Azure service to deploy, pay for or explain.
- The reset can run later than scheduled when nobody visits; the README states this.
- Alternatives considered: an Azure Functions timer trigger or a WebJob (exact timing, but another moving
  part).
