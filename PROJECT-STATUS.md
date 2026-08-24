---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-24
current_phase: UAT — round 3 closed (UAT-025/026 fixed + verified); 5 open REQs, owner/env-gated
last_verified_build: PASS
last_verified_date: 2026-08-24
---

# TechieBlog — Status

## Where I am

Owner UAT round 3 is closed. Both reported defects — the ugly banner-less article card (UAT-025) and the
macOS-vs-Windows typography difference (UAT-026) — were reproduced live, fixed by two parallel `/trblazeui`
sub-agents, re-smoked and **verified**; `REQ-UI-049` is restored to `Verified` and `REQ-UI-005` keeps it with
the gap now closed. Build is GREEN (rung #4, 0 Error(s), 7/7 projects); `req-list-ui.spec.ts` runs 11/11 and
the orchestrator's own re-smoke 42/42 on a cleanly rebuilt host. Five REQs remain open and each is gated on
the owner or the environment rather than on agent work — CI hardening, the committed-PAT rotation, the
async/concurrency conversion, the VPS deploy pipeline, and BlogApp SFTP media storage. Migration
`032-SiteLogoSetting.sql` is still NOT deployed; production is on 031, and the website must ship before
BlogApp per the standing ship-order rule.

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
| 2026-08-24 | fix-issues + scoped verify | UAT-025/026 fixed; REQ-UI-049 restored to Verified, REQ-UI-005 held; 11/11 acceptance, 42/42 re-smoke | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-23 | verify (scoped) | 4 of 5 Verified; REQ-FN-062 config-blocked | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22 | verify (scoped) | 5 REQs graded, 11/11 Playwright | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | verify (all) | render + visual gates across public and admin | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TrBlazeUI: 0 major, 1 minor new this round (**TR-073** — the prebuilt bundle ships `bg-gradient-to-*` direction utilities but no `from-*`/`to-*` colour-stop utilities for any colour) — docs/TechieBlog-TrBlazeUI-Feedback.md
- TechieRag: not used in this project (no AI/RAG features)

## Standards compliance (last verifier check)

- Underscore fields: PASS — build gate, `tests/unit/Ops/SourceConventionTests.cs`
- Test method underscores: PASS — same gate
- Mis-prefixed fields: PASS — same gate

## Deferred / future

- Local database holds no published content by design. `blogpost` #45 `Fix Test Post No Banner` (a prior
  session's repro fixture) is left **unpublished**, not deleted — restore with
  `update blogpost set published=true where postid=45;`
- Home stats band and Download-CV CTA stay unobservable until `UserStats` rows and a `CVFilePath` exist.
- Undriven surfaces: profile save, newsletter Send, subscriber toggles, comment/rating/subscribe submits;
  Firefox + WebKit for REQ-NFR-009; a real screen-reader pass; the macOS BlogApp head.
- `.buildout/*/logs` holds ~133 MB of leftover agent logs (gitignored, safe to delete).
