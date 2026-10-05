[![ci](https://github.com/michaeltg17/DockerUI/actions/workflows/ci.yml/badge.svg)](https://github.com/michaeltg17/DockerUI/actions/workflows/ci.yml)
# Docker UI

<img width="2203" height="848" alt="image" src="https://github.com/user-attachments/assets/9ccbbce7-9587-411e-94be-622dd9bf7343" />

A lightweight, Umbrel-style dashboard for your Docker stacks. It shows every
Docker Compose stack (and standalone container) on your machine as an icon +
name card with a live state badge, and lets you **start**, **stop**, and
**restart** any of them from the browser.

State changes made anywhere (other terminals, other UIs, cron jobs, …) are
picked up automatically and pushed to the browser in real time.

## How it works

- The .NET 10 backend talks to the Docker daemon over the local socket
  (`/var/run/docker.sock`, or the named pipe on Windows) using
  [Docker.DotNet](https://www.nuget.org/packages/Docker.DotNet).
- **Stacks are detected by labels**, not by file location: containers are
  grouped by their `com.docker.compose.project` label. Any stack works,
  wherever its compose file lives. Unlabeled containers show up as their own
  "app".
- A background service polls the daemon every few seconds and pushes the app
  list to connected browsers over **SignalR** whenever it changes.
- The frontend is a small Vite + React (TypeScript) single-page app. The API
  serves its production build from `wwwroot`, so the whole product is one
  container.

## Quick start (production)

```bash
docker compose up -d
```

Then open http://localhost:5000.

The `docker-compose.yml` pulls the image from GHCR
(`ghcr.io/michaeltg17/docker-ui:latest`), which CI publishes on every push to
`main`. To build from source instead, run `docker build -t docker-ui:latest .`
and set `image: docker-ui:latest` in `docker-compose.yml`.

The Docker socket is mounted read-only into the container. To serve on a
different host port, change the `"5000:8080"` mapping in `docker-compose.yml`.

### Configuration

Everything lives in `appsettings.json`, under the `DockerUI` section:

| Key | Default | Description |
| --- | --- | --- |
| `DockerUI:DockerSocketPath` | `/var/run/docker.sock` | Path to the Docker socket (or a Windows named pipe such as `\\.\pipe\docker_engine`) |
| `DockerUI:PollIntervalSeconds` | `5` | How often the daemon is polled for state changes |
| `DockerUI:BaseUrl` | *(none)* | Base URL (scheme + host) used for the auto-detected app web URLs. When omitted, the host the dashboard is being browsed from is used |
| `DockerUI:Icons` | *(none)* | Image-to-icon mappings that extend or override the built-in catalog (see [App icons](#app-icons)) |
| `DockerUI:Apps` | *(none)* | Per-app overrides, keyed by stack name: `Url`, `Icon`, `Hidden` |
| `DockerUI:Order` | *(none)* | Apps listed here come first, in this order; everything else follows alphabetically |

The container ships with working defaults (`/var/run/docker.sock`, 5 s
poll). To customize, copy `appsettings.example.json` to `appsettings.json`
next to `docker-compose.yml`, edit it, and uncomment the `appsettings.json`
volume mount in `docker-compose.yml`:

```json
{
  "AllowedHosts": "*",
  "DockerUI": {
    "DockerSocketPath": "/var/run/docker.sock",
    "PollIntervalSeconds": 5,
    "BaseUrl": "http://192.168.1.46:5000",
    "Icons": [
      { "Image": "myregistry/whatever", "Icon": "jellyfin.png" }
    ],
    "Apps": {
      "my-stack": { "Url": "http://192.168.1.46:3000", "Icon": "custom.png" },
      "vault": { "Hidden": true }
    },
    "Order": ["my-stack"]
  }
}
```

Only `DockerSocketPath` is required (plus `PollIntervalSeconds ≥ 1`); every
other key is optional. A mounted `appsettings.json` **replaces** the packaged
one, so keep the socket path in it.

**Hot reload:** `PollIntervalSeconds`, `BaseUrl`, `Icons`, `Apps` and
`Order` are picked up live — the dashboard detects settings-file changes on
every poll and applies them from the next cycle; changing
`DockerSocketPath` requires a container restart. Standard .NET config
precedence still applies — `DockerUI__*` environment variables override the
file (e.g. `DockerUI__PollIntervalSeconds=10`).

### App web URLs

Each card links to the app's web UI when one can be determined:

1. **`DockerUI:Apps.<name>.Url`** in appsettings.json, if configured.
2. **Auto-detected**: when one of the stack's running containers publishes a
   TCP port, the link uses the first published port from the common web
   ports (80, 8080, 3000, 8000, 5000, 8888, 9000, 9090, 5173, 4200, 443,
   8443) or the smallest one otherwise. The **host** is the one the
   dashboard itself is served under (the host of the current request, or
   `DockerUI:BaseUrl`), so the link works for whoever is browsing — port 80
   becomes `http://host`, 443/8443 become `https://…`.
3. **No link**: when nothing is published, the card is not clickable.

### App icons

Icons are resolved in this order (first match wins):

1. **`DockerUI:Apps.<name>.Icon`** in appsettings.json (see above).
2. **The `dockerui.icon` label** on any container of the stack — any URL or
   data URI:

   ```yaml
   services:
     my-service:
       image: ...
       labels:
         dockerui.icon: "https://example.com/icon.png"
   ```

3. **`DockerUI:Icons` mappings** from appsettings.json: each container's
   image (e.g. `linuxserver/jellyfin:10.9`) is matched against a
   user-provided image → icon mapping.
4. **The stack name against the icon catalog**: the stack's name is matched
   against the icon file names in `ui/public/icons/` — exactly first
   (`wavelog` → `wavelog.svg`), then fuzzily, so names that are not exact
   still resolve: boundary prefix or suffix (`my-wavelog` → `wavelog.svg`,
   `adguard` → `adguard-home.svg`), reordered words, and small typos
   (similarity of 0.8 or higher).
5. **The built-in icon catalog**: each container's image (e.g.
   `linuxserver/jellyfin:10.9`) is matched against a mapping of images to
   icons that is synced from the
   [Umbrel app store](https://github.com/getumbrel/umbrel-apps-gallery)
   (`ui/public/icons/`, served at `/icons/`).
6. **The initials fallback**: the app's initials on a colored background.

To remap images to different icons, add entries to `DockerUI:Icons` in
appsettings.json:

```json
"Icons": [
  { "Image": "myregistry/whatever", "Icon": "jellyfin.png" },
  { "Image": "postgres", "Icon": "pi-hole.png" }
]
```

`Image` is matched against the normalized image name (tags and digests are
ignored), falling back to the last path segment; `Icon` must be a file that
exists in `ui/public/icons/`. To refresh the catalog after new apps land in
the Umbrel store, run `yarn --cwd ui icons:sync` and commit the generated
files.

## Development

Two terminals:

```bash
# 1. API on http://localhost:5000 (uses DockerUI:DockerSocketPath from appsettings.Development.json)
dotnet run --project api/src/Api

# 2. UI on http://localhost:3000 (proxies /api to the API, including the SignalR web socket)
cd ui && yarn && yarn dev
```

### Tests

```bash
# UI (type check, lint, build)
cd ui
yarn check-types
yarn lint
yarn build

# E2E (Docker Desktop must be running)
dotnet build e2e/DockerUI.e2e.slnx
dotnet e2e/DockerUI.E2ETests/bin/Debug/net10.0/DockerUI.E2ETests.dll
```

## API

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/api/apps` | List all stacks/containers as `AppDto` objects |
| `POST` | `/api/apps/{name}/start` | Start all stopped services of the app |
| `POST` | `/api/apps/{name}/stop` | Stop all running services of the app |
| `POST` | `/api/apps/{name}/restart` | Restart all running services of the app |
| `GET` | `/health/live` | Liveness probe |
| `GET` | `/health/ready` | Readiness probe (pings the Docker daemon) |

SignalR hub at `/api/apps/hub` pushes `appsUpdated` (the full app list) to
every connected client whenever the daemon state changes.

## Repository layout

```
├── api/                       # .NET solution + build/SDK config
│   ├── DockerUI.slnx
│   ├── Directory.Build.props  # net10.0, central package management, analysis
│   ├── Directory.Packages.props
│   ├── global.json
│   └── src/
│       └── Api/               # Minimal API: features, endpoints, settings, hub, DI, Program, shared helpers
├── ui/                        # Vite + React SPA (served by the API in prod)
├── e2e/                       # xunit v3 + Playwright scenarios (run in CI)
├── Dockerfile                 # Multi-stage: UI build → API publish → runtime
└── docker-compose.yml
```
