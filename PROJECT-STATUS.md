---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI (migrating from Fluent UI) / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI desktop head (planned)
last_updated: 2026-08-06
current_phase: Discovery — docs amended 2026-08-06 (TrBlazeUI, portfolio home, BlogApp); build still blocked on REQ-FN-043
last_verified_build: FAIL
last_verified_date: 2026-08-02
---

# TechieBlog — Status

## Where I am

A substantial brownfield app (the whole MVP — auth, post lifecycle, taxonomy, series, public reading,
search, comments, ratings, media, resume, RSS, theming — is built) that is now mid-redesign on paper.
The 2026-08-06 amendment + two owner design-review passes reset the target: TrBlazeUI replaces Fluent
UI, the home page becomes a portfolio landing with no public admin entry, a BlogApp MAUI desktop admin
head is added, reader accounts / registration / favourites / public author pages are retired, and
comments and ratings become anonymous behind email verification and a self-hosted captcha, with a
public newsletter archive. The 38-screen TrBlazeUI mockup set (`docs/TechieBlog-UIDesign.md` +
`docs/mockups/`) is the approved visual contract; library gaps are logged in
`docs/TechieBlog-TrBlazeUI-Feedback.md`. **Nothing is built against it yet and the build is still RED**
on the NU1605 package conflict, so the whole redesign plus the pre-existing tail (newsletter delivery,
analytics, sample data, health checks, tests, CI) is open work.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieBlog      (OpenCode: /flow-master *build-phase TechieBlog)
```
Start with REQ-FN-043 (build blocker) in `docs/TechieBlog-Checklist.md`; nothing else can be verified until it is green.

## Open requirements

- [ ] REQ-FN-043 — Fix NU1605 restore failure and pin package references (Phase 6) — **blocker** (strategic fix = TrBlazeUI swap, REQ-UI-048)
- [ ] REQ-UI-048 — TrBlazeUI migration of all BlogUI pages/components/layouts (Phase 9) — needs owner's nuget.config credentials
- [ ] REQ-UI-049 / REQ-UI-050 — Portfolio home page + remove public login/admin entry points (Phase 9)
- [ ] REQ-FN-046 / REQ-FN-047 / REQ-UI-051 / REQ-UI-052 — BlogApp MAUI desktop admin: scaffold, connection setup, login/shell, full surface (Phase 10)
- [ ] REQ-UI-005 / REQ-UI-006 / REQ-UI-033 — Shell, home and dark-mode rows downgraded to re-verify by the 2026-08-06 amendment
- [ ] REQ-UI-027 / REQ-UI-029 / REQ-FN-022 / REQ-FN-023 — Rework comments + ratings to anonymous email-identified (schema change; needs spam defence)
- [ ] REQ-UI-053 / REQ-UI-054 / REQ-FN-050 — Public newsletter archive + issue view (Phase 9)
- [ ] REQ-UI-055 / REQ-UI-056 / REQ-FN-048 / REQ-FN-049 — Email verification (double opt-in) + self-hosted captcha; **REQ-FN-048 blocked on REQ-FN-033 (real SMTP)**
- [ ] REQ-UI-002 / REQ-FN-006 — Registration removed: delete `/register` + `/signup`; keep password rules for staff accounts and reset
- [ ] REQ-UI-013 / REQ-UI-014 / REQ-UI-015 / REQ-UI-028 / REQ-UI-041 / REQ-UI-042 / REQ-FN-024 — N/A (removed): built code for authors pages, favourites and reader account pages must be **deleted**, not rebuilt
- [ ] REQ-FN-029 — Narrow to site-owner flag + username; retire author-lookup queries
- [ ] REQ-UI-043 / REQ-FN-032 / REQ-FN-033 — Newsletter composer, send pipeline, real SMTP service (Phase 5)
- [ ] REQ-UI-044 / REQ-FN-034 / REQ-FN-035 — Analytics dashboard, view tracking, popular posts (Phase 5)
- [ ] REQ-UI-019 / REQ-FN-036 — Admin dashboard tiles are stub data (Needs re-verify)
- [ ] REQ-UI-026 / REQ-FN-040 — Site Settings never persists; no settings table or service (Needs re-verify)
- [ ] REQ-UI-001 / REQ-FN-009 — Role-blind post-login redirect; unused Contributor policy (Needs re-verify)
- [ ] REQ-UI-017 — Post list is `EditorOrAbove`, so Authors cannot reach it (Needs re-verify)
- [ ] REQ-UI-032 / REQ-FN-039 — Site theme is a per-browser preference, not a site setting (PARTIAL)
- [ ] REQ-FN-027 — No admin page maintains `UserStats` for the resume (PARTIAL)
- [ ] REQ-FN-041 — Seed / sample data set (Phase 6)
- [ ] REQ-FN-042 — Configurable storage-provider abstraction (Phase 5)
- [ ] REQ-NFR-002 / REQ-NFR-005 / REQ-NFR-019 / REQ-NFR-023 — Password hashing, auth rate limiting, persisted reset tokens, hashed seed credential (security)
- [ ] REQ-NFR-001 / REQ-NFR-018 — Performance targets and caching layer
- [ ] REQ-NFR-006 / REQ-NFR-007 — Input-validation audit and WCAG 2.1 AA audit
- [ ] REQ-NFR-008 / REQ-NFR-020 / REQ-NFR-021 / REQ-NFR-022 — XML docs, legacy artifact removal, field-naming remediation, nullable enable
- [ ] REQ-NFR-012 — Resilience: retry, circuit breaker, graceful degradation
- [ ] REQ-NFR-013 — Add unhandled-exception handlers (PARTIAL 80%)
- [ ] REQ-NFR-014 / REQ-NFR-015 — Health endpoint and correlation IDs
- [ ] REQ-NFR-016 / REQ-NFR-017 — Test project and CI pipeline

Counts: 56 REQ-UI · 50 REQ-FN · 0 REQ-RAG · 23 REQ-NFR = 129 total; 69 terminal (incl. 8 N/A removed), 60 open.

## Known blockers

- **Build FAILS — `NU1605` package downgrade.** `BlogUI` pins `Microsoft.AspNetCore.Components.Web 10.0.0`
  while the floating `Microsoft.FluentUI.AspNetCore.Components 4.*` resolves to 4.14.4, which requires
  ≥ 10.0.9. Reproduced on ladder rung #2 (`~/.dotnet/dotnet`) and rung #4 (`cmd.exe /c dotnet`) —
  it is a project dependency issue, not an environment one. Tracked as REQ-FN-043.
- **⚠ SECURITY — seeded admin credential is plaintext** in `source/BlogDb/PostgresScripts/003-SeedData.sql`
  (`LoginPass = 'admin_password'`). Tracked as REQ-NFR-023.
- **⚠ SECURITY — password hashing is hand-rolled** (`AppEncrypt.CreateHash`), not a standard salted KDF.
  Tracked as REQ-NFR-002.
- **No automated tests and no CI**, so every completion claim in the migrated plan is manual. Tracked as
  REQ-NFR-016 / REQ-NFR-017.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | Discovery (day-1) | Docs only — no verification run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Docs amendment (*amend-docs) | Docs only — BRD-92…97 added, BRD-30/67 revised, 7 REQ rows added | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Mockups + owner design review | Docs only — 38 screens; 8 BRD IDs retired, 4 revised, BRD-98…101 added; 8 REQ N/A, 6 re-verify, 11 new | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- TrBlazeUI: adopted 2026-08-06 (BRD-92) — migration pending (REQ-UI-048); owner to supply GitHub Packages credentials in nuget.config
- TechieRag: not used by this project (no AI/RAG features)

## Standards compliance (last verifier check)

- Underscore fields: 32 found across 17 files (drift — REQ-NFR-021)
- Test method underscores: not yet run (no test project)
- Mis-prefixed fields: 1 `obj`-prefixed field found; project convention is no-prefix camelCase

## Deferred / future

- ~~MAUI Blazor Hybrid desktop writer~~ — moved in scope 2026-08-06 as BlogApp (Phase 10)
- Social login, magic links, email drip sequences, lead magnets
- Admin theme creator UI, community theme repository
- Multi-tenancy, localization, advanced referrer analytics
