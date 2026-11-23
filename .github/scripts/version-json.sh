#!/usr/bin/env bash
# Prints version.json for the web image (shown to admins at the bottom of the
# sidebar): the version of the commit being deployed and the pull requests it
# contains.
#
# A release (RR.M.N) is only known when it's published, after the image is
# built, so "version" is a Caddy template: the release's APP_VERSION when the
# deploy sets it (production), otherwise the commit's date in Prague and its
# short hash, e.g. 2026.11.08-13e982d (the test instance).
#
# Needs: full history, GH_TOKEN (pull-requests: read), GITHUB_SHA,
# GITHUB_REPOSITORY, GITHUB_SERVER_URL.
set -euo pipefail

sha="$GITHUB_SHA"
version=$(TZ=Europe/Prague git show -s --format='%cd-%h' --abbrev=7 \
  --date=format-local:%Y.%m.%d "$sha")

# The newest pull requests merged into main that this commit contains.
prs=$(
  gh pr list --repo "$GITHUB_REPOSITORY" --state merged --base main --limit 40 \
    --json number,title,mergedAt,mergeCommit \
    --jq 'sort_by(.mergedAt) | reverse | .[] | "\(.mergeCommit.oid)\t\({number, title, mergedAt} | tojson)"' |
    {
      count=0
      while IFS=$'\t' read -r oid pr; do
        if [ "$count" -lt 10 ] && git merge-base --is-ancestor "$oid" "$sha" 2>/dev/null; then
          echo "$pr"
          count=$((count + 1))
        fi
      done
    } | jq -s .
)

jq -n \
  --arg version "{{or (env \`APP_VERSION\`) \`$version\`}}" \
  --arg commit "$sha" \
  --arg builtAt "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  --arg repository "$GITHUB_SERVER_URL/$GITHUB_REPOSITORY" \
  --argjson pullRequests "$prs" \
  '{version: $version, commit: $commit, builtAt: $builtAt, repository: $repository, pullRequests: $pullRequests}'
