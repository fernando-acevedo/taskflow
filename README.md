# TaskFlow

[![Backend CI](https://github.com/fernando-acevedo/taskflow/actions/workflows/backend-ci.yml/badge.svg)](https://github.com/fernando-acevedo/taskflow/actions/workflows/backend-ci.yml)
[![Frontend CI](https://github.com/fernando-acevedo/taskflow/actions/workflows/frontend-ci.yml/badge.svg)](https://github.com/fernando-acevedo/taskflow/actions/workflows/frontend-ci.yml)

Project management for digital agencies: clients, projects, a Kanban board and a workload dashboard,
in English and Spanish. Built with .NET 10, Angular and NgRx, SQL Server, and deployed to Azure.

> **Status: phase 0 (walking skeleton).** The solution structure, health checks, localization and CI are in
> place. Features arrive in the next phases; see [Roadmap](#roadmap).

## Tech stack

| Area | Technology |
| --- | --- |
| API | ASP.NET Core 10 (controllers), EF Core 10, SQL Server, Serilog, OpenAPI + Scalar |
| Frontend | Angular 22 (standalone, zoneless, signals), NgRx, Angular Material, Transloco |
| Tests | xUnit v3, Shouldly, Testcontainers (SQL Server), Vitest |
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

Key decisions are written down as ADRs:

1. [Clean Architecture with four projects](docs/adr/0001-clean-architecture.md)
2. [Controllers, with Minimal API only for Identity endpoints](docs/adr/0002-controllers-with-identity-endpoints.md)
3. [Bearer tokens instead of cookies](docs/adr/0003-bearer-tokens-over-cookies.md)
4. [Target .NET 10 LTS](docs/adr/0004-dotnet-10.md)
5. [Reset the public demo from inside the API](docs/adr/0005-demo-reset-inside-api.md)

## Run it locally

Prerequisites: .NET SDK 10.0.400+, Node.js 24+, and SQL Server LocalDB (included with Visual Studio) or
Docker (see `docker-compose.yml`).

On Windows, the quickest way is the dev script. It starts both servers in their own windows, waits until
they answer and opens the app in your browser; press Enter in its window to stop them.

```powershell
.\dev.cmd              # or .\dev.ps1; add -ApiDocs to also open the API reference, -NoBrowser to skip the browser
```

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

Useful URLs while the API runs in Development:

- `http://localhost:5080/health`: health report, including the database
- `http://localhost:5080/scalar/v1`: interactive API reference
- `http://localhost:5080/api/system/info`: API info; send `Accept-Language: es` for Spanish

## Tests

```bash
cd backend && dotnet test        # integration tests need Docker; without it they are skipped locally
cd frontend && npm test          # Vitest
cd frontend && npm run lint      # ESLint
```

In CI the integration tests always run against SQL Server in a container.

## Roadmap

| Phase | Scope | State |
| --- | --- | --- |
| 0 | Walking skeleton: solution layout, health checks, i18n, CI | Done |
| 1 | Authentication with Identity API Endpoints, roles, guards | Next |
| 2 | Domain and API: clients, projects, members, tasks | Planned |
| 3 | Angular features with NgRx, Kanban with drag and drop | Planned |
| 4 | Dashboard | Planned |
| 5 | Azure deployment and the public demo with nightly reset | Planned |
| 6 | Polish: screenshots, accessibility, docs | Planned |

## License

[MIT](LICENSE)
