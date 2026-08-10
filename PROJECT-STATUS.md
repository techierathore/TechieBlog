---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-09
current_phase: Build — 25 of 29 open REQs implemented; 10 new defect rows filed
last_verified_build: PASS
last_verified_date: 2026-08-09
---

# TechieBlog — Status

## Where I am

A 14-cluster `*build-phase` FIX pass closed **25 of the 29 rows** the 2026-08-09 verifier left open, each
smoked by its builder against a running host with real PostgreSQL data. Build GREEN (rung #4, 0 errors,
7/7 projects); unit suite **383 → 1055 passing, 0 failing**; zero `Needs re-verify` rows remain.

All four anonymous-exposure defects are closed and proven in both directions (draft leak, captcha +
double opt-in, live unsubscribe link, 404 status). Six defects **nobody had reported** were found by the
smoke discipline — a fail-open captcha branch, raw Markdown in every newsletter sent, uploads returning
HTML until rebuild, sessions that never expired, an unclickable nested dialog, and `aria-selected=""`
that meant no tab was ever announced selected. The **projection-completeness gate now exists**, proved
non-vacuous by counterfactual. Two rows were demoted on evidence: REQ-NFR-017 (CI fails) and
REQ-NFR-018 (`ICacheService` has no consumer). Detail per row in the checklist Remarks.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieBlog
```
Build still leads — 10 new `Planned` rows are unbuilt, and REQ-NFR-016/018/026 have code work left. Do
not jump to `*verify all`: 25 rows await verification, but unbuilt work outranks it in the ladder.

## Open requirements

- [ ] **Implemented — awaiting verifier (25)** — REQ-UI-004/005/007/011/012/016/017/031/037/039/040/045/046/056,
      REQ-FN-008/015/017/020/026/032/034/035/037, REQ-NFR-007/010
- [ ] **PARTIAL (5)** — REQ-NFR-001 *(measured miss)*, REQ-NFR-016 *(47.15%)*, REQ-NFR-017 *(owner: CI secret)*,
      REQ-NFR-018 *(cache registered but unused)*, REQ-NFR-026 *(stage 4 outstanding)*
- [ ] **In Progress (1)** — REQ-NFR-025 *(owner: git-history decision; working tree clean)*
- [ ] **Planned — new defect rows (10)** — REQ-FN-054…059, REQ-UI-058, REQ-NFR-030/031/032

Counts: 149 rows. Terminal 108 (96 `Verified` + 4 `N/A` + 8 `N/A removed`); open 41.

## Known blockers

- **⚠ OWNER — CI blocked on a secret.** Restore fails `NU1301/403`: feed is private (401 unauthenticated),
  `TrBlazeUiPackagesToken` unset, `GITHUB_TOKEN` cannot read a user-scoped feed. A preflight step now fails
  in one actionable line. Setup: `GETTING_STARTED.md` → *CI setup*.
- **⚠ OWNER — dead PAT still in git history.** Returns 401 from `api.github.com/user` (already revoked).
  Working tree clean. Recommendation: accept the history; the credential is non-functional.
- **⚠ REQ-NFR-001's recorded plan is wrong — async will not fix it.** Under c100: PostgreSQL 22–24 conns with
  **1–3 active**, `/health` 1059 req/s at p50 15ms, CPU 74–79%. Real lever is round trips — **`/` = 18.9 per
  render**. `SetMinThreads` tried and rejected on interleaved A/B; ships OFF. Variance exceeds the effect.
- **⚠ Admin WCAG rests on a workaround.** axe is 0/0 public and admin, but via an `App.razor` MutationObserver
  (TR-054/063/064). **No screen-reader pass has ever run** — 0 automated nodes is not conformance.
- **⚠ "Both shells for ~1.5s" STILL UNEXPLAINED.** Three real `Routes.razor` nesting defects fixed, but 280
  samples through the handover did not reproduce it — on the pristine build either. Re-check on a deployment.
- **⚠ 80% coverage unreachable as scoped.** `DbAccess` is 3,226/9,434 lines at 0% without PostgreSQL; no-DB
  ceiling **62.4%**. Owner decision: narrow the runsettings `<Include>`, or run Testcontainers in that leg.
- **⚠ Parallel-cluster hazards confirmed.** `bin/` collisions (MSB3021/3030), one `taskkill /IM` killed three
  siblings, and a re-armed `MustChangePassword` made **eight admin audits silently report a FALSE clean pass**.
  Future clusters: private `-p:OutDir`, kill by PID only, assert the exact landing URL.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | Discovery (day-1) | Docs only | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Docs amendment | Docs only — BRD-92…97 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Mockups + design review | Docs only — 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07 | *build-phase (11 clusters) | Build PASS claimed · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | *build-phase (15 clusters) | RED → PASS · 383 tests · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | Build repair (owner-reported RED) | 4 × CS1061 fixed; PASS 7/7 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | *verify all* (first executed run) | 131 REQs graded · 98 Verified, 17 FAIL, 8 Needs re-verify | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | ***build-phase* FIX (14 clusters) + CI repair** | PASS 0 errors 7/7 · tests 383 → **1055** · **25/29 → Implemented** · 0 `Needs re-verify` · axe 52 → **0/0** · 6 unreported defects found · projection gate built · 10 new rows filed · **no verifier run — all capped at `Implemented`** | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-065**. Eight logged this pass — TR-057 (High, `Textarea` controlled-input
  keystroke loss on Blazor Server), TR-058 (`SelectValue` raw value), TR-059 (no Prose container),
  TR-060 (High, nested `Dialog` z-order), TR-061 (High, `ItemGroup` lists announced EMPTY), TR-062
  (`Input` has no `AriaLabel`), TR-063 (`aria-selected=""` — invisible to axe), TR-064 (`MarkdownEditor`
  tabs lack `role=tablist`). TR-054 re-diagnosed: three admin screens render **no** `TabsContent` at all.
- **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-09)

- Underscore fields **0**; `obj`/Hungarian **0**. Build 0 errors / 284 warnings across 7 projects.
- New footgun for the standards doc: a `@* … *@` comment **inside an element's attribute list** compiles
  but is emitted as an **attribute name** at runtime, killing the circuit (`setAttribute` error).
- `SvcUtils.cs:5` declares `namespace BlogSvc;`, shadowing the `BlogEngine.Services.BlogSvc` type → REQ-NFR-032.

## Deferred / future

- **REQ-NFR-026 stage 4** — ~109 Blazor call sites still sync; 43 async members built and waiting. Purely
  additive so far. Not expected to move REQ-NFR-001.
- Cut the home page's 18.9 round trips per render — the actual REQ-NFR-001 lever (N+1 in resume components).
- Wire `ICacheService` to a real consumer per named surface (REQ-NFR-018).
- Decide coverage scope, then flip the CI gate from `::warning::` to enforcing.
- A real screen-reader pass; remove the `App.razor` observer once TrBlazeUI ships TR-054/063/064.
- Firefox + WebKit for REQ-NFR-009; macOS BlogApp head (not buildable from this host).
