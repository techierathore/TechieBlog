---
project: TechieBlog
last_updated: 2026-10-21
current_phase: Build — 88 not built, 2 to fix, 65 of 155 verified, 12 not applicable
last_verified_build: PASS
last_verified_date: 2026-10-21
---

# TechieBlog — Status

## Where I am

`*triage-and-fix` on a production report fixed three bugs, all re-verified as `Verified`. REQ-FN-040: post
pages now honour the saved words-per-page setting. REQ-NFR-018: BlogApp saves now refresh the live site, and
a refresh failure stays on screen. REQ-FN-062: BlogApp picks images with the Windows picker instead of
WebView2's file dialog, which was crashing it. Build passes with 0 errors; 1,554 unit tests pass. Not
deployed yet.

## Next command to run

Claude Code:
```
/TechieFlow:agents:flow-master *build-phase TechieBlog
```
OpenCode:
```
/flow-master *build-phase TechieBlog
```
Why: 88 rows are not built yet: REQ-UI-001, REQ-UI-004, REQ-UI-005, REQ-UI-006, REQ-UI-007, REQ-UI-008 …; 2 rows carry a defect (⚠ in Remarks) that a fix must clear before a verify: REQ-UI-003, REQ-UI-053.

## Open requirements

| Status | Count |
|---|---|
| Not Started | 0 |
| In Progress | 1 |
| Implemented | 0 |
| Needs re-verify | 2 |
| Blocked | 0 |
| PARTIAL | 1 |
| FAIL | 86 |

- [ ] REQ-NFR-025 — Revoke and rotate the committed GitHub PAT out of nuget.config (Phase 6) (In Progress)
- [ ] REQ-NFR-017 — CI pipeline — build, test, artifacts on push and PR (Phase 1) (PARTIAL)
- [ ] REQ-FN-006 — Password strength validation on staff account creation + reset (Phase 2) (FAIL)
- [ ] REQ-FN-008 — Token refresh (Phase 2) (FAIL)
- [ ] REQ-FN-009 — 5-role model + 5 authorization policies (Phase 2) (FAIL)
- [ ] REQ-FN-011 — Profile read/update + change password (Phase 2) (FAIL)
- [ ] REQ-FN-012 — Post CRUD service + repository (Phase 3) (FAIL)
- [ ] REQ-FN-013 — Slug generation, uniqueness and slug-based routing (Phase 3) (FAIL)
- [ ] REQ-FN-014 — Markdown rendering via Markdig (Phase 3) (FAIL)
- [ ] REQ-FN-015 — Draft / Published state handling (Phase 3) (FAIL)
- (80 more open rows in docs/TechieBlog-Checklist.md)

## Known blockers

- The fixes are not live yet. Production still splits posts at 500 words and BlogApp still uses the crashing dialog until the owner deploys and rebuilds BlogApp.
- BlogApp's full choose-and-upload round trip with the new picker was not exercised: BlogApp is connected to production. The first real upload is the final check.
- `tests/verify/_gates.ts` still carries the retired admin password (`admin_password`); the two new specs override it with the UsageGuide's.
- `tests/verify/cluster-b.spec.ts` throws at load without `DB_USERS`, which aborts any unscoped Playwright run.

## Verification log

Last five passes; older passes live in `docs/metrics/gates.jsonl`.

| Date | Phase | Result | Status table |
|---|---|---|---|
| 2026-08-26 | verify (scoped: REQ-UI-048/FN-025/UI-049) | 3/3 PASS on TrBlazeUI 2.0.3, workarounds removed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-25 | TrBlazeUI 2.0.3 upgrade + consumer UAT | 8/8 upgrade spec, 1551 unit, build 0 errors | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-25 | scoped verify (production) | REQ-FN-062 Verified; speaking data/UserStats/uploads confirmed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-09-07 | triage-issues | 77/167 Verified | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-10-21 | triage-and-fix | 65/155 Verified, 12 N/A | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TrBlazeUI: 1 open · 0 closed — docs/TechieBlog-TrBlazeUI-Feedback.md

## Standards compliance

- Last check 2026-10-21: 0 findings (build-gate `SourceConventionTests.cs` passed in the unit run), see the checklist Remarks.

## Deferred / future

- The root cause of the WebView2 file-dialog crash (`EmbeddedBrowserWebView.dll` 0x80000003) was not identified; it was avoided, not fixed upstream.
- Undriven: profile save, newsletter Send, subscriber toggles, comment/rating/subscribe submits; Firefox + WebKit for REQ-NFR-009; a real screen-reader pass; the macOS BlogApp head.
