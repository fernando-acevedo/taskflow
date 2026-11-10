# Load smoke (k6)

A short k6 script (`smoke.js`) that exercises the main API paths under light load. It is **not** part of CI: run it by
hand before a release or after a change to the board, the rate limits or the database access.

## Install k6

- Windows: `winget install k6 --source winget` (or `choco install k6`).
- Linux (Debian/Ubuntu): follow https://grafana.com/docs/k6/latest/set-up/install-k6/ (apt repository), or
  `snap install k6`.
- Any OS with Docker: `docker run --rm -i -e TF_API=... grafana/k6 run - < tests/load/smoke.js`
  (use `http://host.docker.internal:5090` as `TF_API` on Windows/macOS).

## Start the API against a separate database

Never point the test at the development database: it creates clients, projects and thousands of tasks. Use a throwaway
database and another port so a running dev API is not touched. From `backend/` (PowerShell):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__TaskFlow = "Server=(localdb)\MSSQLLocalDB;Database=TaskFlowLoad;Trusted_Connection=True;TrustServerCertificate=True"
# or, with the Docker container from docker-compose.yml, the same connection string as dev but Database=TaskFlowLoad
$env:Database__MigrateOnStartup = "true"
$env:Database__SeedOnStartup = "true"
$env:Seed__Admin__Email = "admin@taskflow.load"
$env:Seed__Admin__Password = "LoadTest12345"
# Rate limits (see below)
$env:RateLimiting__User__TokenLimit = "100000"
$env:RateLimiting__User__TokensPerPeriod = "100000"
$env:RateLimiting__Auth__PermitLimit = "1000"
dotnet run --project src/TaskFlow.Api --urls http://localhost:5090
```

(bash: `export ConnectionStrings__TaskFlow=...` and so on.) Drop the database afterwards
(`sqllocaldb`/`DROP DATABASE TaskFlowLoad`).

## Rate limits

The API limits each user with a token bucket (default burst 300, 50 tokens every 10 s, ~5 requests/s, ADR 0022) and
`/api/auth` per IP (10 a minute). The script logs in once and all VUs share that one user, so with defaults you will see
429s, which count as errors. Two ways out:

1. Raise the limits for the test API, as above: `RateLimiting__User__TokenLimit`, `RateLimiting__User__TokensPerPeriod`,
   `RateLimiting__User__ReplenishmentSeconds`, `RateLimiting__Auth__PermitLimit` (also `Report`, `Export`).
   This is the simplest option and what the thresholds assume.
2. Measure the limit itself: keep the defaults, lower `TF_VUS`, and expect 429s above ~5 requests/s per user. For more
   throughput with the real limits, split the load across several users (run several k6 processes, each with its own
   `TF_EMAIL`).

## Run

```bash
k6 run -e TF_EMAIL=admin@taskflow.load -e TF_PASSWORD=LoadTest12345 -e TF_API=http://localhost:5090 tests/load/smoke.js
k6 run -e TF_PROFILE=board ... tests/load/smoke.js
```

| Variable | Default | Meaning |
|---|---|---|
| `TF_API` | `http://localhost:5080` | API base URL |
| `TF_EMAIL`, `TF_PASSWORD` | none (required) | User to log in with; must be a tenant admin to create the client and project |
| `TF_TENANT` | none | Tenant id, sent as `X-Tenant` |
| `TF_PROFILE` | `smooth` | `smooth` or `board` |
| `TF_VUS`, `TF_DURATION` | 10 / 1m (smooth), 8 / 30s (board) | Load shape |
| `TF_BOARD_TASKS` | 12 | Tasks in the contended column (board) |
| `TF_PROJECT_ID` | none | Reuse a project instead of creating a client and a project |

Profiles:

- `smooth`: each iteration lists projects and tasks, creates a task, moves it to In Progress and Review (`If-Match` with
  the ETag; the answer is `{ task, reordered }`), comments, and reads the project activity (following `nextCursor`) and
  the task activity.
- `board`: VUs drop random tasks at random positions of the To Do column at the same time. At the end `teardown` checks
  that the column positions are exactly `0..n-1` without duplicates (metric `tf_column_positions_valid`, must be 1).

All requests send `X-Client-Id`.

## Reading the result

- Thresholds (a failed one makes k6 exit with code 99): p95 under 500 ms for reads (`kind:read`), under 800 ms for writes
  (`kind:write`), and `http_req_failed` under 1 %.
- 409 and 412 answers to moves are expected under concurrency: they are not errors, and are counted apart in
  `tf_conflicts`. Many of them in `smooth` (where each VU moves its own task) would be suspicious; in `board` they are
  normal.
- `http_req_failed` includes 429 (rate limit), 5xx and unexpected 4xx. Check the `checks` section for which request
  failed, and the API log for the cause.
- `INVALID column positions` in the output (or `tf_column_positions_valid` below 100 %) means the renumbering produced
  gaps or duplicates under concurrency: a real bug.
- The numbers depend on the machine and the database (LocalDB is slower than SQL Server in Docker); compare runs on the same
  setup, not against absolute values.

Assumptions: the script was written against the controllers and could not be run where k6 is not installed (only its
syntax was checked as an ES module); run it once on a new machine before trusting the thresholds.
