# TaskFlow

[![Backend CI](https://github.com/fernando-acevedo/taskflow/actions/workflows/backend-ci.yml/badge.svg)](https://github.com/fernando-acevedo/taskflow/actions/workflows/backend-ci.yml)
[![Frontend CI](https://github.com/fernando-acevedo/taskflow/actions/workflows/frontend-ci.yml/badge.svg)](https://github.com/fernando-acevedo/taskflow/actions/workflows/frontend-ci.yml)

Project management for digital agencies: clients, projects, a Kanban board and a workload dashboard,
in English and Spanish. Built with .NET 10, Angular and NgRx, SQL Server, and deployed to Azure.

> **Status: phase 4 (dashboard).** Authentication, clients, projects with members, tasks with the Kanban
> board, and the dashboards work end to end. Azure deployment and the public demo come next; see [Roadmap](#roadmap).

## Tech stack

| Area | Technology |
| --- | --- |
| API | ASP.NET Core 10 (controllers), EF Core 10, SQL Server, FluentValidation, Serilog, OpenAPI + Scalar |
| Frontend | Angular 22 (standalone, zoneless, signals), NgRx, Angular Material, Transloco |
| Auth | ASP.NET Core Identity API Endpoints, bearer + refresh tokens, roles, rate limiting |
| Tests | xUnit v3, Shouldly, NSubstitute, Testcontainers (SQL Server), Vitest |
| CI | GitHub Actions |
| Hosting (phase 5) | Azure App Service, Azure Static Web Apps, Azure SQL Database |

## Architecture

```
backend/
  src/
    TaskFlow.Domain          entities and business rules, no dependencies
    TaskFlow.Application     use cases, DTOs, validators, abstractions
    TaskFlow.Infrastructure  EF Core, Identity, migrations, background services
    TaskFlow.Api             controllers, auth, ProblemDetails, localization
  tests/                     unit, architecture and integration tests
frontend/
  src/app/
    core/                    app-wide services (i18n, auth, interceptors)
    features/<feature>/      data-access (API + NgRx store), ui, pages
docs/adr/                    architecture decision records
```

### Authentication

- ASP.NET Core Identity API Endpoints under `/api/auth` (`register`, `login`, `refresh`, ...) issue bearer
  tokens: a 1-hour access token and a 14-day refresh token.
- `GET/PUT /api/users/me` returns and updates the signed-in user's profile and roles.
- Admin-only endpoints under `/api/admin/users` invite (create), list, deactivate, reactivate, promote and
  demote users. An invited user gets an email with a link to choose their password
  (`POST /api/invitations/accept`); admins can resend it while it's pending (ADR 0008).
  Deactivating locks the account and invalidates its refresh tokens; admins can't change their own status
  or role.
- `/api/auth/*` is rate limited to 10 requests per minute per IP.
- The Angular app keeps the access token in memory and the refresh token in `localStorage`, refreshes once
  on a 401 (shared by concurrent requests), and restores the session on reload before the first navigation.

### Clients, projects and tasks

- `/api/clients`: everyone can list clients; admins create, edit, archive and restore them. A client with
  active projects can't be archived.
- `/api/projects`: any user creates a project for an active client and becomes its owner. Users see the
  projects they belong to; admins can read all of them but only work in the ones they are members of.
- `/api/projects/{id}/members`: owners add users by email, change roles and remove members; a project always
  keeps at least one owner, and members can leave.
- `/api/projects/{id}/tasks`: members create, edit, move (`PUT .../position`, the Kanban drag and drop) and delete
  tasks, and filter them by status, priority, assignee, overdue and title. Archived projects are read-only.
- Edits of projects and tasks send the `rowVersion` they read; if someone changed the item in between, the
  API answers 409 with the code `Concurrency.Stale`.

In the Angular app, admins manage clients and users; everyone else sees the projects they belong to. A project
opens on its Kanban board, where dragging a card updates the board at once and puts it back with a message if the
API rejects the move. The list view filters tasks by status, priority, assignee, overdue and title, and keeps the
filters in the URL. Members, roles and archiving are managed from the project's tabs.

The home page is a dashboard: your open, overdue and due-this-week tasks, the tasks assigned to you across
projects, and per client the active projects, open tasks and overdue tasks. Each project has an Overview tab with
tasks by status, workload by assignee and open tasks by priority. The figures come from grouped queries
(`GET /api/dashboard`, `GET /api/projects/{id}/dashboard`), so no task list is loaded to compute them.

Key decisions are written down as ADRs:

1. [Clean Architecture with four projects](docs/adr/0001-clean-architecture.md)
2. [Controllers, with Minimal API only for Identity endpoints](docs/adr/0002-controllers-with-identity-endpoints.md)
3. [Bearer tokens instead of cookies](docs/adr/0003-bearer-tokens-over-cookies.md)
4. [Target .NET 10 LTS](docs/adr/0004-dotnet-10.md)
5. [Reset the public demo from inside the API](docs/adr/0005-demo-reset-inside-api.md)
6. [Use cases query the DbContext and check project access themselves](docs/adr/0006-dbcontext-and-access-checks-in-use-cases.md)
7. [Draw the dashboard charts with HTML and CSS instead of a chart library](docs/adr/0007-hand-built-charts.md)

## Run it locally

Prerequisites: .NET SDK 10.0.400+, Node.js 24+, and a SQL Server: Docker Desktop (recommended) or
SQL Server LocalDB (included with Visual Studio).

On Windows, the quickest way is the dev script:

```powershell
.\dev.cmd                       # or .\dev.ps1
.\dev.cmd -Database LocalDb     # skip Docker
.\dev.cmd -ApiDocs -StopDatabase
```

It does the whole setup:

1. **Database.** With Docker available it checks whether the `sqlserver` service from `docker-compose.yml` is
   running and healthy, and starts it if not (starting Docker Desktop first when needed). The first run
   creates a git-ignored `.env` with a random `SA_PASSWORD`. Without Docker it falls back to LocalDB.
2. **Servers.** Opens a window for the API (pointed at the chosen database) and one for the frontend.
3. **Browser.** Waits until both answer and opens the app. Press Enter in the script's window to stop the
   servers; the database container keeps running unless you pass `-StopDatabase`.

Or start each side by hand:

```bash
# API on http://localhost:5080 (creates and migrates the LocalDB database on first run)
cd backend
dotnet run --project src/TaskFlow.Api

# Frontend on http://localhost:4200
cd frontend
npm ci
npm start
```

In Development the API seeds an admin account in the local database: **admin@taskflow.local** / **Admin12345**
(see `appsettings.Development.json`; never used outside local development). Anyone can register a
standard account from the sign-up page, and admins can invite people from the Users page.

Invitations are sent by SMTP, configured under `Email` in `appsettings.json`. Until an SMTP host is set, the
API writes each email, with its link, to its console in Development. To send real email, keep the password
out of the repo with user-secrets:

```bash
cd backend
dotnet user-secrets --project src/TaskFlow.Api set "Email:Smtp:Host" "smtp.example.com"
dotnet user-secrets --project src/TaskFlow.Api set "Email:Smtp:Port" "587"
dotnet user-secrets --project src/TaskFlow.Api set "Email:Smtp:Username" "you@example.com"
dotnet user-secrets --project src/TaskFlow.Api set "Email:Smtp:Password" "<app password>"
dotnet user-secrets --project src/TaskFlow.Api set "Email:FromAddress" "you@example.com"
```

Useful URLs while the API runs in Development:

- `http://localhost:5080/health`: health report, including the database
- `http://localhost:5080/scalar/v1`: interactive API reference
- `http://localhost:5080/api/system/info`: API info; send `Accept-Language: es` for Spanish

## Tests

```bash
cd backend && dotnet test        # integration tests use Docker, or LocalDB on Windows when Docker is missing
cd frontend && npm test          # Vitest
cd frontend && npm run lint      # ESLint
```

Integration tests run against a real SQL Server: a Testcontainers instance when Docker is available,
otherwise a throwaway LocalDB database. In CI they always use the container.

## Roadmap

| Phase | Scope | State |
| --- | --- | --- |
| 0 | Walking skeleton: solution layout, health checks, i18n, CI | Done |
| 1 | Authentication with Identity API Endpoints, roles, guards | Done |
| 2 | Domain and API: clients, projects, members, tasks | Done |
| 3 | Angular features with NgRx, Kanban with drag and drop | Done |
| 4 | Dashboard | Done |
| 5 | Azure deployment and the public demo with nightly reset | Next |
| 6 | Polish: screenshots, accessibility, docs | Planned |

## License

[MIT](LICENSE)
