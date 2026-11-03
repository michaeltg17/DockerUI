#!/usr/bin/env bash
set -euo pipefail

echo "========================================="
echo "  CI - DockerUI"
echo "========================================="

echo
echo "API: building (Release)."
dotnet build api/DockerUI.slnx --configuration Release
echo "API build passed"

echo
echo "UI: installing dependencies."
cd ui
npm ci --no-audit --no-fund

echo
echo "UI: lint."
npm run lint

echo
echo "UI: type check."
npm run check-types

echo
echo "UI: production build."
npm run build
cd ..

echo
echo "E2E: building the test project (Release)."
dotnet build e2e/DockerUIE2E.slnx --configuration Release

echo
echo "E2E: preparing scenario settings files for the non-root dashboard."
# The dashboard container runs as uid 1000 and rewrites the rw-mounted scenario
# appsettings.json in place; on the CI runner the checkout is owned by the
# runner user, so hand the files to the runtime user.
find e2e/scenarios -name appsettings.json -exec chown 1000:1000 {} +

echo
echo "E2E: detecting the daemon socket's group."
# The dashboard container runs as uid 1000 and reaches the socket through
# group access; the socket's group depends on the host (root on Docker
# Desktop, the docker group elsewhere, whose gid is assigned dynamically),
# so pass its gid to the scenario compose files (they fall back to 0, which
# covers root:root sockets).
export SOCKET_GROUP_GID="$(stat -c %g /var/run/docker.sock)"
echo "Daemon socket group gid: ${SOCKET_GROUP_GID}"

echo
echo "E2E: running scenarios (docker daemon required)."
dotnet e2e/bin/Release/net10.0/E2E.dll

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
