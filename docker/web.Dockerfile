# syntax=docker/dockerfile:1
#
# The Vite dev server for web/. Debian-based rather than Alpine on purpose:
# esbuild and rollup ship platform-specific binaries, and the musl variants are
# a recurring source of "works on my machine" in this exact spot.

FROM node:25-bookworm-slim

# corepack picks the pnpm version pinned in web/package.json
# ("packageManager": "pnpm@…"), so the container cannot drift from CI.
#
# From node 25 on, corepack is NO LONGER in the image — Node took it out of the
# distribution. A bare `corepack enable` aborts there with `corepack: not
# found` (exit 127), and only when the image is built: neither `make check` nor
# CI catch that, because no image is built there.
#
# Installed afterwards instead of replaced: fetching pnpm directly via
# `npm i -g pnpm@x.y.z` would be shorter, but would pin the version a SECOND
# time — beside `packageManager` in package.json. Two places for the same
# number drift apart, and that is exactly what this line protects against.
#
# `--force` is no brute force here but necessary: the node image still ships
# yarn shims (`/usr/local/bin/yarnpkg`), and corepack creates the same names.
# Without `--force` the installation aborts with `EEXIST`. Only these shims are
# overwritten — the pnpm version comes unchanged from `packageManager`.
RUN npm install --global --force corepack@latest && corepack enable

WORKDIR /app

# Only the manifest first: this layer only rebuilds when a dependency actually
# changes, not on every source edit.
#
# Three files used to stand here, because a pnpm workspace coordinated two
# packages. There is only this one left — the workspace is gone with that, and
# with it turbo, the root package.json and a 400 MB node_modules at the root.
COPY web/package.json web/pnpm-lock.yaml ./

RUN pnpm install --frozen-lockfile

COPY web/ /app

EXPOSE 5173

# --host 0.0.0.0: Vite binds to localhost by default, which inside a container
# means "unreachable from the host" — the published port would answer nothing.
CMD ["pnpm", "dev", "--host", "0.0.0.0"]
