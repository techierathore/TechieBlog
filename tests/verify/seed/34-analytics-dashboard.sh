#!/usr/bin/env bash
# Seed for the "34-analytics-dashboard" mockup (REQ-UI-044, REQ-UI-067, REQ-UI-070, REQ-UI-071).
# Contract: TF_BASE = app address; exit 0 when the app is in the drawn state. Idempotent enough:
# a repeat run adds at most one view per referring site per day (the app's own 24-hour de-duplication).
#
# Drives the app through HTTP only, exactly as readers do: it opens the first two published posts
# linked from the home page, once from each of two other sites and once with no referring address,
# so the "Where readers came from" card has a named site and a "Direct or unknown" row.
#
# Ratings cannot be created this way (the public form needs a captcha answer and an emailed
# confirmation), so the rated-post state the Rating cell and the ratings dialog draw comes from the
# build phase's test-database seed of 2026-11-21 (one confirmed and one unconfirmed rating on the
# post "fxv0-markdown-showcase-3"), recorded on REQ-UI-067 in docs/TechieBlog-Checklist.md.
set -euo pipefail
BASE="${TF_BASE:-http://localhost:5399}"
mapfile -t POSTS < <(curl -fsS "$BASE/" | grep -oE 'href="/post/[^"]+"' | sed -E 's/^href="//; s/"$//' | sort -u | head -2)
[ "${#POSTS[@]}" -gt 0 ] || { echo "seed 34: no published post is linked from the home page" >&2; exit 1; }
for post in "${POSTS[@]}"; do
  curl -fsS -o /dev/null -A "Seed34-google" -H 'Referer: https://www.google.com/search?q=seed' "$BASE$post"
  curl -fsS -o /dev/null -A "Seed34-linkedin" -H 'Referer: https://www.linkedin.com/feed/' "$BASE$post"
  curl -fsS -o /dev/null -A "Seed34-direct" "$BASE$post"
done
echo "TF_ROUTE /admin/analytics"
