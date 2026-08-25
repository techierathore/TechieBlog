---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-24
current_phase: UAT — round 3 closed (UAT-025..030 fixed + verified); 5 open REQs, owner/env-gated
last_verified_build: PASS
last_verified_date: 2026-08-24
---

# TechieBlog — Status

## Where I am

Owner UAT round 3 closed over **three passes**, each one correcting the previous diagnosis after the owner
deployed and said it had not changed what he saw. Pass 1 fixed the banner-less card (UAT-025) and a missing
webfont (UAT-026) — both real, both live, neither the reported problem. Pass 2 found the actual cause of
"different on Mac vs Windows" was **width**: six disagreeing fixed container caps, replaced by one two-tier
system (`.site-container` fluid to 1600px, `.prose-container` for reading) — UAT-027/028. Pass 3 closed the
remainder: the post page had **three different left edges** (band and comments at 1600, TOC rail at 240,
article at 820), now one `.post-column` with the TOC rail deleted at the owner's request (UAT-029); and
"Windows still looks bigger" turned out to be **type size, not width** — fonts were a fixed 16px/36px at both
viewports, so the same layout rendered physically smaller on the Mac's denser CSS-pixel space. Root type is
now `clamp(16px, 0.89vw, 20px)` with the four px font clamps converted to rem (UAT-030). Windows renders
byte-identical to before; the Mac scales +25% uniformly; the post column occupies **46.3% of the screen on
both machines**. Build GREEN (rung #4, 0 Error(s), 7/7); own verification 46/48 (both non-passes were wrong
assertions in the check, re-confirmed by direct measurement), `req-list-ui.spec.ts` 11/11, unit suite 1551
pass / 0 fail / 3 skip. Five REQs remain open, each gated on the owner or the environment — CI hardening, the
committed-PAT rotation, the async/concurrency conversion, the VPS deploy pipeline, and BlogApp SFTP media
storage. **Nothing above is visible until the site is deployed;** migration `032-SiteLogoSetting.sql` is
still NOT deployed and production is on 031, so the website ships before BlogApp per the ship-order rule.

## Next command to run

```
/TechieFlow:agents:verifier *verify REQ-FN-062      (OpenCode: /flow-verifier *verify REQ-FN-062)
```
Blocked on configuration, not code — re-enter BlogApp's Media storage (SFTP) + Website address first, and deploy the website before BlogApp (migration `032` is new; production is on `031`).

## Open requirements

- [ ] REQ-NFR-017 — CI pipeline: build, test, artifacts on push and PR (PARTIAL)
- [ ] REQ-NFR-025 — Revoke and rotate the committed GitHub PAT out of nuget.config (In Progress, owner-gated)
- [ ] REQ-NFR-026 — Convert DbAccess to async and fix the concurrency ceiling (PARTIAL)
- [ ] REQ-NFR-038 — Containerised production deployment pipeline to the VPS (Implemented, never executed on the server)
- [ ] REQ-FN-062 — BlogApp SFTP media storage (Implemented, blocked on owner credentials)

## Known blockers

- **Routed sub-agent wrappers are broken (TechieFlow framework, not this app).** `.tfcore/utils/tf-routing-bind.sh:141,143`
  generates wrappers telling the agent to read `.claude/trblazeui.md` / `.claude/techierag.md` and stop if absent.
  Those files do not exist here and are **not** created by a build (checked after a full rung-#4 build). The real
  personas are `.claude/commands/{trblazeui,techierag}.md`. Every routed `REQ-UI-*` / `REQ-RAG-*` cluster will
  dead-stop until the generator is fixed.
- **`CLAUDE.md` names the wrong next-free TrBlazeUI feedback id** — says TR-067; the feedback file's own counter
  said TR-073 (now used). Should read **TR-074**.
- **Owner actions outstanding:** deploy the website (migration `032`); run `docs/data/speaking-engagements.sql`
  against production (page is live and empty); optionally enter real `UserStats` so the home stats band renders.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-24 | fix-issues pass 3 + scoped verify | UAT-029/030 (post page one aligned column, TOC rail deleted; fluid root type). Post blocks share ONE left edge at every viewport; column 46.3% of screen on BOTH machines; 46/48 own checks, 11/11 acceptance, 1551 unit | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-24 | fix-issues pass 2 + scoped verify | UAT-027/028 (fluid width system + post-page rebuild); header==body to the pixel, screen filled 45.6%→71.2% @2246 and 54.3%→89.0% @1798; 152/152 re-smoke, 11/11 acceptance, 1555 unit | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-24 | fix-issues + scoped verify | UAT-025/026 fixed; REQ-UI-049 restored to Verified, REQ-UI-005 held; 11/11 acceptance, 42/42 re-smoke | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-23 | verify (scoped) | 4 of 5 Verified; REQ-FN-062 config-blocked | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22 | verify (scoped) | 5 REQs graded, 11/11 Playwright | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | verify (all) | render + visual gates across public and admin | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TrBlazeUI: 0 major, 2 minor new this round — **TR-073** (prebuilt bundle ships `bg-gradient-to-*` but no `from-*`/`to-*` colour-stop utilities) and **TR-074** (`AnchorNav` emits bare `href="#id"`, which resolves against `<base href="/">` and navigates the app away from the current page instead of scrolling) — docs/TechieBlog-TrBlazeUI-Feedback.md
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
- Home stats band and Download-CV CTA stay unobservable until `UserStats` rows and a `CVFilePath` exist.
- Undriven surfaces: profile save, newsletter Send, subscriber toggles, comment/rating/subscribe submits;
  Firefox + WebKit for REQ-NFR-009; a real screen-reader pass; the macOS BlogApp head.
- `.buildout/*/logs` holds ~133 MB of leftover agent logs (gitignored, safe to delete).
