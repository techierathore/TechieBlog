# TechieBlog — Usage Guide

| | |
|---|---|
| App | TechieBlog |
| Kind | app |
| Size | Large |
| Date | 2026-11-02 |

> The owner's test plan. Agents (self-smoke, verifier) and human UAT use the SAME test users and walkthrough below; nobody invents throwaway accounts (`.tfcore/tasks/_smoke-test-policy.md`).

> **The database is the existing `WinPostgre` Docker container on host port `5550`** (`5550 -> 5432`), shared with `AppMngrDb` and `TkStoriesDb`. Never recreate, reset or `docker rm` it, and never start a second PostgreSQL container. Nothing listens on 5432; a dead port means the container is stopped: `docker start WinPostgre`. psql lives inside it: `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog`.

## Test users

Reconciled against the live database and signed in live, 2026-11-02 (`*handoff-phase`).

| # | User | Password source | Role | Exists |
|---|---|---|---|---|
| 1 | `Ravi@techieblog.com` (`ravi`) | `VerifyAdmin#2026` — current dev password; seed `003-SeedData.sql` | Admin, site owner | yes |
| 2 | `editor@techieblog.test` (`maya`) | `TriageYolo2026` — current dev password; seed `019-SampleData.sql` (`Editor#Pass1`) | Editor | yes |
| 3 | `author@techieblog.test` (`arun`) | `TriageYolo2026` — current dev password; seed `019-SampleData.sql` (`Author#Pass1`) | Author | yes |
| 4 | `contributor@techieblog.test` (`priya`) | `Contrib#Pass1` — seed `019-SampleData.sql` | Contributor | yes |
| 5 | ~~`reader@techieblog.test`~~ | — | ~~Reader~~ | retired 2026-08-06 (BRD-1/13/43/44) |
| 6 | anonymous | — | Guest | yes (no account) |

- Landing pages after sign-in: users 1 and 2 `/admin`, user 3 `/BlogsList`, user 4 `/`.
- **A fresh database flags all four accounts `MustChangePassword`** (REQ-NFR-023): first sign-in lands on `/change-password`. On the dev database the flag is `false` for all four (2026-11-02).
- Scripted smoke on a fresh database: clear the flag once, re-arm it after **every** agent has finished:
  `UPDATE bloguser SET mustchangepassword=false WHERE userid IN (1,2,3,4);` … `UPDATE bloguser SET mustchangepassword=true WHERE userid IN (1,2,3,4);`
- The dev database also holds 34 leftover builder accounts (ids 11–44, `fxv…@example.com`). They are not test users.
- Seed passwords are stored as PBKDF2-HMAC-SHA256 hashes, never plaintext (REQ-NFR-023).
- **Dev database content (2026-11-02):** 0 live published posts, 2 drafts, 0 series, 0 comments, 70 subscribers, 38 newsletters. A fresh database gets 10 posts, 2 series, 7 comments, 21 ratings from `019-SampleData.sql`. For public-page checks here, run `TF_BASE=<url> bash tests/verify/seed/_all.sh` first and delete its posts afterwards.

## Execution guide

Prerequisites: .NET 10 SDK on Windows (BlogApp targets `net10.0-windows10.0.19041.0`); Docker with `WinPostgre` (PostgreSQL 18); `read:packages` access to `https://nuget.pkg.github.com/techierathore/index.json` (TrBlazeUI 2.2.1); Node.js + Playwright Chromium for `tests/verify` only.

```
git clone <repo> && cd TechieBlog
docker start WinPostgre
docker exec WinPostgre psql -U PgVectorAdmin -d postgres -c 'CREATE DATABASE "TechieBlog";'
dotnet user-secrets set JwtSigningKey "$(openssl rand -base64 48)" --project source/TechieBlog
dotnet user-secrets set AppEncryptionKey "$(openssl rand -base64 32)" --project source/TechieBlog
dotnet restore TechieBlog.slnx
cmd.exe /c "dotnet build TechieBlog.slnx"
cmd.exe /c "set ASPNETCORE_ENVIRONMENT=Development&&dotnet run --project source/TechieBlog --urls http://0.0.0.0:5399"
```

Open `http://localhost:5399/login` and sign in as user 1. The `CREATE DATABASE` line is for a machine where the database does not exist yet. DbUp applies `source/BlogDb/PostgresScripts/001…` at startup; `AppDbConString` comes from `appsettings.Development.json`.

## How to test, screen by screen

Smoke checklist (quick capability pass):
- [ ] Home loads with the owner hero and published posts (user 6)
- [ ] A post renders Markdown with author, date, category, tags, reading time (user 6)
- [ ] Comment and rate a post with the double opt-in (user 6)
- [ ] Create, preview and publish a post with category and tags (user 3)
- [ ] Search the posts list and delete a post (user 1)
- [ ] Approve a pending comment (user 2)
- [ ] Dashboard counts are live; change the site theme in Settings (user 1)
- [ ] Deactivate and reactivate a subscriber; export the list (user 1)
- [ ] Upload an image and pick it through the image picker (user 1)
- [ ] `/resume` renders and the CV downloads (user 6)
- [ ] Dark mode on public and admin pages: no unreadable text

Screen-to-code map and screenshots: `docs/TechieBlog-DevGuide.md`.

### Home (`/`)
- **Sign in as:** 6
- **Steps:** 1) open `/` 2) check owner hero, stats, featured post, latest articles 3) click a post card.
- **Expected:** real post data; the card opens `/post/{slug}`.
- **Covers:** REQ-UI-005, REQ-UI-006, REQ-UI-049, REQ-FN-020

### Post view (`/post/{slug}`)
- **Sign in as:** 6
- **Steps:** 1) open a published post 2) check Markdown, author, date, category, tags, reading time 3) related posts and series navigation 4) comment with name + email; complete the opt-in 5) rate with the same email, then change the rating.
- **Expected:** no sign-in prompt; unapproved comments never render; no email address is displayed.
- **Covers:** REQ-UI-007, REQ-UI-027, REQ-UI-028, REQ-UI-029

### Category and tag archives (`/category/{slug}`, `/tag/{slug}`)
- **Sign in as:** 6
- **Steps:** 1) open a category from the header 2) open a tag from a post page 3) compare the count with the posts listed.
- **Expected:** each archive lists only its own published posts; counts match.
- **Covers:** REQ-UI-008, REQ-UI-009, REQ-FN-017, REQ-FN-018

### Series (`/series`, `/series/{slug}`)
- **Sign in as:** 6
- **Steps:** 1) open `/series` 2) open a series 3) open part 1 4) use next/previous.
- **Expected:** parts in reading order; navigation stays within the series.
- **Covers:** REQ-UI-010, REQ-UI-024, REQ-FN-019

### Search (`/search`)
- **Sign in as:** 6
- **Steps:** 1) search a word from a post body 2) apply a category filter 3) page through results.
- **Expected:** database results, term highlighted; the filter changes the results.
- **Covers:** REQ-UI-011, REQ-FN-021

### Resume and speaking (`/resume`, `/speaker-profile`)
- **Sign in as:** 6
- **Steps:** 1) open `/resume` 2) use the anchor navigation 3) click **Download CV** 4) open `/speaker-profile`.
- **Expected:** the site owner's hero, experience, skills, awards, stats, contact; the CV downloads.
- **Covers:** REQ-UI-036, REQ-UI-041, REQ-UI-042, REQ-FN-028, REQ-FN-029

### Subscribe and newsletters (`/newsletters`, `/newsletter/{slug}`, `/verify/{token}`, `/unsubscribe/{token}`)
- **Sign in as:** 6
- **Steps:** 1) subscribe from the sidebar form 2) open the confirmation link from the log 3) open `/newsletters` and one issue 4) open an unsubscribe link.
- **Expected:** the subscription confirms; issues list and open; an invalid or used link shows a clear message.
- **Covers:** REQ-UI-030, REQ-FN-030, REQ-FN-032

### Feeds (`/rss`, `/feed.xml`, `/sitemap.xml`, `/robots.txt`)
- **Sign in as:** 6
- **Steps:** 1) open each URL 2) validate the sitemap XML 3) confirm `robots.txt` names the sitemap.
- **Expected:** the feed lists recent posts; the sitemap has posts, categories and tags.
- **Covers:** REQ-UI-046, REQ-FN-037, REQ-FN-038

### Sign in and password reset (`/login`, `/forgot-password`, `/reset-password/{token}`, `/change-password`)
- **Sign in as:** 4
- **Steps:** 1) sign in, sign out 2) forgot-password for that email 3) take the reset link from the application log (no SMTP host) 4) reset, sign in with the new password 5) reuse the link.
- **Expected:** the reused, expired or invalid link shows a clear error. Restore the password afterwards.
- **Covers:** REQ-UI-001, REQ-UI-002, REQ-UI-003, REQ-FN-005, REQ-FN-006, REQ-FN-007, REQ-FN-008

### Role gates (`/access-denied`)
- **Sign in as:** anonymous, then 4, 3, 2, 1
- **Steps:** 1) for each, open `/admin`, `/users`, `/settings`, `/ManagePost`, `/admin/skills`.
- **Expected:** each reaches exactly what its policy allows, else `/access-denied`; unusable menu items are hidden.
- **Covers:** REQ-UI-004, REQ-UI-047, REQ-FN-009

### Post editor and lifecycle (`/ManagePost`, `/BlogsList`, `/admin/preview/{id}`)
- **Sign in as:** 3, then 1
- **Steps:** 1) new post: Markdown with live preview; slug auto-generates and is editable 2) category, tags (one new), series, image 3) save draft: absent from `/` 4) preview 5) publish: on `/` 6) schedule one two minutes out: it publishes itself 7) as user 1, search the posts list, edit and delete a post.
- **Expected:** every field persists; drafts stay private.
- **Covers:** REQ-UI-016, REQ-UI-017, REQ-UI-018, REQ-FN-012…016

### Media library (`/admin/images`)
- **Sign in as:** 1
- **Steps:** 1) upload one image per category tab 2) try an over-size file and a wrong format 3) open a copied image URL 4) delete an image 5) pick an avatar on `/admin/profile` through the picker.
- **Expected:** limits enforced server-side; files served under `/uploads/{category}/…`; the picker binds the path.
- **Covers:** REQ-UI-034, REQ-UI-035, REQ-FN-025, REQ-FN-026

### Profile and resume data (`/admin/profile`, `/admin/experience`, `/admin/skills`, `/admin/awards`, `/admin/stats`, `/admin/speaking`)
- **Sign in as:** 3, then 1
- **Steps:** 1) as the author, set profile fields, upload a CV, save, reload 2) two experience entries, one current 3) skills in two categories, reorder 4) an award with a badge 5) a stat 6) as Admin, switch user on Experience, Skills, Awards, Stats 7) add a speaking engagement.
- **Expected:** values persist; authors see only their own data.
- **Covers:** REQ-UI-013, REQ-UI-037…040, REQ-FN-011, REQ-FN-027

### Comment moderation (`/CommentsList`)
- **Sign in as:** 2
- **Steps:** 1) anonymously comment and complete the opt-in 2) approve it 3) post another, reject it 4) edit a comment 5) bulk-process several.
- **Expected:** approved comments appear on the post; rejected ones do not.
- **Covers:** REQ-UI-021, REQ-UI-029, REQ-FN-022

### Admin dashboard, users, taxonomy (`/admin`, `/users`, `/adduser`, `/admin/categories`, `/admin/tags`)
- **Sign in as:** 1
- **Steps:** 1) dashboard tiles show live counts 2) users: search, change a role, disable an account, add a user 3) categories and tags: add, rename, delete.
- **Expected:** every change persists and shows on the public site.
- **Covers:** REQ-UI-019, REQ-UI-020, REQ-UI-022, REQ-UI-023, REQ-FN-036

### Subscribers, newsletter, analytics, settings (`/admin/subscribers`, `/admin/newsletter`, `/admin/analytics`, `/settings`)
- **Sign in as:** 1
- **Steps:** 1) subscribers: search, filter, **deactivate**, reactivate, export CSV 2) compose and save a newsletter draft 3) change the analytics date range 4) settings: site title, posts per page, theme.
- **Expected:** a deactivated subscriber stays listed as Inactive (none is ever deleted, BRD-57); settings show on the public site.
- **Covers:** REQ-UI-025, REQ-UI-026, REQ-UI-043, REQ-UI-044, REQ-FN-031, REQ-FN-040

### Theming
- **Sign in as:** 6, then 1
- **Steps:** 1) toggle light/dark on home, a post, an archive, search, about, an admin page 2) reload: the choice persists 3) as Admin, switch the theme to Developer Dark, then Minimal Clean 4) re-walk public pages in each.
- **Expected:** no unreadable text, invisible control or broken contrast in any of the six combinations.
- **Covers:** REQ-UI-031, REQ-UI-032, REQ-UI-033, REQ-FN-039

### BlogApp desktop (`source/BlogApp`)
- **Sign in as:** 1
- **Steps:** 1) start BlogApp 2) Connection settings: database, the two secrets, media storage; press **Test** 3) save, sign in 4) upload an image on `/admin/images` 5) open `/resume`.
- **Expected:** the probe passes; with SFTP media storage the image appears on the website.
- **Covers:** REQ-UI-051, REQ-UI-063, REQ-UI-064, REQ-FN-062

## Automated tests

```
cmd.exe /c "dotnet test tests\TechieBlog.Tests\TechieBlog.Tests.csproj"
BASE_URL=http://localhost:5399 TB_BASE=http://localhost:5399 npx playwright test
```
Unit, integration and source-convention tests (1 536 passing on 2026-10-30), then the Playwright browser specs in `tests/verify` against a running host.

## Known limitations

- **Admin posts list: search and row Delete crash the page** ("Something went wrong"). TrBlazeUI 2.2.1 `SelectValue`, TR-077; REQ-UI-017.
- **Not yet re-verified:** REQ-UI-025, REQ-FN-031 (BRD-57: deactivate, not remove), REQ-UI-047 (sidebar active item on TrBlazeUI 2.2.1).
- **Found by reading the code on 2026-11-02, not reproduced** (DevGuide Known issues): an Author could open and edit any post (REQ-UI-016, fixed 2026-11-05: the editor and the preview now refuse another author's post); search filters are not applied (REQ-UI-011); tag links can miss the archive (REQ-UI-007); scheduled publish drops series membership (REQ-FN-016); "Pending Comments" counts rejected and spam (REQ-FN-036).
- **TF-002:** the boot script's NAT-mode WSL fix is untested here; this machine now runs mirrored networking.
- **Real SMTP delivery is unexercised** (REQ-FN-033): with no `EmailSettings:SmtpHost`, mail goes to the log.
- **CI cannot restore TrBlazeUI** until the `TrBlazeUiPackagesToken` repository secret exists (owner, REQ-NFR-017).
- **The deploy pipeline has never run against the VPS** (REQ-NFR-038); `docs/Prod-Deploy-Checklist.md`.
- **No screen-reader pass has been run**; axe reports 0 violations (REQ-NFR-006, REQ-NFR-007).

## Platform notes

- **BlogApp media storage:** uploads stay on the local machine unless **Change connection → Media storage** is SFTP to `/srv/data/techieblog/uploads`; press **Test** first (it writes, reads back and deletes a file). The SFTP path has not been exercised with the real server's credentials (REQ-FN-062).
- **BlogApp before 2026-08-22** saved uploads under `%LOCALAPPDATA%\TechieBlog\BlogApp\wwwroot\uploads` and `C:\srv\data\techieblog\uploads`; **Send to server** on the same panel pushes them up with the same file names.
- **BlogApp thumbnails** do not load against a plain-http local site (mixed content); production uses https.
- **WSL:** WSL `dotnet` cannot build the whole solution; use `cmd.exe /c "dotnet …"`. Bind `0.0.0.0` when a WSL-side browser must reach a Windows-side host.
