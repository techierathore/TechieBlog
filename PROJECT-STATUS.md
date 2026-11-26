---
project: TechieBlog
last_updated: 2026-11-27
current_phase: Phase 1 of 1 (whole product) · UAT — handoff done, 172 of 172 verified
last_verified_build: PASS
last_verified_date: 2026-11-27
---

# TechieBlog — Status

## Where I am

Phase 1 of 1 (Whole product (kept as one plan by owner decision, 2026-10-27)). Every row is Verified (172 of 172, 11 not applicable). Fixed tonight: boxes with no heading keep their top padding (20 boxes, held by a build test); new Blog page at /blog, second in both menus, in the sitemap; usage guide gives both mail timings. Checked on throwaway databases only. Build PASS; nothing is deployed.

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
- Owner-only: CI secret `TrBlazeUiPackagesToken` (REQ-NFR-017); revoke the old GitHub token (REQ-NFR-025); drop `fxd_techieblog` and the 34 `fxv*@example.com` accounts.
- TrBlazeUI TR-078 (open, Blocks: no): `CardContent` keeps `pt-0` when a page passes `p-6`; fixed on the app side.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-11-05 | triage-and-fix | 158/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-05 | build-phase | 159/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-21 | build-phase | 165/165 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-26 | triage-and-fix | 169/169 Verified, 11 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-11-27 | triage-and-fix | 172/172 Verified, 11 N/A | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TechieFlow: 1 open · 3 fixed upstream, not yet re-checked (TF-004, TF-003, TF-001) · 1 closed — docs/TechieBlog-TechieFlow-Feedback.md
- TrBlazeUI: 1 open · 3 closed — docs/TechieBlog-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-11-27: 0 findings, see the checklist Remarks.

## Deferred / future

- Older verify specs and the seed helper name the owner's database; not run. `cluster-b.spec.ts` throws on load.
- Speaking banner flagged by the contrast check (photo placeholder); checker blind spot.
- Mockups other than Home and Blog lack "Blog" in the menu.
- Mail port 465 unsupported; 587 documented.
- DevGuide lacks mail, ratings, referrer, `--profile`, Blog paths; 9 read-only defects need `*triage-issues`.
- WebView2 file-dialog crash avoided, not fixed upstream.
- REQ-FN-062 SFTP path not exercised.
- BlogApp thumbnails fail on plain-http local sites.
