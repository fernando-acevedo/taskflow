# Infrastructure

Bicep templates and scripts for the public demo (phase 5). See
[ADR 0011](../docs/adr/0011-azure-hosting-with-bicep.md) for the decisions.

## What gets created

`main.bicep` deploys into one resource group:

| Resource | SKU | Notes |
| --- | --- | --- |
| App Service plan (Linux) | F1 Free | East US |
| Web app (API) | `DOTNETCORE\|10.0` | HTTPS only, FTPS off, TLS 1.2, Always On off |
| SQL server + database `taskflow` | Free offer, serverless GP_S_Gen5, auto-pause | East US, SQL authentication, firewall rule for Azure services |
| Static Web App (frontend) | Free | East US 2 (Free is only offered in a few regions) |

Names are `<prefix>-<kind>-<uniqueString(resourceGroupId)>`, so they are globally unique and stable per group.
The web app gets its settings from the template: connection string `TaskFlow` (`SQLAzure`,
`Connect Timeout=60`) and these app settings (keys verified against `appsettings.json` and `DemoOptions`):

| App setting | Value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__MigrateOnStartup`, `Database__SeedOnStartup` | `true` |
| `Cors__AllowedOrigins__0` | `https://<static web app hostname>` |
| `Email__FrontendUrl` | `https://<static web app hostname>` (base of emailed links) |
| `Auth__AllowSelfRegistration` | `false` |
| `DataProtection__KeysPath` | `/home/data/keys` (persistent storage) |
| `Seed__Admin__Email`, `Seed__Admin__Password` (secure), `Seed__Admin__DisplayName` | the real admin, which the demo reset preserves |
| `Demo__Enabled` | `true` |
| `Demo__AllowDataWipe` | `true` (explicit opt-in the API requires for the nightly wipe) |
| `Demo__ResetCron`, `Demo__TimeZone` | `0 3 * * *`, `UTC` (params `demoResetCron`, `demoTimeZone`) |
| `Demo__Accounts__0__Password` (secure) | demo admin (index 0 in `Demo:Accounts`) |
| `Demo__Accounts__1__Password` (secure) | demo member (index 1) |
| `ForwardedHeaders__KnownNetworks__0..3` | `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16` |
| `Email__Smtp__Host/Port/Security/Username/Password`, `Email__FromAddress` | only when `smtpHost` is set |

Password policy for the demo accounts and the real admin: 8+ characters with an uppercase letter, a lowercase
letter and a digit (the templates enforce `minLength(8)`; the API enforces the rest at startup/seeding).
The SQL admin password must meet Azure SQL complexity (8+ characters, three of upper, lower, digit, symbol) and must
not contain `;` or `'`, because it is interpolated into the connection string. `deploy.ps1`/`deploy.sh` check all
four passwords before deploying.
The demo restrictions themselves are enforced by the API (ADR 0005), not by the infrastructure.

**Email is optional.** Leave the SMTP parameters empty and the demo works, but invitation and password reset
emails are not sent. To enable it, set `TASKFLOW_SMTP_HOST` (and `TASKFLOW_SMTP_USERNAME`,
`TASKFLOW_SMTP_PASSWORD`, `TASKFLOW_EMAIL_FROM`) before deploying.

**Forwarded headers.** The list covers the private ranges App Service front ends use; never clear it, or every
client would share one rate-limit bucket. Verify after the first deploy: call `/api/auth/login` a few times from
your machine, check the log stream (`az webapp log tail -g rg-taskflow-demo -n <webAppName>`) and confirm the
client IP the app records is your public address, not a `10.x`/`169.254.x` one. If it is not, add the front
end's range to `forwardedHeadersKnownNetworks`.

Outputs: `apiUrl`, `staticWebAppHostname`, `frontendUrl`, `webAppName`, `staticWebAppName`, `sqlServerName`.

## Cost and limits

Everything runs on free tiers, so the cost is zero, with limits:

- **App Service F1**: 60 CPU minutes per day, 1 GB RAM, no Always On (the app unloads after about 20 minutes
  idle, so the first request is a cold start), no health check, no custom domain with TLS, shared workers.
- **Azure SQL free offer**: 100,000 vCore-seconds and 32 GB per month per database. With
  `freeLimitExhaustionBehavior: AutoPause` the database pauses until next month when the allowance runs out
  instead of billing. The database also auto-pauses when idle; the first connection after that takes up to a
  minute (hence `Connect Timeout=60`). Only one free database per subscription.
- **Static Web Apps Free**: 100 GB bandwidth per month, 0.5 GB storage.

Check the current numbers in the Azure documentation before relying on them.

## One-time setup

1. Install the Azure CLI and sign in: `az login` (and `az account set --subscription <id>`).
2. Deploy:
   ```powershell
   .\infra\deploy.ps1                      # or ./infra/deploy.sh
   ```
   It asks for the SQL admin password, the demo admin and member passwords, and the real admin's email and
   password (or reads `TASKFLOW_SQL_ADMIN_PASSWORD`, `TASKFLOW_DEMO_ADMIN_PASSWORD`,
   `TASKFLOW_DEMO_MEMBER_PASSWORD`, `TASKFLOW_SEED_ADMIN_EMAIL`, `TASKFLOW_SEED_ADMIN_PASSWORD`; optional
   `TASKFLOW_SEED_ADMIN_DISPLAY_NAME`, `TASKFLOW_SMTP_*`, `TASKFLOW_EMAIL_FROM`), creates the resource group `rg-taskflow-demo` and
   prints the outputs. To validate first: `az bicep build --file infra/main.bicep`.
3. Let GitHub deploy without stored credentials (OIDC):
   ```bash
   az ad app create --display-name taskflow-github
   az ad sp create --id <appId>
   az ad app federated-credential create --id <appId> --parameters '{
     "name": "github-main",
     "issuer": "https://token.actions.githubusercontent.com",
     "subject": "repo:<owner>/<repo>:environment:production",
     "audiences": ["api://AzureADTokenExchange"] }'
   # Custom role with only what the deploy needs (definition: infra/roles/webapp-deployer.json).
   WEBAPP_ID=$(az webapp show -g rg-taskflow-demo -n <webAppName> --query id -o tsv)
   sed "s#<WEBAPP_RESOURCE_ID>#$WEBAPP_ID#" infra/roles/webapp-deployer.json > webapp-deployer.json
   az role definition create --role-definition webapp-deployer.json
   az role assignment create --assignee <appId> --role "TaskFlow Web App Deployer" --scope $WEBAPP_ID
   ```
   Then create the GitHub environment **production** (Settings > Environments); both deploy workflows declare
   `environment: production`. Restrict it to the `main` branch and, optionally, require reviewers. The federated
   credential trusts only that environment, so no other workflow, branch or PR can get a token.

   **Least privilege.** The role grants `Microsoft.Web/sites/read`, `.../config/read`, `.../publish/Action`,
   `.../restart/Action` and `.../basicPublishingCredentialsPolicies/read` on the web app. It deliberately omits
   `Microsoft.Web/sites/config/list/Action`, which returns the app settings (connection string, admin and demo
   passwords). This is a best-effort set: it was not verified against a live subscription which operations
   `azure/webapps-deploy@v3` calls with an Entra token. If the **Deploy API** workflow fails with
   `AuthorizationFailed`, the error names the missing action: add only that one to the role
   (`az role definition update`). Fallback if that does not converge: the built-in **Website Contributor** role.
   Be aware that it, and any role that can reach the Kudu (SCM) site, can effectively read the app settings, so
   keep the `production` environment protected either way.
4. Get the Static Web Apps deployment token:
   `az staticwebapp secrets list --name <staticWebAppName> --resource-group rg-taskflow-demo --query properties.apiKey -o tsv`
5. In the GitHub repository settings add:
   - Secrets: `AZURE_CLIENT_ID` (the app's `appId`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
     `AZURE_STATIC_WEB_APPS_API_TOKEN`.
   - Variables: `AZURE_WEBAPP_NAME` (`webAppName` output) and `API_URL` (`apiUrl` output).
6. Make sure the GitHub environment `production` exists (see step 3).
7. Run the **Deploy API** workflow, then **Deploy frontend** (they also run on pushes to `main`). The
   **Wake demo** workflow calls `/health` daily at 03:07 UTC so the nightly reset happens early.

The frontend build writes `API_URL` into `frontend/src/environments/environment.ts` (the production
environment) and publishes `frontend/dist/frontend/browser`.

## Recovering the real admin

`forgotPassword` and `resetPassword` are disabled while the demo runs (the demo restrictions block them), so the
real admin cannot recover by email. Redeploying a new `Seed__Admin__Password` does **not** help: `IdentitySeeder`
only creates the admin when the email does not exist yet, it never resets the password of an existing one. To
recover, either:

- set `Demo__Enabled=false` in the web app settings, use `/forgotPassword` (needs SMTP configured), then set it
  back to `true`; or
- with a temporary SQL firewall rule for your IP, copy the `PasswordHash` of an account whose password you know
  (for example the demo admin) onto the real admin row and set a new `SecurityStamp`
  (`UPDATE AspNetUsers SET PasswordHash = (SELECT PasswordHash FROM AspNetUsers WHERE Email = '<demo admin>'), SecurityStamp = NEWID() WHERE Email = '<admin>'`),
  sign in, change the password, and remove the firewall rule.

## Teardown

```bash
az group delete --name rg-taskflow-demo --yes --no-wait
```

This deletes every resource, including the database. Remove the Entra app registration separately
(`az ad app delete --id <appId>`).
