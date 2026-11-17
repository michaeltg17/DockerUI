#!/usr/bin/env bash
set -euo pipefail

echo "========================================="
echo "  CI - DockerUI"
echo "========================================="

# SonarQube Cloud analysis wraps the build steps (the C# analysis rides on the
# dotnet builds, ui/**.ts(x) is picked up by multi-language analysis). Without
# SONAR_TOKEN the pipeline behaves exactly as before. The SonarCloud GitHub
# app already analyzes pull requests (decoration + quality-gate check), so the
# scanner only runs the branch analyses (main, or sonar.branch.name branches).
SONAR_TOKEN="${SONAR_TOKEN:-}"
SONAR_ORG="michaeltg17"
SONAR_PROJECT_KEY="michaeltg17_DockerUI"
SONAR_EVENT="${SONAR_EVENT:-}"
# The checkout is bind-mounted, so its owner differs from the container's root
# (safe.directory keeps the branch fallback working for local runs).
git config --global --add safe.directory "$(pwd)" >/dev/null 2>&1 || true
SONAR_BRANCH="${SONAR_BRANCH:-$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)}"

sonar_enabled() {
  [[ -n "${SONAR_TOKEN}" ]] && [[ "${SONAR_EVENT}" != "pull_request" ]] && [[ -n "${SONAR_BRANCH}" && "${SONAR_BRANCH}" != "HEAD" ]]
}

if sonar_enabled; then
  echo
  echo "SonarQube Cloud: starting analysis of branch ${SONAR_BRANCH} (michaeltg17/${SONAR_PROJECT_KEY})."
  branch_args=()
  gate_url="https://sonarcloud.io/api/qualitygates/project_status?projectKey=${SONAR_PROJECT_KEY}"
  if [[ "${SONAR_BRANCH}" != "main" ]]; then
    branch_args+=("/d:sonar.branch.name=${SONAR_BRANCH}")
    gate_url+="&branch=${SONAR_BRANCH}"
  fi
  dotnet sonarscanner begin \
    /k:"${SONAR_PROJECT_KEY}" /o:"${SONAR_ORG}" \
    /d:sonar.token="${SONAR_TOKEN}" \
    /d:sonar.exclusions="ui/node_modules/**,ui/dist/**,ui/coverage/**,**/bin/**,**/obj/**" \
    "${branch_args[@]}"
else
  echo
  echo "Skipping Sonar analysis (needs SONAR_TOKEN, and a named branch; PRs are analyzed by the SonarCloud GitHub app)."
fi

echo
echo "API: building (Release)."
dotnet build api/DockerUI.slnx --configuration Release
echo "API build passed"

echo
echo "UI: installing dependencies."
cd ui
npm ci --no-audit --no-fund --ignore-scripts

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
if [[ -z "${ready}" ]]; then
  docker compose -f lighthouse/docker-compose.yml logs
  echo "Lighthouse: the lighthouse scenario dashboard did not become ready" >&2
  exit 1
fi

lighthouse_exit=0
# The explicit steps instead of `autorun`: autorun skips the upload step when the
# assertions fail, which is exactly when the reports are needed. upload with the
# filesystem target is what writes the report json/html into .lighthouseci.
# `lhci` is the pinned @lhci/cli the CI image installs (no npx at run time).
lhci collect --config=lighthouserc.cjs || lighthouse_exit=$?
lhci assert --config=lighthouserc.cjs || lighthouse_exit=$?
lhci upload --target=filesystem --outputDir=.lighthouseci --config=lighthouserc.cjs || true

echo
# Persist a score/metric summary next to the reports so the CI artifact shows
# the numbers at a glance. The directory is in .gitignore, so this never
# touches git.
echo "Lighthouse: summarizing the audit reports."
if compgen -G ".lighthouseci/*.report.json" > /dev/null; then
  lhr=$(ls -t .lighthouseci/*.report.json | head -1)
  node scripts/summarize-lighthouse.mjs "$lhr" .lighthouseci/audit-summary.json
fi

echo "Lighthouse: tearing down the lighthouse scenario."
docker compose -f lighthouse/docker-compose.yml down --remove-orphans
for stack in lighthouse/stacks/*/; do
  docker compose -f "${stack}docker-compose.yml" down --remove-orphans
done

# The sonar window closes after every build that feeds it, even when the
# Lighthouse audit failed - the upload and quality gate still stand.
if sonar_enabled; then
  echo
  echo "SonarQube Cloud: uploading analysis results."
  sonar_end_log="$(mktemp)"
  sonar_skipped=0
  if ! dotnet sonarscanner end /d:sonar.token="${SONAR_TOKEN}" 2>&1 | tee "${sonar_end_log}"; then
    if grep -q "Automatic Analysis is enabled" "${sonar_end_log}"; then
      # The GitHub app's Automatic Analysis owns this project, so SonarCloud
      # rejects manual uploads until it is turned off in the project's
      # Administration > Analysis Method; don't fail the build over that.
      echo "SonarQube Cloud: skipped - the project has Automatic Analysis enabled (GitHub app); turn it off in the project's Administration > Analysis Method to run CI-side analysis." >&2
      sonar_skipped=1
    else
      exit 1
    fi
  fi
  rm -f "${sonar_end_log}"

  if [[ "${sonar_skipped}" -eq 0 ]]; then
    echo
    echo "SonarQube Cloud: waiting for the quality gate."
    gate_status=
    for _ in $(seq 1 36); do   # up to ~6 min
      gate_status=$(curl -sf -H "Authorization: Bearer ${SONAR_TOKEN}" "${gate_url}" \
        | jq -r '.projectStatus.status')
      case "${gate_status}" in
        OK)
          echo "Quality gate: OK"
          break
          ;;
        ERROR)
          echo "Quality gate: FAILED" >&2
          curl -sf -H "Authorization: Bearer ${SONAR_TOKEN}" "${gate_url}" \
            | jq -r '.projectStatus.conditions[] | select(.status=="ERROR") | "  - " + .status + " " + (.metricKey // "?")' >&2
          exit 1
          ;;
        *) sleep 10 ;;   # IN_PROGRESS / UNAVAILABLE
      esac
    done
    if [[ "${gate_status}" != "OK" ]]; then
      echo "Quality gate: still ${gate_status:-unknown} after the wait window" >&2
      exit 1
    fi
  fi
fi

if [[ "${lighthouse_exit}" -ne 0 ]]; then
  echo "Lighthouse: the audit failed (exit ${lighthouse_exit})" >&2
  exit "${lighthouse_exit}"
fi

echo
echo "========================================="
echo "  All CI checks passed!"
echo "========================================="
