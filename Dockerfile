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
COPY api/Directory.Build.props api/Directory.Packages.props api/global.json api/
COPY api/src/Api/Api.csproj api/src/Api/
RUN dotnet restore api/src/Api/Api.csproj
COPY api/src/ ./api/src/
RUN dotnet publish api/src/Api/Api.csproj -c Release -o /app/api /p:UseAppHost=false

# ---- Stage 3: runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
COPY --from=api-build /app/api ./
COPY --from=ui-build /app/dist ./wwwroot
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s \
  CMD bash -c '</dev/tcp/127.0.0.1/8080' || exit 1
ENTRYPOINT ["dotnet", "Api.dll"]
