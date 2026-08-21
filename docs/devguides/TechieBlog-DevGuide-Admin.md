# TechieBlog — Developer Guide · Admin

> ✅ **Runtime-verified 2026-08-09 as Admin and Editor** — supersedes the 2026-08-02 `STATIC-ONLY`
> banner, whose stated reason (solution does not compile, REQ-FN-043) is stale. Every screen below was
> exercised on **both heads**: the web host and the BlogApp desktop head.

Index: [TechieBlog-DevGuide.md](./TechieBlog-DevGuide.md)

## Runtime verification (2026-08-09)

Every count below was cross-checked against PostgreSQL **at the instant of measurement** (a start-of-run
snapshot proved unsafe — sibling agents moved the data mid-run).

| Screen | Observed | Detail |
|--------|----------|--------|
| `/admin` dashboard | **renders ✓ (runtime-confirmed)** | Posts 10, Users 4, Comments 16, Subscribers 7 — every tile an exact psql match. Needs-Attention 6/1/1 exact. Quick actions genuinely role-gated: an Editor is offered only the 2 non-`AdminOnly` destinations and both open without an access-denied bounce |
| `/admin` popular posts | **renders-empty (NO-DATA, downstream defect)** | shows an explicit empty state rather than a fabricated ranking — correct behaviour, but it can never populate because view tracking is dead code (`REQ-FN-034`) |
| `/users`, `/AddUser` | **renders ✓** | 4 rows with email + role badge, search narrows 4→1, all 7 create-form controls present. **Mutation half unproven** — the change-role Select's option list could not be driven, so role persistence is unverified |
| `/CommentsList` | **renders ✓** | 16/16 rows all cells populated, tabs exact vs psql, 26 per-row controls + bulk actions; delete dialog opened and **cancelled** |
| `/admin/categories`, `/admin/tags` | **renders ✓** | 5 and 15 rows; per-row counts sum to the published-only totals exactly (8 and 27); editors load populated; delete dialogs opened and **cancelled** |
| `/admin/subscribers` | **renders ✓** | 7 rows = psql, summary "7 total (6 active)" exact, CSV export produced a real download. **Gap:** no delete/remove control exists — `Unsubscribe` is reachable only from the public token |
| `/settings` | **renders ✓** | all six tabs render and every value equals its `SiteSetting` row; 21 controls checked, 0 blank. The TR-032 `TabsTrigger` crash is **gone**. At 390 the tabs wrap to two rows and Storage is reachable |
| theme selector | **renders ✓** | preview does not persist and does not write LocalStorage; after Save a **fresh anonymous context** received the saved site theme. Restored afterwards |
| `/admin/analytics` | **renders-empty (NO-DATA, downstream defect)** | rating and comment tiles carry real numbers and the date range provably moves them; Views/Unique are 0 and the trend, popular and category panels show empty states — because `postviews` is never written (`REQ-FN-034`) |
| `AdminLayout` | **renders ✓** | 6 group headings, 17 entries for Admin vs 10 for Editor — refused groups are **hidden, not rendered empty**; exactly one active highlight; account menu names the identity |
| `/admin/images` | **render-error (DEFECT)** | gallery and per-category validation work end to end (upload → serve → delete), but the **user-filter Select displays the raw value `0`** instead of its "All Users" label. Reproduced on both heads |
| `/admin/skills` | **render-error (DEFECT)** | 13 skills in 5 categories = psql, but the **admin user selector shows the raw id `1`** instead of a user name — same defect class as above |
| `/admin/experience`, `/admin/awards` | **render-error (DEFECT)** | lists, ordering, add/edit/delete and the user selector all render, but the acceptance-named **company-logo picker and badge-image picker do not exist** — both are plain text path inputs with 0 `ImagePicker` instances |
| `/admin/profile` | **visual-broken (DEFECT)** | all 10 fields match psql byte-for-byte and the **`REQ-FN-053` data-loss regression holds** (md5 over the nine at-risk columns identical across a no-edit save). At 390 `clear-image` **overlaps** `upload-new-image` and is invisible in the render |
| `/admin/newsletter` | **renders ✓ with a dead link (DEFECT)** | compose, preview, live audience estimate, send and per-recipient delivery log all work — but every message carries an unsubscribe link to `/unsubscribe/{token}`, which **404s with a zero-byte body**. No page is routed there |
| `/ManagePost` (see Author guide) | **render-error (DEFECT)** | the Markdown textarea **loses and reorders keystrokes**; saving with no category selected surfaces a **raw PostgreSQL FK violation** to the user |

**Dark mode:** measured, not eyeballed — contrast resolved through a 1-px canvas across 8 admin screens
(43–140 text nodes each): **0 nodes below WCAG AA**. **Icons:** 333 rendered `<svg>` nodes across 13
routes, **0 empty** — no Lucide alias-name misses remain.

An Admin sees every screen in the app. The eleven below are guarded by
`@attribute [Authorize(Policy = "AdminOnly")]` and use `AdminLayout`.

## Admin · Users (`/users`)

**File:** `source/BlogUI/Pages/AdminPages/UsersList.razor` + `.razor.cs` — **injects the repository
directly; there is no user service.**

| Control | What it does | Source call | Render status |
|---------|--------------|-------------|---------------|
| User table | Lists all users | `BlogUserRepo.GetAll(...)` | static-only (unconfirmed) |
| Role change / enable-disable | Persists the edited user | `BlogUserRepo.Update(...)` | static-only (unconfirmed) |

**Data lineage:** page → `IBlogUserRepo` → `BlogUserRepo.cs` → `SELECT * FROM BlogUser` (and the
stored function `SelectBlogUserById` for single reads) / inline `UPDATE BlogUser`.

**Known issues (static):** Story 2.6 specifies search/filter, last-login display, delete with
confirmation and an audit-log entry per admin action. Only list + update were found —
`{unresolved — TODO: confirm whether search/delete exist in the markup}`. No audit-log write was
located anywhere in the codebase.

## Admin · Add user (`/AddUser`)

**File:** `source/BlogUI/Pages/AdminPages/AddUser.razor` — `@inject BlogModels.IBlogUserRepo UserRepo`

| Control | Source call | Render status |
|---------|-------------|---------------|
| Create user form | `{unresolved — TODO: the insert call was not located in the page; BlogUserRepo exposes Insert / InsertToGetId and the stored function InsertBlogUser}` | unresolved |

**Known issue (static):** if this page writes a password, confirm it hashes through
`AppEncrypt.CreateHash` rather than storing plaintext — the seed script sets a plaintext password
(REQ-NFR-023, ⚠ SECURITY), so the pattern is already present in the repo.

## Admin · Categories (`/admin/categories`, `/CategoriesList`) and category editor (`/admin/category`, `/admin/category/{PageId:long}`)

**Files:** `Pages/AdminPages/CategoriesList.razor`, `Pages/AdminPages/ManageCategory.razor` + `.razor.cs`

| Screen | Control | Source call |
|--------|---------|-------------|
| CategoriesList | Table with post counts | `CategoryService.GetAllWithCounts()` |
| CategoriesList | Delete | `CategoryService.DeleteCategory(...)` |
| ManageCategory | Load | `CategoryService.GetCategory(PageId)` |
| ManageCategory | Save | `CategoryService.SaveCategory(...)` |

**Lineage:** page → `CategorySvc` → `CategoryRepo` → `INSERT INTO Category`, `UPDATE Category`,
`DELETE FROM Category`, `SELECT c.CategoryId …` joined for counts.

## Admin · Tags (`/admin/tags`, `/TagsList`) and tag editor (`/ManageTag`, `/ManageTag/{PageId:long}`)

**Files:** `Pages/AdminPages/TagsList.razor`, `Pages/AdminPages/ManageTag.razor`

| Screen | Control | Source call |
|--------|---------|-------------|
| TagsList | Table with post counts | `TagService.GetAllWithCounts()` |
| TagsList | Delete | `TagService.DeleteTag(...)` |
| ManageTag | Load | `TagService.GetSingleTag(PageId)` |
| ManageTag | Slug | `SlugGenerator.GenerateSlug(...)` (static helper, called in the page) |
| ManageTag | Save | `TagService.SaveTag(...)` |

**Lineage:** page → `TagSvc` → `BlogTagRepo` → `INSERT INTO Tag`, `DELETE FROM Tag`,
`INSERT INTO PostTag`, `DELETE FROM PostTag`, `SELECT t.TagId …` with the `COUNT` that Story 7.5 fixed.

**Known issue (static):** the tag-merge capability indicated in mockup `26-admin-tags.html` was not
found. `{unresolved — TODO: confirm whether merge exists.}`

## Admin · Subscribers (`/admin/subscribers`)

**File:** `source/BlogUI/Pages/AdminPages/SubscribersList.razor`

| Control | What it does | Source call | Render status |
|---------|--------------|-------------|---------------|
| Subscriber table | Lists subscribers | `SubscriberService.GetAllSubscribers()` | static-only |
| Status change | Activate / unsubscribe | `SubscriberService.UpdateSubscriberStatus(...)` | static-only |
| Export | CSV download via JS interop | `SubscriberService.GetSubscribersForExport()` + `IJSRuntime` | static-only |

**Lineage:** page → `SubscriberSvc` → `SubscriberRepo` → `INSERT INTO Subscriber`,
`UPDATE Subscriber`, `SELECT SubscriberId …`.

**Known issue (static):** no newsletter composer or send action exists on this page — the "Newsletter
management" claim in the migrated plan (Story 7.7) covers the subscriber list only. Newsletter
composition and SMTP delivery are REQ-UI-043 / REQ-FN-032 / REQ-FN-033, all Not Started.

## Admin · Media library (`/admin/images`)

**File:** `source/BlogUI/Pages/AdminPages/ManageImages.razor` + `.razor.cs`

```mermaid
flowchart LR
  MI["ManageImages.razor.cs"] --> IS["IBlogImageService"]
  IS --> Disk[/"wwwroot/uploads/{category}"/]
  IS --> IR["BlogImageRepo"]
  IR --> DB[("blogimage")]
  MI --> UR["IBlogUserRepo.GetAll — user filter"]
```

| Control | What it does | Source call | Render status |
|---------|--------------|-------------|---------------|
| Gallery by category | Lists images | `ImageService.GetImagesByCategoryAsync(...)` | static-only |
| Upload | Validates then stores | `ImageService.ValidateImageAsync(...)` → `UploadImageAsync(...)` | static-only |
| Delete | Removes row and file | `ImageService.DeleteImageAsync(...)` | static-only |
| Copy URL | Public path for an image | `ImageService.GetImageUrl(...)` | static-only |
| User filter | Owner selection | `UserRepo.GetAll(...)` | static-only |

**Lineage:** page → `IBlogImageService` (`BlogEngine/Services/BlogImageService.cs`) → disk write under
`source/BlogUI/wwwroot/uploads/{category}/` **and** `BlogImageRepo` → `INSERT INTO blogimage` /
`UPDATE blogimage` / `SELECT * FROM blogimage`.

**Business rules:** seven categories with per-category size and format limits (profiles 2 MB; logos,
awards 500 KB; icons 200 KB; blog, general 5 MB; cv 10 MB / PDF only). Filenames are
`{category}_{userId}_{timestamp}_{guid}.{ext}`.

**Known issues (static):**
1. The screen is `AdminOnly` while `ImagePicker` (which uploads through the same service) is used on
   `AuthorOrAbove` pages — Authors can create uploads they can never browse or delete (REQ-UI-034).
2. Files live on local disk with no storage abstraction (REQ-FN-042) — a container redeploy without a
   mounted volume loses every upload.
3. Thumbnail generation (feature-ideation §2.3 item 4) was not found.
   `{unresolved — TODO: confirm whether thumbnails are generated.}`

## Admin · Site settings (`/settings`)

**File:** `source/BlogUI/Pages/AdminPages/Settings.razor`

```mermaid
flowchart LR
  ST["Settings.razor"] --> TS["ThemeService"]
  ST --> LS[/"browser localStorage"/]
  TS --> LS
  ST -.->|"never reached"| DB[("database")]
```

| Control | What it claims to do | What it actually does | Render status |
|---------|----------------------|-----------------------|---------------|
| Theme selector | Set the **site** theme | `ThemeService.SetThemeAsync(...)` → writes `techieblog-theme` to **browser local storage** (`Common/ThemeService.cs:46`) | **DEFECT (static) — per-visitor, not site-wide** |
| Pagination word count | Persist a blog setting | `LocalStorage.SetItemAsync(PaginationWordCountKey, ...)` (`Settings.razor:335`) | static-only — browser-scoped |
| General settings (site title, tagline) | Persist | **nothing** — `// TODO: Implement actual save to database for other settings when settings service is created` (`Settings.razor:337`) | **DEFECT (static) — silently discarded** |
| Blog settings (posts per page, comment moderation) | Persist | **nothing** — same TODO | **DEFECT (static) — silently discarded** |
| SEO settings | Persist | **nothing** — same TODO | **DEFECT (static) — silently discarded** |
| Social media settings | Persist | **nothing** — same TODO | **DEFECT (static) — silently discarded** |
| Save button | Confirms success | Sets `StatusMessage = "Settings saved successfully."` regardless (`Settings.razor:338-339`) | **DEFECT (static) — false success message** |

**This is the second-highest-value finding.** The page presents five settings sections and reports
success, but only the pagination word count is written — to the current browser's local storage, not
the database. `LoadDefaultSettings()` even carries the comment
*"In a real implementation, other settings would be loaded from a database"* (`Settings.razor:325`).
There is no `SiteSettings` table in any migration and no settings repository or service in
`BlogEngine`. Logged to REQ-FN-040 (dropped from `In Progress 90%`) and REQ-UI-026.

**Knock-on effects:**
- BRD-68 ("admin selects the site theme") is not met — the selection is per-visitor.
- BRD-69 (site title, tagline, posts-per-page, SMTP, storage settings) is not met.
- The comment-moderation toggle has no persistent home, so whatever gates comment approval at runtime
  is `{unresolved — TODO}`.

**Fix sketch:** add a `SiteSettings` table + migration, a `SettingsRepo` and `SettingsSvc` (both are
named in the archived architecture doc §5.1/§7.2 but were never built), load through them at startup,
and cache per the caching design (REQ-NFR-018).

## Admin · Resume data screens

`/admin/profile`, `/admin/experience`, `/admin/skills`, `/admin/awards` are `AuthorOrAbove` and are
documented in the [Author guide](./TechieBlog-DevGuide-Author.md). For Admins these pages additionally
render a **user selector** fed by `UserRepo.GetAll(...)`, so an Admin can edit any user's resume data.

The missing `ManageStats` screen (for `UserStats`, whose repository *is* registered at
`BlogSvcInitializer.cs:69`) is also covered there.

## Admin · Everything else

Admins inherit the Editor screens (dashboard, comment moderation, all posts — see the
[Editor guide](./TechieBlog-DevGuide-Editor.md)), the Author screens, and every public screen.

## Admin-relevant cross-cutting risks

| Risk | Where | REQ |
|------|-------|-----|
| Seeded admin password is plaintext | `source/BlogDb/PostgresScripts/003-SeedData.sql:59` | REQ-NFR-023 |
| Password hashing is hand-rolled | `source/BlogModel/Common/AppEncrypt.cs:93` | REQ-NFR-002 |
| No rate limiting on login | `source/TechieBlog/Program.cs` (no rate-limit middleware) | REQ-NFR-005 |
| No audit log for admin actions | nowhere | REQ-FN-010 |
| DbUp runs with DDL rights at every startup | `Program.cs:110-135` | operational note |

---
Generated 2026-08-02 · reflects code as built · ⚠ STATIC-ONLY
