# Architecture decision records

| # | Title | Status | Decision |
| --- | --- | --- | --- |
| [1](0001-clean-architecture.md) | Clean Architecture with four projects | Accepted | Domain, Application, Infrastructure and Api projects with inward dependencies, enforced by architecture tests. |
| [2](0002-controllers-with-identity-endpoints.md) | Controllers for the API, Minimal API only for Identity endpoints | Accepted | Use MVC controllers everywhere except the built-in Identity API Endpoints, which only exist as Minimal APIs. |
| [3](0003-bearer-tokens-over-cookies.md) | Bearer tokens instead of cookies | Accepted | Issue an access token plus a refresh token, since the frontend and API are on different sites. |
| [4](0004-dotnet-10.md) | Target .NET 10 LTS | Accepted | Build on .NET 10 (LTS, supported until November 2028) rather than .NET 8. |
| [5](0005-demo-reset-inside-api.md) | Reset the public demo from inside the API | Accepted | A background service resets the demo on a cron schedule and catches up after sleeping; no external scheduler. |
| [6](0006-dbcontext-and-access-checks-in-use-cases.md) | Use cases query the DbContext and check project access themselves | Accepted | Handlers use `IAppDbContext` directly (no repositories) and start with `ProjectAccess.GetAsync`. |
| [7](0007-hand-built-charts.md) | Draw the dashboard charts with HTML and CSS instead of a chart library | Accepted | Build the few dashboard figures as small components instead of adding Chart.js. |
| [8](0008-invitations-by-email.md) | Admins invite users by email instead of setting their password | Accepted | Invited accounts have no password until the user follows an emailed link to choose one. |
| [9](0009-activity-log.md) | The activity log is recorded explicitly in the handlers, not by an EF interceptor | Accepted | Handlers write activity entries themselves, in the same transaction as the change. |
| [10](0010-notifications-delivery.md) | Notifications reach the browser by polling, not SignalR | Accepted | Store notifications in the database and poll the unread count every 30 seconds. |
| [11](0011-azure-hosting-with-bicep.md) | Host the demo on Azure free tiers, defined in Bicep | Accepted | App Service F1, Static Web Apps Free and serverless Azure SQL, created from Bicep and deployed by GitHub Actions. |
| [12](0012-tenancy-model.md) | One shared database with a TenantId, filtered automatically and failing closed | Accepted | Tenant and TenantMembership; `ITenantOwned` entities get a global EF filter and a SaveChanges interceptor, backed by architecture tests. |
| [13](0013-tenant-resolution-and-roles.md) | The tenant comes from an X-Tenant header, checked against the membership | Accepted | Membership cached per user and tenant; roles Owner/Admin/Member per tenant; the global Admin becomes super-admin, who creates tenants. |
| [14](0014-background-work-and-demo-per-tenant.md) | Background work and the demo run per tenant | Accepted | `TenantScope` for jobs without a request; the demo reset only touches the demo tenant; `Demo:AllowDataWipe` allows that reset. |

Add a new ADR when making a decision of comparable weight, and add it to this table.
