# Phase 8 — Docker + CI + Azure Deployment Templates (Buildable Plan)

> **STATUS — PLANNED (2026-07-18).** Not yet built. No `dotnet build` / `dotnet test` / `docker
> build` run has been executed against these samples; counts below are the pre-existing baseline
> (446 main-API tests, 19 try-on tests) that the Validate gate must reproduce unchanged.

## Reference
- Design spec: [`2026-07-18-phase8-docker-ci-azure-design.md`](../specs/2026-07-18-phase8-docker-ci-azure-design.md) — topology, full secret/config matrix, CI design, Azure architecture, out-of-scope list.
- House format: `docs/projectStandards/implementation-plan-format.md`.

### Contract checklist (confirm against landed code before editing)
- [ ] `src/FashionSaaS.API/Program.cs` has no `MapHealthChecks`/`AddHealthChecks` call (verified 2026-07-18 — no match for `health` anywhere under `src/FashionSaaS.API`).
- [ ] `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Controllers/HealthController.cs` exists and exposes `GET /api/health` returning 200/`healthy` or 503 (verified — reads `TryOnDbContext.Database.CanConnectAsync`).
- [ ] `fashionsaas-storefront/angular.json` → `architect.build.configurations.production.fileReplacements` swaps `src/environments/environment.ts` → `environment.prod.ts` (verified).
- [ ] `services/fashionsaas-tryon/docker-compose.servicebus.yml` — `servicebus-emulator` (image `mcr.microsoft.com/azure-messaging/servicebus-emulator:latest`, ports `5672`/`5300`, env `SQL_SERVER`/`MSSQL_SA_PASSWORD`/`ACCEPT_EULA`, mounts `./servicebus-emulator-config.json`) + `servicebus-sql` (`mcr.microsoft.com/mssql/server:2022-latest`) — verified, pattern reused as-is in Task 4.

### Locked decisions in force
- **D1** — packaging + pipeline only, no live deploy, no registry push, no CD.
- **D2** — 3 Dockerfiles: API, TryOn API, storefront (nginx). `.NET 10` image tags confirmed to exist: `mcr.microsoft.com/dotnet/sdk:10.0`, `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu 24.04 "Noble" default variant). Source: https://learn.microsoft.com/en-us/dotnet/architecture/microservices/net-core-net-framework-containers/official-net-docker-images , https://github.com/dotnet/dotnet-docker/discussions/6801
- **D3** — root `docker-compose.yml`: sqlserver, api, tryon-api, servicebus-emulator (+ sql sidecar), storefront; `.env`/`.env.example`; named volumes; migrations applied manually, not on container start.
- **D4** — `.github/workflows/ci.yml`: backend / tryon / storefront / docker jobs, no push. `actions/setup-dotnet` `dotnet-version: '10.0.x'` resolves the latest 10.0 patch. Source: https://github.com/actions/setup-dotnet
- **D5** — `infra/` Bicep: Container Apps (chosen over App Service for Containers — per-revision traffic splitting + scale-to-zero fits 3 independently-scaled services better than one App Service plan), Azure SQL, Service Bus + `tryon-events` topic, ACR, Key Vault, Log Analytics. Parameterized, not applied.
- **D6** — no new runtime deps. **One code touch**: `src/FashionSaaS.API` gets a `/health` endpoint (`MapHealthChecks`, built into the ASP.NET Core shared framework — no NuGet package) since the try-on API already has one and the API Dockerfile's `HEALTHCHECK` needs a target.
- **D7** — storefront Dockerfile takes `API_BASE_URL` / `TRYON_API_BASE_URL` build args, writes them into `environment.prod.ts` before `ng build --configuration production`. Tradeoff: one image per environment instead of promote-one-image-everywhere — acceptable since no registry/CD exists yet (D1/D4).

---

## 1. Ordered task checklist
Execute top-to-bottom. Each Dockerfile/config task is independently verifiable; run its own
verification command before moving to the next group.

### Group A — Main API: health endpoint + Dockerfile
- [ ] **A1** Add `MapHealthChecks("/health")` to `src/FashionSaaS.API/Program.cs` (the one code touch permitted by D6).
- [ ] **A2** Create `src/FashionSaaS.API/Dockerfile` — multi-stage SDK build → aspnet runtime.
- [ ] **A3** Create `src/FashionSaaS.API/.dockerignore`.

### Group B — Try-On API: Dockerfile
- [ ] **B1** Create `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Dockerfile`.
- [ ] **B2** Create `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/.dockerignore`.

### Group C — Storefront: Dockerfile + nginx
- [ ] **C1** Create `fashionsaas-storefront/Dockerfile` (node build stage with build-arg injection → nginx serve stage).
- [ ] **C2** Create `fashionsaas-storefront/nginx.conf` (SPA fallback to `index.html`).
- [ ] **C3** Create `fashionsaas-storefront/.dockerignore`.

### Group D — Root compose + env
- [ ] **D1** Create `docker-compose.yml` at repo root.
- [ ] **D2** Create `.env.example` at repo root.
- [ ] **D3** Add `.env` to root `.gitignore` if not already covered (it already is — `.env` is present in `.gitignore:9`; no edit needed, confirm only).

### Group E — CI
- [ ] **E1** Create `.github/workflows/ci.yml`.

### Group F — Azure Bicep
- [ ] **F1** Create `infra/main.bicep`.
- [ ] **F2** Create `infra/modules/logAnalytics.bicep`.
- [ ] **F3** Create `infra/modules/keyVault.bicep`.
- [ ] **F4** Create `infra/modules/acr.bicep`.
- [ ] **F5** Create `infra/modules/sql.bicep`.
- [ ] **F6** Create `infra/modules/serviceBus.bicep`.
- [ ] **F7** Create `infra/modules/containerApps.bicep`.

### Group G — Infra docs
- [ ] **G1** Create `infra/README.md`.

### Group H — Validate
- [ ] **H1** `dotnet build FashionSaaS.sln -warnaserror` — zero warnings, unchanged from pre-Phase-8 baseline plus the new `/health` line.
- [ ] **H1b** Serena `get_diagnostics_for_file` (`min_severity: 2`) on `src/FashionSaaS.API/Program.cs` — clean.
- [ ] **H2** `dotnet test FashionSaaS.sln` — expect **446 passed** (Domain 24, Application 334, Infrastructure 88), 0 failed.
- [ ] **H3** `dotnet build services/fashionsaas-tryon/FashionSaaS.TryOn.sln -warnaserror` — zero warnings (untouched by this phase).
- [ ] **H4** `dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln` — expect **19 passed**, 0 failed.
- [ ] **H5** `npm --prefix fashionsaas-storefront ci && npm --prefix fashionsaas-storefront run build:prod` — succeeds, `dist/fashionsaas-storefront/browser` populated.
- [ ] **H6** `npm --prefix fashionsaas-storefront run test:ci` — green (untouched by this phase).
- [ ] **H7** `docker build -f src/FashionSaaS.API/Dockerfile -t fashionsaas-api:local .` — succeeds.
- [ ] **H8** `docker build -f services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Dockerfile -t fashionsaas-tryon:local services/fashionsaas-tryon` — succeeds.
- [ ] **H9** `docker build -f fashionsaas-storefront/Dockerfile -t fashionsaas-storefront:local fashionsaas-storefront` — succeeds.
- [ ] **H10** `docker compose -f docker-compose.yml config` — validates without error (requires a local `.env` copied from `.env.example`).
- [ ] **H11** Lint `.github/workflows/ci.yml` — `actionlint` if available locally, else GitHub's own workflow-syntax validation on push; at minimum confirm valid YAML (`python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` or equivalent).
- [ ] **H12** `az bicep build --file infra/main.bicep` — compiles without error (requires Azure CLI + Bicep CLI; if unavailable locally, note as a residual and flag for Dan to run).

---

## 2. Code samples — files to create / modify

### A1 — `src/FashionSaaS.API/Program.cs`
`E:\AIcLOTHING\src\FashionSaaS.API\Program.cs` (modelled on the existing middleware pipeline, `Program.cs:120-162`).

Add after the `app.MapControllers();` line (`Program.cs:160`), and add the required `using` at the
top alongside the existing ones (`Program.cs:1-12`):

```csharp
// add to the using block at the top of the file
using Microsoft.Extensions.Diagnostics.HealthChecks;
```

```csharp
// add before builder.Services.AddControllers() (anywhere in the ── Services ── block), e.g.
// directly after the AddExceptionHandler/AddProblemDetails lines (Program.cs:66-67):
builder.Services.AddHealthChecks();
```

```csharp
// add directly after app.MapControllers(); (Program.cs:160), before await app.RunAsync();
app.MapHealthChecks("/health");
```

`Microsoft.Extensions.Diagnostics.HealthChecks` ships in the ASP.NET Core shared framework
(`Microsoft.NET.Sdk.Web`) already referenced by this project — no new `PackageReference`.

### A2 — `src/FashionSaaS.API/Dockerfile`
`E:\AIcLOTHING\src\FashionSaaS.API\Dockerfile` (new; multi-stage, modelled on the standard
ASP.NET Core container pattern documented at
https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images).

```dockerfile
# syntax=docker/dockerfile:1
# Build context MUST be the repository root:
#   docker build -f src/FashionSaaS.API/Dockerfile -t fashionsaas-api:local .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, for layer caching.
COPY ["src/FashionSaaS.API/FashionSaaS.API.csproj", "src/FashionSaaS.API/"]
COPY ["src/FashionSaaS.Application/FashionSaaS.Application.csproj", "src/FashionSaaS.Application/"]
COPY ["src/FashionSaaS.Domain/FashionSaaS.Domain.csproj", "src/FashionSaaS.Domain/"]
COPY ["src/FashionSaaS.Infrastructure/FashionSaaS.Infrastructure.csproj", "src/FashionSaaS.Infrastructure/"]
RUN dotnet restore "src/FashionSaaS.API/FashionSaaS.API.csproj"

# Now copy the rest of the source and publish.
COPY ["src/", "src/"]
RUN dotnet publish "src/FashionSaaS.API/FashionSaaS.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is required for the container HEALTHCHECK below; not present in the base runtime image.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FashionSaaS.API.dll"]
```

### A3 — `src/FashionSaaS.API/.dockerignore`
`E:\AIcLOTHING\src\FashionSaaS.API\.dockerignore` (new). Placed alongside the Dockerfile but
Docker only honors a `.dockerignore` at the **build context root** — since the build context is
the repo root (Group A2's `docker build -f ... .`), the effective file to create is at the repo
root, shared by all three Dockerfiles' build-context concerns for the two .NET images. To keep
each Dockerfile's ignore rules self-documented without fighting Docker's single-context-root
limitation, create one root-level file scoped broadly:

`E:\AIcLOTHING\.dockerignore` (new; covers both .NET Dockerfiles, whose build context is the repo root):

```
**/bin/
**/obj/
**/.vs/
**/node_modules/
**/dist/
.git/
.claude/
docs/
*.md
.env
```

(No separate file is created at `src/FashionSaaS.API/.dockerignore` — Docker does not read
per-Dockerfile ignore files when a `-f` path is used with a different context root; the root
`.dockerignore` above is the single effective file for Group A and B's builds.)

### B1 — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Dockerfile`
`E:\AIcLOTHING\services\fashionsaas-tryon\src\FashionSaaS.TryOn.Api\Dockerfile` (new; same pattern
as A2, adjusted for the try-on solution's own project set and its own repo-relative build context
— `services/fashionsaas-tryon` — per H8's build command).

```dockerfile
# syntax=docker/dockerfile:1
# Build context MUST be services/fashionsaas-tryon:
#   docker build -f services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Dockerfile \
#     -t fashionsaas-tryon:local services/fashionsaas-tryon

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj", "src/FashionSaaS.TryOn.Api/"]
COPY ["src/FashionSaaS.TryOn.Application/FashionSaaS.TryOn.Application.csproj", "src/FashionSaaS.TryOn.Application/"]
COPY ["src/FashionSaaS.TryOn.Domain/FashionSaaS.TryOn.Domain.csproj", "src/FashionSaaS.TryOn.Domain/"]
COPY ["src/FashionSaaS.TryOn.Infrastructure/FashionSaaS.TryOn.Infrastructure.csproj", "src/FashionSaaS.TryOn.Infrastructure/"]
RUN dotnet restore "src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj"

COPY ["src/", "src/"]
RUN dotnet publish "src/FashionSaaS.TryOn.Api/FashionSaaS.TryOn.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Existing endpoint (Controllers/HealthController.cs) — no code change needed here.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:8080/api/health || exit 1

ENTRYPOINT ["dotnet", "FashionSaaS.TryOn.Api.dll"]
```

### B2 — `services/fashionsaas-tryon/.dockerignore`
`E:\AIcLOTHING\services\fashionsaas-tryon\.dockerignore` (new — this build context root is
`services/fashionsaas-tryon`, distinct from the repo-root context used by A2, so this file *is*
effective here).

```
**/bin/
**/obj/
**/.vs/
.git/
```

### C1 — `fashionsaas-storefront/Dockerfile`
`E:\AIcLOTHING\fashionsaas-storefront\Dockerfile` (new; modelled on the standard Angular
multi-stage node-build → nginx-serve pattern; build-arg injection per D7 into
`src/environments/environment.prod.ts`, whose committed shape is exactly two string fields plus
`production`/`tenantSlug` — see `environment.prod.ts:1-6`).

```dockerfile
# syntax=docker/dockerfile:1
# Build context MUST be fashionsaas-storefront:
#   docker build -f fashionsaas-storefront/Dockerfile \
#     --build-arg API_BASE_URL=https://api.example.com/api \
#     --build-arg TRYON_API_BASE_URL=https://tryon.example.com/api \
#     -t fashionsaas-storefront:local fashionsaas-storefront

FROM node:22-alpine AS build
WORKDIR /app

ARG API_BASE_URL=https://api.fashionsaas.com/api
ARG TRYON_API_BASE_URL=https://tryon.fashionsaas.com/api

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Overwrite environment.prod.ts with the build-arg values before the production build swaps it
# in via angular.json's fileReplacements (production config only — see angular.json:34-39).
RUN printf 'export const environment = {\n  production: true,\n  apiBaseUrl: '\''%s'\'',\n  tenantSlug: '\'''\'', // Determined at runtime\n  tryOnApiBaseUrl: '\''%s'\'',\n};\n' \
    "$API_BASE_URL" "$TRYON_API_BASE_URL" > src/environments/environment.prod.ts

RUN npm run build:prod

FROM nginx:1.27-alpine AS final
COPY --from=build /app/dist/fashionsaas-storefront/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget -qO- http://localhost:80/ >/dev/null 2>&1 || exit 1
```

**Verify at build time**: `npm run build:prod` (→ `ng build --configuration production`) must
emit to `dist/fashionsaas-storefront/browser/` — this is the Angular CLI's default output path
for the `@angular/build:application` builder (no `outputPath` override present in `angular.json`),
appending `/browser` to the project-name-derived default `dist/<project-name>`. Confirm against
the actual `dist/` folder after the first local `npm run build:prod` — flagged as a lead, not a
verified fact, since no prior build output exists in the repo to inspect directly.

### C2 — `fashionsaas-storefront/nginx.conf`
`E:\AIcLOTHING\fashionsaas-storefront\nginx.conf` (new).

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location ~* \.(?:css|js|svg|png|jpg|jpeg|gif|ico|woff2?)$ {
        expires 30d;
        add_header Cache-Control "public, immutable";
        try_files $uri =404;
    }
}
```

### C3 — `fashionsaas-storefront/.dockerignore`
`E:\AIcLOTHING\fashionsaas-storefront\.dockerignore` (new).

```
node_modules/
dist/
.angular/
.git/
*.md
```

### D1 — `docker-compose.yml`
`E:\AIcLOTHING\docker-compose.yml` (new; root compose, incorporating the existing
`services/fashionsaas-tryon/docker-compose.servicebus.yml` service definitions for
`servicebus-emulator`/`servicebus-sql` verbatim in shape).

```yaml
name: fashionsaas

services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: fashionsaas-sqlserver
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD}
    ports:
      - "1433:1433"
    volumes:
      - sqlserver-data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 20s
    networks:
      - fashionsaas

  servicebus-sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: fashionsaas-servicebus-sql
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${SERVICEBUS_SQL_SA_PASSWORD}
    networks:
      fashionsaas:
        aliases:
          - servicebus-sql

  servicebus-emulator:
    image: mcr.microsoft.com/azure-messaging/servicebus-emulator:latest
    container_name: fashionsaas-servicebus-emulator
    pull_policy: always
    volumes:
      - ./services/fashionsaas-tryon/servicebus-emulator-config.json:/ServiceBus_Emulator/ConfigFiles/Config.json
    ports:
      - "5672:5672"
      - "5300:5300"
    environment:
      SQL_SERVER: servicebus-sql
      MSSQL_SA_PASSWORD: ${SERVICEBUS_SQL_SA_PASSWORD}
      ACCEPT_EULA: "Y"
    depends_on:
      - servicebus-sql
    networks:
      fashionsaas:
        aliases:
          - servicebus-emulator

  api:
    build:
      context: .
      dockerfile: src/FashionSaaS.API/Dockerfile
    container_name: fashionsaas-api
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_HTTP_PORTS: "8080"
      ConnectionStrings__DefaultConnection: ${API_DB_CONNECTION_STRING}
      JwtSettings__Secret: ${JWT_SECRET}
      EncryptionSettings__BankFieldKey: ${ENCRYPTION_BANK_FIELD_KEY}
      SmtpSettings__Username: ${SMTP_USERNAME}
      SmtpSettings__Password: ${SMTP_PASSWORD}
      Cloudinary__CloudName: ${CLOUDINARY_CLOUD_NAME}
      Cloudinary__ApiKey: ${CLOUDINARY_API_KEY}
      Cloudinary__ApiSecret: ${CLOUDINARY_API_SECRET}
      Cors__AllowedOrigins__0: ${STOREFRONT_ORIGIN}
    ports:
      - "5000:8080"
    depends_on:
      sqlserver:
        condition: service_healthy
    networks:
      - fashionsaas

  tryon-api:
    build:
      context: services/fashionsaas-tryon
      dockerfile: src/FashionSaaS.TryOn.Api/Dockerfile
    container_name: fashionsaas-tryon-api
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_HTTP_PORTS: "8080"
      ConnectionStrings__TryOnConnection: ${TRYON_DB_CONNECTION_STRING}
      JwtSettings__Secret: ${JWT_SECRET}
      GeminiSettings__ApiKey: ${GEMINI_API_KEY}
      ServiceBusSettings__ConnectionString: ${SERVICEBUS_CONNECTION_STRING}
    ports:
      - "5050:8080"
    depends_on:
      sqlserver:
        condition: service_healthy
      servicebus-emulator:
        condition: service_started
    networks:
      - fashionsaas

  storefront:
    build:
      context: fashionsaas-storefront
      dockerfile: Dockerfile
      args:
        API_BASE_URL: ${STOREFRONT_API_BASE_URL}
        TRYON_API_BASE_URL: ${STOREFRONT_TRYON_API_BASE_URL}
    container_name: fashionsaas-storefront
    ports:
      - "4200:80"
    depends_on:
      - api
    networks:
      - fashionsaas

networks:
  fashionsaas:

volumes:
  sqlserver-data:
```

**Migrations — locked choice (D3):** the `api` service does **not** run `dotnet ef database
update` on startup. Rationale: auto-migrating inside the API's own startup path means every
container restart re-evaluates pending migrations against a live database with no operator gate —
risky for a shared dev/staging SQL instance and impossible to audit from CI. Instead, migrations
are applied manually (`dotnet ef database update --project src/FashionSaaS.Infrastructure --startup-project src/FashionSaaS.API`
from a dev machine, or an explicit one-off `docker compose run` invocation of the same command)
before the `api`/`tryon-api` containers are expected to serve traffic. This is documented, not
automated, in `infra/README.md` (Group G).

### D2 — `.env.example`
`E:\AIcLOTHING\.env.example` (new; every variable the compose file references, documented,
placeholder values only — no real secrets).

```dotenv
# SQL Server (main app + try-on) — SA password, both containers.
MSSQL_SA_PASSWORD=Ch4ngeMe_DevOnly!

# SQL Server sidecar dedicated to the Service Bus emulator (separate instance, separate password
# by convention — matches services/fashionsaas-tryon/docker-compose.servicebus.yml).
SERVICEBUS_SQL_SA_PASSWORD=Ch4ngeMe_DevOnly!

# --- api (FashionSaaS.API) ---
API_DB_CONNECTION_STRING=Server=sqlserver;Database=AiClothing;User Id=sa;Password=Ch4ngeMe_DevOnly!;Encrypt=False;TrustServerCertificate=True
JWT_SECRET=REPLACE-WITH-A-REAL-32-CHAR-MINIMUM-SECRET
ENCRYPTION_BANK_FIELD_KEY=REPLACE-WITH-A-REAL-32-BYTE-AES-KEY
SMTP_USERNAME=
SMTP_PASSWORD=
CLOUDINARY_CLOUD_NAME=
CLOUDINARY_API_KEY=
CLOUDINARY_API_SECRET=
STOREFRONT_ORIGIN=http://localhost:4200

# --- tryon-api (FashionSaaS.TryOn.Api) ---
TRYON_DB_CONNECTION_STRING=Server=sqlserver;Database=TryOnDb;User Id=sa;Password=Ch4ngeMe_DevOnly!;Encrypt=False;TrustServerCertificate=True
GEMINI_API_KEY=REPLACE-WITH-A-REAL-GEMINI-API-KEY
SERVICEBUS_CONNECTION_STRING=Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;

# --- storefront (Angular build args, baked in at image build time — see D7) ---
STOREFRONT_API_BASE_URL=http://localhost:5000/api
STOREFRONT_TRYON_API_BASE_URL=http://localhost:5050/api
```

### D3 — root `.gitignore`
No edit required — `.env` is already listed (`E:\AIcLOTHING\.gitignore:9`). Confirm this line
survives any future `.gitignore` edits; do not remove it.

### E1 — `.github/workflows/ci.yml`
`E:\AIcLOTHING\.github\workflows\ci.yml` (new).

```yaml
name: CI

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

jobs:
  backend:
    name: Backend (FashionSaaS.sln)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore FashionSaaS.sln

      - name: Build (warnings as errors)
        run: dotnet build FashionSaaS.sln --no-restore -warnaserror

      - name: Test
        run: dotnet test FashionSaaS.sln --no-build --logger "console;verbosity=normal"

  tryon:
    name: Try-On (FashionSaaS.TryOn.sln)
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: services/fashionsaas-tryon
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore FashionSaaS.TryOn.sln

      - name: Build (warnings as errors)
        run: dotnet build FashionSaaS.TryOn.sln --no-restore -warnaserror

      - name: Test
        run: dotnet test FashionSaaS.TryOn.sln --no-build --logger "console;verbosity=normal"

  storefront:
    name: Storefront (Angular)
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: fashionsaas-storefront
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: fashionsaas-storefront/package-lock.json

      - name: Install
        run: npm ci

      - name: Build (production)
        run: npm run build:prod

      - name: Test
        run: npm run test:ci

  docker:
    name: Docker image builds (no push)
    runs-on: ubuntu-latest
    needs: [backend, tryon, storefront]
    steps:
      - uses: actions/checkout@v4

      - name: Build API image
        run: docker build -f src/FashionSaaS.API/Dockerfile -t fashionsaas-api:ci .

      - name: Build TryOn API image
        run: |
          docker build -f services/fashionsaas-tryon/src/FashionSaaS.TryOn.Api/Dockerfile \
            -t fashionsaas-tryon:ci services/fashionsaas-tryon

      - name: Build Storefront image
        run: |
          docker build -f fashionsaas-storefront/Dockerfile \
            -t fashionsaas-storefront:ci fashionsaas-storefront
```

**Note on the exact test counts:** this workflow does not hard-fail on a specific number (no test
runner flag enforces "at least N tests ran" here); Task H2/H4 above establish the current known
counts as the manual baseline to re-check after landing. If a stricter automated count-gate is
wanted, that is a follow-on decision for Dan, not assumed here (see Open Questions).

### F1 — `infra/main.bicep`
`E:\AIcLOTHING\infra\main.bicep` (new; subscription-scope orchestrator wiring the modules below).

```bicep
targetScope = 'subscription'

@description('Short environment name, e.g. dev, staging, prod.')
param environmentName string

@description('Azure region for all resources.')
param location string = 'eastus'

@description('SKU for the Azure SQL Database (main + try-on).')
param sqlSkuName string = 'S0'

@description('SKU for the Container Apps Environment workload profile.')
param containerAppsSkuName string = 'Consumption'

@secure()
@description('SQL admin login password. Not applied by this template — pass at deploy time.')
param sqlAdminPassword string

var resourceGroupName = 'rg-fashionsaas-${environmentName}'
var namePrefix = 'fsaas-${environmentName}'

resource rg 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: resourceGroupName
  location: location
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module acr 'modules/acr.bicep' = {
  name: 'acr'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    skuName: sqlSkuName
    adminPassword: sqlAdminPassword
  }
}

module serviceBus 'modules/serviceBus.bicep' = {
  name: 'serviceBus'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
  }
}

module containerApps 'modules/containerApps.bicep' = {
  name: 'containerApps'
  scope: rg
  params: {
    namePrefix: namePrefix
    location: location
    skuName: containerAppsSkuName
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    acrLoginServer: acr.outputs.loginServer
    keyVaultUri: keyVault.outputs.vaultUri
  }
}

output resourceGroupName string = rg.name
output acrLoginServer string = acr.outputs.loginServer
output sqlServerFqdn string = sql.outputs.serverFqdn
output serviceBusNamespaceFqdn string = serviceBus.outputs.namespaceFqdn
```

### F2 — `infra/modules/logAnalytics.bicep`
`E:\AIcLOTHING\infra\modules\logAnalytics.bicep` (new).

```bicep
param namePrefix string
param location string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

output workspaceId string = workspace.id
```

### F3 — `infra/modules/keyVault.bicep`
`E:\AIcLOTHING\infra\modules\keyVault.bicep` (new; holds the secrets the compose file keeps in
`.env` — `JwtSettings__Secret`, `EncryptionSettings__BankFieldKey`, SMTP credentials, Cloudinary
credentials, Gemini API key, Service Bus connection string, SQL admin password. Secret **values**
are not set by this template — only the vault + access policy shape).

```bicep
param namePrefix string
param location string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}-kv'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

output vaultUri string = keyVault.properties.vaultUri
output vaultName string = keyVault.name
```

### F4 — `infra/modules/acr.bicep`
`E:\AIcLOTHING\infra\modules\acr.bicep` (new).

```bicep
param namePrefix string
param location string

@allowed(['Basic', 'Standard', 'Premium'])
param skuName string = 'Basic'

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: replace('${namePrefix}acr', '-', '')
  location: location
  sku: {
    name: skuName
  }
  properties: {
    adminUserEnabled: false
  }
}

output loginServer string = acr.properties.loginServer
output registryName string = acr.name
```

### F5 — `infra/modules/sql.bicep`
`E:\AIcLOTHING\infra\modules\sql.bicep` (new; one logical server, two databases —
`AiClothing` and `TryOnDb` — mirroring the compose `sqlserver` service hosting both).

```bicep
param namePrefix string
param location string
param skuName string = 'S0'

@secure()
param adminPassword string

param adminLogin string = 'fashionsaasadmin'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${namePrefix}-sql'
  location: location
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    version: '12.0'
  }
}

resource mainDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AiClothing'
  location: location
  sku: {
    name: skuName
  }
}

resource tryOnDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'TryOnDb'
  location: location
  sku: {
    name: skuName
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output mainDbName string = mainDb.name
output tryOnDbName string = tryOnDb.name
```

### F6 — `infra/modules/serviceBus.bicep`
`E:\AIcLOTHING\infra\modules\serviceBus.bicep` (new; namespace + the `tryon-events` topic that
`ServiceBusSettings.TopicName` defaults to — `services/fashionsaas-tryon/src/FashionSaaS.TryOn.Application/Messaging/ServiceBusSettings.cs:13`).

```bicep
param namePrefix string
param location string

@allowed(['Basic', 'Standard', 'Premium'])
param skuName string = 'Standard'

resource namespace 'Microsoft.ServiceBus/namespaces@2023-01-01-preview' = {
  name: '${namePrefix}-sb'
  location: location
  sku: {
    name: skuName
    tier: skuName
  }
}

resource topic 'Microsoft.ServiceBus/namespaces/topics@2023-01-01-preview' = {
  parent: namespace
  name: 'tryon-events'
}

output namespaceFqdn string = '${namespace.name}.servicebus.windows.net'
output namespaceName string = namespace.name
output topicName string = topic.name
```

### F7 — `infra/modules/containerApps.bicep`
`E:\AIcLOTHING\infra\modules\containerApps.bicep` (new; environment + 3 container apps — api,
tryon-api, storefront — mirroring the compose topology. Images are referenced by tag but **not
pushed** by this phase — the `image` parameters below default to placeholders Dan fills in at
actual-deploy time, consistent with D1/D5's "templates only" scope).

```bicep
param namePrefix string
param location string

@allowed(['Consumption'])
param skuName string = 'Consumption'

param logAnalyticsWorkspaceId string
param acrLoginServer string
param keyVaultUri string

@description('Fully-qualified image references; left as placeholders until an image is pushed to the ACR provisioned by acr.bicep.')
param apiImage string = '${acrLoginServer}/fashionsaas-api:latest'
param tryOnApiImage string = '${acrLoginServer}/fashionsaas-tryon:latest'
param storefrontImage string = '${acrLoginServer}/fashionsaas-storefront:latest'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: last(split(logAnalyticsWorkspaceId, '/'))
}

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-cae'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource tryOnApiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-tryon-api'
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'tryon-api'
          image: tryOnApiImage
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

resource storefrontApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-storefront'
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 80
      }
      registries: [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'storefront'
          image: storefrontImage
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
  identity: {
    type: 'SystemAssigned'
  }
}

output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
output tryOnApiFqdn string = tryOnApiApp.properties.configuration.ingress.fqdn
output storefrontFqdn string = storefrontApp.properties.configuration.ingress.fqdn
```

**Secret wiring not modeled in full here:** binding the Container Apps' env vars listed in the
design spec's §3 matrix (`JwtSettings__Secret`, `ConnectionStrings__*`, etc.) to Key Vault
references (`secretRef` + a `Microsoft.App/containerApps` `secrets` block pulling from
`keyVaultUri`) is the natural next increment once real secret values exist in the vault — left as
a documented gap in `infra/README.md` (Group G) rather than templated blind, since the exact
`secretRef` list depends on which secrets Dan actually populates.

### G1 — `infra/README.md`
`E:\AIcLOTHING\infra\README.md` (new).

```markdown
# FashionSaaS — Azure Infrastructure (Bicep)

Templates only. **Nothing here has been applied.** Dan runs these deployments himself with his
own Azure credentials and subscription.

## What this provisions

- Azure Container Apps Environment + 3 Container Apps (api, tryon-api, storefront)
- Azure SQL logical server with 2 databases (`AiClothing`, `TryOnDb`)
- Azure Service Bus namespace + `tryon-events` topic
- Azure Container Registry
- Key Vault (shape only — no secret values are set by these templates)
- Log Analytics workspace (Container Apps log sink)

## Prerequisites

- Azure CLI (`az`) logged in (`az login`) against the target subscription.
- Bicep CLI (`az bicep install` if not already present).
- An existing container image for each of the three apps pushed to the ACR this template
  provisions, **or** accept the placeholder `*:latest` image references and push before the
  Container Apps will start successfully.

## Deploy commands (NOT run as part of Phase 8 — for Dan to execute)

\`\`\`bash
# 1. Validate/compile
az bicep build --file infra/main.bicep

# 2. What-if (dry run) against a target subscription
az deployment sub what-if \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters environmentName=dev sqlAdminPassword='<secret>'

# 3. Actual deployment
az deployment sub create \
  --location eastus \
  --template-file infra/main.bicep \
  --parameters environmentName=dev sqlAdminPassword='<secret>'
\`\`\`

## Known gaps (deliberately out of this phase)

- Container Apps `env` blocks reference only non-secret settings (`ASPNETCORE_ENVIRONMENT`,
  `ASPNETCORE_HTTP_PORTS`). The full secret matrix in
  `docs/superpowers/specs/2026-07-18-phase8-docker-ci-azure-design.md` §3 (JWT secret, connection
  strings, Cloudinary, Gemini, Service Bus) is **not** wired to Key Vault `secretRef`s yet — do
  that once real secret values are decided and placed in the vault this template provisions.
- No CI/CD pipeline pushes images to the ACR or runs `az deployment` automatically — that is a
  future CD phase, not this one (see design spec §6, out of scope).
- SQL firewall rule `AllowAllWindowsAzureIps` (0.0.0.0/0.0.0.0 Azure-services-only special case)
  is the least-restrictive default Microsoft documents for "let Azure services reach this SQL
  server" — tighten to specific outbound IPs (Container Apps environment's static IPs) before any
  production use.
```

---

## 3. Exact test list (testing-expert)

**No new tests are written in this phase** — it is packaging/CI/infra only, and D6 permits exactly
one code touch (`MapHealthChecks`) which is a one-line wiring call with no branching logic to
unit-test in isolation (the underlying `HealthCheckMiddleware` is framework-owned and already
covered by ASP.NET Core's own test suite). The Validate gate (Group H) instead re-runs the
**existing** test suites and asserts their **counts are unchanged**:

- **`dotnet test FashionSaaS.sln`** — expect 446 passed / 0 failed / 0 skipped (Domain 24,
  Application 334, Infrastructure 88), matching the pre-Phase-8 baseline exactly.
- **`dotnet test services/fashionsaas-tryon/FashionSaaS.TryOn.sln`** — expect 19 passed / 0 failed
  / 0 skipped.
- **`npm run test:ci`** (fashionsaas-storefront) — expect the pre-existing vitest suite green,
  unchanged.

> **Known coverage gap:** no automated test exercises `GET /health` on the main API after A1 lands
> (the try-on API's equivalent `HealthController` *does* have coverage in
> `FashionSaaS.TryOn.Api.Tests`, per the existing 19-test baseline). Adding one is a reasonable
> follow-on but is out of scope for a documentation/packaging phase per D6 — flagged, not silently
> skipped.

## 4. Observability
- No new metrics/traces/spans are introduced. The `/health` endpoint added in A1 is consumed only
  by the Docker `HEALTHCHECK` directive (A2) and, later, by Container Apps' own liveness probing
  (not configured in F7 — Container Apps' `probes` block is left at platform defaults, another
  documented gap in `infra/README.md`).

## 5. OPEN QUESTIONS (decisions, not facts)
1. **Should CI hard-fail if test counts drop below the known baseline (446 / 19), not just report the run?** A `dotnet test --logger trx` + count-parsing step could enforce this. *Default: not implemented — the workflow only requires tests to be green; confirm if Dan wants a stricter regression gate.*
2. **Is `App Service for Containers` preferred over Container Apps for any operational reason (e.g. existing team familiarity, VNet integration constraints)?** D5 recommends Container Apps per the one-line justification in the design spec. *Default: Container Apps as designed; confirm before F7 is built for real.*
3. **Key Vault secret wiring into Container Apps (`secretRef`)** — left undone in F7 per the "Known gaps" note in G1. *Default: documented as a follow-on; confirm whether Dan wants it modeled now with placeholder secret names even though no real values exist yet.*
4. **MfaSettings — is `MfaSettings:IssuerKey` actually bound anywhere in `src/`?** No `MfaSettings` C# class was found via `SectionName` search under `src/`, yet the key exists in `appsettings.Development.json`. *Default: treated as an optional/possibly-dead config key in the secret matrix (design spec §3.1); flag for Dan — may indicate the setting is read via raw `IConfiguration` indexing rather than the Options pattern, or may be vestigial.*

## 6. Assumptions
- The Angular production build output path is `dist/fashionsaas-storefront/browser` (default for
  `@angular/build:application` with no `outputPath` override in `angular.json`) — not verified
  against an actual local build in this session; verify on first `npm run build:prod` (Task H5).
- `node:22-alpine` and `nginx:1.27-alpine` are used for the storefront Dockerfile's two stages —
  these are third-party base images, not application-code dependencies subject to the "no new
  library without approval" rule, but are still a choice worth surfacing: Node 22 is the current
  Active LTS as of this writing and matches Angular 21's tooling requirements; confirm if a
  different pinned tag is preferred.
- Azure region `eastus` is a placeholder default in `main.bicep`; Dan overrides at deploy time via
  the `location` parameter.
- The existing `services/fashionsaas-tryon/docker-compose.servicebus.yml` file is left untouched
  (not deleted/superseded) — the root `docker-compose.yml` (D1) duplicates its service shapes
  rather than `include:`-ing it, since Docker Compose's `include` directive support and the
  relative-path semantics for the mounted `servicebus-emulator-config.json` were judged higher-risk
  to get right silently than an intentional, visible duplication; revisit if Dan prefers a single
  source of truth via `include:`.

**No further changes to this plan will be made without your sign-off.**
