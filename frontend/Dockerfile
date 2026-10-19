# Web image: the Angular build served by Caddy, which is also the reverse
# proxy and TLS terminator (config: deploy/Caddyfile, mounted at runtime).
# Build context: frontend/ (see deploy/docker-compose.yml).
FROM node:24-bookworm-slim AS build
WORKDIR /src
ENV NX_DAEMON=false NX_NO_CLOUD=true NX_SKIP_NX_CACHE=true
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx nx build web --configuration=production

FROM caddy:2.10-alpine
COPY --from=build /src/dist/apps/web/browser /srv/app
