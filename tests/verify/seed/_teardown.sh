#!/usr/bin/env bash
# Removes every fxv0 item through the admin UI. Posts, series, comments, resume rows and media are deleted.
# The app offers no remove for subscribers (consent evidence, REQ-FN-059) or sent newsletter issues: those are
# only deactivated / reported as TEARDOWN-LEFT lines.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export TF_BASE="${TF_BASE:-http://localhost:5450}"
exec node "$HERE/_content.mjs" teardown
