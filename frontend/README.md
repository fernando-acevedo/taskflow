# TaskFlow frontend

Angular 22 app. See the [root README](../README.md) for how to run, test and lint it.

## Content-Security-Policy

`public/staticwebapp.config.json` holds the CSP and Permissions-Policy headers with two placeholders. `npm run build` runs `scripts/apply-csp.mjs` afterwards, which writes into `dist` the `sha256` hashes of the built `index.html` inline scripts (the theme anti-flash script and the one that applies the critical-CSS stylesheet) and the origin of `apiUrl` (from `src/environments/environment.ts`, which the deploy workflow fills from `vars.API_URL`) into `connect-src`, then verifies the result (`npm run check:csp` repeats the check). Change an inline script or add one and the build output follows; a build with no inline script fails.
