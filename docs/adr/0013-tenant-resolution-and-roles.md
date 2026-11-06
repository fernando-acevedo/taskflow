# 13. The tenant comes from an X-Tenant header, checked against the membership

- Status: Accepted
- Date: 2026-11-06

## Context

With tenants in the database (ADR 0012), each request needs one current tenant, and the global Identity role
"Admin" no longer fits: it is used by `ClientsController`, `AdminUsersController`, `ProjectAccess`, `ListProjects`,
`GetMyDashboard`, user creation, the seed and the demo reset. The tenant could travel in the bearer token or in a
header. `MapIdentityApi` does not let us add claims easily, and a claim would force a new token to switch tenants.

## Decision

- **Resolution.** The frontend sends `X-Tenant: <tenant id>`. A middleware after authentication validates that the
  user has a `TenantMembership` there and sets the tenant for the request (read by the EF filter of ADR 0012). If the
  header is missing, the user's default tenant is used. A tenant the user does not belong to gets 403, and one that
  does not exist gets 404.
- **Cache.** The membership check is cached 30 s per `(userId, tenantId)`, like `SessionValidator`, with a `Forget`
  called when a membership is added, removed or changes role, so the change takes effect at once.
- **`ICurrentUser`** gains `TenantId`, `TenantRole` (Owner, Admin, Member or none) and `IsSuperAdmin`.
- **Roles.**
  - Inside a tenant: Owner and Admin do what the global Admin does today (manage clients, users and projects of
    that tenant); Member does not. A `TenantAdmin` authorization policy replaces `Roles = "Admin"` on the
    controllers. `ProjectAccess` keeps its project levels (ADR 0006); its "admin sees every project" rule now means
    Owner or Admin of the current tenant.
  - The Identity role "Admin" becomes **super-admin** of the platform.
- **Super-admin.** Tenants are created only by the super-admin for now (no self-registration; it can come later).
  A super-admin can enter any tenant for support, but only by choosing it explicitly in `X-Tenant`; it is never
  implicit. Inside a tenant they do not belong to, they act with Owner rights (support access) and the entry is
  recorded in the `TenantSupportAccesses` table (at most one row per user and tenant every 30 minutes), not in the
  activity log; the tenant's Owners and Admins can read it. A super-admin who **is** a member of a tenant uses their
  real role there, with or without the header; the implicit Owner applies only to tenants where they are not a
  member and that they chose with `X-Tenant`.
- **Accounts across tenants.** An account is global, a membership is not. Adding a project member by email only finds
  users who belong to the current tenant (anyone else answers like an unknown email), and inviting an email returns
  the same answer whether or not the account existed, so a tenant cannot probe for accounts of others. Removing a
  membership is atomic with the "last Owner" check (Serializable transaction) and also takes the user out of the
  tenant's projects and unassigns their tasks; if they were the only Owner of a project, the Owner who removes them
  (or, failing that, the tenant's oldest other Owner) becomes that project's Owner. Notifications only go to members
  of the current tenant.
- **Invitations (ADR 0008).** Inviting an email that already has an account adds a membership to the current tenant
  and sends a notice email, with no password link. A new email creates the account as before, plus the membership.
  Deactivating a user deactivates the account in every tenant; removing someone from one tenant is a separate
  operation on the membership.
- **Endpoints.** Listing the user's tenants, creating a tenant (super-admin) and managing the members of the
  current tenant are built in T-62; the frontend selector in T-64.

## Consequences

- Switching tenant is a header change, with no new token. Every cache that holds permissions or sessions uses user
  plus tenant as the key.
- A request without a resolved tenant sees no tenant data (fail closed) but can still call the endpoints that list
  its tenants and the Identity ones.
- Existing data keeps working: the former Admin becomes Owner of the default tenant (ADR 0012).
