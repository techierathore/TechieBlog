---
project: TechieBlog
last_updated: 2026-10-28
current_phase: Phase 1 of 1 (kept whole) · Handoff — 158 of 159 verified
last_verified_build: PASS
last_verified_date: 2026-10-28
---

# TechieBlog — Status

## Where I am

Phase 1 of 1 (whole product). 158 of 159 rows are Verified. The owner's three decisions are applied:
BlogApp was tested against the local database (all five BlogApp rows pass) and its production
connection is restored; subscribers are deactivated, not removed; the profile page has no user
picker. Token refresh is proven. Only REQ-UI-047 is open, waiting on a TrBlazeUI fix. Nothing is
deployed yet.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *handoff-phase TechieBlog
```
OpenCode:
```
/flow-master *handoff-phase TechieBlog
```
Why: every row in this phase's scope is terminal and handoff has not run yet; working docs/TechieBlog-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 0 |
| Blocked | 1 |

- [ ] REQ-UI-047 — Admin layout with grouped navigation (Phase 1) (Blocked)

## Known blockers

- REQ-UI-047: TrBlazeUI never paints the active sidebar item (TR-076), still open in 2.2.0.
- BRD-57 still says subscribers can be removed; the owner chose deactivate. Needs `*amend-docs`.
- Owner-only: CI secret `TrBlazeUiPackagesToken` (REQ-NFR-017); revoke the old GitHub token (REQ-NFR-025); drop the builders' leftover scratch database `fxd_techieblog` on WinPostgre.
- Undeletable test leftovers in the local database: soft-deleted test posts and users, sent test newsletters, test subscribers. No published post remains.
- TF-002: the verify boot script cannot reach the app on NAT-mode WSL (worked around). TF-001: Coding Standards over its word limit.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-09-07 | triage-issues | 77/167 Verified | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-21 | triage-and-fix | 65/155 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-27 | amend-docs | 65/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-28 | build-phase | 156/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-28 | build-phase | 158/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TechieFlow: 2 open · 0 closed — docs/TechieBlog-TechieFlow-Feedback.md
- TrBlazeUI: 2 open · 0 closed — docs/TechieBlog-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-10-28: 0 findings (build-gate `SourceConventionTests.cs` passed in the unit run), see the checklist Remarks.

## Deferred / future

- The root cause of the WebView2 file-dialog crash (`EmbeddedBrowserWebView.dll` 0x80000003) was not identified; it was avoided, not fixed upstream.
- Mockups carry no `data-testid` anchors, so mockup parity is "not measured" on every screen; adding anchors is `*amend-docs` work.
- REQ-FN-062's SFTP upload path was not exercised (needs the real server's SSH credentials); the folder and desktop paths pass.
- BlogApp's media thumbnails do not load against a plain-http local site (mixed content); production uses https.
