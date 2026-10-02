#!/usr/bin/env bash
set -euo pipefail

echo "========================================="
echo "  CI - docker-ui"
echo "========================================="

echo
echo "API: restore, build and test (Release)."
dotnet test api/src/UnitTests --configuration Release
dotnet test api/src/IntegrationTests --configuration Release
echo "API tests passed"

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
echo "UI: unit tests."
yarn test --run

echo
echo "UI: production build."
yarn build

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
