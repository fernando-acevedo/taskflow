# 1. Clean Architecture with four projects

- Status: Accepted
- Date: 2026-10-16

## Context

TaskFlow is a portfolio project meant to show how a production .NET application is structured. Reviewers
look for clear boundaries and testable business rules, but a ten-project solution would read as
over-engineering for an app of this size.

## Decision

The backend is split into four projects, with dependencies pointing inward:

| Project | Holds | Depends on |
| --- | --- | --- |
| `TaskFlow.Domain` | Entities, enums, value objects, domain rules | nothing |
| `TaskFlow.Application` | Use cases (handlers), DTOs, validators, abstractions | Domain |
| `TaskFlow.Infrastructure` | EF Core, Identity, migrations, background services | Application |
| `TaskFlow.Api` | Controllers, auth, error handling, localization, hosting | Infrastructure |

Additional choices:

- Application talks to the database through an `IAppDbContext` abstraction instead of generic repositories;
  EF Core already provides the unit of work and repository patterns.
- No MediatR and no AutoMapper (both moved to commercial licenses in 2025). Handlers are plain classes
  registered in DI; mapping is manual or generated with Mapperly.
- Expected failures travel as a `Result`; exceptions are reserved for the unexpected.
- Architecture tests in the unit test projects fail the build if a layer references an outer one.

## Consequences

- Business rules can be unit tested without a database.
- Adding a feature touches up to four projects, which is more ceremony than a single-project API.
