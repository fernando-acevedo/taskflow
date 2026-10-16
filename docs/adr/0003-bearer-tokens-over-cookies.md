# 3. Bearer tokens instead of cookies

- Status: Accepted
- Date: 2026-10-16

## Context

Identity API Endpoints can issue either an authentication cookie or bearer tokens (an access token plus a
refresh token). In production the Angular app is served from Azure Static Web Apps
(`*.azurestaticapps.net`) and the API from Azure App Service (`*.azurewebsites.net`). These are different
sites, so a cookie would be a cross-site cookie: it needs `SameSite=None`, and browsers increasingly block
third-party cookies. Static Web Apps "linked backends" would put both on one origin, but they require the
paid Standard plan.

## Decision

- Use bearer tokens from `/api/auth/login` and renew them with `/api/auth/refresh`.
- The access token lives only in memory; the refresh token is stored in `localStorage`.
- An HTTP interceptor attaches the token, refreshes once on a 401 (shared by concurrent requests) and
  logs out if the refresh fails.
- Mitigate XSS with Angular's built-in sanitization and a strict Content Security Policy in
  `staticwebapp.config.json`.

## Consequences

- Works on the free tiers with two different domains.
- A refresh token in `localStorage` is readable by script if an XSS bug ever ships. The CSP and the
  absence of `innerHTML`-style bindings are the mitigation; HttpOnly cookies on a shared domain would be the
  upgrade path.
