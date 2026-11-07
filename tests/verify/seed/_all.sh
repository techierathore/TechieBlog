#!/usr/bin/env bash
# Runs every fxv0 piece once (no 120 s cap here). Run this BEFORE the per-screen seeds so each of those is a fast no-op check.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TF_BASE="${TF_BASE:-http://localhost:5450}"
exec node "$HERE/_content.mjs" seed post:markdown post:programming post:draft series comments subs newsletter resume media users verify --print-route
