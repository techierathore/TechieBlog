---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-09
current_phase: Build — first full verify run done; 98 Verified, 29 open defects to fix
last_verified_build: PASS
last_verified_date: 2026-08-09
---

# TechieBlog — Status

## Where I am

**The first executed `*verify all` is complete** — 131 of 139 REQs graded against the running app (the
other 8 are `N/A (removed)`), across nine clusters, with the acceptance, data-render and visual-truth
gates all applied. **98 are now `Verified`** on runtime evidence rather than migrated claims, and the
`⚠ STATIC-ONLY` stamp on the BlogApp desktop head is **lifted**: 19/19 admin routes were driven over
WebView2 CDP with grid counts matching PostgreSQL exactly, plus a publish round trip that appeared on
the separate web host.

**29 rows are open, and most were previously marked done.** The serious ones are functional, not
cosmetic: `/series/{slug}` **leaks unpublished drafts to anonymous visitors**; every newsletter carries
an unsubscribe link to a route that 404s; the sidebar subscribe form on every public page writes
subscribers with **no captcha and no double opt-in**; unmatched routes return a **blank zero-byte page**;
RSS does not exist at all; and post-view tracking is **dead code**, which silently empties the whole
analytics surface. The Markdown editor also loses keystrokes.

The `REQ-FN-053` data-loss regression **holds** (md5 over the nine at-risk columns identical across a
no-edit save), and the committed GitHub PAT is **still in `nuget.config`** despite two commits titled
as removing it.

## Next command to run

```
/TechieFlow:agents:flow-master *build-phase TechieBlog
```
Fix the 29 open rows: 17 `FAIL`, 8 `Needs re-verify`, 4 `In Progress`. Start with the anonymous-exposure
set — `REQ-FN-015`, `REQ-UI-056`, `REQ-FN-032`, `REQ-UI-012`.

## Open requirements

- [ ] **FAIL (17)** — REQ-UI-012, REQ-UI-016, REQ-UI-031, REQ-UI-037, REQ-UI-039, REQ-UI-046,
      REQ-UI-056, REQ-FN-015, REQ-FN-017, REQ-FN-020, REQ-FN-026, REQ-FN-032, REQ-NFR-001,
      REQ-NFR-007, REQ-NFR-010, REQ-NFR-016, REQ-NFR-026
- [ ] **Needs re-verify (8)** — REQ-UI-004, REQ-UI-005, REQ-UI-007, REQ-UI-011, REQ-UI-017,
      REQ-UI-040, REQ-UI-045, REQ-FN-035 *(render/visual gate failures)*
- [ ] **In Progress (4)** — REQ-FN-008 *(token refresh unreachable)*, REQ-FN-034 *(view tracking never
      called)*, REQ-FN-037 *(no RSS feed)*, REQ-NFR-025 *(owner: revoke the committed PAT)*
- [ ] **N/A — no test surface (4)** — REQ-FN-010, REQ-FN-033, REQ-NFR-008, REQ-NFR-009

Counts: 139 rows. Terminal 110 (98 `Verified` + 4 `N/A` + 8 `N/A removed`); open 29.

## Known blockers

- **⚠ Anonymous exposure — 4 defects.** `/series/{slug}` lists unpublished drafts to anonymous
  visitors (`BlogPostRepo.cs:246-248` omits `Published = TRUE` where the sibling COUNT includes it);
  the sidebar subscribe form bypasses captcha *and* double opt-in (`isconfirmed=t` on write);
  `/unsubscribe/{token}` 404s so every mailed unsubscribe link is dead; unmatched routes return a
  zero-byte blank page instead of `/404`.
- **⚠ Projection-completeness is now a FIFTH instance and the gate is still not built.** `REQ-FN-015`
  is the same shape as the four already logged: a read projection omitting a column the write path
  honours, invisible to compiler and unit tests. This one has a security consequence.
- **⚠ SECURITY — live GitHub PAT still committed** in `nuget.config` (`ClearTextPassword`), verified
  present this run despite commits titled "Removed nuget config". Must be revoked and reissued by the
  owner. → REQ-NFR-025.
- **⚠ The two user-secret stores disagree.** WSL `~/.microsoft/usersecrets/techieblog-host-secrets`
  and Windows `%APPDATA%\Microsoft\UserSecrets\...` hold **different** `JwtSigningKey` /
  `AppEncryptionKey`. Both heads read the same encrypted `SiteSetting` rows and a mismatch fails
  silently. Currently inert (0 rows with `issecret=true`); BlogApp's store was aligned to the WSL pair.
- **⚠ Analytics cannot populate.** `TrackViewAsync` has no caller anywhere — `postviews` stayed 0
  through the entire run — so popular posts, the traffic chart and per-post stats are permanently empty.
- **⚠ Prerender→interactive handover shows both shells for ~1.5s**, and the post page blanks entirely
  inside that window. It is the root cause of the `document-title` a11y violation and of repeated
  phantom "zero rows" / "2 elements" readings in automation.
- **Test suite is green but thin** — 383/386 pass, yet BlogEngine line coverage is **24%** against an
  80% target and no bUnit test touches a real BlogUI component.
- **Concurrency ceiling unchanged** — 221 sync Dapper call sites remain; post TTFB measured 3.09s
  under load, 1.7× the 2s budget.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | Discovery (day-1) | Docs only | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Docs amendment | Docs only — BRD-92…97 added | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Mockups + design review | Docs only — 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07 | Unified *build-phase (11 clusters) | Build PASS claimed · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | Unified *build-phase (15 clusters) | Build RED → PASS · 383/383 tests · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | Build repair (owner-reported RED) | 4 × CS1061 fixed; PASS 0 errors 7/7 · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | ***verify all* (9 clusters, first executed run)** | Build PASS 0 errors 7/7 · 131 REQs graded · **98 Verified**, 17 FAIL, 8 Needs re-verify, 4 In Progress, 4 N/A · BlogApp STATIC-ONLY lifted · 183 screenshots · seed data restored | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-057**. TR-032 (`TabsTrigger` splat) is **confirmed fixed** — `/settings`
  renders all six tabs. Still open from app-side observation: `DatePicker` drops `data-testid`
  (TR-030/046 class), and 333 rendered icons showed **0** alias-name misses after the Lucide rename.
- **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-09)

- Underscore fields **0** in hand-written source; `obj`/Hungarian **0** (re-confirmed by grep this run).
- Nullable enabled in all 6 source projects; build 0 errors / 248 warnings.
- XML docs: no project sets `GenerateDocumentationFile`, so the recorded 92% cannot be proved or
  disproved — REQ-NFR-008 is now `N/A` pending that switch.

## Deferred / future

- Build the projection-completeness gate (now 5 known instances).
- Wire `TrackViewAsync` into the post page, then re-verify the analytics surface.
- Raise BlogEngine coverage toward 80% and add bUnit tests over real BlogUI components.
- REQ-NFR-026 stages 3–4, then re-measure the concurrency ceiling.
- Install Firefox + WebKit so REQ-NFR-009 browser compatibility becomes observable.
- macOS (`net10.0-maccatalyst`) BlogApp head — not buildable from the Windows host.
