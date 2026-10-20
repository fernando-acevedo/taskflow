# 6. Use cases query the DbContext and check project access themselves

- Status: Accepted
- Date: 2026-10-19

## Context

Phase 2 adds clients, projects with members, and tasks. Two questions come up in every use case: how a
handler reads and writes these entities, and how it decides whether the caller may touch a given project.
Permissions depend on the caller's role inside that project (Owner or Member), not only on the global
Admin role, so a plain `[Authorize(Roles = ...)]` on the controller is not enough.

## Decision

- **Data access.** Application defines `IAppDbContext` with one `DbSet` per aggregate; the EF Core
  `AppDbContext` in Infrastructure implements it. There are no repositories: EF Core already is a unit of
  work over repositories, and wrapping it would duplicate that abstraction. Application references the
  `Microsoft.EntityFrameworkCore` package (not the SQL Server provider) for `DbSet` and async LINQ.
- **Domain rules return results.** `Result` and `Error` moved from Application to Domain, so entity methods
  (`Project.AddMember`, `TaskItem.Move`, ...) return an `Error` when a rule fails instead of throwing.
- **Access per project.** Handlers call `ProjectAccess.GetAsync(projectId, level)` first. It loads the
  project with its members and returns `Projects.NotFound` (404) or `Projects.Forbidden` (403) unless the
  caller qualifies: `View` for members and admins, `Work` for members, `Manage` for owners. This replaces
  the ASP.NET Core `AuthorizationHandler` the analysis proposed: that handler would have to load the project
  in the Api layer, and the use case would load it again.
- **Concurrency.** Projects and tasks carry a SQL Server `rowversion`. Edits send the version they read; the
  handler compares it and answers `Concurrency.Stale` (409). A write that lands between the check and the
  save raises `DbUpdateConcurrencyException`, which `ConcurrencyExceptionHandler` turns into the same 409.

## Consequences

- Handlers are straightforward to read: the query they run is in front of you.
- Handler unit tests would need a real or in-memory database, so use cases are covered by integration tests
  against SQL Server; the domain rules and `ProjectAccess.Allows` are unit tested.
- A forgotten `ProjectAccess` call is a security hole. Every project-scoped handler starts with it, and the
  integration tests exercise outsiders and admins against those endpoints.
