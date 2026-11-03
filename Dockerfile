# syntax=docker/dockerfile:1

# ---- Stage 1: build the React UI -------------------------------------------------
FROM node:24-alpine AS ui-build
WORKDIR /app
COPY ui/package.json ui/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY ui/ ./
RUN npm run build

# ---- Stage 2: build the .NET API --------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY .editorconfig ./
# global.json must sit in the stage root (the SDK resolves it from the working
# directory upwards, never into the project folder).
COPY api/global.json ./
COPY api/Directory.Build.props api/Directory.Packages.props api/
COPY api/Api.csproj api/
RUN dotnet restore api/Api.csproj
COPY api/ ./api/
RUN dotnet publish api/Api.csproj -c Release -o /app/api /p:UseAppHost=false

# ---- Stage 3: runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
# curl powers the HTTP healthcheck below; install it in its own layer (before the
# app COPYs) so code rebuilds do not repeat the package download.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=api-build /app/api ./
COPY --from=ui-build /app/dist ./wwwroot
# Run as a dedicated non-root user with a fixed uid (1000 = the standard first-user
# uid on Linux hosts, so a host appsettings.json created by the normal user is owned
# correctly without a chown). The base image occupies uid/gid 1000 with its own
# 'ubuntu' account, which is dropped first (guarded for base images that do not ship
# it); dockerui is then created fresh, so it inherits no supplementary groups and the
# build does not depend on a user the base image happens to ship.
# Only /app and the packaged appsettings.json are handed to the user: settings saves
# write a .tmp sibling next to the file (needs the directory writable) and copy it
# over the original in place (needs the file writable); a bind-mounted
# appsettings.json must be owned by uid 1000 on the host, everything else stays
# root-owned. The daemon socket is mounted read-only and its owning group depends on
# the host (root:root on Docker Desktop, root:docker on Linux hosts), so its gid is
# granted per deployment through the compose files' group_add
# ("${SOCKET_GROUP_GID:-0}") instead of baking gids into the image.
RUN if getent passwd 1000 >/dev/null; then userdel -r "$(getent passwd 1000 | cut -d: -f1)"; fi \
    && if getent group 1000 >/dev/null; then groupdel "$(getent group 1000 | cut -d: -f1)"; fi \
    && groupadd --gid 1000 dockerui \
    && useradd --uid 1000 --gid dockerui --create-home --shell /usr/sbin/nologin dockerui \
    && chown dockerui:dockerui /app /app/appsettings.json
USER dockerui
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Api.dll"]
