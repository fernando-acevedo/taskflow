# 11. Host the demo on Azure free tiers, defined in Bicep

- Status: Accepted
- Date: 2026-11-06

## Context

Phase 5 puts TaskFlow online as a public demo at no cost. It needs an API host, a static host for the Angular
app and a SQL Server database, plus a repeatable way to create them and to deploy new versions.

## Decision

- **Bicep instead of ad-hoc `az` scripts.** `infra/main.bicep` declares the resources and
  `infra/deploy.ps1` / `deploy.sh` only create the resource group and run the deployment. Deployments are
  idempotent, the template documents the settings the API needs, and `az group delete` removes everything.
  Secrets are secure parameters, read from environment variables by `main.bicepparam` and never stored.
- **Free tiers.** App Service F1 on Linux (.NET 10) for the API, Static Web Apps Free for the frontend, and the
  Azure SQL free offer (serverless, auto-pause, `AutoPause` when the monthly allowance is spent so it can't
  bill). Names use `uniqueString(resourceGroup().id)`. Static Web Apps lives in East US 2 because the Free plan
  is not offered in every region.
- **Migrations and seeding on startup.** The App Service sets `Database__MigrateOnStartup` and
  `Database__SeedOnStartup` to true. F1 runs a single instance, so there is no race between instances, and no
  pipeline needs database access or a firewall rule. Trade-off: the first request after a deploy is slow and a
  failing migration shows up as an unhealthy app (the deploy workflow waits for `/health` and fails). This
  would need a separate migration step before scaling out.
- **OIDC instead of publish profiles.** GitHub Actions signs in with `azure/login` using a federated
  credential bound to the GitHub environment `production` (which can restrict branches and require reviewers), and the identity has only a custom deploy role on the web app (`infra/roles/webapp-deployer.json`; no permission to list app settings). There is
  no long-lived secret to leak or rotate. The Static Web Apps deployment token is the one stored secret, as
  that service offers no OIDC option.
- **Frontend `apiUrl` injected at build.** The production environment file has an empty `apiUrl`; the deploy
  workflow writes the `API_URL` repository variable into it before `npm run build` and fails if it is empty.
  One build artifact per deploy, no runtime config fetch, and the API origin stays out of the source. The same
  origin is the CORS allow-list entry on the API side (the Static Web App hostname).
- **Demo restrictions live in the API.** The infrastructure only turns demo mode on (`Demo__Enabled`) and
  supplies the account passwords; what demo users may not do is enforced by the API (ADR 0005), and
  self-registration is off (`Auth__AllowSelfRegistration=false`). The real admin (`Seed__Admin__*`) is kept by
  the nightly reset. Email is optional: without SMTP settings the demo runs but sends no invitations or resets.
- The deploy workflows add `staticwebapp.config.json` with a navigation fallback so Angular routes survive a
  reload, and `demo-wake.yml` calls `/health` after 03:00 UTC to trigger the reset of ADR 0005.

## Consequences

- Everything is free, with limits: cold starts, 60 CPU minutes per day and a database that pauses.
- A deployment restarts the API, which runs pending migrations once.
- Creating the Entra app registration and the GitHub secrets stays a manual one-time step
  (see `infra/README.md`).
- Alternatives considered: Terraform (another tool for one cloud), publish profiles (a secret that expires and
  grants full access), running migrations from CI (needs database network access).
