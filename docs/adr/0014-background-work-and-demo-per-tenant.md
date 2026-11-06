# 14. Background work and the demo run per tenant

- Status: Accepted
- Date: 2026-11-06

## Context

Two background processes run without a request, so there is no `X-Tenant` and the fail-closed filter (ADR 0012)
would see nothing: the due-soon notifications (ADR 0010) and the demo reset (ADR 0005). The reset is also the
dangerous one: `DemoResetter` runs `ExecuteDelete` over every table plus a raw `DELETE FROM [TaskLabels]`, so it
wipes the whole database. That cannot stay once production and demo share a database.

## Decision

- **`TenantScope.RunAsync(tenantId, work)`** sets the current tenant for the work inside it, the same way a request
  does, and clears it afterwards. It is the only way to run tenant data code outside a request, and it is used by
  the jobs below and by tests.
- **Due-soon notices.** `DueSoonNotificationService` lists the tenants (from the excluded `Tenant` table) and runs
  `NotifyDueSoonHandler` once per tenant inside a `TenantScope`. A failure in one tenant is logged and does not stop
  the others. The handler's no-duplicates rule keeps working per tenant.
- **Demo as a tenant.** The demo is a tenant with `IsDemo = true`. The reset deletes and reseeds only the rows of
  that tenant (`WHERE TenantId = <demo>`, in dependency order), with no global `DELETE` and no raw SQL without a
  tenant. If the demo tenant does not exist, it is created. The reset refuses to touch a tenant whose `IsDemo` is
  false. This amends ADR 0005: the schedule and the catch-up stay as they are; what the reset deletes changes.
- **`Demo:AllowDataWipe`** is kept, now meaning "allow the reset of the demo tenant". The reset still needs
  `Demo:Enabled` and this flag, and even with both on it can only reach the demo tenant.
- **Demo accounts are demo-only.** If an account with a demo email already is a super-admin or belongs to another
  tenant, the reset aborts with an error log and changes nothing, instead of overwriting a real account. The
  notification preferences of users who also belong to another tenant are not deleted.
- **Same database.** Production tenants and the demo live together. The few places that need raw SQL or
  `IgnoreQueryFilters` for this are on the allowed list of the architecture test (ADR 0012), each with its reason.

## Consequences

- The demo can be reset on schedule in the same database as real customers without touching their rows.
- The due-soon job makes one pass per tenant; with few tenants this is cheap.
- Any new background job must go through `TenantScope`; one that queries without it returns nothing and logs the
  fail-closed warning instead of leaking data.

## Migration of existing data

The `AddTenants` migration moves all existing rows to the default tenant (ADR 0012). The public demo was never
deployed (the first Azure deployment is still pending), so there is no production demo data to move. A database that
did already hold demo data (for example a developer's) ends up with it in the default tenant, not in the demo tenant.
To clean it by hand, delete that data (clients, projects, tasks and the demo users) in the default tenant and let the
reset create the demo tenant, or restore the database; do not run the reset against the default tenant.
