# TaskFlow frontend

Angular 22 app. See the [root README](../README.md) for how to run, test and lint it.

## Content-Security-Policy

`public/staticwebapp.config.json` holds the CSP and Permissions-Policy headers with two placeholders. `npm run build` runs `scripts/apply-csp.mjs` afterwards, which writes into `dist` the `sha256` hashes of the built `index.html` inline scripts (the theme anti-flash script and the one that applies the critical-CSS stylesheet) and the origin of `apiUrl` (from `src/environments/environment.ts`, which the deploy workflow fills from `vars.API_URL`) into `connect-src`, then verifies the result (`npm run check:csp` repeats the check). Change an inline script or add one and the build output follows; a build with no inline script fails.

## Telemetry (optional)

Off by default; nothing extra is downloaded or sent.

- Every call to `${apiUrl}/api/` carries a W3C `traceparent` header (`core/http/trace.interceptor.ts`, after `authInterceptor`, so the retry after a token refresh keeps the trace id with a new span id). The API's OpenTelemetry spans join that trace, and its ProblemDetails return `traceId`. For unexpected errors the snackbar adds "Support code: <first 8 characters of the trace id>" for the user to report.
- `TelemetryErrorHandler` replaces Angular's `ErrorHandler`: it always logs to the console. To also report exceptions to Application Insights, set `appInsightsConnectionString` in `src/environments/environment.ts` (empty by default). The SDK (`@microsoft/applicationinsights-web`) is a dynamic import, a separate lazy chunk loaded on the first error, so the initial bundle does not grow. Only exceptions are sent (page views, route, ajax/fetch and exception auto-collection are off), with the user id and tenant id but never the email, and tokens and emails are scrubbed from messages. The SDK's own correlation headers are disabled so it does not compete with our `traceparent`.
- Deploy: `deploy-frontend.yml` writes `appInsightsConnectionString` from the optional repository variable `APPINSIGHTS_CONNECTION_STRING` (not a secret; leave it unset to keep reporting off). `scripts/apply-csp.mjs` then adds the connection string's `IngestionEndpoint` origin to `connect-src` (only when set); `npm run check:csp` (also run by `build`) fails if that origin is missing from `connect-src`.
- Reports are stripped of URL fragments and queries (`stripUrlSecrets`, a telemetry initializer): emailed links carry their tokens there.

## API types

The API's types are generated from the backend's versioned contract (`docs/api/openapi-1.0.json`) into
`src/app/core/api/schema.d.ts` (committed, LF). `core/api/types.ts` exports readable aliases (`TaskDto`, `ProjectDto`, ...)
that the `*-api.service.ts` files use instead of hand-written DTOs; narrower front-end types derive from them
(`Pick`/`Omit`). ESLint flags a new `interface ...Dto` in an `*-api.service.ts`.

### Why `typescript` is pinned and `overrides` exists

`openapi-typescript` (7.13.0, also pinned) declares a peer dependency on TypeScript 5, while the app uses TypeScript 6.
`overrides` in `package.json` makes it resolve to the app's own `typescript` (`"$typescript"`) so npm installs a single
compiler and `npm ci` has no peer conflict. The generator uses the compiler's printer, so its output can change with the
TypeScript version: `typescript` therefore has an exact version (no `^`/`~`), and a bump is a deliberate change.
After bumping it, run `npm run api:types` and review the diff of `schema.d.ts` (it should be empty or cosmetic) before
committing; `npm run api:check` fails in CI if it drifts.

When the backend contract changes:

1. `UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiSnapshot` (from `backend/`) refreshes the contract.
2. `npm run api:types` regenerates `schema.d.ts`; fix whatever the compiler reports.

`npm run api:check` (run in CI before lint) fails when `schema.d.ts` differs from the contract.
