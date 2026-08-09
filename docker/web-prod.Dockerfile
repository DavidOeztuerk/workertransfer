# syntax=docker/dockerfile:1
#
# Die Oberfläche als GEBAUTES Artefakt — das Gegenstück zu docker/web.Dockerfile,
# das den Vite-Dev-Server fährt.
#
# Beide existieren nebeneinander, weil sie verschiedene Fragen beantworten:
# der Dev-Server ist der schnellere Entwicklungsweg (Bind-Mount, Reload), dieses
# Image ist das, was ausgeliefert wird. Eine Staging-Umgebung, die den
# Dev-Server prüft, prüft das Falsche.

FROM node:25-bookworm-slim AS build

# Ab node 25 ist corepack nicht mehr im Image enthalten, und die yarn-Shims sind
# es noch — beides ist in docker/web.Dockerfile ausführlich begründet. Hier
# dieselbe Zeile aus demselben Grund.
RUN npm install --global --force corepack@latest && corepack enable

WORKDIR /app

COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY apps/web/package.json apps/web/
COPY packages/ui/package.json packages/ui/

RUN pnpm install --frozen-lockfile

COPY . /app

# Ohne gesetzte VITE_-Variablen — und das ist der Punkt. Sie würden hier fest
# in das Bündel eingesetzt und das Image an eine Umgebung binden. Die Adressen
# kommen zur Laufzeit aus /config.js (siehe apps/web/src/env.ts und ADR-0028).
RUN pnpm --filter @workertransfer/web build

FROM nginx:1.29-alpine AS runtime

COPY docker/web-nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/apps/web/dist /usr/share/nginx/html

EXPOSE 80

# nginx:alpine bringt seinen eigenen Startbefehl mit; er wird hier nur
# wiederholt, damit sichtbar ist, was läuft.
CMD ["nginx", "-g", "daemon off;"]
