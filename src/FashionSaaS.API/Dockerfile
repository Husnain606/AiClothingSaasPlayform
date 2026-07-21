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
