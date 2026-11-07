#!/usr/bin/env bash
# Seed for the "03-category-archive" mockup (verify-phase step 4, group W0). Idempotent; drives the app through its own UI.
# Contract: TF_BASE = app address; exit 0 when the app is in the drawn state. Prints TF_ROUTE lines for parameter values.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TF_BASE="${TF_BASE:-http://localhost:5450}"
exec node "$HERE/_content.mjs" seed post:markdown post:programming --print-route
