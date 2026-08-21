# REQ-NFR-008 — `source/BlogEngine/DbAccess/` partition

XML-documentation pass over all 24 repositories in `source/BlogEngine/DbAccess/`, to the standard in
`docs/TechieBlog-Coding-Standards.md` §"XML Documentation (MANDATORY on public members)". House voice
taken from `CategoryRepo` and `NewsletterRepo`.

## Numbers

| Metric | Count |
|---|---|
| Files in partition | 24 (all reviewed) |
| Files edited | 12 |
| Public members total | 651 |
| Members documented this pass | 62 |
| Members already conforming | 589 |
| Class-level `<remarks>` blocks materially extended | 4 |

Of the 62: **44** carried only `/// <inheritdoc />` (no summary, no remarks, no per-member
Business Logic / Flow / Side Effects) and were written from scratch; **18** had a summary but a
`<remarks>` block missing its **Business Logic** section.

`<inheritdoc />` was removed entirely from the partition. It is the wrong tool here — the interface
states the contract, but which table, which stored function, which columns, which ordering and which
null semantics are properties of the *implementation*, and that is the documentation this pass exists
to add. The two reference files (`CategoryRepo`, `NewsletterRepo`) use none.

### Per file

| File | Members documented | Note |
|---|---|---|
| `BlogCommentRepo.cs` | 17 | all were `<inheritdoc />` |
| `PostRatingRepo.cs` | 9 | all were `<inheritdoc />` |
| `SubscriberRepo.cs` | 8 | all were `<inheritdoc />` |
| `UserCredentialRepo.cs` | 7 | missing `<remarks>` on the refusing + delegating members |
| `EmailVerificationTokenRepo.cs` | 5 | all were `<inheritdoc />` |
| `PasswordResetTokenRepo.cs` | 5 | `<remarks>` present, **Business Logic** absent |
| `UserLoginRepo.cs` | 4 | + class-level `Usage` and projection section added |
| `VerifiedEmailRepo.cs` | 4 | all were `<inheritdoc />` |
| `BlogUserRepo.cs` | 2 | + class-level `Usage` and projection section added |
| `BlogImageRepo.cs` | 1 | was `<inheritdoc />` |
| `LoginLogRepo.cs` | 0 | class-level: password-exclusion + column-width sections added |
| `BlogPostRepo.cs` | 0 | class-level: projection matrix + write-back warning added |

The remaining 12 files were audited and already conformed.

## Standards compliance

Audited and **clean** across all 24 files — nothing to fix:

- No `_underscore` fields, no `obj`/`str`/`int`/`bln` Hungarian prefixes, no `a`/`v`-prefixed
  parameters or locals. (`aSingleId`, `aEntity`, `vResult`, `vConn` had all been cleaned by the
  REQ-NFR-026 pass.)
- File-scoped namespaces present in all 24.
- Instance fields bare camelCase.

One cosmetic fix: a typo (`A over-long` → `An over-long`) in `LoginLogRepo.Truncate`.

## SQL injection (REQ-NFR-003)

**No risks found.** Every statement binds through `DynamicParameters` or an anonymous parameter
object. Three constructs look like concatenation and are not:

- `BlogCommentRepo.SetModerationStatusBulkAsync` composes with `string.Format`, but the only
  substituted value is one of two compile-time constants chosen by a `bool`. Documented as such.
- Several `$"…"` interpolations build SQL constants from *other constants* at compile time
  (`NewsletterRepo`, `PostViewRepo`, `AnalyticsRepo`).
- `$"%{query}%"` in the search members interpolates into a *parameter value*, never into SQL text.

## Defects

### FIXED in partition — projection omission causing silent data loss

`source/BlogEngine/DbAccess/BlogPostRepo.cs:115` (`SelectByIdSql`) and `:129` (`SelectBySlugSql`)
did not project `PublishedOn` or `ScheduledPublishOn`, while `UpdateSql` writes both columns
unconditionally from the entity handed to it.

An earlier fix (REQ-UI-017) added those columns to `SelectAllSql` and `SelectAllByUserSql` but not to
the by-id and by-slug lookups, so the projections had drifted.

Impact, confirmed against the live database:

1. **Render.** A scheduled post opened in the editor loaded with `ScheduledPublishOn == null`, so
   `BlogPost.IsScheduled` was false and the status badge read **"Draft"** for a row plainly carrying
   a future publish date. The schedule pickers came up empty.
2. **Data loss.** Every read-modify-write in `BlogSvc` — `PublishPostAsync`, `UnpublishPostAsync`
   (`BlogSvc.cs:1361`), `QuickPublishAsync` (`:1402`), `SchedulePostAsync` (`:1528`) — loads through
   `GetSingleAsync` and saves through `UpdateAsync`. The unprojected columns returned `null` and the
   update **stored that null**: unpublishing a post permanently erased its first-publication date,
   and saving a scheduled post silently cancelled its schedule.
3. `QuickPublishAsync`'s `if (!post.PublishedOn.HasValue)` guard — whose XML doc claims "the first
   publication date is preserved" — could never see a value, so every re-publish reset `PublishedOn`
   to now. That documented behaviour is now actually true.

Fix: both statements now project `p.PublishedOn, p.ScheduledPublishOn`, matching `SelectAllSql`
column for column. No other file changed.

### Reported, NOT fixed — narrow projections that are correct-but-dangerous

| Location | Issue | Impact |
|---|---|---|
| `BlogPostRepo.cs:97` `SelectPagedSql` | No JOIN, so no `BlogWriter`; also omits `DeletedOn`, `PublishedOn`, `ScheduledPublishOn`, `SeriesId`, `SeriesPartNumber` | A post read this way reports Author "Unknown" and `Status` "Draft" regardless of the row. Writing one back would destroy the same columns. Left alone because fixing it needs a new JOIN, i.e. a behaviour change beyond a doc pass. |
| `BlogPostRepo.cs:140` `SelectPublishedSql` | Omits `PublishedOn` (also `IsDeleted`, `DeletedOn`, `SeriesId`, `SeriesPartNumber`) | A public listing built on it must date posts by `CreatedOn`, not by when they went live. |
| `SubscriberRepo.cs:35` and the 4 other read constants | **No read in this repository projects `UnsubscribeToken`**, though `Subscriber.UnsubscribeToken` exists and the column is created + backfilled by migration 015 | Latent, not live: nothing currently builds an unsubscribe link from a `SubscriberRepo` read — the newsletter send path uses `NewsletterRepo`, whose projection does include it (`NewsletterRepo.cs:72`). Any future caller would silently get an empty token. |
| `SubscriberRepo.cs:35` vs `:53`/`:60`/`:67`/`:75` | `IsActive` is derived inconsistently: `COALESCE(IsConfirmed, TRUE)` in the base columns, `TRUE` hard-coded in `SelectActiveSql`, bare `IsConfirmed` in the other three | A legacy row with NULL `IsConfirmed` is reported active by `GetAll`/`GetSingle`/`GetByEmail` but is excluded from `GetActiveSubscribers`, `GetByStatus`, `SearchByEmail` and both counts. Low impact — the column defaults to FALSE and every insert binds it — so documented in place rather than changed. |

All four are now described explicitly in the XML docs, which is the documentation that prevents a
recurrence.

### Checked and clean

- No table referenced by this folder is missing from the migrations. (`BlogPost` is created as `Post`
  in `001-CreateTables.sql:206` and renamed by `004-FixPostTable.sql:23` — not a defect.)
- No `SvcToken` references remain (REQ-FN-052 deletion is complete in this partition).
- No `DELETE` or `UPDATE` without a `WHERE`.
- No `…Async` member that is secretly synchronous. `LoginLogRepo.UpdateLogOutAsync` returns a
  completed task, but it does no I/O by design — the schema has no sign-out column — and its doc says
  so.

## Build, tests and smoke

- `~/.dotnet/dotnet build TechieBlog.slnx` → **0 errors, 13 warnings** (unchanged; no warning added).
  Several intermediate runs showed `MSB3030` file-copy errors caused by sibling agents rebuilding
  shared outputs concurrently — waited and rebuilt, per instruction.
- `~/.dotnet/dotnet test --filter "FullyQualifiedName!~Integration"` → **383 passed, 0 failed**
  (baseline held).
- **Self-smoke:** `tests/verify/cluster-dbaccess-post-projection.spec.ts`, **2/2 passed**. The host
  was booted by this agent and driven with headless Playwright against the live migrated PostgreSQL
  in `techieblog-pg`.
  - RENDER-TRUTH: `/ManagePost/17` renders Status **"Scheduled"**, "for Aug 21, 2026 6:18 AM",
    Publish Date "August 21, 2026", Publish Time "06:18 AM" — cross-checked against a live
    `SELECT scheduledpublishon FROM BlogPost WHERE postid=17` = `2026-08-21 06:18:31`, injected into
    the spec rather than hard-coded. The public article page still renders after the same change to
    `SelectBySlugSql`.
  - VISUAL-TRUTH: 1280 and 390, no horizontal page scroll at either width. Screenshots in
    `test-results/cluster-dbaccess/`.
  - **Counterfactual run.** The fix was temporarily reverted, rebuilt and re-smoked: the spec
    **failed** with `Received string: "Draft"`. Restored, rebuilt, re-smoked green. That is what
    separates a passing test from a test that proves the fix — a green build and a green unit suite
    were both green while the defect was live, because the fakes under `tests/unit/` never execute
    the SQL.
- **DB left as found.** The seeded Admin's `MustChangePassword` was cleared to reach the admin
  surface and **re-armed to TRUE** afterwards; all four seeded accounts verified `t` by re-select.
  No password altered, no account invented, no fixture rows written. Post 17's schedule and post 1's
  publication date verified unchanged after the run.

## Not touched

`source/BlogModel/Interfaces/*`, `source/BlogEngine/{Services,Common,DaCore,Storage}/*`, the
`REQ-NFR-008` checklist row (shared by eight agents), and the sync twins' role as a temporary surface
— documented throughout as deleted by REQ-NFR-026 stage 4, with the `…Async` members named as the
replacement.
