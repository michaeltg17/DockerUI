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
echo "E2E: running scenarios (docker daemon required)."
dotnet e2e/bin/Release/net10.0/E2E.dll

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
