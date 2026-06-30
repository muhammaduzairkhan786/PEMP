# PEMP — Azure + Entra setup guide

How to stand up the **real** Azure (UK) + Entra backing for PEMP. The app code and
`infra/main.bicep` are production-ready; the steps below are the parts that need your
**Azure subscription and Entra tenant admin** — they can't be automated for you
(they create billable resources and require admin consent).

> Local demo needs none of this: `dotnet run --project src/Pemp.Web` uses SQLite +
> local ASP.NET Core Identity (email/password + authenticator TOTP — see `docs/DEMO.md`).
> This guide is for the cloud deployment, where Entra SSO replaces local Identity.

Prerequisites: `az` CLI logged in (`az login`), rights to create resources in the
subscription, and an Entra tenant admin for app registration + consent.

---

## 1. Register the Entra application (SSO)

```bash
# Create the app registration (single-tenant; redirect URIs added after we know the URL)
az ad app create \
  --display-name "PEMP" \
  --sign-in-audience AzureADMyOrg \
  --web-redirect-uris "https://localhost:5001/signin-oidc"

# Note the appId (clientId) it prints — used as entraClientId below.
APP_ID=<appId>

# Create a client secret OR (preferred) use managed identity / certificate.
# For the web app we use OIDC with a client secret stored in Key Vault:
az ad app credential reset --id $APP_ID --display-name "pemp-web" --years 1
# → record the password; it goes into Key Vault as "AzureAd--ClientSecret" (step 4).
```

In the Entra portal for this app:
- **API permissions** → add Microsoft Graph `User.Read` (delegated) → **Grant admin consent**.
- **Authentication** → enable **ID tokens**.
- Map roles to **Entra security groups** (FR-ADM-01) — create groups for the five PEMP
  roles; assign users/B2B guests (FR-AUTH-07). The app reads role from group claims.
- Enforce **MFA via Conditional Access** for these users (SEC-IAM-03/FR-AUTH-03).

## 2. Pick the SQL Entra admin

Use an Entra **group** (e.g. "PEMP SQL Admins") so membership is auditable:
```bash
az ad group create --display-name "PEMP SQL Admins" --mail-nickname pemp-sql-admins
az ad group member add --group "PEMP SQL Admins" --member-id <your-user-object-id>
SQL_ADMIN_OBJECT_ID=$(az ad group show --group "PEMP SQL Admins" --query id -o tsv)
```

## 3. Deploy the infrastructure (UK region)

```bash
az group create -n pemp-rg -l uksouth

az deployment group create \
  -g pemp-rg \
  -f infra/main.bicep \
  -p infra/main.parameters.json \
  -p sqlAdminObjectId=$SQL_ADMIN_OBJECT_ID entraClientId=$APP_ID

# Capture outputs
az deployment group show -g pemp-rg -n main --query properties.outputs
```
This provisions Log Analytics + App Insights, Key Vault, Azure SQL (Entra-only auth),
private Storage, and the Linux Web App with a managed identity already granted
**Key Vault Secrets User** and **Storage Blob Data Contributor**.

## 4. Store the Entra client secret in Key Vault

```bash
az keyvault secret set --vault-name <kv-name-from-output> \
  --name "AzureAd--ClientSecret" --value "<secret-from-step-1>"
```
The Web App reads it via Key Vault reference (managed identity) — no secret in config.

## 5. Grant the Web App identity access to SQL

Azure SQL uses Entra-only auth, so add the Web App's managed identity as a DB user
(run against the `pemp` database as the Entra admin):
```sql
CREATE USER [pemp-web-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [pemp-web-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [pemp-web-<suffix>];
-- (EF Core EnsureCreated/migrations need DDL; grant db_ddladmin for first run.)
```
`pemp-web-<suffix>` is the Web App name (the identity name) from the deployment output.

## 6. Finish app config

- Add the deployed URL to the app registration redirect URIs:
  `https://<webAppUrl>/signin-oidc` and front-channel logout `https://<webAppUrl>/signout-callback-oidc`.
- The Bicep already set `UseSqlite=false`, the SQL connection string (Entra Default auth),
  and `AzureAd__*` settings on the Web App.

## 7. Deploy the app

```bash
dotnet publish src/Pemp.Web -c Release -o ./publish
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy -g pemp-rg -n <webAppName> --src-path app.zip --type zip
```

Browse `https://<webAppUrl>` → you'll be redirected to Entra SSO; role comes from group
membership; MFA enforced by Conditional Access.

---

## Still out of scope here (Phase-2 architecture / later passes)
Private Endpoints + VNet for SQL/KV/Storage, WAF (App Gateway / Front Door),
Azure Cache for Redis, Service Bus + Functions for async report/notifications, and
CI/CD with SAST/DAST gates (SEC-SDL). These are in `design/architecture.md`; the Bicep
here is the demo-grade core, not the full hardened topology.
