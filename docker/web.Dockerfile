# syntax=docker/dockerfile:1
#
# The Vite dev server for web/. Debian-based rather than Alpine on purpose:
# esbuild and rollup ship platform-specific binaries, and the musl variants are
# a recurring source of "works on my machine" in this exact spot.

FROM node:25-bookworm-slim

# corepack picks the pnpm version pinned in the root package.json
# ("packageManager": "pnpm@…"), so the container cannot drift from CI.
#
# Ab node 25 ist corepack NICHT mehr im Image enthalten — Node hat es aus der
# Distribution genommen. Ein blankes `corepack enable` bricht dort mit
# `corepack: not found` (exit 127) ab, und zwar erst beim Bauen des Images:
# weder `make check` noch die CI fangen das, weil dort kein Image gebaut wird.
#
# Nachinstalliert statt ersetzt: pnpm direkt über `npm i -g pnpm@x.y.z` zu
# holen wäre kürzer, würde die Version aber ein ZWEITES Mal festschreiben —
# neben `packageManager` in der package.json. Zwei Orte für dieselbe Zahl
# laufen auseinander, und genau davor schützt die Zeile hier.
#
# `--force` ist hier kein Draufhauen, sondern nötig: das node-Image bringt
# weiterhin yarn-Shims mit (`/usr/local/bin/yarnpkg`), und corepack legt
# dieselben Namen an. Ohne `--force` bricht die Installation mit `EEXIST` ab.
# Überschrieben werden ausschließlich diese Shims — die pnpm-Version kommt
# unverändert aus `packageManager`.
RUN npm install --global --force corepack@latest && corepack enable

WORKDIR /app

# Manifests first: this layer only rebuilds when a dependency actually changes,
# not on every source edit.
# Nur das Manifest: diese Schicht baut neu, wenn sich eine Abhaengigkeit
# aendert, nicht bei jeder Quelltextaenderung.
#
# Frueher standen hier drei Dateien, weil ein pnpm-Workspace zwei Pakete
# koordinierte. Es gibt nur noch dieses eine — der Workspace ist damit weg, und
# mit ihm turbo, die Wurzel-package.json und ein 400 MB grosses node_modules an
# der Wurzel.
COPY web/package.json web/pnpm-lock.yaml ./

RUN pnpm install --frozen-lockfile

COPY web/ /app

EXPOSE 5173

# --host 0.0.0.0: Vite binds to localhost by default, which inside a container
# means "unreachable from the host" — the published port would answer nothing.
CMD ["pnpm", "dev", "--host", "0.0.0.0"]
