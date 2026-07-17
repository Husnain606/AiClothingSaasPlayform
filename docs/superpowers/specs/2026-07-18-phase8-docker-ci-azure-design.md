# Phase 8 — Docker + CI + Azure Deployment Templates (Design Spec)

> **Status: DESIGN.** Documentation-only artifact. No application code changes are proposed here
> beyond the one noted exception (D6 — `/health` on the main API). Nothing in this phase is
> applied to a live environment; Dan runs any actual `docker compose up` / `az deployment` himself.

## 1. Overview

FashionSaaS today ships as three independently buildable trees with no packaging layer:

| # | Deployable | Path | Stack | Tests |
|---|---|---|---|---|
| 1 | Main API | `src/FashionSaaS.API` | .NET 10 Web API (`FashionSaaS.sln`) | 446 (Domain 24, Application 334, Infrastructure 88) |
| 2 | Try-On API | `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api` | .NET 10 Web API (`FashionSaaS.TryOn.sln`) | 19 |
| 3 | Storefront | `fashionsaas-storefront` | Angular 21 (App Router-style standalone app, vitest via `@angular/build:unit-test`) | vitest suite (`npm run test:ci`) |

Phase 8 adds the packaging + pipeline layer around these three: Dockerfiles, a full-stack root
compose for local/dev, a GitHub Actions CI workflow that builds and tests all three, and
parameterized Bicep templates that encode the target Azure architecture. **No live deployment, no
registry push, and no CD** happen as part of this phase — see §6.

## 2. Container topology

```
                                   ┌───────────────────────────┐
                                   │        docker-compose      │
                                   │   (local / dev only)       │
                                   └───────────────────────────┘

┌─────────────┐   HTTP    ┌───────────────┐        ┌───────────────┐
│ storefront  │──────────▶│      api      │──────▶│   sqlserver    │
│ (nginx:     │  /api/*   │ FashionSaaS   │  SQL   │ mssql/server   │
│  static +   │           │   .API        │        │  :2022-latest  │
│  SPA        │           │  :8080        │        │  db: AiClothing│
│  fallback)  │           └───────────────┘        └───────┬────────┘
│  :4200      │                                            │
│             │  HTTP     ┌───────────────┐                │
│             │──────────▶│  tryon-api    │────────────────┘
└─────────────┘ /tryon/*  │ FashionSaaS   │  SQL (db: TryOnDb)
                          │ .TryOn.Api    │
                          │  :8080        │
                          └───────┬───────┘
                                  │ AMQP 5672 / mgmt 5300
                                  ▼
                          ┌───────────────────────┐      ┌────────────────┐
                          │ servicebus-emulator    │─────▶│ servicebus-sql │
                          │ mcr.microsoft.com/     │ SQL  │ mssql/server   │
                          │ azure-messaging/       │      │  :2022-latest  │
                          │ servicebus-emulator    │      └────────────────┘
                          └───────────────────────┘
```

- `sqlserver` hosts both `AiClothing` (main API) and `TryOnDb` (try-on API) — two databases, one
  engine instance, to keep the local compose to a single SQL container plus the Service-Bus
  emulator's own dedicated SQL sidecar (`servicebus-sql`), which mirrors the existing
  `services/fashionsaas-tryon/docker-compose.servicebus.yml` topology unchanged.
- `servicebus-emulator` + `servicebus-sql` are lifted from the existing
  `docker-compose.servicebus.yml` pattern (image, env vars, config-file mount) rather than
  reinvented — see plan Task 4.
- `storefront` is nginx serving the Angular `dist/fashionsaas-storefront/browser` output, proxying
  nothing itself — the Angular app calls `api`/`tryon-api` directly via baked-in
  `environment.prod.ts` base URLs (see §3, D7).
- Azure Container Apps is the target runtime (§5) — the compose topology maps 1:1 onto four
  container apps + one Azure SQL server + one Service Bus namespace, with no compose-only service
  (`servicebus-emulator`) present in Azure (replaced by real Azure Service Bus).

## 3. Secret / config matrix

Every environment variable each container needs, derived from the actual `appsettings*.json` and
`Configuration`/`Settings` classes (source-of-truth: `src/FashionSaaS.Application/Configuration/*.cs`,
`services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/**/*Settings.cs`). ASP.NET Core's
default configuration binds env vars using `__` (double underscore) as the section-path separator,
e.g. `JwtSettings__Secret` binds to `JwtSettings:Secret`.

### 3.1 `api` (FashionSaaS.API)

| Env var | Maps to config key | Source (committed default) | Required at runtime | Notes |
|---|---|---|---|---|
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | none (dev-only, gitignored) | Yes | `Server=...;Database=AiClothing;User Id=sa;Password=...` |
| `JwtSettings__Secret` | `JwtSettings:Secret` | none (dev-only, gitignored) | Yes | HS256 signing key, ≥32 chars |
| `JwtSettings__Issuer` | `JwtSettings:Issuer` | `appsettings.json`: `"FashionSaaS"` | No (has default) | |
| `JwtSettings__Audience` | `JwtSettings:Audience` | `appsettings.json`: `"FashionSaaSUsers"` | No (has default) | |
| `EncryptionSettings__BankFieldKey` | `EncryptionSettings:BankFieldKey` | none (dev-only, gitignored) | Yes | AES key for bank-field encryption |
| `MfaSettings__IssuerKey` | `MfaSettings:IssuerKey` | `"FashionSaaS-Dev"` in dev appsettings only | No (has default in code path used) | No `MfaSettings` C# class was found bound via `SectionName` in `src/` — confirm at build time whether this key is actually read; treat as optional pass-through |
| `SmtpSettings__Host` | `SmtpSettings:Host` | `"smtp.gmail.com"` | No (has default) | |
| `SmtpSettings__Port` | `SmtpSettings:Port` | `587` | No (has default) | |
| `SmtpSettings__From` | `SmtpSettings:From` | `"noreply@fashionsaas.com"` | No (has default) | |
| `SmtpSettings__Username` | `SmtpSettings:Username` | empty | Yes (for real email) | |
| `SmtpSettings__Password` | `SmtpSettings:Password` | not in any committed appsettings | Yes (for real email) | Not present in `appsettings.Development.json` either — must be supplied |
| `Cloudinary__CloudName` | `Cloudinary:CloudName` | none (dev-only, gitignored) | Yes | |
| `Cloudinary__ApiKey` | `Cloudinary:ApiKey` | none (dev-only, gitignored) | Yes | |
| `Cloudinary__ApiSecret` | `Cloudinary:ApiSecret` | none (dev-only, gitignored) | Yes | |
| `Cors__AllowedOrigins__0` | `Cors:AllowedOrigins:0` | `appsettings.json`: `["http://localhost:4200"]` | No (has default) | In compose, set to the storefront's exposed origin |
| `ASPNETCORE_ENVIRONMENT` | — | — | Yes | `Production` in compose/Azure |
| `ASPNETCORE_HTTP_PORTS` | — | — | Yes | `8080` — container listens on plain HTTP behind the platform's own TLS termination (Container Apps / nginx) |

### 3.2 `tryon-api` (FashionSaaS.TryOn.Api)

| Env var | Maps to config key | Source (committed default) | Required at runtime | Notes |
|---|---|---|---|---|
| `ConnectionStrings__TryOnConnection` | `ConnectionStrings:TryOnConnection` | none (dev-only, gitignored) | Yes | `Server=...;Database=TryOnDb;...` |
| `JwtSettings__Secret` | `JwtSettings:Secret` | none (dev-only, gitignored) | Yes | **Must match the main API's secret** — the try-on service validates JWTs issued by the main API (cross-service acceptance test exists in `FashionSaaS.TryOn.Api.Tests`) |
| `JwtSettings__Issuer` | `JwtSettings:Issuer` | `"FashionSaaS"` | No (has default) | |
| `JwtSettings__Audience` | `JwtSettings:Audience` | `"FashionSaaSUsers"` | No (has default) | |
| `GeminiSettings__ApiKey` | `GeminiSettings:ApiKey` | placeholder in dev appsettings only | Yes | Google Gemini API key — real secret |
| `GeminiSettings__BaseUrl` | `GeminiSettings:BaseUrl` | `"https://generativelanguage.googleapis.com"` | No (has default) | |
| `GeminiSettings__Model` | `GeminiSettings:Model` | `"gemini-2.5-flash-image"` | No (has default) | |
| `GeminiSettings__AllowedGarmentImageHosts__0` | `GeminiSettings:AllowedGarmentImageHosts:0` | `["res.cloudinary.com"]` | No (has default; SSRF allow-list — see `aab5b5c` fix) | Extend only if a second image host is introduced |
| `ServiceBusSettings__ConnectionString` | `ServiceBusSettings:ConnectionString` | emulator connection string in dev appsettings only | Yes | Local/dev: `UseDevelopmentEmulator=true` pointed at `servicebus-emulator:5672`; Azure: managed-identity or SAS connection string from the real namespace |
| `ServiceBusSettings__TopicName` | `ServiceBusSettings:TopicName` | `"tryon-events"` | No (has default) | Matches the Bicep-provisioned topic name (§5) |
| `ASPNETCORE_ENVIRONMENT` | — | — | Yes | `Production` |
| `ASPNETCORE_HTTP_PORTS` | — | — | Yes | `8080` |

### 3.3 `storefront` (Angular / nginx)

Angular bakes `environment.prod.ts` values in at **build time** via the `fileReplacements`
mechanism in `angular.json` (`production` config swaps `environment.ts` → `environment.prod.ts`).
There is no runtime env-var injection today. Per D7, the Dockerfile accepts build args and writes
them into a generated `environment.prod.ts` before `ng build` runs:

| Docker build arg | Bakes into | Committed default (`environment.prod.ts`) |
|---|---|---|
| `API_BASE_URL` | `environment.apiBaseUrl` | `https://api.fashionsaas.com/api` |
| `TRYON_API_BASE_URL` | `environment.tryOnApiBaseUrl` | `https://tryon.fashionsaas.com/api` |

**Tradeoff (one line):** build-args are simpler than a runtime-config.json fetch, but they mean a
new image must be built per environment (dev/staging/prod) rather than one image promoted across
environments — acceptable here since the CI job doesn't push images anywhere yet (D1/D4).

### 3.4 Compose-only infrastructure secrets

| Env var | Used by | Notes |
|---|---|---|
| `MSSQL_SA_PASSWORD` | `sqlserver`, `servicebus-sql` | SA password for both SQL Server containers |
| `ACCEPT_EULA` | `sqlserver`, `servicebus-emulator`, `servicebus-sql` | Must be `Y` |
| `SQL_SERVER` | `servicebus-emulator` | Hostname of its SQL sidecar (`servicebus-sql`) |

All of the above are supplied via a root `.env` file (gitignored, per the existing `.env` entry in
`.gitignore`), with `.env.example` committed documenting every variable and a placeholder value.

## 4. CI pipeline design

`.github/workflows/ci.yml`, triggered on `push`/`pull_request` to `master`, four parallel jobs:

1. **`backend`** — `actions/setup-dotnet` with `dotnet-version: '10.0.x'`; `dotnet build FashionSaaS.sln -warnaserror`; `dotnet test` — asserts the known-good counts (446: Domain 24 / Application 334 / Infrastructure 88) are not silently reduced.
2. **`tryon`** — same pattern against `services/fashionsaas-tryon/FashionSaaS.TryOn.sln`; asserts 19 tests.
3. **`storefront`** — `actions/setup-node`, `npm ci`, `npm run build:prod`, `npm run test:ci` (vitest, `--watch=false`).
4. **`docker`** — depends on the three build jobs; runs `docker build` for all three Dockerfiles to prove they build. **No `docker push`** — no registry credentials exist in this repo/org yet (D1, D4).

Each job runs independently and fails the workflow on any red step; no job is allowed to swallow
a non-zero exit code.

## 5. Azure architecture (what the Bicep encodes)

| Resource | Bicep module | Maps to compose service |
|---|---|---|
| Azure Container Apps Environment + 3 Container Apps (api, tryon-api, storefront) | `infra/modules/containerApps.bicep` | `api`, `tryon-api`, `storefront` |
| Azure SQL Database (logical server + 2 databases: `AiClothing`, `TryOnDb`) | `infra/modules/sql.bicep` | `sqlserver` |
| Azure Service Bus namespace + `tryon-events` topic | `infra/modules/serviceBus.bicep` | `servicebus-emulator` (dev-only stand-in) |
| Azure Container Registry | `infra/modules/acr.bicep` | (new — images have nowhere to live in Azure without one; not used by CI in this phase) |
| Key Vault | `infra/modules/keyVault.bicep` | replaces the `.env` file's secrets |
| Log Analytics workspace | `infra/modules/logAnalytics.bicep` | (new — required by Container Apps Environment for logs) |

**Container Apps over App Service for Containers**: Container Apps gives per-revision traffic
splitting and scale-to-zero on the consumption plan, which fits three independently-scaled
services (api, tryon-api, storefront) better than App Service's per-plan sizing model.

Everything under `infra/` is parameterized (environment name, SKUs) and documented with exact
`az deployment sub create` / `az deployment group create` commands in `infra/README.md` — **none
of it is applied** as part of this phase.

## 6. Explicitly out of scope

- **Live deployment** — no `az deployment` run against a real subscription.
- **Registry push** — the `docker` CI job builds images locally in the runner and discards them; no `ACR_*` or `DOCKER_*` secrets exist to push with.
- **CD** — no environment promotion, no GitHub Actions `deploy` job, no `workflow_dispatch` for infra apply.
- **Any change to application `.cs`/`.ts` logic** other than the one noted exception (a `/health` endpoint on the main API, D6) — the main API has no health endpoint today (verified: no match for `health` under `src/FashionSaaS.API`), while the try-on API already has one (`Controllers/HealthController.cs`, DB-connectivity check).
- **EF Core auto-migration on container startup** — deliberately not wired into the API image (see plan Task 4 discussion); migrations are applied out-of-band.

<!-- last reviewed: 2026-07-18 -->
