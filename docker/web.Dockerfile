# syntax=docker/dockerfile:1
#
# The Vite dev server for apps/web. Debian-based rather than Alpine on purpose:
# esbuild and rollup ship platform-specific binaries, and the musl variants are
# a recurring source of "works on my machine" in this exact spot.

FROM node:25-bookworm-slim

# corepack picks the pnpm version pinned in the root package.json
# ("packageManager": "pnpm@…"), so the container cannot drift from CI.
RUN corepack enable

WORKDIR /app

# Manifests first: this layer only rebuilds when a dependency actually changes,
# not on every source edit.
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/
COPY packages/ui/package.json packages/ui/

RUN pnpm install --frozen-lockfile

COPY . /app

EXPOSE 5173

# --host 0.0.0.0: Vite binds to localhost by default, which inside a container
# means "unreachable from the host" — the published port would answer nothing.
CMD ["pnpm", "--filter", "@workertransfer/web", "dev", "--host", "0.0.0.0"]
