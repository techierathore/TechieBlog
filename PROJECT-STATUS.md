---
project: TechieBlog
last_updated: 2026-10-30
current_phase: Phase 1 of 1 (whole product) · Verify — 3 to verify
last_verified_build: PASS
last_verified_date: 2026-10-28
---

# TechieBlog — Status

## Where I am

Phase 1 of 1 (whole product). 156 of 159 rows are Verified; 3 wait for the verifier. BRD-57 now says
subscribers are deactivated, not removed; the code already did that, so REQ-UI-025 and REQ-FN-031
only need re-verifying. TrBlazeUI is on 2.2.1, which fixes the sidebar highlight, so REQ-UI-047 is
unblocked. Build 0 errors, 1 536 unit tests pass. Nothing is deployed yet.

## Next command to run

Claude Code:
```
/TechieFlow:agents:verifier *verify all TechieBlog
```
OpenCode:
```
/flow-verifier *verify all TechieBlog
```
Why: 3 rows are built and not verified: REQ-UI-025, REQ-UI-047, REQ-FN-031; working docs/TechieBlog-Checklist.md.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 0 |
| Implemented | 0 |
| Needs re-verify | 3 |
| Blocked | 0 |

- [ ] REQ-FN-031 — Subscriber list / search / status / deactivate / export (Phase 5) (Needs re-verify)
- [ ] REQ-UI-025 — Subscribers admin page (Phase 5) (Needs re-verify)
- [ ] REQ-UI-047 — Admin layout with grouped navigation (Phase 1) (Needs re-verify)

## Known blockers

- Owner-only: CI secret `TrBlazeUiPackagesToken` (REQ-NFR-017); revoke the old GitHub token (REQ-NFR-025); drop the builders' leftover scratch database `fxd_techieblog` on WinPostgre.
- The local database has no published post, so home-page checks that need a post card cannot run until one is seeded.
- TF-002: the verify boot script cannot reach the app on NAT-mode WSL (worked around). TF-001: fixed upstream, not yet re-checked here.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-10-21 | triage-and-fix | 65/155 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-27 | amend-docs | 65/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-28 | build-phase | 156/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-28 | build-phase | 158/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-30 | amend-docs | 156/159 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TechieFlow: 1 open · 1 fixed upstream, not yet re-checked (TF-001) · 0 closed — docs/TechieBlog-TechieFlow-Feedback.md
- TrBlazeUI: 0 open · 2 closed — docs/TechieBlog-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-10-30: 0 findings, see the checklist Remarks.

## Deferred / future

- The root cause of the WebView2 file-dialog crash (`EmbeddedBrowserWebView.dll` 0x80000003) was not identified; it was avoided, not fixed upstream.
- Mockups carry no `data-testid` anchors, so mockup parity is "not measured" on every screen; adding anchors is `*amend-docs` work.
- REQ-FN-062's SFTP upload path was not exercised (needs the real server's SSH credentials); the folder and desktop paths pass.
- BlogApp's media thumbnails do not load against a plain-http local site (mixed content); production uses https.
