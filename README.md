# Docker UI

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

| Setting | Env var | Default | Description |
| --- | --- | --- | --- |
| `DockerUi:DockerSocketPath` | `DockerUi__DockerSocketPath` | `/var/run/docker.sock` | Path to the Docker socket (or a Windows named pipe such as `\\.\pipe\docker_engine`) |
| `DockerUi:PollIntervalSeconds` | `DockerUi__PollIntervalSeconds` | `5` | How often the daemon is polled for state changes |
| `DockerUi:SettingsFile` | `DockerUi__SettingsFile` | *(none)* | Optional path to a single JSON settings file (see below). Relative paths are resolved against the content root |
| `DockerUi:IconsOverrideFile` | `DockerUi__IconsOverrideFile` | *(none)* | Optional path to a JSON file with icon mappings that override the built-in catalog (see below) |

### Settings file

`DockerUi:SettingsFile` points to a single JSON file —
`./config/settings.json` in the compose setup, where the volume is already
mounted — that covers every user-level customization:

```json
{
  "baseUrl": "http://192.168.1.46:5000",
  "icons": [
    { "image": "myregistry/whatever", "icon": "jellyfin.svg" }
  ],
  "apps": {
    "forgejo": { "url": "http://192.168.1.46:3000", "icon": "forgejo.svg" },
    "vault": { "hidden": true }
  },
  "order": ["forgejo", "vault"]
}
```

| Key | Meaning |
| --- | --- |
| `baseUrl` | Base URL used for the auto-detected app web URLs (see below). When omitted, the host the dashboard is being browsed from is used |
| `icons` | Icon mappings in the same format as `IconsOverrideFile`; entries here take precedence over the separate file |
| `apps.<name>.url` | Full web URL for the app; takes precedence over the auto-detected one |
| `apps.<name>.icon` | Icon for the app (URL or data URI); takes precedence over the `dockerui.icon` label and the catalog |
| `apps.<name>.hidden` | Removes the app from the dashboard |
| `order` | Apps listed here come first, in this order; everything else follows alphabetically |

The file is optional: when it is missing, defaults are used. When it cannot
be parsed, the error is logged and defaults are used, so a typo can never
take the dashboard down.

### App web URLs

Each card links to the app's web UI when one can be determined:

1. **`apps.<name>.url`** from the settings file, if configured.
2. **Auto-detected**: when one of the stack's running containers publishes a
   TCP port, the link uses the first published port from the common web
   ports (80, 8080, 3000, 8000, 5000, 8888, 9000, 9090, 5173, 4200, 443,
   8443) or the smallest one otherwise. The **host** is the one the
   dashboard itself is served under (the host of the current request, or
   `baseUrl` from the settings file), so the link works for whoever is
   browsing — port 80 becomes `http://host`, 443/8443 become `https://…`.
3. **No link**: when nothing is published, the card is not clickable.

### App icons

Icons are resolved in this order (first match wins):

1. **`apps.<name>.icon`** from the settings file (see above).
2. **The `dockerui.icon` label** on any container of the stack — any URL or
   data URI:

   ```yaml
   services:
     my-service:
       image: ...
       labels:
         dockerui.icon: "https://example.com/icon.png"
   ```

3. **Icon mappings from the settings file / `IconsOverrideFile`**, then
   **the built-in icon catalog**: each container's image (e.g.
   `linuxserver/jellyfin:10.9`) is matched against a mapping of images to
   icons that is synced from the
   [Umbrel app store](https://github.com/getumbrel/umbrel-apps-gallery)
   (`ui/public/icons/`, served at `/icons/`).
4. **The initials fallback**: the app's initials on a colored background.

To remap images to different icons without rebuilding, drop a file at
`./config/icons.json` (the volume is already mounted in `docker-compose.yml`):

```json
[
  { "image": "myregistry/whatever", "icon": "jellyfin.svg" },
  { "image": "postgres", "icon": "pi-hole.svg" }
]
```

`image` is matched against the normalized image name (tags and digests are
ignored), falling back to the last path segment; `icon` must be a file that
exists in `ui/public/icons/`. To refresh the catalog after new apps land in
the Umbrel store, run `yarn --cwd ui icons:sync` and commit the generated
files.

## Development

Two terminals:

```bash
# 1. API on http://localhost:5000 (uses DockerUi:DockerSocketPath from appsettings.Development.json)
dotnet run --project api/src/Api

# 2. UI on http://localhost:3000 (proxies /api to the API, including the SignalR web socket)
cd ui && yarn && yarn dev
```

### Tests

```bash
# API (unit + integration; integration tests use a nonexistent socket, no daemon needed)
dotnet test api/tests/UnitTests
dotnet test api/tests/IntegrationTests

# UI (type check, lint, unit tests, build)
cd ui
yarn check-types
yarn lint
yarn test --run
yarn build
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
├── api/
│   └── src/
│       ├── Api/               # Minimal API: features, endpoints, hub, DI, Program
│       ├── Core/              # Small shared helpers
│       ├── CrossCutting/      # Settings + DI configurator
│       ├── UnitTests/         # AppCatalog grouping logic
│       └── IntegrationTests/  # Endpoints against WebApplicationFactory
├── ui/                        # Vite + React SPA (served by the API in prod)
├── docker-ui.slnx
├── Directory.Build.props      # net10.0, central package management, analysis
├── Directory.Packages.props
├── Dockerfile                 # Multi-stage: UI build → API publish → runtime
└── docker-compose.yml
```
