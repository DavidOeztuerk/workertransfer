#!/usr/bin/env sh
# Migrate, then serve. Each service owns its own Alembic history and its own
# database (ADR-0010 / ADR-0004), so every container migrates only itself —
# there is no central migration step that would need to know all services.
#
# This is what makes `docker compose up` sufficient on a fresh clone: no
# separate script, no manual psql, no ordering to remember.
set -eu

if [ -z "${SERVICE_DIR:-}" ]; then
  echo "entrypoint: SERVICE_DIR is not set (build arg missing)" >&2
  exit 1
fi

MIGRATIONS="/app/apps/${SERVICE_DIR}/migrations/versions"

if [ -d "$MIGRATIONS" ] && [ -n "$(ls -A "$MIGRATIONS" 2>/dev/null)" ]; then
  echo "==> ${SERVICE_DIR}: alembic upgrade head"
  # Alembic reads WORKER_DATABASE_URL (see each service's migrations/env.py),
  # which compose sets per service.
  cd "/app/apps/${SERVICE_DIR}" && alembic upgrade head
  cd /app
else
  echo "==> ${SERVICE_DIR}: no revisions yet, skipping migrations"
fi

exec "$@"
