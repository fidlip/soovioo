#!/bin/sh
# Run on the server by .github/workflows/deploy.yml:
#   echo <registry token> | sh deploy.sh <image tag> <registry user>
# Rollback by hand to an earlier commit's images (still on the server, or
# pullable after `docker login ghcr.io`): sh deploy.sh <older sha>
set -eu
cd "$(dirname "$0")"
tag="$1"

if [ $# -ge 2 ]; then
  docker login ghcr.io -u "$2" --password-stdin
  trap 'docker logout ghcr.io >/dev/null' EXIT
fi

# Pin the tag in .env so a manual `docker compose ...` later uses the same images.
sed -i '/^IMAGE_TAG=/d' .env
echo "IMAGE_TAG=$tag" >> .env

docker compose pull api web
docker compose up -d --remove-orphans
# Old images of the past week stay around for a quick rollback.
docker image prune -af --filter until=168h
