#!/usr/bin/env bash
set -euo pipefail

echo "========================================="
echo "  CI - docker-ui"
echo "========================================="

echo
echo "API: restore, build and test (Release)."
# Exit code 8 = no tests discovered (the test projects may be temporarily empty).
# That is tolerated; real test failures (2) and infrastructure errors (10) still fail CI.
dotnet test api/tests/UnitTests --configuration Release --ignore-exit-code 8
dotnet test api/tests/IntegrationTests --configuration Release --ignore-exit-code 8
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
