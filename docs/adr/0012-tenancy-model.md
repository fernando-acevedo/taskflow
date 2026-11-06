# 12. One shared database with a TenantId, filtered automatically and failing closed

- Status: Accepted
- Date: 2026-11-06

## Context

Phase 7 turns TaskFlow into a multi-tenant app: several agencies share one deployment and must never see each
other's clients, projects or tasks. Today `AppDbContext` has no tenant, handlers query `IAppDbContext` directly
(ADR 0006, no repositories), and the Identity role "Admin" is global. Three ways to separate tenants:

| | Shared database + `TenantId` | Schema per tenant | Database per tenant |
| --- | --- | --- | --- |
| Azure SQL free tier (ADR 0011) | Fits in one database | One database, migrations per schema | Does not fit (database and vCore limits) |
| Migrations | One | One per schema, one EF model per schema | One per database |
| Isolation | Logical (filters and tests) | Medium | Strong |
| Code change | Medium and mechanical | High | High |
| Demo reset | `WHERE TenantId` | Drop the schema | Drop the database |

## Decision

One shared database. Every business row carries a `TenantId`.

- **Model.** `Tenant` (name, slug, `IsDemo`) and `TenantMembership` (tenant, user, role Owner/Admin/Member). A user
  can belong to several tenants from day one; the Identity user stays global. The roles and how a request picks its
  tenant are in ADR 0013.
- **Which tables.** Entities that implement the marker `ITenantOwned`: clients, projects, project members, labels,
  tasks, comments, mentions, time entries, activity and notifications. `TenantId` is an EF Core shadow property, so
  the Domain barely changes.
- **Which tables stay out.** The Identity tables (users are global), `NotificationPreferences` (per user, not per
  tenant), `DemoResetLog` (global) and `Tenant` / `TenantMembership` themselves. These are listed explicitly in one
  place; an entity is either `ITenantOwned` or on that list.
- **The filter is automatic, not written by hand.** No handler writes a `Where` for the tenant. Because there are no
  repositories, the guarantee lives in `AppDbContext`: in `OnModelCreating`, every `ITenantOwned` entity gets a global
  query filter `TenantId == current tenant`. It **fails closed**: with no tenant resolved the filter matches
  nothing, so a missing tenant yields empty results instead of every tenant's data. A warning is logged when a query
  runs without a tenant, so "nothing shows up" is not a silent bug.
- **Writes.** A `SaveChanges` interceptor sets `TenantId` on added `ITenantOwned` entities and rejects a save that
  modifies or deletes a row of another tenant, or adds one naming another tenant.
- **Tests that keep it so** (in `ArchitectureTests`):
  - fail if an entity is neither `ITenantOwned` (with its filter applied) nor on the exclusion list, so a new
    entity cannot ship without a decision;
  - fail if `IgnoreQueryFilters`, `FromSql*` or `ExecuteSql*` appear in the source outside a short allowed list
    (each entry with a reason, for example the tenant-resolution code itself).

  Isolation tests with two tenants exercise every controller across them (T-65).
- **Unique indexes.** `Client.Name` was unique across the database; it becomes unique per `(TenantId, Name)`. The
  label and project indexes are already per project or per client and do not change.
- **Existing data.** The migration creates a default tenant with a fixed id, fills `TenantId` on current rows, turns
  today's global Admin into Owner of it and everyone else into Member.
- **Caches** (session, membership) are keyed by user and tenant, never by user alone.

## Consequences

- One database and one migration path fit the free tier, and the change in the handlers is small: they keep
  querying `IAppDbContext` and the filter does the rest.
- Isolation is logical. It depends on the filter, the interceptor and the architecture tests, so a bug there leaks
  data. That is why the tests and the review focus on forgotten filters, raw SQL and caches without a tenant.
- Code that must see across tenants (the super-admin listing tenants, the migration) cannot use the filtered
  entities; it works on the excluded tables (`Tenant`, `TenantMembership`) or runs inside a `TenantScope` (ADR 0014).
- **Row-Level Security of SQL Server** could be added later as a second defense, with `SESSION_CONTEXT` set per
  connection to the current tenant. It is not part of this phase.
