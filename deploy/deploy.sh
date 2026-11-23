#!/bin/sh
# Run on the server by .github/workflows/deploy.yml:
#   echo <registry token> | APP_VERSION=<release> sh deploy.sh <image tag> <registry user>
# APP_VERSION is the release (RR.M.N) on production, empty on the test
# instance. Rollback by hand to an earlier release's images (still on the
# server, or pullable after `docker login ghcr.io`):
#   APP_VERSION=26.11.0 sh deploy.sh <its commit>
set -eu
cd "$(dirname "$0")"

# One deploy at a time on the server, of any stack (app, backoffice, their
# test instances): parallel pulls and image prunes break each other.
exec 9>/run/lock/soovioo-deploy.lock
flock 9
tag="$1"

if [ $# -ge 2 ]; then
  docker login ghcr.io -u "$2" --password-stdin
  trap 'docker logout ghcr.io >/dev/null' EXIT
fi

# Pin the tag in .env so a manual `docker compose ...` later uses the same images.
sed -i '/^IMAGE_TAG=/d; /^APP_VERSION=/d' .env
echo "IMAGE_TAG=$tag" >> .env
echo "APP_VERSION=${APP_VERSION:-}" >> .env

# Caddy's sites of other stacks (Caddyfile); created here so it is ours, not root's.
mkdir -p sites

docker compose pull api web
docker compose up -d --remove-orphans
# Old images of the past week stay around for a quick rollback.
docker image prune -af --filter until=168h
