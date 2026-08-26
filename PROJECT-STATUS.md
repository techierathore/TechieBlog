---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-26
current_phase: UAT — every agent-verifiable REQ Verified; 3 open REQs, all owner/env-gated
last_verified_build: PASS
last_verified_date: 2026-08-26
---

# TechieBlog — Status

## Where I am

TrBlazeUI **2.0.3** is in (all three csprojs; build GREEN rung #4, 0 Error(s), 7/7; `dotnet test` 1551/3/0).
Its release closed every open library finding (TR-066…TR-074), and each fix was measured against a running
host before the matching workaround was removed — `tests/verify/trblazeui-203-upgrade.spec.ts` 8/8 — and the
verifier re-graded REQ-UI-048 / REQ-FN-025 / REQ-UI-049 **3/3 PASS on 2026-08-26** (one minor library finding,
TR-075, logged without demotion). Gone:
`ManageImages` `NativeSelect` (TR-067), four DatePicker/TimePicker test-hook wrappers (TR-072), the
`ItemContent min-w-0` copy (TR-071), five hand rules in `utilities.css` (TR-072c/073). Kept: the
`PostMarkdownEditor` document latch (TR-069, still required). Detail in the checklist Remarks of REQ-UI-048 /
REQ-FN-025 / REQ-UI-049. **The working tree is not yet deployed** — the owner commits and deploys.

**REQ-NFR-038 (the deploy pipeline) is now `Verified`** — graded 2026-08-26 against the live site the owner
has been deploying through it. Before that, UAT round 3 (Mac/Windows parity, UAT-025..031) was closed and
owner-confirmed on the live site. Three REQs remain open, each gated on the owner — CI hardening, the
committed-PAT rotation, the deferred async/concurrency stage. Nothing is left for an agent to verify.

## Next command to run

```
(owner-run) walk docs/TechieBlog-UsageGuide.md §"UAT plan" — open rows REQ-NFR-017, REQ-NFR-025, REQ-NFR-026
```
All three remaining rows are owner-gated (CI secrets, the PAT rotation, the deferred async stage); when the owner un-defers one, resume with `/TechieFlow:agents:flow-master *build-phase TechieBlog` naming that REQ.

## Open requirements

- [ ] REQ-NFR-017 — CI pipeline: build, test, artifacts on push and PR (PARTIAL)
- [ ] REQ-NFR-025 — Revoke and rotate the committed GitHub PAT out of nuget.config (In Progress, owner-gated)
- [ ] REQ-NFR-026 — Convert DbAccess to async and fix the concurrency ceiling (PARTIAL, stage 4 deferred by the owner)

## Known blockers

- **Routed sub-agent wrappers are broken (TechieFlow framework, not this app).** `.tfcore/utils/tf-routing-bind.sh:141,143`
  generates wrappers telling the agent to read `.claude/trblazeui.md` / `.claude/techierag.md` and stop if absent.
  Those files do not exist here and are **not** created by a build (checked after a full rung-#4 build). The real
  personas are `.claude/commands/{trblazeui,techierag}.md`. Every routed `REQ-UI-*` / `REQ-RAG-*` cluster will
  dead-stop until the generator is fixed.
- ~~`CLAUDE.md` names the wrong next-free TrBlazeUI feedback id~~ — fixed 2026-08-25; it now says **TR-075** and
  defers to the feedback file's own bottom counter line.
- **No owner actions outstanding.** ⚠ **Read this before writing an "owner actions" list again.** On
  2026-08-25 three items were repeated back to the owner — load the speaking data, enter `UserStats`, configure
  BlogApp SFTP — **all three of which he had already done.** They were copied from these stale notes without
  checking the deployed site. Verified live the same day: `/speaker-profile` serves **42 session rows**, the home
  stats band renders **4 populated cards** (22+ years · 18+ sessions · 1.2 M+ reads · 31+ articles), and **9
  `/uploads/` images return HTTP 200**, one uploaded 2026-08-24. **Anything in this section must be re-checked
  against `https://techierathore.com` immediately before it is reported, not carried forward from a prior pass.**

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-26 | verify (scoped: REQ-NFR-038, against the LIVE site) | PASS → `Verified` — Caddy→Kestrel container chain observed end-to-end (308/301 redirects, LE cert, /healthz Healthy incl. schema 31/31, uploads served, `blazor.web.js` 200,645 B, live `_blazor` WebSocket + server-driven theme flip, home render/visual clean @1280/390); Actions/GHCR/SSH internals not observable from here and not claimed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-26 | verify (scoped: REQ-UI-048 · REQ-FN-025 · REQ-UI-049) | 3/3 PASS on TrBlazeUI 2.0.3 with workarounds removed — 16/16 gate sweep (15 screens × 1280/390), 8/8 upgrade UAT, 13/13 Select first-paint, 7/7 upload limits; stats band observed for the first time; new library finding TR-075 (Select-in-Dialog focus drop) logged, no demotion | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-25 | TrBlazeUI 2.0.3 upgrade + consumer UAT (builder smoke, not a verify-phase run) | REQ-UI-048 / REQ-FN-025 / REQ-UI-049 Remarks updated; 8/8 upgrade spec, 1551 unit, build 0 errors; workarounds for TR-067/071/072/072c/073 removed after live measurement | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-25 | scoped verify (production) | REQ-FN-062 Verified from live evidence; speaking data (42 rows), UserStats (4 cards) and uploads all confirmed already done by the owner — three stale "owner actions" retracted | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-25 | fix-issues pass 4 + scoped verify | UAT-031 — raised the root-font (20px→34px) and .site-container (1600px→100rem) ceilings that discarded proportional scaling above 2246. Physical font 34.2px AND container 89% of screen at every 4K scaling; 11/11 acceptance | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-24 | fix-issues pass 3 + scoped verify | UAT-029/030 (post page one aligned column, TOC rail deleted; fluid root type). Post blocks share ONE left edge at every viewport; column 46.3% of screen on BOTH machines; 46/48 own checks, 11/11 acceptance, 1551 unit | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-24 | fix-issues pass 2 + scoped verify | UAT-027/028 (fluid width system + post-page rebuild); header==body to the pixel, screen filled 45.6%→71.2% @2246 and 54.3%→89.0% @1798; 152/152 re-smoke, 11/11 acceptance, 1555 unit | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-24 | fix-issues + scoped verify | UAT-025/026 fixed; REQ-UI-049 restored to Verified, REQ-UI-005 held; 11/11 acceptance, 42/42 re-smoke | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-23 | verify (scoped) | 4 of 5 Verified; REQ-FN-062 config-blocked | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22 | verify (scoped) | 5 REQs graded, 11/11 Playwright | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | verify (all) | render + visual gates across public and admin | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TrBlazeUI: **1 open, minor** — **TR-075** (2026-08-26): a styled `Select` inside `DialogContent` drops focus to `<body>` after a pick, so Escape cannot dismiss the dialog until the user Tabs back in; mouse unaffected, no REQ demoted. 2.0.3 otherwise closed TR-066…TR-074 (consumer measurements in the "Consumer verification on 2.0.3" table of docs/TechieBlog-TrBlazeUI-Feedback.md). Next free id **TR-076**.
- TechieRag: not used in this project (no AI/RAG features)

## Standards compliance (last verifier check)

- Underscore fields: PASS — build gate, `tests/unit/Ops/SourceConventionTests.cs`
- Test method underscores: PASS — same gate
- Mis-prefixed fields: PASS — same gate

## Deferred / future

- Local database holds no published content by design. `blogpost` #45 `Fix Test Post No Banner` (a prior
  session's repro fixture) is left **unpublished**, not deleted — restore with
  `update blogpost set published=true where postid=45;`. Its body was replaced with heading-bearing sample
  text so the new post-page TOC rail had something real to build against.
- The post page's TOC rail (`PostTocRail`, its JS anchor workaround and its tests) was **deleted** at the
  owner's request in UAT-029, not hidden. TR-074 (TrBlazeUI `AnchorNav` emitting base-relative `#id` links)
  is therefore moot for this page but remains a real library defect worth fixing upstream.
- The home stats band **is populated and live** (4 cards, verified 2026-08-25) — the long-standing "no `UserStats` rows"
  caveat is retired. Only the Download-CV CTA is still unobservable, and only because no `CVFilePath` is set.
- Undriven surfaces: profile save, newsletter Send, subscriber toggles, comment/rating/subscribe submits;
  Firefox + WebKit for REQ-NFR-009; a real screen-reader pass; the macOS BlogApp head.
- `.buildout/*/logs` holds ~133 MB of leftover agent logs (gitignored, safe to delete).
