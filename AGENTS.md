# Docker UI

A self-hosted dashboard for Docker Compose stacks: one card per stack with
live state, plus start/stop/restart. .NET 10 minimal API (serves the React
SPA) + Vite/React/TypeScript frontend, communicating with the Docker daemon
through its socket.

## Layout

- `api/src/Api` — ASP.NET Core minimal API. Feature-based: `Features/Apps`
  (endpoints, `AppService`, `AppCatalog`, SignalR hub, background monitor),
  `Features/Health`. `DependencyConfigurator` composes DI; `Configure()` adds
  the exception handler, static files, and the SPA fallback.
- `api/src/Core` — tiny shared helpers (string/type extensions).
- `api/src/CrossCutting` — `DockerUiSettings` (all settings, bound from the
  `DockerUi` appsettings section; the app-state monitor reloads the
  configuration when a settings file changes) + validator and DI
  configurator.
- `ui/` — Vite + React SPA. `src/features/apps` holds the single feature
  (types, api, hooks, components); `src/app` is the shell; `src/components/ui`
  is the shared UI kit (button, spinner, notifications).
- `api/tests/UnitTests` — pure logic (`AppCatalog`).
- `api/tests/IntegrationTests` — endpoint behavior via `WebApplicationFactory`,
  pointed at a nonexistent socket (no Docker daemon required).

## Conventions

- Central package management: add package versions in
  `Directory.Packages.props` (root), references in csproj files.
- C# style: file-scoped namespaces, primary constructors, expression bodies
  where short. Analysis rules are enforced (`AnalysisMode=AllEnabledByDefault`),
  keep the build warning-free when practical.
- TypeScript: strict mode, `@/` path alias to `ui/src`, kebab-case file and
  folder names, LF line endings. Tailwind for styling; shadcn-style theme
  tokens from `index.css`.
- The API response interceptor in the UI unwraps `response.data`; use the
  `http.get/post` helpers from `ui/src/lib/api-client.ts` for typed calls.

## Commands

```bash
# API
dotnet run --project api/src/Api            # dev on :5000
dotnet test api/tests/UnitTests
dotnet test api/tests/IntegrationTests

# UI (from ui/)
yarn install
yarn dev                                    # :3000, proxies /api -> :5000 (ws)
yarn check-types
yarn lint
yarn test --run
yarn build                                  # tsc + vite build (base=/)

# Product
docker compose up -d --build                # http://localhost:5000
```

## Gotchas

- Docker.DotNet 3.125.x client construction:
  `new DockerClientConfiguration(endpoint, credentials, ...).CreateClient(apiVersion)`
  with `unix:///path/to.sock` or `npipe://./pipe/docker_engine` URIs.
  `WaitBeforeKillSeconds` is `uint?`. Container list responses expose `ID`
  (not `Id`) and `Labels` as `IDictionary<string,string>`.
- `DockerUiException` → 503, `NotFoundException` → 404 (mapped in
  `Api/Extensions/ExceptionHandlerExtensions.cs`); problems are RFC 9457
  `application/problem+json` with the human message in `detail`.
- The SignalR hub is at `/api/apps/hub`; the vite dev proxy must forward
  web sockets (`ws: true`) for live updates in development.
- UI build output (`ui/dist`) is copied into the API image's `wwwroot`;
  `MapFallbackToFile("index.html")` serves the SPA for all non-`/api` routes.

## Workflow
Commit on `dev` → push `dev` → open (or update) the `dev` → `main` PR.

When creating or updating the `dev` → `main` PR:
1. Run `git fetch origin main` first
2. Compare `origin/main..dev` to identify only the actual new changes.
3. Check if a PR already exists (use `github_list_pull_requests`).
4. If none exists, create one with title and description summarizing the changes.
5. If one exists, update its title and description to reflect the actual current diff.