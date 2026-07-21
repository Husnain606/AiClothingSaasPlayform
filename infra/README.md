# FashionSaaS — Azure Infrastructure (Bicep)

> **THIS HAS NOT BEEN APPLIED OR TESTED.** These templates were written and manually reviewed for
> Bicep syntax correctness only — `az`/Bicep CLI are not available in the environment that
> authored them, so **nothing here has been compiled (`az bicep build`), what-if'd, or deployed**.
> Dan validates and applies these himself, with his own Azure subscription and credentials, per
> Phase 8's locked decision D1 (templates only, no live deployment).
>
> Before running anything below for real:
> 1. `az bicep build --file infra/main.bicep` — confirm it actually compiles.
> 2. `az deployment sub what-if ...` (command below) — review every planned change.
> 3. Only then `az deployment sub create ...`.

## What this provisions

| Resource | Module | Mirrors (local compose equivalent) |
|---|---|---|
| Resource group | `infra/main.bicep` | — |
| Log Analytics workspace | `infra/modules/logAnalytics.bicep` | — (new; required by Container Apps for logs) |
| Azure Container Registry | `infra/modules/acr.bicep` | — (new; images have nowhere to live in Azure without one) |
| Azure SQL logical server + 2 databases (`AiClothing`, `TryOnDb`) | `infra/modules/sql.bicep` | `sqlserver` |
| Azure Service Bus namespace + `tryon-events` topic (publish-only, no subscriptions) | `infra/modules/serviceBus.bicep` | `servicebus-emulator` (+ `servicebus-sql` sidecar) |
| Key Vault + secrets (mirrors every `.env.example` credential) | `infra/modules/keyVault.bicep` | root `.env` file |
| Container Apps Environment + 3 Container Apps (api, tryon-api, storefront) | `infra/modules/containerApps.bicep` | `api`, `tryon-api`, `storefront` |

**Container Apps over App Service for Containers**: Container Apps gives per-revision traffic
splitting and scale-to-zero on the Consumption plan, which fits three independently-scaled
services (api, tryon-api, storefront) better than App Service's per-plan sizing model. This
matches D5's locked decision and the design spec's §5 rationale.

**`tryon-events` topic shape**: mirrors
`services/fashionsaas-tryon/servicebus-emulator-config.json` exactly — `DefaultMessageTimeToLive`
`PT1H`, `DuplicateDetectionHistoryTimeWindow` `PT20S`, `RequiresDuplicateDetection` `false`, and no
subscriptions (publish-only; the try-on service only ever publishes to this topic today).

## Prerequisites

- Azure CLI (`az`) installed and logged in against the target subscription: `az login`, then
  `az account set --subscription <subscription-id>`.
- Bicep CLI available (`az bicep install` if `az bicep version` reports it missing).
- Contributor (or equivalent) rights on the target subscription — `main.bicep` deploys at
  **subscription scope** because it creates the resource group itself (see below).
- Real values for every secret parameter (see "Secrets" below) — none are defaulted to anything
  usable in production; `sqlAdminPassword`, `jwtSecret`, `encryptionBankFieldKey`,
  `cloudinaryCloudName`/`cloudinaryApiKey`/`cloudinaryApiSecret`, and `geminiApiKey` have **no
  default** and the deployment will prompt for or fail without them.
- A container image for each of the three apps pushed to the ACR this template provisions, **or**
  accept the placeholder `*:latest` image references in `containerApps.bicep` and push before the
  Container Apps will actually start successfully (no CI/CD pushes images yet — see "Known gaps").

## Why subscription scope, not resource group scope

`main.bicep` sets `targetScope = 'subscription'` and creates the `rg-fashionsaas-<env>` resource
group as its first resource, then deploys every module into that group via `scope: rg`. This means
a single `az deployment sub create` command stands up an entire environment (dev/staging/prod)
from nothing — no manual `az group create` step, and the environment name alone determines both
the resource group name and every resource's name prefix. The tradeoff: subscription-scope
deployments need `az deployment sub ...` (not `az deployment group ...`) and Contributor-or-higher
rights at the subscription level rather than just within an existing resource group. If Dan prefers
resource-group-scope deployment against a group he creates and owns separately, that's a small
change (drop `targetScope`/the `rg` resource, add `scope: resourceGroup()` implicitly, deploy with
`az deployment group create --resource-group <existing-rg> ...`) — flagging as a choice, not a
fact, since either is valid.

## Deploy commands (NOT run as part of Phase 8 — for Dan to execute)

```bash
# 1. Compile/validate the template
az bicep build --file infra/main.bicep

# 2. What-if (dry run) against the target subscription — review every planned change before
#    committing to anything. Prefer a parameter file over inline --parameters for secrets so
#    they never land in shell history; see "Parameter file" below.
az deployment sub what-if \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.dev.json

# 3. Actual deployment
az deployment sub create \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters infra/main.parameters.dev.json \
  --name fashionsaas-dev-$(date +%Y%m%d%H%M%S)
```

### Parameter file

No `main.parameters.*.json` file is committed — it would either be incomplete (no secrets) or
would need to hold real secrets (which must never be committed). Create one locally, gitignored,
per environment, shaped like:

```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "environmentName": { "value": "dev" },
    "location": { "value": "eastus" },
    "sqlAdminPassword": { "value": "<real value — never commit>" },
    "jwtSecret": { "value": "<same value as the API's JwtSettings:Secret today>" },
    "encryptionBankFieldKey": { "value": "<real value>" },
    "cloudinaryCloudName": { "value": "<real value>" },
    "cloudinaryApiKey": { "value": "<real value>" },
    "cloudinaryApiSecret": { "value": "<real value>" },
    "geminiApiKey": { "value": "<real value>" },
    "smtpUsername": { "value": "" },
    "smtpPassword": { "value": "" }
  }
}
```

Alternatively, pull each value from an existing Key Vault at deploy time with
`az deployment sub create ... --parameters sqlAdminPassword=$(az keyvault secret show ...)` style
substitution, or a `Microsoft.KeyVault/vaults/secrets` reference in the parameters file
(`"reference": { "keyVault": {...}, "secretName": "..." }`) if Dan already has a bootstrap vault
from a prior environment.

## Secrets — Key Vault naming convention

`keyVault.bicep` provisions one Key Vault secret per credential the compose stack's `.env` file
documents, using the standard translation from ASP.NET Core's `__` (double-underscore) env-var
config-binder separator to Key Vault's allowed secret-name character set (alphanumerics and
hyphens only — no `__` or `:`): every `__` becomes `--`.

| `.env.example` variable | Key Vault secret name | Source |
|---|---|---|
| `JWT_SECRET` (→ `JwtSettings__Secret`) | `JwtSettings--Secret` | Dan-supplied param |
| `ENCRYPTION_BANK_FIELD_KEY` (→ `EncryptionSettings__BankFieldKey`) | `EncryptionSettings--BankFieldKey` | Dan-supplied param |
| `SMTP_USERNAME` (→ `SmtpSettings__Username`) | `SmtpSettings--Username` | Dan-supplied param (optional) |
| `SMTP_PASSWORD` (→ `SmtpSettings__Password`) | `SmtpSettings--Password` | Dan-supplied param (optional) |
| `CLOUDINARY_CLOUD_NAME` (→ `Cloudinary__CloudName`) | `Cloudinary--CloudName` | Dan-supplied param |
| `CLOUDINARY_API_KEY` (→ `Cloudinary__ApiKey`) | `Cloudinary--ApiKey` | Dan-supplied param |
| `CLOUDINARY_API_SECRET` (→ `Cloudinary__ApiSecret`) | `Cloudinary--ApiSecret` | Dan-supplied param |
| `GEMINI_API_KEY` (→ `GeminiSettings__ApiKey`) | `GeminiSettings--ApiKey` | Dan-supplied param |
| `API_DB_CONNECTION_STRING` (→ `ConnectionStrings__DefaultConnection`) | `ConnectionStrings--DefaultConnection` | Computed by `main.bicep` from `sql.bicep` outputs + `sqlAdminPassword` |
| `TRYON_DB_CONNECTION_STRING` (→ `ConnectionStrings__TryOnConnection`) | `ConnectionStrings--TryOnConnection` | Computed by `main.bicep` from `sql.bicep` outputs + `sqlAdminPassword` |
| `SERVICEBUS_CONNECTION_STRING` (→ `ServiceBusSettings__ConnectionString`) | `ServiceBusSettings--ConnectionString` | Computed inside `keyVault.bicep` via `listKeys()` on the Service Bus namespace's default authorization rule — never a Dan-supplied param |

This translation (`--` for `__`) matches the convention Azure App Service's and Container Apps'
own Key-Vault-reference documentation uses for the same problem (env-var section separators vs.
Key Vault's restricted character set) — confirm against current Microsoft Learn docs before
relying on it if the exact reference syntax matters for a specific consuming service.

## Known gaps (deliberately out of this phase)

- **Container Apps do not yet read these Key Vault secrets.** The `containers[].env` blocks in
  `containerApps.bicep` carry only non-secret settings (`ASPNETCORE_ENVIRONMENT`,
  `ASPNETCORE_HTTP_PORTS`). Wiring the secrets above into each Container App's
  `configuration.secrets` (via `keyVaultUrl` + `identity: 'system'`) plus a role assignment
  granting each app's system-assigned identity **Key Vault Secrets User**
  (`4633458b-17de-408a-b874-0445c86b69e6`) on this vault is a real next increment, deliberately
  **not** done here — it's a design decision (Key Vault reference vs. Container Apps' own
  `secrets` block populated at deploy time) that should be Dan's call, not assumed. The
  `containerApps.bicep` module already accepts `keyVaultUri` as a parameter so this is a body-only
  change when decided.
- **ACR pull RBAC is not modeled either.** `containerApps.bicep`'s `registries` block references
  the ACR by login server with `identity: 'system'` (pull via each Container App's system-assigned
  identity), but — like the Key Vault secrets above — no role assignment grants that identity
  **AcrPull** on the registry. Without it, Container Apps will fail to pull the image at
  deploy/revision time. Add an `AcrPull` (`7f951dda-4ed3-4680-a7ca-43fe172d538d`) role assignment
  per app once real images exist to pull.
- **No CI/CD pipeline pushes images to the ACR or runs `az deployment` automatically.** That's a
  future CD phase (see design spec §6, explicitly out of scope for Phase 8).
- **SQL firewall rule `AllowAllWindowsAzureIps`** (the `0.0.0.0`/`0.0.0.0` special case documented
  by Microsoft as "allow Azure-hosted services to reach this server") is the least-restrictive
  default — tighten to the Container Apps environment's actual outbound IPs before any production
  use.
- **Container Apps `probes` are left at platform defaults** — no explicit liveness/readiness probe
  is configured against the `/health` (api) or `/api/health` (tryon-api) endpoints added in Group A.

## What was and wasn't verified

- **Verified**: every `.bicep` file was manually read for syntax correctness (matching braces,
  correct `resource`/`module`/`param`/`output` block shapes, correct `parent`/`scope` usage,
  correct string interpolation). Secret names cross-checked directly against
  `E:\AIcLOTHING\.env.example` and the Service Bus topic properties cross-checked directly against
  `services/fashionsaas-tryon/servicebus-emulator-config.json`.
- **Not verified**: `az bicep build`, `az deployment sub what-if`, or any real compile/deploy —
  `az`/Bicep CLI are not installed in the environment that authored this. Every Azure resource API
  version used was chosen for being a version I'm reasonably confident is valid and stable as of my
  training data, but this stack (especially `Microsoft.App/*` for Container Apps, which changes
  schema release-to-release more than most providers) post-dates some of that training — **do not
  treat any API version here as authoritative; check Microsoft Learn for the current version before
  applying.**
