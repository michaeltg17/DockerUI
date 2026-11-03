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
COPY api/Directory.Build.props api/Directory.Packages.props api/global.json api/
COPY api/Api.csproj api/
RUN dotnet restore api/Api.csproj
COPY api/ ./api/
RUN dotnet publish api/Api.csproj -c Release -o /app/api /p:UseAppHost=false

# ---- Stage 3: runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=api-build /app/api ./
COPY --from=ui-build /app/dist ./wwwroot
# Run as a dedicated non-root user with a fixed uid (1000 = the standard first-user
# uid on Linux hosts, so a host appsettings.json created by the normal user is owned
# correctly without a chown). The Ubuntu base already ships user+group 'ubuntu' with
# uid/gid 1000, so rename them to 'dockerui' (the uid stays stable across base-image
# upgrades). /app must be owned by the user: settings saves write a .tmp sibling next
# to appsettings.json and copy it over the original in place.
# The daemon socket is mounted read-only and its owning group depends on the host:
# root:root on Docker Desktop, root:docker on Linux hosts, where 'docker' is gid 999
# on Ubuntu/Debian, 993 on RHEL, 983 on Arch, and 997 on openSUSE. Granting dockerui
# those gids as supplementary groups (creating them in the image when the base lacks
# them) lets it reach the daemon on any of those hosts without running as root.
RUN groupmod -n dockerui ubuntu \
    && usermod -l dockerui -d /home/dockerui -m ubuntu \
    && for gid in 0 999 993 983 997; do \
         getent group "$gid" || groupadd -g "$gid" "dockerui-gid-$gid"; \
       done \
    && usermod -aG 0,999,993,983,997 dockerui \
    && chown -R dockerui:dockerui /app
USER dockerui
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD bash -c '</dev/tcp/127.0.0.1/8080' || exit 1
ENTRYPOINT ["dotnet", "Api.dll"]
