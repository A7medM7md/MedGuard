# syntax=docker/dockerfile:1
#
# One multi-stage Dockerfile for every MedGuard image. Pick one with --target
# (docker-compose.yml does this for you):
#
#   api                -> MedGuard.API on Kestrel, with the Angular SPA in wwwroot
#   identity           -> MedGuard.IdentityServer (Duende) on Kestrel
#   ingestion-bridge   -> MQTT -> Kafka bridge
#   monitoring-worker  -> Kafka -> SQL consumer + outbox dispatcher
#   edge               -> nginx router: /identity/ -> identity, the rest -> api, <tool>.<domain> -> ops tools
#   prometheus, grafana -> observability images with the repo's config baked in
#   migrator           -> EF Core migrations bundle (runs once, then exits)
#   simulator          -> fake MQTT devices (demo / load test)
#   kafkalab           -> the Kafka fundamentals lab CLI
#
# Build context = repository root (needs both medguard-backend and medguard-angular).

ARG DOTNET_VERSION=8.0

# ---------------------------------------------------------------- .NET restore
# Project files first, so `dotnet restore` stays cached until a .csproj changes.
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY medguard-backend/src/MedGuard.Domain/MedGuard.Domain.csproj                     src/MedGuard.Domain/
COPY medguard-backend/src/MedGuard.Application/MedGuard.Application.csproj           src/MedGuard.Application/
COPY medguard-backend/src/MedGuard.Infrastructure/MedGuard.Infrastructure.csproj     src/MedGuard.Infrastructure/
COPY medguard-backend/src/MedGuard.Contracts/MedGuard.Contracts.csproj               src/MedGuard.Contracts/
COPY medguard-backend/src/MedGuard.ServiceDefaults/MedGuard.ServiceDefaults.csproj   src/MedGuard.ServiceDefaults/
COPY medguard-backend/src/MedGuard.API/MedGuard.API.csproj                           src/MedGuard.API/
COPY medguard-backend/src/MedGuard.IdentityServer/MedGuard.IdentityServer.csproj     src/MedGuard.IdentityServer/
COPY medguard-backend/src/MedGuard.IngestionBridge/MedGuard.IngestionBridge.csproj   src/MedGuard.IngestionBridge/
COPY medguard-backend/src/MedGuard.Monitoring.Worker/MedGuard.Monitoring.Worker.csproj src/MedGuard.Monitoring.Worker/
COPY medguard-backend/src/MedGuard.DeviceSimulator/MedGuard.DeviceSimulator.csproj   src/MedGuard.DeviceSimulator/
COPY medguard-backend/src/MedGuard.KafkaLab/MedGuard.KafkaLab.csproj                 src/MedGuard.KafkaLab/
RUN for p in API IdentityServer IngestionBridge Monitoring.Worker DeviceSimulator KafkaLab; do \
        dotnet restore "src/MedGuard.$p/MedGuard.$p.csproj" || exit 1; \
    done

FROM restore AS build
COPY medguard-backend/src/ src/

FROM build AS publish-api
RUN dotnet publish src/MedGuard.API/MedGuard.API.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS publish-identity
RUN dotnet publish src/MedGuard.IdentityServer/MedGuard.IdentityServer.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS publish-bridge
RUN dotnet publish src/MedGuard.IngestionBridge/MedGuard.IngestionBridge.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS publish-worker
RUN dotnet publish src/MedGuard.Monitoring.Worker/MedGuard.Monitoring.Worker.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS publish-simulator
RUN dotnet publish src/MedGuard.DeviceSimulator/MedGuard.DeviceSimulator.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM build AS publish-kafkalab
RUN dotnet publish src/MedGuard.KafkaLab/MedGuard.KafkaLab.csproj -c Release --no-restore -o /out /p:UseAppHost=false

# A self-contained executable that applies pending migrations and exits. Building
# it runs the API's Program.cs up to builder.Build() to find the DbContext, and
# AddInfrastructure insists on a connection string: a dummy one is enough, since
# nothing connects at build time. The real one is passed with --connection.
FROM build AS publish-migrator
RUN dotnet tool install --global dotnet-ef --version 8.0.8
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN ConnectionStrings__MedGuardDb="Server=design-time-only;Database=MedGuard" \
    dotnet ef migrations bundle \
        --project src/MedGuard.Infrastructure/MedGuard.Infrastructure.csproj \
        --startup-project src/MedGuard.API/MedGuard.API.csproj \
        --configuration Release \
        --self-contained -r linux-x64 \
        -o /out/efbundle

# ---------------------------------------------------------------- Angular SPA
FROM node:24-alpine AS spa
WORKDIR /spa
COPY medguard-angular/package.json medguard-angular/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY medguard-angular/ ./
# "selfhost": every URL is derived from window.location.origin (same-origin
# API under /api, IdentityServer under /identity). See environment.selfhost.ts.
RUN npx ng build --configuration selfhost

# ---------------------------------------------------------------- runtime images
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS aspnet-base
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

FROM aspnet-base AS api
COPY --from=publish-api /out ./
# The SPA is served by the API's own Kestrel (UseStaticFiles + MapFallbackToFile).
COPY --from=spa /spa/dist/medguard-angular/browser ./wwwroot
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.API.dll"]

FROM aspnet-base AS identity
COPY --from=publish-identity /out ./
# Never ship a signing key from the repo: the container keeps its own keys in
# the /app/data volume (DataPath, see HostingExtensions.cs).
RUN rm -rf ./keys && mkdir -p /app/data && chown -R $APP_UID /app/data
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.IdentityServer.dll"]

FROM aspnet-base AS ingestion-bridge
COPY --from=publish-bridge /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.IngestionBridge.dll"]

FROM aspnet-base AS monitoring-worker
COPY --from=publish-worker /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.Monitoring.Worker.dll"]

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS simulator
WORKDIR /app
COPY --from=publish-simulator /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.DeviceSimulator.dll"]

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION} AS kafkalab
WORKDIR /app
COPY --from=publish-kafkalab /out ./
USER $APP_UID
ENTRYPOINT ["dotnet", "MedGuard.KafkaLab.dll"]

# Path/host router in front of the Kestrel apps and the ops tools
# (Cloudflare Tunnel -> edge). The config is baked in, so it is versioned and
# deployed with the app images; the basic-auth file is written at start-up.
FROM nginx:1.27-alpine AS edge
RUN apk add --no-cache openssl && mkdir -p /etc/nginx/medguard
COPY deploy/nginx/edge.conf  /etc/nginx/conf.d/default.conf
COPY deploy/nginx/tools.conf /etc/nginx/conf.d/tools.conf
COPY deploy/nginx/common.inc deploy/nginx/tools-auth.inc /etc/nginx/medguard/
COPY --chmod=755 deploy/nginx/40-medguard-htpasswd.sh /docker-entrypoint.d/

# Observability (ADR 0011) with its config baked in: the server only has
# docker-compose.yml, not the repo.
FROM prom/prometheus:v3.4.1 AS prometheus
COPY medguard-backend/observability/prometheus.yml /etc/prometheus/prometheus.yml

FROM grafana/grafana:12.0.2 AS grafana
COPY medguard-backend/observability/grafana/provisioning /etc/grafana/provisioning
COPY medguard-backend/observability/grafana/dashboards  /var/lib/grafana/dashboards

FROM mcr.microsoft.com/dotnet/runtime-deps:${DOTNET_VERSION} AS migrator
WORKDIR /app
COPY --from=publish-migrator /out/efbundle ./efbundle
USER $APP_UID
ENTRYPOINT ["./efbundle"]
