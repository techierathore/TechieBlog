# TechieBlog — Developer Guide · Author

> ✅ **Runtime-verified 2026-08-09 as Author (and Admin, for the scoping comparison)** — supersedes the
> 2026-08-02 `STATIC-ONLY` banner, whose stated reason (solution does not compile, REQ-FN-043) is stale.

Index: [TechieBlog-DevGuide.md](./TechieBlog-DevGuide.md)

## Runtime verification (2026-08-09)

| Screen / control | Observed | Detail |
|------------------|----------|--------|
| `/BlogsList` | **renders ✓** | the 2026-08-02 note that an Author "cannot reach any post list" is **resolved**: the page is `AuthorOrAbove` with server-side scoping. Author sees exactly her own 2 rows, all authored "Arun Nair", **0 "Unknown" cells**, published rows showing `PublishedOn` not `CreatedOn`. Admin sees 10 with tabs All 10 / Published 8 / Drafts 1 / **Scheduled 1** — the Scheduled tab that could once only show 0 now works |
| `/BlogsList` tab strip | **visual-broken (DEFECT)** | at 390 the "Scheduled (1)" tab measures `right=411` in a 390px viewport with `overflow-x:visible`, so 21px is **clipped rather than scrollable** and the count digit is cut. Still operable. 1280 clean |
| `/ManagePost` Markdown body | **renders ✓** (was a DEFECT; **fixed 2026-08-09**, re-proved 2026-08-11) | The textarea used to lose and reorder keystrokes — a 15-character string retained only 3–12 characters — because TrBlazeUI's `<Textarea>` is a CONTROLLED input and every keystroke round-tripped, the returning render writing a stale value into the DOM. `PostMarkdownEditor` now uses an UNCONTROLLED raw `<textarea>` seeded once via `editorSeed`/`editorRevision` (library gap TR-057; do NOT "restore" `<Textarea>`). Re-measured 2026-08-11 after the REQ-UI-016 reload fix: `## Live heading` typed one key at a time at 40ms and 120ms per key arrived exact, both immediately and 2.5s later |
| `/ManagePost/{id}` route change | **renders ✓** (was a DEFECT; **fixed 2026-08-11**) | Switching posts client-side used to leave the previous post's fields on screen — see the corrected note below the table. Now every bound field reloads and a save after a switch touches only the post in the URL |
| `/ManagePost` category | **render-error (DEFECT)** | saving with the dropdown on its "-- Select Category --" default writes `CategoryId=0` and surfaces the **raw database error** `23503: ... violates foreign key constraint "blogpost_categoryid_fkey"` to the user, with no row saved. Reproduced in all 4 runs and independently on the desktop head |
| `/ManagePost` schedule | **renders ✓** | scheduling persisted and the **background publisher was proved end to end** — a row set due was picked up by `ScheduledPostPublisher`'s minute tick, which flipped `published=true` and cleared `scheduledpublishon`. Note `data-testid="publish-date-picker"` never reaches the DOM (TrBlazeUI `DatePicker` drops unmatched attributes) |
| `/ManagePost` tags / series / featured image | **renders ✓** | inline tag creation wrote the tag plus exactly 1 junction row; series selector auto-assigned the next part number; picker present |
| `/admin/preview/{id}` | **renders ✓** | draft renders in full with the "not published" banner, author, reading time and 655 chars of Markdig HTML |
| `/admin/series`, `ManageSeries` | **renders ✓** | both series with real authors and part badges matching the published-only count; full create/update/delete round trip returned the count to 2 |
| slug generation | **renders ✓** | auto-generated from the title; a second post with an identical title produced a distinct `-2` slug |

**Cross-head note:** a post authored and published in the **BlogApp desktop head** appeared immediately
on the **web host** (separate process, same database), proving the shared `BlogUI`/`BlogEngine` write path.

**CORRECTED 2026-08-11 — this was never a harness trap, it was a product defect (REQ-UI-016).** The
entry here used to read *"`Blazor.navigateTo('/ManagePost')` from `/ManagePost` is a no-op, so the
editor keeps the post it just saved and a 'new post' silently becomes an update"* and blamed the
test harness. **The harness was innocent.** `ManagePost` loaded its post in `OnInitializedAsync`,
which the Blazor router runs **once per visit** to the editor, so no route-parameter change ever
re-read the row. Navigating from `/ManagePost/5` to `/ManagePost/6` left post 5's title, slug, body
and **every** metadata sidebar field on screen under post 6's URL — and a save from that state wrote
post 5's content over post 6. That is data corruption on the ordinary user path "edit post A, then
edit post B", with no test harness anywhere near it.

**Fixed 2026-08-11:** the per-post load moved to `OnParametersSetAsync`, guarded by a `loadedPostId`
field so it re-reads exactly when the route parameter changes; `ClearPostFields()` resets every
per-post field first; and `ManagePost` now hands `PostMarkdownEditor` a `ResetKey` that releases the
editor's "user has typed" latch (the TR-057 keystroke fix) when — and only when — the document
changes. Verified live: switching `/ManagePost/5` → `/ManagePost/7` → `/ManagePost/5` reloaded all
ten bound fields against psql truth, a save after the switch changed post 7's row only and left post
5 byte-for-byte identical, and typing 15 characters after a switch still yielded all 15 in order.

An Author sees every Reader screen plus the ten below, all guarded by
`@attribute [Authorize(Policy = "AuthorOrAbove")]` (Admin, Editor, Author) and rendered in `AdminLayout`.

## Author · Post editor (`/ManagePost`, `/ManagePost/{PageId:long}`)

**File:** `source/BlogUI/Pages/AdminPages/ManagePost.razor` + `.razor.cs` — the single richest screen in
the app, with six injected dependencies.

```mermaid
flowchart TB
  MP["ManagePost.razor.cs"] --> B["BlogSvc"]
  MP --> C["CategorySvc"]
  MP --> T["TagSvc"]
  MP --> S["SeriesSvc"]
  MP --> MD["MarkdownEditor.razor"]
  MP --> IP["ImagePicker.razor"]
  B --> BR["BlogPostRepo"]
  C --> CR["CategoryRepo"]
  T --> TR["BlogTagRepo"]
  S --> SR["BlogSeriesRepo"]
  BR --> DB[("PostgreSQL")]
  TR --> DB
  CR --> DB
  SR --> DB
```

| Control | What it does | Source call | Render status |
|---------|--------------|-------------|---------------|
| Load existing post | Fetch for edit | `BlogService.GetSinglePost(PageId)` | static-only (unconfirmed) |
| Title + Markdown body | Editing surface with live preview | `Components/MarkdownEditor.razor` → `MarkdownRenderer` | static-only (unconfirmed) |
| Category dropdown | Choose one category | `CategoryService.GetAllCategories()` | static-only (unconfirmed) |
| Tag input | Autocomplete + inline create | `TagService.GetAllTags()`, `GetOrCreateTag(...)` | static-only (unconfirmed) |
| Tag persistence | Replace the post's tag set | `TagService.GetTagsForPost(...)`, `SetTagsForPost(...)` | static-only (unconfirmed) |
| Series selector | Attach to a series with a part number | `SeriesService.GetAllWithCounts()`, `GetNextPartNumber(...)` | static-only (unconfirmed) |
| Featured image | Pick or upload | `Components/ImagePicker.razor` → `IBlogImageService` | static-only (unconfirmed) |
| Save draft | Persist without publishing | `BlogService.SaveDraft(...)` | static-only (unconfirmed) |
| Save | Persist | `BlogService.SavePost(...)` | static-only (unconfirmed) |
| Publish | Set published state | `BlogService.PublishPost(...)` | static-only (unconfirmed) |
| Unpublish | Back to draft | `BlogService.UnpublishPost(...)` | static-only (unconfirmed) |
| Schedule | Set a future publish time | `BlogService.SchedulePost(...)` | static-only (unconfirmed) |
| Cancel schedule | Revert to draft | `BlogService.CancelSchedule(...)` | static-only (unconfirmed) |

**Data lineage**

| Step | Symbol | File |
|------|--------|------|
| Page | `ManagePost.razor.cs` `[Inject] BlogSvc BlogService`, `CategorySvc`, `TagSvc`, `SeriesSvc`, … | `Pages/AdminPages/ManagePost.razor.cs` |
| Services | `BlogSvc.SavePost/SaveDraft/PublishPost/UnpublishPost/SchedulePost/CancelSchedule` | `BlogEngine/Services/BlogSvc.cs` |
| | `TagSvc.GetOrCreateTag/GetTagsForPost/SetTagsForPost` | `BlogEngine/Services/TagSvc.cs` |
| Data access | `BlogPostRepo` → `INSERT INTO BlogPost` / `UPDATE BlogPost`; `BlogTagRepo` → `INSERT INTO Tag`, `INSERT INTO PostTag`, `DELETE FROM PostTag` | `BlogEngine/DbAccess/` |

**Business rules:** a post starts as Draft; publish sets the published flag and date; scheduling stores
`ScheduledFor` and leaves the post unpublished until `ScheduledPostPublisher` promotes it.

**Known issues (static):**
- Slug generation for **posts** was not located in this page — `ManageTag.razor` calls
  `SlugGenerator.GenerateSlug` explicitly, but `ManagePost.razor.cs` does not.
  `{unresolved — TODO: confirm whether BlogSvc.SavePost generates the slug server-side, and what happens on a slug collision (BRD-15 requires uniqueness).}`
- The editor's auto-save (PRD Story 3.2 AC7, "every 30 seconds") was not found in the page.
  `{unresolved — TODO: confirm whether auto-save exists.}`

## Author · Post list (`/BlogsList`)

**File:** `source/BlogUI/Pages/AdminPages/BlogsList.razor` + `.razor.cs` · **Guard:** `EditorOrAbove`
(note: *not* `AuthorOrAbove` — an Author cannot open their own post list; see Known issues)

| Control | Source call | Render status |
|---------|-------------|---------------|
| Post table | `BlogService.GetAllPosts(...)` | static-only |
| Publish action | `BlogService.QuickPublish(...)` | static-only |
| Unpublish action | `BlogService.UnpublishPost(...)` | static-only |
| Cancel schedule | `BlogService.CancelSchedule(...)` | static-only |
| Delete | `BlogService.DeletePost(...)` | static-only |

**Known issue (static):** `BlogsList.razor:11` declares `[Authorize(Policy = "EditorOrAbove")]`, so the
**Author role cannot reach the post list at all** even though `ManagePost` is `AuthorOrAbove`. Either the
list should be `AuthorOrAbove` with a per-author filter, or the "My Posts" screen from mockup
`18-my-posts.html` is genuinely missing. Logged to REQ-UI-017.

## Author · Draft preview (`/admin/preview/{PostId:long}`)

**File:** `source/BlogUI/Pages/AdminPages/PreviewPost.razor`

| Control | Source call | Render status |
|---------|-------------|---------------|
| Rendered post | `BlogService.GetSinglePost(PostId)` + `MarkdownRenderer.ToHtml(...)` | static-only |
| Reading time | `ReadingTimeCalculator.Calculate(...)` | static-only |
| Publish from preview | `BlogService.QuickPublish(...)` | static-only |

**Business rules:** shows unpublished content, so the `AuthorOrAbove` guard is the only thing keeping a
draft private — there is no per-author ownership check visible on this page
(`{unresolved — TODO: confirm whether GetSinglePost filters by author}`).

## Author · Series list (`/admin/series`, `/SeriesList`) and editor (`/admin/series/new`, `/admin/series/{PageId:long}`)

**Files:** `Pages/AdminPages/SeriesList.razor`, `Pages/AdminPages/ManageSeries.razor` + `.razor.cs`

| Screen | Control | Source call |
|--------|---------|-------------|
| SeriesList | Series table with counts | `SeriesService.GetAllWithCounts()` |
| SeriesList | Delete | `SeriesService.DeleteSeries(...)` |
| ManageSeries | Load series | `SeriesService.GetSeries(PageId)` |
| ManageSeries | Parts list | `SeriesService.GetPostsInSeries(...)` |
| ManageSeries | Save | `SeriesService.SaveSeries(...)` |

**Lineage:** page → `SeriesSvc` → `BlogSeriesRepo` → `INSERT/UPDATE/DELETE BlogSeries`,
`SELECT s.SeriesId …` joined to `BlogPost`.

## Author · Manage profile (`/admin/profile`)

**File:** `source/BlogUI/Pages/AdminPages/ManageProfile.razor` + `.razor.cs` — **injects `IBlogUserRepo`
directly; there is no user service.**

| Control | Source call | Render status |
|---------|-------------|---------------|
| Load profile | `UserRepo.GetSingle(userId)` | static-only |
| Username availability | `UserRepo.IsUsernameAvailable(...)` | static-only |
| Save username | `UserRepo.UpdateUsername(...)` | static-only |
| Save basic info + socials | `UserRepo.Update(...)` | static-only |
| Save resume fields (title, tagline, phone, location, CV, ResumeEnabled) | `UserRepo.UpdateResumeFields(...)` | static-only |
| Avatar / CV pickers | `Components/ImagePicker.razor` → `IBlogImageService` | static-only |

**Lineage:** page → `BlogUserRepo` (`BlogEngine/DbAccess/BlogUserRepo.cs`) → inline `UPDATE BlogUser`
against the columns added by migration `012-ResumeAndImageManagement.sql`.

**Note:** `ManageProfile.razor.cs` is one of the 17 files still carrying `_underscore`-prefixed fields
(REQ-NFR-021).

## Author · Manage experience (`/admin/experience`, `/admin/experience/{EventId:long}`)

**File:** `Pages/AdminPages/ManageExperience.razor` + `.razor.cs`

| Control | Source call | Render status |
|---------|-------------|---------------|
| Entry list | `EventRepo.GetByUserAndType(userId, "Experience")` | static-only |
| Load one | `EventRepo.GetSingle(eventId)` | static-only |
| Create | `EventRepo.InsertToGetId(...)` | static-only |
| Update | `EventRepo.Update(...)` | static-only |
| Delete | `EventRepo.Delete(...)` | static-only |
| User selector (Admin only) | `UserRepo.GetAll(...)` | static-only |
| Company logo | `ImagePicker` (category `logos`) | static-only |

**Lineage:** page → `IUserEventRepo` → `UserEventRepo.cs` → inline SQL on `userevents` (note the
lower-case table name in the SQL — PostgreSQL folds unquoted identifiers, so this matches `UserEvents`).

**Business rules:** experience rows are `UserEvents` discriminated by `Type = "Experience"`;
`IsCurrent` renders as "Present"; `DisplayOrder` drives manual ordering.

## Author · Manage skills (`/admin/skills`)

**File:** `Pages/AdminPages/ManageSkills.razor` + `.razor.cs`

| Control | Source call |
|---------|-------------|
| Skills grouped by category | `SkillsRepo.GetByUserId(userId)` |
| Load one | `SkillsRepo.GetById(...)` |
| Create / update / delete | `SkillsRepo.Create/Update/Delete(...)` |
| User selector (Admin) | `UserRepo.GetAll(...)` |

**Lineage:** page → `IUserSkillsRepo` → `UserSkillsRepo.cs` → `INSERT/UPDATE/DELETE userskills`,
`SELECT DISTINCT` for the category list.

## Author · Manage awards (`/admin/awards`)

**File:** `Pages/AdminPages/ManageAwards.razor` + `.razor.cs`

| Control | Source call |
|---------|-------------|
| Award list | `AwardsRepo.GetByUserId(userId)` |
| Load one | `AwardsRepo.GetById(...)` |
| Create / update / delete | `AwardsRepo.Create/Update/Delete(...)` |
| Badge image | `ImagePicker` (category `awards`) |
| User selector (Admin) | `UserRepo.GetAll(...)` |

**Lineage:** page → `IUserAwardsRepo` → `UserAwardsRepo.cs` → `INSERT/UPDATE/DELETE userawards`.

## Author · Manage stats — MISSING

`docs/OldDocs/feature-ideation-images-resume.md` §1.3 and the resume page both expect a **UserStats** editor
(`ManageStats.razor`), and `IUserStatsRepo` **is registered** in `BlogSvcInitializer.cs:69`. No admin
page exists to maintain those rows — the "About" and "Community" statistics on `/resume` can only be
populated by direct SQL. Logged to REQ-UI-037/REQ-FN-027 as a gap.

## Author · Media library (`/admin/images`)

Guarded `AdminOnly`, so an Author **cannot** open the library — but the `ImagePicker` component they use
inside the editor and profile pages talks to the same service. See the Admin guide for the screen.

`Components/ImagePicker.razor.cs` calls `ImageService.ValidateImageAsync`, `UploadImageAsync` and
`GetImagesByCategoryAsync` on `IBlogImageService` (`BlogEngine/Services/BlogImageService.cs`), which
writes to `wwwroot/uploads/{category}/` and records metadata through `BlogImageRepo` (`INSERT INTO blogimage`).

**Known issue (static):** an Author can upload through the picker but cannot browse or delete their
uploads, because the only gallery screen is `AdminOnly`. Logged to REQ-UI-034.

---
Generated 2026-08-02 · reflects code as built · ⚠ STATIC-ONLY
