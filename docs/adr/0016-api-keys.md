# 16. API keys let agents call the API with a limited, revocable credential

- Status: Accepted
- Date: 2026-11-06

## Context

External agents (the Gantt panel's, scripts) need to create and update tasks. Giving them a user's login is not an
option: the access token expires hourly, the refresh token is as powerful as the password, and nothing tells the
agent's actions from the person's. We want a credential that is scoped, revocable, expiring and traceable.

## Decision

An `ApiKey` belongs to one user and one tenant. Any tenant member can create keys for themselves (D12).

- The secret is `tf_` plus 32 random bytes in base64url. It is shown once, in the response that creates the key.
  Only its SHA-256 hash is stored (the secret is random and long, so a slow hash adds nothing), together with a
  short prefix so people can recognize the key in a list.
- Every key expires, by default 90 days after creation and at most 365. A key can be revoked; it is never deleted, so
  the activity log keeps pointing at it.
- Authentication: a second scheme, `ApiKey`, selected by a policy scheme when the bearer token starts with `tf_`;
  every other token goes to the Identity bearer scheme as before. The principal carries the owner's id, the key id,
  its tenant and its scope.
- The tenant is the key's: `TenantResolutionMiddleware` ignores `X-Tenant` for keys and checks that the owner still
  belongs to the tenant (same cached membership check, so the automatic tenant filter of ADR 0012 applies to
  everything the key does). The key lookup itself runs before a tenant is known, so it is the one place that uses
  `IgnoreQueryFilters`, listed in `TenantArchitectureTests`.
- Keys skip `SessionValidationMiddleware` (they have no security stamp) but the owner must be active; that check is
  cached for 30 seconds like the stamp's.
- Scopes: `Read` (View access) and `Write` (View and Work). `Manage` is always denied to a key. The scope is enforced
  in `ProjectAccess` through `ICurrentUser.ApiKeyScope`; on top of that a key can only reach `/api/projects/**`, and a
  `Read` key only safe methods, so endpoints that do not go through `ProjectAccess` (clients, users, tenants) stay
  closed. A key can neither manage keys nor be used in the public demo, where key management is disabled.
- Each key has its own rate limit (`RateLimiting:ApiKey:PermitLimit`, per minute, default 120). `UseRateLimiter` runs
  after authentication so the limiter can partition by key.
- `LastUsedAt` is written at most every 5 minutes per key. Activity entries made through a key carry its `ApiKeyId`.
- OpenAPI/Scalar are published in every environment (D13). They describe the API and contain no data; the document
  declares the bearer scheme.

Endpoints: `POST /api/api-keys`, `GET /api/api-keys`, `DELETE /api/api-keys/{id}` (revoke).

## Consequences

- A leaked key is limited to one user's projects in one tenant, for at most a year, and can be revoked at once (the
  next request fails: the lookup is not cached).
- Every request with a key costs one indexed lookup plus a cached owner check.
- Actions of a key are attributed to its owner, with the key id in the activity log.
