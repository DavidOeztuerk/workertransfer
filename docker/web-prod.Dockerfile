# syntax=docker/dockerfile:1
#
# The UI as a BUILT artefact — the counterpart to docker/web.Dockerfile, which
# runs the Vite dev server.
#
# Both exist side by side because they answer different questions: the dev
# server is the faster development path (bind mount, reload), this image is
# what is shipped. A staging environment that checks the dev server checks the
# wrong thing.

FROM node:25-bookworm-slim AS build

# From node 25 on, corepack is no longer in the image, and the yarn shims still
# are — both are explained at length in docker/web.Dockerfile. The same line
# here for the same reason.
RUN npm install --global --force corepack@latest && corepack enable

WORKDIR /app

COPY web/package.json web/pnpm-lock.yaml ./

RUN pnpm install --frozen-lockfile

COPY web/ /app

# Without VITE_ variables set — and that is the point. They would be baked into
# the bundle here and tie the image to one environment. The addresses come at
# runtime from /config.js (see web/src/env.ts and ADR-0028).
RUN pnpm build

FROM nginx:1.29-alpine AS runtime

COPY docker/web-nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

# nginx:alpine brings its own start command; it is only repeated here so it is
# visible what runs.
CMD ["nginx", "-g", "daemon off;"]
