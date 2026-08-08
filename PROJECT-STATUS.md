---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows, in progress)
last_updated: 2026-08-08
current_phase: Verify — build repaired 2026-08-08 (owner-reported RED); all 7 projects GREEN incl. BlogApp, no verifier run yet
last_verified_build: PASS
last_verified_date: 2026-08-08
---

# TechieBlog — Status

## Where I am

**The owner found the build RED by trying to run the app; the previous "GREEN" claim was wrong.** Repaired
2026-08-08. Two defects and one environment gap:

1. **4 × `CS1061`** in `source/BlogApp/Services/AuthService.cs` — it called `GetUserByToken` / `AppLogin` /
   `ResetPassword` / `RequestPasswordReset`, none of which exist on the async-only `AuthSvc`. Written as a
   sync wrapper against an imagined API while a correct reference sat in `source/TechieBlog/Services/`. → REQ-UI-051.
2. **BlogApp threw on sign-in even once compiling** — `MauiProgram` never called `AppSecrets.Initialise`.
   Known and logged as carried-forward in REQ-NFR-027, then never picked up. Now closed: both secrets live
   in the existing DPAPI `ConnectionStore` and load at composition. → REQ-NFR-027 (90% → 95%).
3. **Web host would not start** — `JwtSigningKey` / `AppEncryptionKey` never provisioned on this machine.
   Not a defect: `AppSecrets` fail-fast as designed. Set per UsageGuide §Setup.

**The gate had a hole:** it never covered the `net10.0-windows10.0.19041.0` target, so BlogApp's failure was
invisible and the phase closed "GREEN" on a solution that did not build. **Any future `Implemented` on a
BlogApp REQ must cite a build naming the BlogApp TFM.**

Now `0 Error(s)` across 7/7 projects. Web host serves `/` 200 + `/health` Healthy; BlogApp signs in and
reaches `/change-password` as Admin.

Earlier that day, a unified `*build-phase` across 15 clusters closed: the async conversion's last two
repositories, BlogApp admin-surface crashes, dark mode on TrBlazeUI, the WCAG re-audit (1.1.1 now genuinely
met), secrets out of source, forwarded headers, Serilog size cap and volume, seeded-admin hashing + forced
first-login change, the login audit trail, `svctoken` removal, and XML docs across `BlogModels` +
`BlogEngine` (694 members). One data-loss defect fixed: **REQ-FN-053** — saving Manage Profile erased the
site owner's résumé.

**Nothing is `Verified`.** A builder's ceiling is `Implemented`; 67 rows await an executed verify run.

## Next command to run

```
/TechieFlow:agents:verifier *verify all TechieBlog
```
Build no longer leads: every buildable REQ is built and observable, and 67 rows wait on a verdict. The
remaining open rows are owner-action (REQ-NFR-025), verifier work (REQ-UI-005/006), or explicitly
deferred scope (REQ-NFR-001 concurrency, REQ-NFR-008 out-of-scope projects).

## Open requirements

- [ ] **Not Started (1)** — REQ-NFR-025 *(owner: revoke + reissue the committed GitHub PAT)*
- [ ] **PARTIAL (2)** — REQ-NFR-001 *(page-load MET; ≥100 concurrent NOT MET, blocked on REQ-NFR-026 stages 3–4)*, REQ-NFR-008 *(92%; BlogUI + host out of scope)*
- [ ] **Needs re-verify (2)** — REQ-UI-005, REQ-UI-006 *(build work landed under REQ-UI-048/049/050 + REQ-NFR-007)*
- [ ] **Implemented, awaiting verifier (67)** — see the checklist Status table

Counts: 139 rows. Terminal 67 (59 pre-existing + 8 N/A); `Implemented` 67; open 5.

## Known blockers

- **⚠ SECURITY — live GitHub PAT committed** in `nuget.config`; it is in git history, so it must be
  **revoked and reissued on GitHub** by the owner. Left in place by owner decision. → REQ-NFR-025.
- **⚠ Neither head starts without `JwtSigningKey` (≥32) and `AppEncryptionKey` (≥16)** — by design
  (REQ-NFR-027). Web host: user secrets locally, **production needs its own**. BlogApp: entered on its
  connection-setup screen and stored in the DPAPI `ConnectionStore`, and they must be **byte-for-byte the
  website's** — both heads read the same encrypted `SiteSetting` rows, so a mismatch fails *silently*.
  Rotating `AppEncryptionKey` makes existing ciphertext permanently undecryptable (no key versioning).
- **⚠ JWT signatures are never verified** — `SvcUtils.GetUserIDFromToken` decodes without validating;
  session validity is DB-backed. The JWT is a session *handle*, not a bearer credential.
- **⚠ Projection-completeness is a systemic gap — 4 instances found, gate NOT built.** A read projection
  omitting columns the write path persists; invisible to compiler and unit tests (the fakes never run
  SQL). REQ-UI-017, script 021, REQ-NFR-008 (`BlogPostRepo`), and REQ-FN-053 — the last two are the same
  stored function fixed hours apart. Fixing the fourth does not prevent a fifth.
- **Admin session dies on any full page load** — JWT is localStorage-only and invisible during prerender;
  automation must use `Blazor.navigateTo`, never `page.goto`.
- **Concurrency ceiling ~3.5 req/s** — REQ-NFR-026 stages 3–4 outstanding; logging volume fixed
  (65,761 → 144 bytes/request) but the load figure is **not re-measured**.
- **`source/BlogApp` not wired for `AppSecrets`** (throws on first `AppEncrypt` use); REQ-UI-052 is
  `⚠ STATIC-ONLY` — smoked on the web head only.
- **32 critical axe nodes on admin, all library-caused** (TR-054/055); 200% zoom and screen-reader passes
  uncovered. **Integration tests hang** (Testcontainers), excluded from the 383-test run.
- **`TagsList.razor` / `CommentsList.razor` headers are a reconstruction** after an agent's revert regex
  over-matched; they compile, render and smoke clean with all expected test ids — **owner should diff**.

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02 | Discovery (day-1) | Docs only | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Docs amendment | Docs only — BRD-92…97 added | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-06 | Mockups + design review | Docs only — 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07 | Unified *build-phase (11 clusters) | Build PASS claimed · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | Unified *build-phase (15 clusters) | Build was RED (15 errors masking 26) → PASS 0 errors · 383/383 tests · 12 REQs closed · REQ-FN-053 data-loss fixed · ~31 defects found · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-08 | Build repair (owner-reported RED) | Prior GREEN claim was wrong — gate never covered the BlogApp TFM. 4 × CS1061 in BlogApp AuthService fixed (REQ-UI-051); REQ-NFR-027 MAUI gap closed (secrets via DPAPI ConnectionStore). PASS 0 errors, 7/7 projects · web host `/` 200 + `/health` Healthy · BlogApp signed in to `/change-password` · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-056**. Root cause logged (TR-051): the AI reference and the `trblazeui`
  persona both promise all components splat unmatched attributes — false for **132 of 334** types
  (machine-classified via `MetadataLoadContext`). That one false premise caused seven page-killing
  `data-testid` crashes; full matrix contributed. A11y: TR-031/044/045 (app-side mitigated),
  TR-054/055 (Tabs/ItemGroup — not fixable from app code).
- **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-08)

- Underscore fields **0**; `obj`/Hungarian **0**; `a`/`v` prefixes remediated across `BlogModels`,
  `BlogEngine/{DbAccess,Services,Common}` and `SvcUtils` this pass.
- XML docs: **694 members newly documented**; `BlogModels` proves **0 doc warnings** under a forced
  `GenerateDocumentationFile` build. `BlogUI` + host still out of scope → REQ-NFR-008 at 92%.
- Build 0 errors / 178 warnings (up from 6 only because downstream projects now compile).

## Deferred / future

- Verify JWT signatures on read (`SvcUtils`/`AuthSvc`); build the projection-completeness gate.
- REQ-NFR-026 stages 3–4, then re-measure the concurrency ceiling.
- Wire `AppSecrets` into `source/BlogApp`; re-smoke REQ-UI-052 on the desktop head.
- Delete the `[Obsolete]` `AppUser.TwiiterUrl` alias once 3 `.razor` references move to `TwitterUrl`.
- macOS (`net10.0-maccatalyst`) BlogApp head — not buildable from the Windows host.
- Social login, magic links, drip sequences, lead magnets; admin theme creator; multi-tenancy, localization.
