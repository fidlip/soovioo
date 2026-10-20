# Web image: the Angular build served by Caddy, which is also the reverse
# proxy and TLS terminator (config: deploy/Caddyfile, mounted at runtime).
# Build context: frontend/ (see deploy/docker-compose.yml).

# Caddy with the Cloudflare DNS module: the wildcard certificate
# *.soovioo.cloud can only be issued through the DNS-01 challenge.
FROM caddy:2.11.6-builder-alpine AS caddy
RUN xcaddy build --with github.com/caddy-dns/cloudflare@v0.2.4

FROM node:24-bookworm-slim AS build
WORKDIR /src
ENV NX_DAEMON=false NX_NO_CLOUD=true NX_SKIP_NX_CACHE=true
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx nx build web --configuration=production

FROM caddy:2.11.6-alpine
COPY --from=caddy /usr/bin/caddy /usr/bin/caddy
COPY --from=build /src/dist/apps/web/browser /srv/app
