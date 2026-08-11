---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-11
current_phase: Build — 6 defect rows open after the first full verify since 2026-08-09
last_verified_build: PASS
last_verified_date: 2026-08-11
---

# TechieBlog — Status

## Where I am

A `*build-phase` pass (7 clusters) closed the last 9 `Planned` rows, then a chained **`*verify all` graded 51 rows
against the running app** — the first executed verifier run since 2026-08-09. Terminal **108 → 149**; open **54 → 13**.
Build GREEN (rung #4, 0 errors, 7/7); suite **1355 → 1411**; all five standards greps 0, checked with positive controls.

**The verifier found 6 real defects, one serious:** `/ManagePost/{id}` does not reload on a route-parameter change, so
editing post A then post B leaves A's title and slug under B's URL — **a save would overwrite the wrong post**.

Four clusters found silent-failure defects their REQ never asked about: `DropWrite` returning `true` while discarding
(invisible dropped views); `/healthz` output-cached so it could only go **stale green, never stale red**, which would
have voided REQ-NFR-039 itself; a scratch file hijacking the xunit entry point so **the whole suite was not running**
while exiting 0; and BlogApp's Serilog silently ceasing to write at 1 GB. Three rows had their premise corrected —
REQ-NFR-033's true count was **46, not 17** (3 reachable unauthenticated, incl. `/healthz` leaking host/port/username).

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieBlog
```
Fixes **REQ-UI-016** (wrong-post save risk — first), **REQ-FN-025**, and render defects **REQ-UI-024/034/038/039**.
Build leads: these are broken, not merely unverified.

## Open requirements

- [ ] **FAIL (2)** — REQ-UI-016 *(editor stale on route change; a save writes the wrong post)*, REQ-FN-025
      *(upload dialog advertises 2MB and 10MB for the same upload)*
- [ ] **Needs re-verify (4)** — REQ-UI-024 *(code matches `'Complete'`, DB stores `'Completed'`)*,
      REQ-UI-034/038/039 *(selectors render a raw id/`"0"`; `/admin/experience` is the correct reference)*
- [ ] **Implemented — awaiting verifier (4)** — REQ-NFR-001, REQ-NFR-037, REQ-NFR-038, REQ-FN-055
- [ ] **PARTIAL (2)** — REQ-NFR-017 *(owner: CI secret)*, REQ-NFR-026 *(stage 4 deferred by owner)*
- [ ] **In Progress (1)** — REQ-NFR-025 *(owner: git-history decision)*

Counts: 162 rows. Terminal 149 (137 `Verified` + 4 `N/A` + 8 `N/A removed`); open 13.

## Known blockers

- **⚠ OWNER — CI *and* DEPLOY blocked on one secret.** `TrBlazeUiPackagesToken` unset, feed private, restore
  `NU1301/403`. Plus 3 more secrets (`JWT_SIGNING_KEY`, `APP_ENCRYPTION_KEY`, `ANALYTICS_VISITOR_SALT` — each makes
  the container **refuse to start**) and `chown -R 1654:1654` on `/srv/data/techieblog/{uploads,dp-keys}`.
  Runbook: `docs/Prod-Deploy-Checklist.md`.
- **⚠ A green deploy is still not proof.** REQ-NFR-039 now gates DbUp's journal (27/27) and is no longer
  output-cached, but REQ-NFR-040's unwritable-uploads failure is logged and **still not gated**. On this stack
  "every automated probe is green" has been wrong three times — drive a real browser before believing a deploy.
- **⚠ OWNER — dead PAT still in git history.** Revoked (401). Recommendation: accept the history.
- **⚠ REQ-NFR-026 stage 4** — 220 sync Dapper call sites remain, each with a live sync caller. Cleanup only; the
  ceiling it existed to fix is met.
- **⚠ Quote Release/Production perf only.** Development logs ~61 KB/request vs Production ~124 B.
- **⚠ Admin WCAG rests on a workaround.** axe 0/0 over 15 admin + 9 public routes, but via an `App.razor`
  MutationObserver (TR-054/063/064) with **no screen-reader pass ever run**. REQ-UI-060 proved the gap: the default
  axe tag set **structurally cannot see** a missing `h1`. "Both shells for ~1.5 s" is STILL UNEXPLAINED.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02…08-06 | Day-1 docs, amendment, mockups | Docs only — BRD-92…97, 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07…08-08 | *build-phase x2 + build repair | 383 tests; PASS 7/7 · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | *verify all* (first executed run) | 131 graded · 98 Verified, 17 FAIL, 8 Needs re-verify | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09…08-10 | *build-phase FIX + *build-phase | 1055 → 1291 tests · coverage 82.5% · REQ-NFR-001 both halves MET (Release) · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | *build-phase (owner requests) + local Docker verification | 1355 tests · deploy pipeline · BLOCKING `_framework` defect found and fixed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | ***build-phase* (7 clusters) + chained *verify all*** | Build PASS 0 errors 7/7 · 1355 → **1411** tests · last 9 `Planned` rows closed · migrations 026/027/028 · **verifier EXECUTED, 51 rows graded: 45 → `Verified`, 2 → `FAIL`, 4 → `Needs re-verify`** · terminal **108 → 149**, open **54 → 13** | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-067**. Logged this pass — **TR-066**: `CardTitle` hardcodes `<h3>` with no
  heading-level parameter, so a card-shaped page cannot produce a valid document outline; it also silently disables
  `Routes.razor`'s `<FocusOnNavigate Selector="h1" />`. Prior: TR-057…TR-065.
- **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-11)

- **All five enforcement greps 0** — REQ-NFR-035 closed the last 11 `a`-prefixed parameters.
- **Zeros validated with positive controls** (field pattern matches 1215 real fields; `a`/`v` pattern matches a
  synthetic `aFoo`), because a blind pattern once hid 7 underscore fields for the life of the project.
- The `ex.Message` grep now covers all of `source/` over `*.cs` **and** `*.razor`, and runs as a build-time test.
- Coding Standards §Logging corrected — its snippet still taught the anti-pattern REQ-NFR-036/037 existed to fix.

## Deferred / future

- REQ-NFR-026 stage 4; remaining REQ-NFR-001 lever is Blazor render CPU, not data access.
- **Deliberately not re-exercised** — REQ-NFR-005 (tripping the limiter would lock out sibling verifiers on the
  shared IP) and REQ-NFR-023 (the run itself clears `MustChangePassword`; now re-armed). Both keep `Verified` on
  unit coverage.
- **Write paths never driven in verification** — profile save (REQ-FN-011 update half, REQ-FN-053), newsletter Send,
  subscriber toggles, comment/rating/subscribe submits. Read-side preconditions asserted instead.
- **REQ-UI-059's evidence spans two runs** — this seed has `publishedon == createdon` everywhere, so rendering alone
  cannot discriminate; the build pass back-dated a post to prove the reorder. Seed one back-dated post to fix this.
- **BlogApp (MAUI) is build-verified only** — REQ-UI-051/052, REQ-FN-046/047 have no runtime coverage from WSL.
- `newsletter` table has 0 rows, so REQ-UI-054 / REQ-FN-050 could not be exercised.
- Markdown editor keeps its split pane at 390px; Newsletter Send is enabled with an empty composer.
- A real screen-reader pass; Firefox + WebKit for REQ-NFR-009; macOS BlogApp head (not buildable from this host).
