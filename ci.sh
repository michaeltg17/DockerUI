#!/usr/bin/env bash
set -euo pipefail

echo "========================================="
echo "  CI - docker-ui"
echo "========================================="

echo
echo "API: building (Release)."
dotnet build docker-ui.slnx --configuration Release
echo "API build passed"

echo
echo "UI: installing dependencies."
cd ui
yarn install --frozen-lockfile --non-interactive

echo
echo "UI: lint."
yarn lint

echo
echo "UI: type check."
yarn check-types

echo
echo "UI: production build."
yarn build
cd ..

echo
echo "E2E: building the test project (Release)."
dotnet build e2e/docker-ui.e2e.slnx --configuration Release

echo
echo "E2E: running scenarios (docker daemon required)."
dotnet e2e/DockerUI.E2ETests/bin/Release/net10.0/DockerUI.E2ETests.dll

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
