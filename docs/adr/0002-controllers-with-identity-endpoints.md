# 2. Controllers for the API, Minimal API only for Identity endpoints

- Status: Accepted
- Date: 2026-10-16

## Context

ASP.NET Core supports both MVC controllers and Minimal APIs. The target clients (enterprise teams hiring
freelancers) mostly maintain controller-based APIs. The built-in Identity API Endpoints
(`MapIdentityApi<TUser>()`) only exist as Minimal API endpoints.

## Decision

- All application endpoints are thin controllers decorated with `[ApiController]`: one controller per
  resource, each action validates input, calls a handler and maps the `Result` to an HTTP response.
- Routes are nested by ownership, for example `/api/projects/{projectId}/tasks`.
- Identity endpoints stay as Minimal API under `/api/auth`, mapped with `MapIdentityApi`.

## Consequences

- The codebase is familiar to most .NET reviewers.
- Two endpoint styles coexist. This is intentional and limited to `/api/auth`; it is not an inconsistency
  to "fix".
