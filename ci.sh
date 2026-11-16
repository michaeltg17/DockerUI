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
find e2e/scenarios lighthouse -name appsettings.json -exec chown 1000:1000 {} +

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
echo "Lighthouse: bringing up the lighthouse scenario."
# The e2e suite tore down its scenarios, so the audit gets a fresh instance of
# the lighthouse/ scenario (dashboard + demo stacks) with real content on the
# page.
for stack in lighthouse/stacks/*/; do
  docker compose -f "${stack}docker-compose.yml" up -d
done
docker compose -f lighthouse/docker-compose.yml up -d

echo
echo "Lighthouse: waiting for the dashboard to become ready."
ready=
for _ in $(seq 1 60); do
  if curl -fs http://127.0.0.1:5015/health/live > /dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 2
done
if [ -z "${ready}" ]; then
  docker compose -f lighthouse/docker-compose.yml logs
  echo "Lighthouse: the lighthouse scenario dashboard did not become ready" >&2
  exit 1
fi

lighthouse_exit=0
npx --yes @lhci/cli@0.16.0 autorun --config=lighthouserc.cjs || lighthouse_exit=$?

echo
echo "Lighthouse: tearing down the lighthouse scenario."
docker compose -f lighthouse/docker-compose.yml down --remove-orphans
for stack in lighthouse/stacks/*/; do
  docker compose -f "${stack}docker-compose.yml" down --remove-orphans
done

if [ "${lighthouse_exit}" -ne 0 ]; then
  echo "Lighthouse: the audit failed (exit ${lighthouse_exit})" >&2
  exit "${lighthouse_exit}"
fi

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
