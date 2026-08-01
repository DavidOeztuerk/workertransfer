# syntax=docker/dockerfile:1
#
# One image definition for every Python service. A new service needs no new
# Dockerfile — only a compose block that sets SERVICE_DIR and a command
# (see docker-compose.yml).
#
# The environment lives at /opt/venv, deliberately outside /app: compose
# bind-mounts the repository over /app for hot reload, and a venv inside that
# path would be shadowed by the host's (wrong platform, wrong contents) the
# moment the container starts.

FROM python:3.14-slim AS base

# uv is the package manager for this repo (never pip/poetry — see CLAUDE.md).
COPY --from=ghcr.io/astral-sh/uv:latest /uv /uvx /bin/

ENV UV_PROJECT_ENVIRONMENT=/opt/venv \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy \
    PATH="/opt/venv/bin:$PATH" \
    PYTHONUNBUFFERED=1

WORKDIR /app

# The whole workspace is copied before syncing because uv resolves every member
# from the root pyproject; copying manifests alone would need each member's
# pyproject.toml listed by hand and would rot the moment a package is added.
COPY . /app

# --frozen: build from uv.lock exactly, never silently re-resolve.
# Runtime deps only — uvicorn and alembic are runtime deps of the services, so
# no dev group is needed to serve or to migrate.
RUN uv sync --all-packages --frozen --no-dev

# Which apps/<dir> owns the Alembic history this container should apply.
ARG SERVICE_DIR
ENV SERVICE_DIR=${SERVICE_DIR}

COPY docker/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
