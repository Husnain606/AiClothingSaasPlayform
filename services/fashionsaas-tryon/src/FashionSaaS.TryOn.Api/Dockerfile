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
