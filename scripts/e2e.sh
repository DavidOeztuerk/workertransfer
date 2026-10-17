#!/usr/bin/env bash
# The Playwright journeys — against a stack without the platform's model, as
# CI runs them (scripts/without-model.sh says why).
#
# Arguments go to Playwright: scripts/e2e.sh e2e/transfer-journey.spec.ts
set -euo pipefail

cd "$(dirname "$0")/.."

exec ./scripts/without-model.sh bash -c 'cd web && pnpm e2e "$@"' _ "$@"
