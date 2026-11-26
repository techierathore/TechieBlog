---
project: TechieBlog
last_updated: 2026-11-26
current_phase: Phase 1 of 1 (whole product) · UAT — handoff done, 169 of 169 verified
last_verified_build: PASS
last_verified_date: 2026-11-26
---

# TechieBlog — Status

## Where I am

Phase 1 of 1 (Whole product (kept as one plan by owner decision, 2026-10-27)). Every row is Verified (169 of 169, 11 not applicable). Fixed today: reader confirmation mail. The mail server now comes from Settings at every send; Settings has "Send test mail" on the website and in BlogApp; forms show the masked address or say confirmation is unavailable; the dashboard warns until mail is set. Proven against a mail catcher on throwaway databases. Build PASS; nothing is deployed.

## Next command to run

Claude Code:
```
(owner) set current_phase to Released after UAT — no agent command
```
OpenCode:
```
(owner) set current_phase to Released after UAT — no agent command
```
Why: every row in this phase's scope is terminal and handoff has run; waiting on the owner; working docs/TechieBlog-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 0 |

- None

## Known blockers

- Owner-only: deploy, then set up mail on the live Settings screen (Prod-Deploy-Checklist §7 step 6); live mail delivery is unproven until that test mail.
- Owner-only: drop the throwaway database `tbsmoke_20261126092035` on WinPostgre; the database guard refuses agents a DROP.
- Owner-only: CI secret `TrBlazeUiPackagesToken` (REQ-NFR-017); revoke the old GitHub token (REQ-NFR-025); drop `fxd_techieblog` and the 34 `fxv*@example.com` accounts.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-02 | handoff-phase | NOT READY: 155/159 Verified, 12 N/A, 4 to re-verify | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-05 | triage-and-fix | 158/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-05 | build-phase | 159/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-21 | build-phase | 165/165 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-26 | triage-and-fix | 169/169 Verified, 11 N/A | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TechieFlow: 1 open · 3 fixed upstream, not yet re-checked (TF-004, TF-003, TF-001) · 1 closed — docs/TechieBlog-TechieFlow-Feedback.md
- TrBlazeUI: 0 open · 3 closed — docs/TechieBlog-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-11-26: 0 findings, see the checklist Remarks.

## Deferred / future

- 21 older verify specs and the seed helper name the owner's `TechieBlog` database; not run today.
- Mail port 465 is unsupported; 587 is documented.
- DevGuide needs the mail, ratings-dialog, referrer and `--profile` code paths.
- DevGuide 2026-11-02 defects found by reading, not reproduced (9 rows). Needs `*triage-issues`.
- The WebView2 file-dialog crash was avoided, not fixed upstream.
- REQ-FN-062's SFTP path was not exercised (needs server SSH credentials).
- BlogApp thumbnails fail against a plain-http local site; production uses https.
