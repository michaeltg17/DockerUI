# Docker UI

A self-hosted dashboard for Docker Compose stacks: one card per stack with
live state, plus start/stop/restart. .NET 10 minimal API (serves the React
SPA) + Vite/React/TypeScript frontend, communicating with the Docker daemon
through its socket.

## Layout

- `api/` — the .NET solution (`DockerUI.slnx`) and its build/SDK config
  (`Directory.Build.props`, `Directory.Packages.props`, `global.json`); the
  single `Api` project (csproj) sits in the same folder.
- `Api` — ASP.NET Core minimal API. Feature-based: `Features/Apps`
  (endpoints, `AppService`, `AppCatalog`, SignalR hub, background monitor),
  `Features/Health`. `Settings/` holds `DockerUISettings` (all settings, bound
  from the `DockerUI` appsettings section; the app-state monitor reloads the
  configuration when a settings file changes) and its startup validator.
   `Program` composes DI (`AddDependencies`); `Configure()` adds the exception
   handler, static files, and the SPA fallback.
- `ui/` — Vite + React SPA. `src/features/apps` holds the single feature
  (types, api, hooks, components); `src/app` is the shell; `src/components/ui`
  is the shared UI kit (button, spinner, notifications).
- `e2e/` — the only tests in the repo; separate xunit v3 + Playwright
  solution (`DockerUIE2E.slnx`) whose `E2E` project (csproj) sits in the same
  folder; the `scenarios/` demo stacks live next to it (part of the project,
  never copied to the test output).
  Each scenario under `e2e/scenarios` is its own DockerUI instance (own port,
  own `appsettings.json`) plus demo stacks; `Environments/` orchestrates the
  compose environments, `Playwright/` holds the browser fixture and the
  `AppsPage` locators.

## Conventions

- Central package management: add package versions in the solution's
  `Directory.Packages.props` (`api/` for the API, `e2e/` for tests),
  references in csproj files.
- The API response interceptor in the UI unwraps `response.data`; use the
  `http.get/post` helpers from `ui/src/lib/api-client.ts` for typed calls.

## Code standards

- C#: `using` directives and short type names (not inline fully-qualified
  names); file-scoped namespaces, primary constructors, expression bodies
  where short.
- C#: no magic strings or numbers; prefer `nameof`, constants, or well-named
  variables.
- Both solutions build warning-free (`AnalysisMode=AllEnabledByDefault` +
  `TreatWarningsAsErrors`), so new code must not add analyzer warnings.
- TypeScript: strict mode, kebab-case files/folders, LF line endings, `@/`
  alias to `ui/src`, Tailwind with the shadcn-style tokens from `index.css`.

## Commands

```bash
# API
dotnet run --project api/Api.csproj         # dev on :5000

# UI (from ui/)
npm install
npm run dev                                 # :3000, proxies /api -> :5000 (ws)
npm run check-types
npm run lint
npm run build                               # tsc + vite build (base=/)

# Product
docker compose up -d --build                # http://localhost:5000

# E2E (Docker Desktop must be running)
dotnet build e2e/DockerUIE2E.slnx
dotnet e2e/bin/Debug/net10.0/E2E.dll
# Builds the DockerUI image once, then brings up the basic/settings/error
# scenarios (ports 5010-5012) and tears them all down afterwards.

# Local CI (same checks as GitHub CI: Dockerfile.ci runs ci.sh)
docker build -t docker-ui-ci:latest -f Dockerfile.ci .
# <ws> is the checkout as a POSIX path (E:\1\Repos\docker-ui ->
# /e/1/Repos/docker-ui on Docker Desktop); client and daemon must see
# the same path, so mount the checkout onto that path.
docker run --rm --network host -w <ws> -v <ws>:<ws> \
  -v //var/run/docker.sock:/var/run/docker.sock docker-ui-ci:latest
```

## Gotchas

- Docker.DotNet 3.125.x client construction:
  `new DockerClientConfiguration(endpoint, credentials, ...).CreateClient(apiVersion)`
  with `unix:///path/to.sock` or `npipe://./pipe/docker_engine` URIs.
  `WaitBeforeKillSeconds` is `uint?`. Container list responses expose `ID`
  (not `Id`) and `Labels` as `IDictionary<string,string>`.
- `DockerUIException` → 500, `DaemonUnavailableException` → 503,
  `NotFoundException` → 404, `ConflictException` → 409 (mapped in
  `api/Setup/ExceptionHandlerConfigurator.cs`); problems are RFC 9457
  `application/problem+json` with the human message in `detail`.
- The SignalR hub is at `/api/apps/hub`; the vite dev proxy must forward
  web sockets (`ws: true`) for live updates in development.
- UI build output (`ui/dist`) is copied into the API image's `wwwroot`;
  `MapFallbackToFile("index.html")` serves the SPA for all non-`/api` routes.
- E2E: the browser fixture launches the locally installed Chrome
  (`Channel = "chrome"`) so Playwright never downloads browsers (the Playwright
  CDN stalls on some networks; `Dockerfile.ci` therefore installs
  `google-chrome-stable` and the Docker CLI instead). The suite must be run via
  the built test dll (xunit v3 in-process runner): `dotnet test` fails to
  discover xunit v3 tests on this machine's SDK. CI runs the suite inside the
  `Dockerfile.ci` container with the runner's Docker socket mounted and
  `--network host` (the scenarios publish ports on the runner's localhost,
  5010-5012). The checkout is mounted onto its own path (`-w` +
  `-v ws:ws`): the scenario compose files bind-mount `./appsettings.json`,
  and the daemon resolves that source on its own host, so the client's and
  the daemon's view of the path must be identical. The dashboard lists every
  compose project on the machine, so scenarios assert on their own apps, not
  the full card list.

## Workflow
Commit on `dev` → push `dev` → open (or update) the `dev` → `main` PR.

When creating or updating the `dev` → `main` PR:
1. Run `git fetch origin main` first
2. Compare `origin/main..dev` to identify only the actual new changes.
3. Check if a PR already exists (`gh pr list --repo michaeltg17/docker-ui --head dev --state open`).
4. If none exists, create one with title and description summarizing the changes.
5. If one exists, update its title and description to reflect the actual current diff.

## Automatic dev cycle

Applies only when the user asks to start the automatic dev cycle.

- The task board is
  https://github.com/users/michaeltg17/projects/8/views/1.
- Tasks ready for start developing are the ones in the `ready` column;
  never pick ones in the backlog. Ignore any task assigned to
  `michaeltg17`. Only pick unassigned tasks not in the backlog column.
- When picking a task, move its card to `In progress`.
- Do the work, then commit and push on `dev` and open/update the
  `dev` → `main` PR, following the `## Workflow` instructions above.
- A task counts as finished when it is committed, pushed, the PR is
  updated, and the e2e tests pass in CI. If the GitHub CI run is
  cancelled or stuck, do not wait for it: validate locally that CI
  passes by running the `Dockerfile.ci` container (it executes
  `ci.sh`: API Release build, UI lint/type check/build, and the full
  e2e suite against the local Docker daemon), then treat the task as
  done and go to the next one.
- On finish, move the card to `Done`. If the work needs user review,
  move it to `In review` instead, assign it to `michaeltg17`, and leave a
  comment on the task describing what was done and what to review.
- Card operations (list / move / assign / comment) are provided by the
  `github-project-board` skill — load it when starting the cycle.
- Then pick the next eligible task from the `ready` column and repeat;
  stop and report a summary when no eligible tasks remain.