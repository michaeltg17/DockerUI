# Architecture

## Overview

One container, two runtimes: an ASP.NET Core 10 minimal API that owns the
Docker integration and serves a static React SPA. No database — the Docker
daemon is the source of truth, polled every few seconds.

```
Browser (React SPA)
   │  GET/POST /api/apps...          ▲ appsUpdated (AppDto[])
   ▼                                 │
ASP.NET Core (Api) ─── SignalR hub ──┘
   │  IContainerOperations / ISystemOperations
   ▼
Docker.DotNet ── unix:///var/run/docker.sock (ro) ── Docker daemon
```

## api/src/Api

- **Features/Apps**
  - `Endpoints/` — `GET /api/apps`, `POST /api/apps/{name}/start|stop|restart`.
    Route values are validated by `AppNameValidator` (400 on garbage).
  - `AppService` — the only class that talks to Docker. Wraps every call in
    `DockerUIException` (→ 503) so daemon problems surface as a clean
    problem-details response.
  - `AppCatalog` — pure function: container snapshots → `AppDto[]`. Groups by
    the `com.docker.compose.project` label, resolves each app's icon (see
    App icons below), and computes the aggregate state (`Running` / `Partial`
    / `Stopped`). Unit tested in isolation.
  - `Icons/AppIconCatalog` — resolves container images to icons (full image
    name, then last path segment) and app names to icons (exact match against
    the normalized icon file names, then fuzzy: boundary prefix/suffix,
    token subset, Levenshtein similarity ≥ 0.8).
  - `Background/AppStateMonitor` — `BackgroundService` that polls
    `AppService.GetAppsAsync`, serializes the result, and broadcasts over
    SignalR only when the JSON changed. Also implements `IAppStateMonitor`
    so endpoints can force a re-broadcast right after a state change. On
    every cycle it checks the settings files' last write times and calls
    `IConfigurationRoot.Reload()` on change, so edited settings apply from
    the next poll even when file-change events don't propagate through the
    bind mount (e.g. Docker Desktop).
  - `Hubs/AppAppsHub` — SignalR hub mapped at `/api/apps/hub`; server →
    client only (`appsUpdated`).
- **Features/Health** — `/health/live` (no checks) and `/health/ready`
  (pings the daemon via `ISystemOperations`).
- **DependencyConfigurator** — all DI. `AddDockerClient` builds the client
  from `DockerUI:DockerSocketPath` (unix socket or Windows named pipe).
  `Configure()` adds the exception handler, static files, endpoint mapping,
  and the SPA fallback.
- **Extensions/ExceptionHandlerExtensions** — global exception middleware
  mapping exceptions to RFC 9457 problem details:
  `DockerUIException` → 503, `NotFoundException` → 404,
  `BadHttpRequestException` → 400, else 500.
- **Extensions/StringExtensions** and **Extensions/TypeExtensions** — small
  pure helpers (e.g. `string.JoinNonEmpty`, `Type.GetNameWithoutGenericArity`).
- **Builders/** — a small generic builder hierarchy (`IBuilder<T>`,
  `Builder<T>`, `BuilderWithValues<TBuilder,T>`, `BuilderWithInstance<TBuilder,T>`).

Settings (`DockerUISettings` and its validator, plus `AppUserSettings` /
`AppIconMapping`) live in the API project's `Settings/` folder, bound from the
`DockerUI` appsettings section and validated at startup via
`DependencyConfigurator.AddSettingsDependencies`.

## App icons

`AppCatalog` resolves each app's icon in this order (first match wins); when
nothing matches, the UI's `AppIcon` renders the app's initials on a colored
background.

1. **`DockerUI:Apps.<name>.Icon`** — a per-stack override in appsettings.json,
   keyed by the app's *name*: this stack gets this icon, whatever runs in it.
   Any URL or data URI.
2. **The `dockerui.icon` container label** — the same per-stack override, but
   declared on a container in the stack's own `docker-compose.yml`, so it
   travels with the stack. First non-empty label across the stack's
   containers wins; any URL or data URI.
3. **`DockerUI:Icons`** — image → icon mappings in appsettings.json. A rule
   that applies to any app running a matching *image*, whatever the stack is
   named. Images are matched normalized (tags and digests stripped), falling
   back to the last path segment.
4. **The stack name against the icon catalog** — the app's name is matched
   against the icon file names in `ui/public/icons/`, exactly first
   (`wavelog` → `wavelog.svg`), then fuzzily (see `Icons/AppIconCatalog`
   for the scoring rules).
5. **The built-in image catalog** — the Umbrel-synced `app-icons.json`
   embedded in the assembly: container image → icon file served at
   `/icons/`. Same image matching as 3.
6. **Initials fallback** — no icon from any source.

1–2 (and 3, which the user also configures) are explicit choices, so they
beat the inference in 4–5: a wrong guess is just an odd icon, but overriding
a deliberate choice would not be. Within the automatic sources, the user's
own image mappings (3) win over the auto-generated catalog (5), and a
stack name the user chose (4) is a stronger signal than which container
image happens to be recognized first (5).

## ui/

- `src/app` — shell: providers (React Query, error boundary, notifications)
  and a single-route router (`/` → `AppsPage`).
- `src/features/apps`
  - `api/` — thin typed wrappers over `http.get/post` (axios instance with a
    `response.data`-unwrapping interceptor; errors raise a toast and reject).
  - `hooks/use-apps` — `useApps` query + start/stop/restart mutations
    (invalidate the query on success).
  - `hooks/use-apps-hub` — SignalR connection that writes `appsUpdated`
    payloads straight into the React Query cache (no refetch) and
    invalidates after reconnects.
  - `components/` — `AppsGrid`, `AppCard` (state badge + actions),
    `AppIcon` (custom `dockerui.icon` or initials fallback).
- Styling: Tailwind CSS with shadcn-style theme tokens; UI kit in
  `src/components/ui` (button, spinner, notifications).

## Deployment

`Dockerfile` is multi-stage: `node:24-alpine` builds `ui/dist`,
`dotnet/sdk:10.0` publishes the API, `dotnet/aspnet:10.0` runs it with the
UI in `wwwroot`. `docker-compose.yml` mounts the host Docker socket
read-only and exposes `5000:8080`. All configuration lives in
`appsettings.json` under the `DockerUI` section; an optionally mounted
`appsettings.json` replaces the packaged defaults (see the README) and is
hot-reloaded for everything except `DockerSocketPath`.
