# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

TaskFlow is a project-management app for digital agencies (clients, projects, Kanban, workload dashboard) in
English and Spanish. Backend is ASP.NET Core 10 + EF Core 10 + SQL Server; frontend is Angular 22 (standalone,
zoneless, signals) + NgRx + Angular Material + Transloco. The project is built in phases (see the Roadmap in
`README.md`); phases 1 (authentication), 2 (domain and API for clients, projects, members and tasks), 3 (the
Angular screens, with the Kanban board) and 4 (dashboards) are done; phase 5 (Azure deployment and the public
demo) comes next.

## Commands

Local dev on Windows: `.\dev.cmd` (or `.\dev.ps1`) starts SQL Server (Docker container from `docker-compose.yml`,
falling back to LocalDB), the API and the frontend, then opens the browser. Flags: `-Database Docker|LocalDb`,
`-ApiDocs`, `-NoBrowser`, `-StopDatabase`.

Backend (run from `backend/`):

```bash
dotnet build                                   # warnings are errors (TreatWarningsAsErrors + code style in build)
dotnet run --project src/TaskFlow.Api          # http://localhost:5080, migrates + seeds the DB in Development
dotnet test                                    # all tests; integration tests need Docker or LocalDB
dotnet test tests/TaskFlow.Api.IntegrationTests --filter "FullyQualifiedName~AuthFlowTests"   # single class/test
dotnet tool restore                            # once, for dotnet-ef
dotnet ef migrations add <Name> --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.Api --output-dir Persistence/Migrations
```

Frontend (run from `frontend/`):

```bash
npm ci
npm start                                      # http://localhost:4200
npm test                                       # Vitest via @angular/build:unit-test (CI adds -- --watch=false)
npm test -- --watch=false --include src/app/core/auth/auth.interceptor.spec.ts   # single spec
npm run lint                                   # ESLint
npm run build
```

Dev URLs while the API runs: `/health`, `/scalar/v1` (API reference), `/api/system/info`. Dev seed admin:
`admin@taskflow.local` / `Admin12345` (from `appsettings.Development.json`).

## Backend architecture

Clean Architecture with four projects in `backend/src` (ADR 0001). Dependencies point inward:
`Api → Infrastructure → Application → Domain`. `ArchitectureTests` in the unit-test projects enforce this,
so a reference added in the wrong direction fails the tests.

- **Domain**: `Client`, `Project` (aggregate root owning `ProjectMember`) and `TaskItem`, with private setters.
  Rules live in entity methods that return `Result`/`Error` (`Domain/Common`) instead of throwing; each
  entity file also holds its `XxxErrors`. `TaskItem` methods take the `Project` so its rules (archived is
  read-only, assignee must be a member) apply.
- **Application**: use cases are plain classes, one file per use case (e.g. `Users/SetUserActive.cs` holds
  `SetUserActiveCommand` + `SetUserActiveHandler`). Any class whose name ends in `Handler` is registered
  automatically by `AddApplication()`, and so is every FluentValidation validator. No mediator library. Handlers
  return `Result`/`Result<T>` (from the Domain) instead of throwing for expected failures. Ports such as
  `IAppDbContext`, `IIdentityService` and `ICurrentUser` live here and are implemented elsewhere. Handlers query
  `IAppDbContext` directly (no repositories), and every project-scoped handler starts with
  `ProjectAccess.GetAsync(projectId, ProjectAccessLevel.View|Work|Manage)`, which returns 404/403 errors (ADR
  0006). Use `TimeProvider` for the current time. Projects and tasks carry a `rowversion`: edits compare it and
  return `CommonErrors.Stale`.
- **Infrastructure**: `AppDbContext` (EF Core, SQL Server, connection string `TaskFlow`), ASP.NET Identity
  (`ApplicationUser`, `IdentityService`, `IdentitySeeder`). `InitializeDatabaseAsync` runs migrations and seeding
  on startup when `Database:MigrateOnStartup` / `Database:SeedOnStartup` are true. Migrations under
  `Persistence/Migrations` are generated code, and `.editorconfig` turns analyzers off there.
- **Api**: controllers derive from `ApiControllerBase`, which maps results to HTTP via `OkOrProblem` /
  `CreatedOrProblem` / `NoContentOrProblem`. Route ids are passed to commands from small request records declared
  next to the controller. Enums are serialized as strings. Validation errors become `ValidationProblemDetails`;
  other `ErrorType`s map to 404/409/403/400. The `ProblemDetails` title is localized by looking up the error
  **code** in `Resources/SharedResource{.es}.resx`, and the code is sent as `extensions.code` so the frontend can
  translate it. When you add an error code, add its translations to both `.resx` files.
- Identity uses ASP.NET Core Identity API Endpoints (`MapIdentityApi`) under `/api/auth` with bearer + refresh
  tokens, not cookies (ADRs 0002, 0003). That group is rate limited per IP (behind a proxy, from
  `X-Forwarded-For` of the networks in `ForwardedHeaders:KnownNetworks`); `Auth:AllowSelfRegistration` turns
  `/register` off. Everything else is controllers. `SessionValidationMiddleware` rejects a bearer token whose
  security stamp is no longer the user's (`SessionValidator`, cached 30 s), so deactivating a user or changing
  their role takes effect at once: call `SessionValidator.Forget` after renewing a stamp.
- Admins invite users: the account has no password until the user follows the emailed link (ADR 0008). Email
  goes through `Infrastructure/Email` (`IEmailSender`: MailKit SMTP, or the log in Development when no SMTP host
  is set); integration tests swap in `TestEmailSender` and read the link from it.
- Request localization supports `en` (default) and `es` from `Accept-Language`.
- Central package management: add NuGet versions to `backend/Directory.Packages.props`, not to the csproj files.

### Tests

xUnit v3 + Shouldly + NSubstitute. `TaskFlow.Api.IntegrationTests` hosts the API with `WebApplicationFactory`:
`SqlServerApiFactory` uses a Testcontainers SQL Server, falls back to a throwaway LocalDB database on Windows, and
skips when neither is available. When `CI` is set, a missing Docker fails the run instead. All database test
classes share one instance through `[Collection(SqlServerTestGroup.Name)]`, so new ones should join that
collection rather than start another container. The factory seeds its own admin (`admin@tests.local`) and lifts
the auth rate limit. `ProjectApi` sets up clients, projects and tasks. `NoDatabaseApiFactory` covers endpoints
that don't need a database. `AuthApi` contains helpers for registering and logging in. Use-case unit tests live in
`TaskFlow.Application.UnitTests`, mirroring the Application folders (`Users/SetUserActiveHandlerTests.cs`).

## Frontend architecture

- The frontend calls the API directly at `environment.apiUrl` (`src/environments/`), with no dev-server proxy,
  so the API's CORS settings in `appsettings*.json` must allow the frontend origin. `authInterceptor` attaches
  the bearer token only to URLs under `${apiUrl}/api/`.
- `src/app/core/` holds app-wide concerns. `auth/` contains the NgRx auth feature, `authInterceptor` and
  `token-refresher` (refreshes once on a 401, shared by concurrent requests), token storage (access token in
  memory, refresh token in `localStorage`), guards, and `restoreSession`, an app initializer that restores the
  session before the first navigation. `i18n/` contains the Transloco setup and an interceptor that sends
  `Accept-Language`. `http/api-error.ts` turns a failed call into an `ApiError` whose `code` is the API's error
  code; `notifications/notifier.ts` shows it as a snackbar translated from `apiErrors.<code>`.
- `src/app/features/<feature>/` is split into `data-access/` (API service + NgRx `store/` with
  `*.actions.ts`, `*.effects.ts` (functional effects), `*.feature.ts` (`createFeature`)), `ui/` and `pages/`.
  Each feature has a `<feature>.routes.ts` loaded with `loadChildren`, which provides its state and effects on
  the route (`provideState` / `provideEffects`); auth is the exception, registered at the root in `app.config.ts`.
  Lists use `@ngrx/entity`. Feature reducers reset on `ShellActions.logoutClicked` / `AuthApiActions.sessionExpired`.
  Write effects use `concatMap` (none is dropped). Form dialogs stay open until the API answers: the page passes
  a `SaveFn` that tags the submitted action with a `submissionId` (`shared/data-access/submission.ts`), effects
  copy it onto their success/failure action, and `Submissions.dispatchAndWait` returns that outcome;
  `applyApiFieldErrors` shows validation messages under the fields. `src/app/shared/ui/` holds reusable
  presentational pieces (confirm dialog, empty state, `PagedList`, `SearchField`, `NewPasswordFields`); list-page
  layout classes (`.page-header`, `.filters`, `.rows`, `.chip`) are global in `styles.scss`.
- TypeScript runs in `strict` mode with `strictTemplates`, and ESLint uses the type-checked typescript-eslint
  configs. Redux DevTools is registered only in development, with passwords and tokens masked.
- Projects and tasks share one route (`features/projects/projects.routes.ts`): `ProjectPage` loads the project,
  members and tasks, and its tabs (board, list, members, settings) read them from the store. The board moves a
  card optimistically (`tasks/data-access/store/board.ts` mirrors the API's renumbering) and keeps `moveUndo`
  to revert; each card's "Move to" menu dispatches the same `taskDropped` for keyboard users. Moving pauses
  while a move is in flight, and tasks reload after each move because the API
  renumbers (and re-versions) the neighbors. The list view's filters live in the URL query string and are read
  with `@ngrx/router-store`.
- `features/dashboard/` serves the home page (`dashboard.routes.ts`, `myDashboard` state) and the project's
  Overview tab (registered on the projects route, `projectDashboard` state). Charts are hand-built components in
  `dashboard/ui/` (ADR 0007); their colors are the `--tf-chart-*` variables in `styles.scss`, and every chart
  shows its numbers as text.
- Translations live in `public/i18n/en.json` and `es.json`, and every user-visible string needs both. API
  error codes are translated under `apiErrors` (`Projects.LastOwner` → `apiErrors.Projects.LastOwner`), so a new
  backend error code needs entries in both `.resx` files and both JSON files.

## Conventions

- Architecture decisions are recorded as ADRs in `docs/adr/`. Add one when making a comparable decision.
- `.editorconfig`: LF line endings (CRLF for `.cmd`/`.bat`), 2-space indent, 4 for C#, file-scoped namespaces.
- CI (`.github/workflows/`) builds the backend in Release and runs the tests; for the frontend it runs
  lint, tests and build.
- `infra/` is intentionally empty until phase 5 (Azure deployment).
