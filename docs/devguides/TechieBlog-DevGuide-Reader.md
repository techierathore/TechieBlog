# TechieBlog — Developer Guide · Reader (and Contributor)

> ⚠ **This guide documents a role that no longer exists.** Reader accounts and public registration were
> retired on 2026-08-06 (BRD-1/13/43/44), and `019-SampleData.sql` deliberately seeds **no** Reader
> account. The screens described here — `/profile`, `/my-favorites`, `/my-comments` — are `N/A (removed)`
> in the checklist and `MyFavorites.razor` / `FavoriteToggle.razor` no longer exist in the tree.
>
> ✅ **Contributor runtime-verified 2026-08-09** — supersedes the 2026-08-02 `STATIC-ONLY` banner, whose
> stated reason (solution does not compile, REQ-FN-043) is stale.

Index: [TechieBlog-DevGuide.md](./TechieBlog-DevGuide.md)

## Runtime verification (2026-08-09) — Contributor only

| Observation | Result |
|-------------|--------|
| Sign-in landing | Contributor signs in successfully and lands on `/`, per `RoleLandingRoutes` — it has no staff surface |
| Policy matrix | **denied** `/users`, `/admin` and `/BlogsList`; every denial landed on the access-denied surface, never a raw 403 |
| Access-denied affordance | correctly offers **no** "Go to Dashboard" button for this role, because there is no dashboard it may open |
| Forced password change | with `MustChangePassword` set, the Contributor sign-in was held on `/change-password` and navigating to `/` or `/BlogsList` **bounced straight back** — the flag is genuinely enforced, not merely displayed |
| `ContributorOrAbove` policy | registered but still attached to **no page**, so it grants nothing beyond anonymous access. Unchanged from the static finding, and documented as deliberate |

**Engagement is anonymous now.** Commenting and rating no longer require an account at all: they are
email-keyed with a captcha and double opt-in, and runtime checks found no sign-in gate and zero `/login`
links in either component. The Favourite toggle this guide describes has been removed outright.

A Reader sees every Guest screen plus the four below. **Contributor is functionally identical to
Reader** — the `ContributorOrAbove` policy exists (`Program.cs:96`) but no page requires it, so the
role grants nothing extra (index finding #5).

> **Login trap:** after a successful login `LoginPage.razor.cs:106` sends *every* role to `/admin`,
> which requires `EditorOrAbove`. A Reader therefore lands on `/access-denied` rather than the site.
> See index §3.

## Reader · Profile (`/profile`)

**File:** `source/BlogUI/Pages/AdminPages/ProfilePage.razor` (+ `.razor.cs`) · **Guard:** `[Authorize]` ·
**Layout:** `AdminLayout`

```mermaid
flowchart LR
  PP["ProfilePage.razor.cs"] --> AS["AuthSvc"]
  AS --> UR["BlogUserRepo"]
  UR --> DB[("BlogUser")]
```

| Control | What it does | Source call | Render status |
|---------|--------------|-------------|---------------|
| Profile details | Loads the signed-in user | `AuthService.GetUserProfile(...)` | static-only (unconfirmed) |
| Save profile | Persists display name, bio, avatar, socials | `AuthService.UpdateProfile(...)` | static-only (unconfirmed) |
| Change password | Verifies current, sets new | `AuthService.ChangePassword(...)` | static-only (unconfirmed) |

**Data lineage**

| Step | Symbol | File |
|------|--------|------|
| Page | `ProfilePage.razor` `@inject BlogEngine.Services.AuthSvc AuthService`, `AuthenticationStateProvider` | `Pages/AdminPages/ProfilePage.razor` |
| Service | `AuthSvc.GetUserProfile`, `UpdateProfile`, `ChangePassword` | `BlogEngine/Services/AuthSvc.cs` |
| Data access | `BlogUserRepo.GetSingle`, `Update` | `BlogEngine/DbAccess/BlogUserRepo.cs` |
| SQL | stored function `SelectBlogUserById` for reads; inline `UPDATE BlogUser` for writes | `BlogUserRepo.cs` |

**Business rules:** the user id comes from the `ClaimsPrincipal` (`PrimarySid` claim issued by
`AuthSvc`), not from a route parameter — a user cannot open someone else's profile here.

**Known issues:** password change ultimately calls `AppEncrypt.CreateHash`, a hand-rolled hash rather
than a standard salted KDF (REQ-NFR-002, ⚠ SECURITY).

## Reader · My Favourites (`/my-favorites`)

**File:** `source/BlogUI/Pages/UserPages/MyFavorites.razor` · **Guard:** `[Authorize]` ·
**Layout:** `MainLayout`

| Control | Source call | Render status |
|---------|-------------|---------------|
| Favourites list | `FavoriteService.GetUserFavorites(userId)` | static-only (unconfirmed) |
| Count | `FavoriteService.GetUserFavoriteCount(userId)` | static-only (unconfirmed) |

**Data lineage:** page → `FavoriteSvc` (`BlogEngine/Services/FavoriteSvc.cs`) → `UserFavoriteRepo`
(`BlogEngine/DbAccess/UserFavoriteRepo.cs`) → inline SQL `SELECT FavoriteId …` / `INSERT INTO UserFavorite`
/ `DELETE FROM UserFavorite` against the table created by migration `009-CreateUserFavorite.sql`.

**Business rules:** the current user id is resolved from `AuthenticationStateProvider`; the page
redirects unauthenticated users via `NavigationManager`.

**Known issues:** the favourites list returns `UserFavorite` rows — confirm the post title/slug shown on
each card is joined in the repository rather than fetched per row
(`{unresolved — TODO: check whether GetUserFavorites projects post fields or the page issues N+1 lookups}`).

## Reader · Engagement on a post (`/post/{Slug}`)

The post page itself is documented under Guest. These controls become active only when signed in:

| Control | Component | Service | Data access |
|---------|-----------|---------|-------------|
| Comment form | `Components/…` on `PostView.razor` | `CommentSvc.AddComment` | `BlogCommentRepo` → `INSERT INTO blogcomment` |
| Star rating | `Components/StarRating.razor` | `RatingSvc.RatePost`, `GetUserRating`, `GetPostRatingStats` | `PostRatingRepo` → `INSERT/UPDATE/DELETE PostRating` |
| Favourite toggle | `Components/FavoriteToggle.razor` | `FavoriteSvc.ToggleFavorite`, `IsFavorited` | `UserFavoriteRepo` |

**Business rules:** one rating per user per post (enforced by a unique index from migration `010`);
comments may require approval before display, controlled by the moderation setting — but see the
Admin guide: that setting is **not persisted**, so its runtime source is
`{unresolved — TODO: locate where the comment-moderation flag is read at runtime}`.

## Reader · Subscribe (component, public pages)

**Component:** `source/BlogUI/Components/Sidebar.razor` (subscribe block) → `SubscriberSvc.Subscribe`
→ `SubscriberRepo` → `INSERT INTO Subscriber` / duplicate check via `SELECT SubscriberId …`.

Available to anonymous visitors too; listed here because it is part of the reader journey.

## Missing screen — My Comments

`docs/TechieBlog-BRD.md` BRD-13 and mockup `mockups/14-my-comments.html` specify a comment-history page
for readers. **No such page exists** in `source/BlogUI/Pages/` — tracked as REQ-UI-015 (Not Started).
Nothing to map.

---
Generated 2026-08-02 · reflects code as built · ⚠ STATIC-ONLY
