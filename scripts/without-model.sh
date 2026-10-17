#!/usr/bin/env bash
# Runs a command against a stack WITHOUT the platform's model, as CI has it.
#
# CI's .env comes from .env.example, where the key is empty: no platform model.
# The E2E journeys and the route map are written against that — "kein
# Sprachmodell eingerichtet", `POST /profiles/me/draft` 503. Locally the model
# is usually on (the local Ollama, ADR-0051), and both would measure something
# else than CI. So the AI services are restarted without the key for the
# command and restarted with it afterwards — also when it fails or is
# interrupted.
#
#   scripts/without-model.sh ./scripts/route-map.sh
set -euo pipefail

cd "$(dirname "$0")/.."

AI_SERVICES=(profile-service jobs-service applications-service scout-service messages-service)

has_model() {
  [ -f .env ] && grep -qE '^WORKERTRANSFER_AI_KEY=.+' .env
}

wait_healthy() {
  for service in "${AI_SERVICES[@]}"; do
    container="workertransfer-${service%-service}"
    for _ in $(seq 1 60); do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$container" 2>/dev/null)" = healthy ] && break
      sleep 2
    done
  done
}

restart_ai() {
  docker compose up -d --no-build "${AI_SERVICES[@]}" >/dev/null 2>&1
  wait_healthy
}

if has_model; then
  printf '\033[2mThe platform model is switched off for this run, as in CI ...\033[0m\n'
  # The shell beats .env in compose's interpolation: empty here is empty there.
  WORKERTRANSFER_AI_KEY='' restart_ai
  trap 'printf "\033[2mSwitching the platform model back on ...\033[0m\n"; restart_ai' EXIT
fi

"$@"
