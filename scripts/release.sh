#!/usr/bin/env bash
# Drafts a release of Soovioo and of its backoffice with the same version
# (GitHub draft releases). Edit the notes on GitHub and publish them: the
# "Release" workflow of each repository then puts the commit into production.
#
#   scripts/release.sh [-v RR.M.N] [-c <commit>] [-b <backoffice commit>] [-n]
#
#   -v  version; default: the next one of this month (26.11.0, 26.11.1, …)
#   -c  commit of the app; default: the one running on the test instance
#   -b  commit of the backoffice; default: its newest one deployed to test
#   -n  only print the notes, create nothing
#
# Needs: gh (logged in), jq, curl, and claude (Claude Code) for the summary
# of the changes; without claude the notes are just the list of pull requests.
set -euo pipefail

APP_REPO=fidlip/soovioo
BACKOFFICE_REPO=fidlip/soovioo-backoffice
TEST_URL=https://test.soovioo.cloud

version='' commit='' backoffice_commit='' dry_run=''
while getopts 'v:c:b:n' opt; do
  case "$opt" in
    v) version="${OPTARG#v}" ;;
    c) commit="$OPTARG" ;;
    b) backoffice_commit="$OPTARG" ;;
    n) dry_run=1 ;;
    *) sed -n '2,13p' "$0" >&2; exit 2 ;;
  esac
done

die() { echo "release: $*" >&2; exit 1; }
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

full_sha() { gh api "repos/$1/commits/$2" --jq .sha; }

# The commit is in main and its CI on main (tests, images, test instance) passed.
check_commit() {
  local repo=$1 sha=$2 status passed
  status=$(gh api "repos/$repo/compare/$sha...main" --jq .status)
  [ "$status" = ahead ] || [ "$status" = identical ] || die "$repo: $sha is not in main"
  passed=$(gh api "repos/$repo/actions/runs?head_sha=$sha&event=push&branch=main" \
    --jq '[.workflow_runs[] | select(.name == "CI" and .conclusion == "success")] | length')
  [ "$passed" != 0 ] || die "$repo: $sha has no successful CI run on main"
}

all_tags() { gh api --paginate "repos/$1/releases" --jq '.[].tag_name'; }

latest_release() {
  gh api "repos/$1/releases" --jq '[.[] | select((.draft or .prerelease) | not)][0].tag_name // empty'
}

# Pull requests merged into main between the previous release and the commit
# (the last 30 before it when there's no release yet): "number<TAB>title".
pull_requests() {
  local repo=$1 previous=$2 sha=$3
  if [ -n "$previous" ]; then
    gh api "repos/$repo/compare/$previous...$sha" --jq '.commits[].sha' |
      while read -r c; do
        gh api "repos/$repo/commits/$c/pulls" \
          --jq '.[] | select(.merged_at and .base.ref == "main") | "\(.number)\t\(.title)"'
      done | sort -n -u
  else
    local until
    until=$(gh api "repos/$repo/commits/$sha" --jq .commit.committer.date)
    gh pr list --repo "$repo" --state merged --base main --limit 100 \
      --json number,title,mergedAt \
      --jq "[.[] | select(.mergedAt <= \"$until\")] | sort_by(.mergedAt) | .[-30:] | .[] | \"\(.number)\t\(.title)\""
  fi
}

# The pull requests' titles and descriptions, for the summary.
describe() {
  local repo=$1
  while IFS=$'\t' read -r number _; do
    gh pr view "$number" --repo "$repo" --json number,title,body \
      --jq '"### #\(.number) \(.title)\n\(.body[0:1500])\n"'
  done
}

# Release notes in Czech from the users' side, by Claude Code.
summarize() {
  local what=$1 input=$2
  command -v claude >/dev/null || return 0
  claude -p "Napiš česky poznámky k vydání $what pro správce škol a operátory. \
Vstup jsou pull requesty, které vydání obsahuje. Piš z pohledu uživatele, ne vývojáře: \
co je nového, co se opravilo, co se změnilo v ovládání. Seskup do sekcí Novinky, Opravy \
a Ostatní (jen sekce, které mají obsah), krátké odrážky, ke každé číslo PR (#123). \
Čistě vývojářské věci (CI, e2e testy, refaktoring, dokumentace) shrň do jedné odrážky \
v Ostatní, nebo je vynech. Nic si nevymýšlej. Vypiš jen markdown poznámek, bez úvodu." \
    < "$input" || echo "_(Shrnutí se nepovedlo vygenerovat.)_"
}

# --- the app -------------------------------------------------------------

if [ -z "$commit" ]; then
  commit=$(curl -fsS "$TEST_URL/version.json" | jq -r '.commit // empty') ||
    die "can't read the commit running on $TEST_URL"
  [ -n "$commit" ] || die "$TEST_URL/version.json names no commit"
fi
commit=$(full_sha "$APP_REPO" "$commit")
check_commit "$APP_REPO" "$commit"

app_tags=$(all_tags "$APP_REPO")
if [ -z "$version" ]; then
  month=$(TZ=Europe/Prague date +%y.%-m)
  last=$(echo "$app_tags" | sed -n "s/^v${month//./\\.}\.\([0-9]*\)$/\1/p" | sort -n | tail -1)
  version="$month.$(( ${last:--1} + 1 ))"
fi
echo "$version" | grep -Eq '^[0-9]{2}\.[0-9]{1,2}\.[0-9]+$' || die "$version is not RR.M.N"
tag="v$version"
echo "$app_tags" | grep -qx "$tag" && die "$APP_REPO already has $tag (perhaps a draft)"

previous=$(latest_release "$APP_REPO")
pull_requests "$APP_REPO" "$previous" "$commit" > "$work/app-prs"
describe "$APP_REPO" < "$work/app-prs" > "$work/app-input"
{
  summarize "Soovioo $version" "$work/app-input"
  echo
  echo "<details><summary>Pull requesty${previous:+ od $previous}</summary>"
  echo
  sed 's/^\([0-9]*\)\t\(.*\)$/- #\1 \2/' "$work/app-prs"
  echo
  echo "</details>"
  echo
  echo "Commit: $commit"
} > "$work/app-notes"

# --- the backoffice: the same version ------------------------------------

if [ -z "$backoffice_commit" ]; then
  backoffice_commit=$(gh run list --repo "$BACKOFFICE_REPO" --workflow CI --branch main \
    --event push --status success --limit 1 --json headSha --jq '.[0].headSha // empty')
  [ -n "$backoffice_commit" ] || die "$BACKOFFICE_REPO: no successful CI run on main"
fi
backoffice_commit=$(full_sha "$BACKOFFICE_REPO" "$backoffice_commit")
check_commit "$BACKOFFICE_REPO" "$backoffice_commit"
all_tags "$BACKOFFICE_REPO" | grep -qx "$tag" && die "$BACKOFFICE_REPO already has $tag (perhaps a draft)"

protocol=$(gh api "repos/$APP_REPO/contents/backend/src/Soovioo.Api/Backoffice/BackofficeClient.cs?ref=$commit" \
  --jq .content | base64 -d | sed -n 's/.*ProtocolVersion = \([0-9]*\);.*/\1/p')
verified="Ověřeno napojení na Soovioo $version (komunikační protokol verze ${protocol:-?})."

backoffice_previous=$(latest_release "$BACKOFFICE_REPO")
pull_requests "$BACKOFFICE_REPO" "$backoffice_previous" "$backoffice_commit" > "$work/bo-prs"
{
  if [ -n "$backoffice_previous" ] && [ ! -s "$work/bo-prs" ]; then
    echo "Beze změn. $verified"
  else
    describe "$BACKOFFICE_REPO" < "$work/bo-prs" > "$work/bo-input"
    summarize "backoffice Soovioo $version" "$work/bo-input"
    echo
    echo "$verified"
    echo
    echo "<details><summary>Pull requesty${backoffice_previous:+ od $backoffice_previous}</summary>"
    echo
    sed 's/^\([0-9]*\)\t\(.*\)$/- #\1 \2/' "$work/bo-prs"
    echo
    echo "</details>"
  fi
  echo
  echo "Commit: $backoffice_commit"
} > "$work/bo-notes"

if [ -n "$dry_run" ]; then
  echo "=== $APP_REPO $tag @ $commit"
  cat "$work/app-notes"
  echo
  echo "=== $BACKOFFICE_REPO $tag @ $backoffice_commit"
  cat "$work/bo-notes"
  exit 0
fi

gh release create "$tag" --repo "$APP_REPO" --draft --target "$commit" \
  --title "Soovioo $version" --notes-file "$work/app-notes"
gh release create "$tag" --repo "$BACKOFFICE_REPO" --draft --target "$backoffice_commit" \
  --title "Soovioo backoffice $version" --notes-file "$work/bo-notes"
echo
echo "Drafts ready — edit the notes and publish (the app and the backoffice separately):"
echo "  https://github.com/$APP_REPO/releases"
echo "  https://github.com/$BACKOFFICE_REPO/releases"
