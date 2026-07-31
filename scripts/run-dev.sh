#!/usr/bin/env bash
# run-dev.sh — bring up the local stack: Postgres, both backend services, Vite.
#
# Prerequisites:
#   - Python 3.14 + uv          (uv sync --all-packages --all-groups)
#   - Node >=24 + pnpm >=11     (pnpm install)
#   - Docker                    (for the Postgres container)
#
# Everything is configured through WORKER_-prefixed variables, because that is
# the env_prefix pydantic-settings reads (worker_platform.configuration).
# A variable without that prefix is silently ignored — this script used to set
# IDENTITY_JWT_SECRET, which never reached the service at all.
#
# Override:
#   SKIP_COMPOSE=1              use an already-running Postgres instead
#   WORKER_JWT_SECRET=...       shared HS256 secret (identity signs, others verify)
#   IDENTITY_PORT / CONSENT_PORT
#   VITE_API_BASE_URL

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

PGHOST="${PGHOST:-127.0.0.1}"
PGPORT="${PGPORT:-5432}"
IDENTITY_PORT="${IDENTITY_PORT:-8001}"
CONSENT_PORT="${CONSENT_PORT:-8002}"

# identity-service mints the tokens and consent-service verifies them, so both
# processes must see the SAME secret (ADR-0007 — one trust domain until the
# gateway lands in Phase 10).
export WORKER_JWT_SECRET="${WORKER_JWT_SECRET:-dev-only-secret-change-me-in-production-32bytes}"
export WORKER_CORS_ALLOW_ORIGINS="${WORKER_CORS_ALLOW_ORIGINS:-http://localhost:5173,http://127.0.0.1:5173}"

IDENTITY_DB="postgresql+asyncpg://worker:worker@${PGHOST}:${PGPORT}/identity"
CONSENT_DB="postgresql+asyncpg://worker:worker@${PGHOST}:${PGPORT}/consent"

pids=()
cleanup() {
  for pid in "${pids[@]:-}"; do
    [ -n "$pid" ] && kill "$pid" 2>/dev/null || true
  done
}
trap cleanup INT TERM EXIT

# ---- database ----
if [ "${SKIP_COMPOSE:-0}" != "1" ]; then
  echo "==> Starting Postgres (docker compose)"
  docker compose up -d postgres

  echo "==> Waiting for Postgres to accept connections"
  for _ in $(seq 1 60); do
    if docker compose exec -T postgres pg_isready -U worker -d identity >/dev/null 2>&1; then
      break
    fi
    sleep 1
  done
  docker compose exec -T postgres pg_isready -U worker -d identity >/dev/null 2>&1 || {
    echo "Postgres did not become ready in time" >&2
    exit 1
  }
fi

# ---- migrations ----
# Per-service Alembic (ADR-0010): each service owns its own history and runs it
# against its own database. Doing this before the services start means a fresh
# clone is usable without any manual psql work.
run_migrations() {
  local service="$1" url="$2"
  if [ -d "apps/${service}/migrations/versions" ] &&
     [ -n "$(ls -A "apps/${service}/migrations/versions" 2>/dev/null)" ]; then
    echo "==> Migrating ${service}"
    (cd "apps/${service}" && WORKER_DATABASE_URL="$url" uv run alembic upgrade head)
  else
    echo "==> Skipping ${service} migrations (no revisions yet)"
  fi
}

run_migrations identity-service "$IDENTITY_DB"
run_migrations consent-service "$CONSENT_DB"

# ---- backend ----
echo "==> identity-service on :${IDENTITY_PORT}"
WORKER_DATABASE_URL="$IDENTITY_DB" WORKER_PORT="$IDENTITY_PORT" uv run worker-identity &
pids+=($!)

echo "==> consent-service on :${CONSENT_PORT}"
WORKER_DATABASE_URL="$CONSENT_DB" WORKER_PORT="$CONSENT_PORT" uv run worker-consent &
pids+=($!)

sleep 2

# ---- frontend ----
echo "==> Vite dev server on :5173"
export VITE_API_BASE_URL="${VITE_API_BASE_URL:-http://localhost:${IDENTITY_PORT}}"
pnpm dev
