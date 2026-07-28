#!/usr/bin/env bash
# run-dev.sh — Start backend (identity-service) + frontend (Vite) locally.
#
# Prerequisites:
#   - Python 3.14 + uv (uv sync --all-packages --all-groups)
#   - Node >=24 + pnpm >=11 (pnpm install)
#   - PostgreSQL running on 127.0.0.1:5432 with database "identity"
#     (user: worker, password: worker — see IDENTITY_DATABASE_URL below)
#
# Override env vars:
#   IDENTITY_DATABASE_URL       default: postgresql+asyncpg://worker:worker@127.0.0.1:5432/identity
#   IDENTITY_PORT               default: 8001
#   IDENTITY_JWT_SECRET          default: dev-only-secret
#   IDENTITY_CORS_ORIGINS        default: http://localhost:5173,http://127.0.0.1:5173
#   VITE_API_BASE_URL            default: http://localhost:8001

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# ---- backend ----
DB_URL="${IDENTITY_DATABASE_URL:-postgresql+asyncpg://worker:worker@127.0.0.1:5432/identity}"

echo "==> Starting identity-service on port ${IDENTITY_PORT:-8001}"
echo "    Database: $DB_URL"

WORKER_DATABASE_URL="$DB_URL" \
  IDENTITY_JWT_SECRET="${IDENTITY_JWT_SECRET:-dev-only-secret-change-me-in-production-32bytes}" \
  WORKER_CORS_ALLOW_ORIGINS="${IDENTITY_CORS_ORIGINS:-http://localhost:5173,http://127.0.0.1:5173}" \
  uv run -p identity-identity-service python -c "
from identity_service.main import run
run()
" &

BACKEND_PID=$!
trap 'kill $BACKEND_PID 2>/dev/null; exit' INT TERM

sleep 2

# ---- frontend ----
echo "==> Starting Vite dev server (port 5173)"
export VITE_API_BASE_URL="${VITE_API_BASE_URL:-http://localhost:8001}"

pnpm dev

wait $BACKEND_PID