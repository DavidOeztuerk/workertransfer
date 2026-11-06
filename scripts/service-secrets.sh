#!/usr/bin/env bash
# The services and how the secrets they hold alone come about (ADR-0058). Sourced
# by scripts/env-services.sh and scripts/k8s-render-check.sh, so that there is one
# list on this side. scripts/initdb/01-service-roles.sh has its own, because it is
# mounted alone; ServiceSecretTests and DatabaseRoleTests compare them with the tree.

SERVICE_DATABASES="advisor applications assessment companies consent github identity jobs messages notification portfolio profile resume scout transfer"

upper() {
  printf %s "$1" | tr '[:lower:]' '[:upper:]'
}

# Hex and not the base64 of the other secrets: `=`, `+` and `/` would otherwise
# stand in a connection string.
service_secret() {
  openssl rand -hex 32
}

# A service's evidence secret: what the environment sets, otherwise what .env holds.
# Called from the repository root.
evidence_secret_of() {
  local key value
  key="WORKERTRANSFER_EVIDENCE_SECRET_$(upper "$1")"
  value="${!key:-}"
  if [ -z "$value" ] && [ -f .env ]; then
    value=$(grep -E "^${key}=" .env | head -1 | cut -d= -f2- || true)
  fi
  printf %s "$value"
}
