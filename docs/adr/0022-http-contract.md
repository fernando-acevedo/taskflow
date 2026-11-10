# 22. HTTP contract: security headers, rate limits, compression, versioning and conditional requests

- Status: Accepted
- Date: 2026-11-10

## Context

The review of the API found gaps in the HTTP surface: no security headers or HSTS, a rate limit only per API key and
per IP on the auth endpoints, no compression, no versioning, and concurrency control only through the `rowVersion`
in the body.

## Decision

- **Security headers and HSTS.** `X-Content-Type-Options: nosniff`, `Referrer-Policy`, a locked-down
  `Content-Security-Policy` for API responses and `Strict-Transport-Security` outside Development.
- **Rate limit per user and tenant.** A token bucket partitioned by user (and tenant) replaces the unlimited "people"
  partition; API keys keep their own budget (ADR 0016). Exports use a stricter policy.
- **Compression.** Brotli and gzip response compression for JSON.
- **Versioning.** `Asp.Versioning`, assuming v1 when none is given: the current `/api/...` routes keep working, the
  `api-version` header is accepted, and responses carry `api-supported-versions`.
- **Conditional requests.** `GET` of a resource that has a `rowversion` answers an `ETag`; writes accept `If-Match` and
  answer 412 when it does not match (the `rowVersion` in the body keeps working and answers 409). `If-None-Match` on
  `GET` of resources and lists answers 304 with a weak ETag. `Client`, `Label` and `TaskComment` gain a `rowversion`
  like projects and tasks (a migration).

## Consequences

- Existing clients keep working: nothing here removes a route, a field or a status they use.
- 412 (header) and 409 (body) are two signals for the same conflict; the frontend keeps handling 409 and may adopt
  `If-Match` later.
- Weak ETags on lists save bandwidth, not database work, since the list is still computed to build the tag.
- The new `rowversion` columns need a migration and a field in the affected DTOs.

## Implementation notes

- **Compression and BREACH.** BREACH needs a response that both compresses and reflects a secret the attacker can
  also guess-probe through a value they control in the request. API responses are JSON built from stored data and never
  echo request values next to secrets (tokens are in the `Authorization` header, never in a body that reflects input),
  so `EnableForHttps = true` is acceptable. Only JSON, CSV and text types are compressed.
- **Security headers** are set when the response starts, so they survive the exception handler clearing headers. The
  API reference (`/scalar`) gets a CSP that allows its own scripts, styles and Scalar's font and proxy hosts.
- **Rate limits** are token buckets read from `RateLimiting:User|Report|Export` (`TokenLimit`, `TokensPerPeriod`,
  `ReplenishmentSeconds`), keyed by user only (the limiter runs before the tenant is resolved, so partitioning by the raw `X-Tenant` header would let a caller dodge the limit and create limiters without end); dashboards use `Report`, the time-summary CSV `Export`.
  A 429 is a localized `ProblemDetails` (`code` `RateLimit.Exceeded`) with `Retry-After`. Anonymous requests are limited only on `/api/auth`.
- **Versioning** keeps the unversioned routes as v1 (header or query `api-version`; an unknown version is a 400
  `UnsupportedApiVersion`). `MapIdentityApi` endpoints are not versioned. OpenAPI documents are named by version, so
  they live at `/openapi/1.0.json` and `/scalar/1.0`; the old `v1` addresses redirect.
- **Sessions**: a password or email change through `/api/auth/manage/info` now drops the cached security stamp, like `/resetPassword`.
- **Optimistic concurrency** on clients, labels and comments uses the body `rowVersion` (409 `Concurrency.Stale`); deleting
  a label or comment takes no version.

## Amendment (review fixes)

- **ETag responses** (strong and weak) carry `Cache-Control: private, no-cache` and `Vary: Authorization, X-Tenant`, so a
  shared cache never serves one user's answer to another. The weak list ETag is a SHA-256 (first 16 bytes) of the
  serialized body (same JSON options as the response, enums as strings) plus the query string. Its cost: the list is
  still queried and serialized on every request, only the transfer is saved.
- **`If-Match` semantics.** A comma-separated list matches when any strong entry matches; `*` matches any existing
  item (no comparison) and answers 412 when the item does not exist; a weak entry (`W/"..."`) never matches (strong
  comparison, RFC 9110) and ends in 412.
- **428 Precondition Required (breaking change within v1).** `PUT`, `archive` and `restore` of clients, `PUT` of labels
  and `PUT` of comments now require a version: `If-Match` or `rowVersion` in the body. Without either they answer 428
  `Concurrency.PreconditionRequired` instead of a 400 validation error. Clients that already send the body `rowVersion`
  are not affected; those that sent none were already failing with 400.
- **Telemetry sampling** is `ParentBased(TraceIdRatioBased)` with `Telemetry:SamplingRatio` (1.0 in Development, default
  0.25 in `appsettings.json`).
- **Create task** accepts `startDate`, `isBlocked` and `statusNote`, like update (they were silently dropped before).
