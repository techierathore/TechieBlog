# TechieBlog — Checklist

> Migrated from `MVP-EXECUTION-PLAN.md` (with `FIX-PLAN.md`, `FAST-TRACK-BACKLOG.md`, `SM-HANDOFF.md`,
> `epic-image-resume-multiauthor.md` — all now archived under `docs/OldDocs/` — plus the 44
> `docs/stories/*.story.md` files and 19 `docs/qa/gates/*.yml`, which stay in place as historical records)
> on 2026-08-02. Phase structure, completion %, and status remarks carried over verbatim — verify
> before building. `Done (pre-existing)` rows must NOT be rebuilt by any build agent.

## Table of Contents

1. [Goal](#goal)
2. [Requirements Status](#requirements-status)
3. [UI / Pages](#ui--pages)
4. [Functional requirements](#functional-requirements)
5. [RAG / AI requirements (→ /techierag)](#rag-ai-requirements-techierag)
6. [Non-functional](#non-functional)

## Goal

Deliver TechieBlog — a production-ready, Blazor-native blogging engine on .NET 10 LTS, distributed as
a clone-and-own template — with a complete authoring, reading and engagement feature set, CSS-variable
theming, a resume/portfolio surface for the site owner, and the operational hardening (tests, CI,
health checks, analytics, newsletter delivery) still outstanding. Traces to `docs/TechieBlog-BRD.md` §1.

**Phase map** (carried from the migrated plan): **1** Foundation & UI scaffolding · **2** Authentication
& user management · **3** Content management core · **4** Engagement & social · **5** Media,
subscribers & analytics · **6** SEO, theming & production polish · **7** Bug fixes & polish ·
**8** Resume, image management & multi-author profiles · **9** UI re-platform (TrBlazeUI) &
portfolio home *(added 2026-08-06)* · **10** BlogApp desktop admin *(added 2026-08-06)*.

## Requirements Status

<!-- SINGLE SOURCE OF TRUTH for the WHOLE app. Build, self-smoke and the verifier all write their
     outcomes into THIS table. Bugs and change notes live in Remarks, never in docs/qa/*.md. -->

| ID | Requirement | Status | % | Remarks | Details |
|----|-------------|--------|---|---------|---------|
| REQ-UI-001 | Login page (Phase 2) | Needs re-verify | 75% | Story 1.8 + 2.1; QA gate 2.1 PASS per MVP-EXECUTION-PLAN §Epic 2. ⚠ DevGuide 2026-08-02: post-login redirect is role-blind — `LoginPage.razor.cs:106` sends EVERY role to `/admin`, which requires `EditorOrAbove`, so Readers/Contributors land on `/access-denied` (static — confirm at runtime) | [view](#d-req-ui-001) |
| REQ-UI-002 | Registration page (Phase 2) | N/A (removed 2026-08-06) | — | ~~BRD-1~~ retired — no reader accounts. `RegisterPage.razor` + `/register` and `/signup` routes to be **removed**; mockup 14 deleted | [view](#d-req-ui-002) |
| REQ-UI-003 | Forgot / reset password pages (Phase 2) | Done (pre-existing) | 100% | Story 2.3 "Completed"; QA gate 2.3 PASS; wired by FIX-002 | [view](#d-req-ui-003) |
| REQ-UI-004 | Access-denied page (Phase 2) | Done (pre-existing) | 100% | Story 2.4; duplicate component exists — see REQ-NFR-020 | [view](#d-req-ui-004) |
| REQ-UI-005 | Main layout, header nav, sidebar, footer, mobile nav (Phase 1) | Needs re-verify | 75% | Story 1.6 DONE; QA gate 1.6 PASS. [BRD-93] 2026-08-06: public shell must drop the login link and user menu (REQ-UI-050); shell also rebuilds in TrBlazeUI (REQ-UI-048) | [view](#d-req-ui-005) |
| REQ-UI-006 | Home page — featured + recent grid + sidebar (Phase 1/3) | Needs re-verify | 75% | Story 1.7 Complete, 3.8 Complete; QA gate 3.8 PASS. [BRD-30 rev] 2026-08-06: home becomes a portfolio-style landing — superseded by REQ-UI-049; current page stays until Phase 9 builds the replacement | [view](#d-req-ui-006) |
| REQ-UI-007 | Post view page — article, meta, engagement (Phase 3) | Done (pre-existing) | 100% | Story 1.7 + 3.8; full-width layout | [view](#d-req-ui-007) |
| REQ-UI-008 | Category archive page (Phase 3) | Done (pre-existing) | 100% | Story 1.7; dynamic filter fixed by FIX-008 | [view](#d-req-ui-008) |
| REQ-UI-009 | Tag archive page (Phase 3) | Done (pre-existing) | 100% | Story 1.7; category names fixed by FIX-009; counts fixed by Story 7.5 | [view](#d-req-ui-009) |
| REQ-UI-010 | Series view page (Phase 3) | Done (pre-existing) | 100% | Story 1.7 + 3.7; QA gate 3.7 PASS | [view](#d-req-ui-010) |
| REQ-UI-011 | Search results page (Phase 3) | Done (pre-existing) | 100% | Story 1.7; wired to real service by FIX-004 with paging + highlighting | [view](#d-req-ui-011) |
| REQ-UI-012 | About page and 404 page (Phase 1) | Done (pre-existing) | 100% | Story 1.7; dark-mode fixed by Story 7.3 | [view](#d-req-ui-012) |
| REQ-UI-013 | User profile page (Phase 2) | N/A (removed 2026-08-06) | — | Reader accounts dropped — no public `/profile`. Staff profile is REQ-UI-040 (`/admin/profile`). Built page to be retired | [view](#d-req-ui-013) |
| REQ-UI-014 | My Favourites page (Phase 4) | N/A (removed 2026-08-06) | — | ~~BRD-44~~ retired with reader accounts; `MyFavorites.razor` to be removed | [view](#d-req-ui-014) |
| REQ-UI-015 | My Comments history page (Phase 2) | N/A (removed 2026-08-06) | — | ~~BRD-13~~ retired — anonymous commenting leaves no per-reader history. Never built, now out of scope | [view](#d-req-ui-015) |
| REQ-UI-016 | Post editor — Markdown + live preview + metadata sidebar (Phase 3) | Done (pre-existing) | 100% | Story 1.10 DONE + 3.2 "Completed"; QA gate 3.2 PASS | [view](#d-req-ui-016) |
| REQ-UI-017 | Post list / all posts with status filters (Phase 3) | Needs re-verify | 75% | Story 1.10 + 3.1; QA gate 3.1 PASS. ⚠ DevGuide 2026-08-02: `BlogsList.razor:11` is `[Authorize(Policy="EditorOrAbove")]` while the editor is `AuthorOrAbove` — an Author cannot reach any post list; the "My Posts" screen (mockup 18) may be genuinely missing (static) | [view](#d-req-ui-017) |
| REQ-UI-018 | Draft preview page (Phase 3) | Done (pre-existing) | 100% | Story 3.5 "Completed"; QA gate 3.5 PASS | [view](#d-req-ui-018) |
| REQ-UI-019 | Admin dashboard — stat tiles + quick actions (Phase 1/5) | Needs re-verify | 50% | Story 1.11 DONE. ⚠ DevGuide 2026-08-02: only the post tiles are real. `AdminDashboard.razor.cs:63-68` hardcodes `TotalUsers=1`, `TotalSubscribers=1`, `TotalComments=0`, `PendingComments=0`; "Popular posts" is recent posts with `Views=0` (`:55-59`). The page injects only `BlogSvc` (`:15`) though `CommentSvc.GetAdminCounts` exists (static) | [view](#d-req-ui-019) |
| REQ-UI-020 | Users list + add user (Phase 2) | Done (pre-existing) | 100% | Story 2.6 "DONE (UsersList.razor exists)" | [view](#d-req-ui-020) |
| REQ-UI-021 | Comment moderation queue (Phase 4) | Done (pre-existing) | 100% | Story 4.2 DONE (CommentsList.razor); wired by FIX-005 | [view](#d-req-ui-021) |
| REQ-UI-022 | Categories list + manage category (Phase 3) | Done (pre-existing) | 100% | Story 1.12 + 3.3 "Completed ✅"; QA gate 3.3 PASS | [view](#d-req-ui-022) |
| REQ-UI-023 | Tags list + manage tag (Phase 3) | Done (pre-existing) | 100% | Story 1.12 + 3.4 "Completed"; QA gate 3.4 PASS | [view](#d-req-ui-023) |
| REQ-UI-024 | Series list + manage series (Phase 3) | Done (pre-existing) | 100% | Story 3.7 Complete | [view](#d-req-ui-024) |
| REQ-UI-025 | Subscribers admin page (Phase 5) | Done (pre-existing) | 100% | Story 7.7 DONE — "SubscribersList.razor complete" | [view](#d-req-ui-025) |
| REQ-UI-026 | Site settings page (Phase 6) | Needs re-verify | 40% | Story 6.4 DONE — "Settings.razor with theme dropdown". ⚠ DevGuide 2026-08-02: five settings sections render and the Save button reports "Settings saved successfully", but only the pagination word count is written (to browser local storage); `Settings.razor:337` carries `// TODO: Implement actual save to database` — general, blog, SEO and social settings are silently discarded (static) | [view](#d-req-ui-026) |
| REQ-UI-027 | Star rating component (Phase 4/9) | Needs re-verify | 60% | FIX-013 — StarRating.razor in PostView + PostCard. [BRD-40/41 rev] 2026-08-06: must work for anonymous visitors keyed by email — no sign-in gate. Mockup: 02-post-view.html | [view](#d-req-ui-027) |
| REQ-UI-028 | Favourite toggle component (Phase 4) | N/A (removed 2026-08-06) | — | ~~BRD-43~~ retired with reader accounts; `FavoriteToggle.razor` to be removed from post page and cards | [view](#d-req-ui-028) |
| REQ-UI-029 | Comment form + list on post page (Phase 4/9) | Needs re-verify | 60% | FIX-005. [BRD-36 rev] 2026-08-06: anonymous form — name + email + body, email never published, no sign-in prompt. Mockup: 02-post-view.html | [view](#d-req-ui-029) |
| REQ-UI-030 | Subscribe form component (Phase 5) | Done (pre-existing) | 100% | Story 7.6 DONE — "All components verified working" | [view](#d-req-ui-030) |
| REQ-UI-031 | Light/dark toggle in header (Phase 1/6) | Done (pre-existing) | 100% | Story 1.6 AC7; persists via Blazored.LocalStorage | [view](#d-req-ui-031) |
| REQ-UI-032 | Theme selector in Site Settings (Phase 7) | Needs re-verify | 70% | Story 7.8 DONE — "Theme dropdown added to Settings". ⚠ DevGuide 2026-08-02: the selection is written to browser local storage (`ThemeService.cs:46`), so it is a per-visitor preference, not the admin-selected **site** theme BRD-68 requires (static). [BRD-67 rev] 2026-08-06: themes re-expressed as TrBlazeUI/shadcn variable sets in Phase 9 | [view](#d-req-ui-032) |
| REQ-UI-033 | Dark-mode corrections — sidebar, public, search, about, admin (Phase 7) | Needs re-verify | 75% | Stories 7.1–7.4 DONE; CSS in fluent-dark-mode.css, FluentAnchor + dialog/checkbox theming. [BRD-92] 2026-08-06: fluent-dark-mode.css retires with the Fluent UI removal — dark-mode coverage must be re-established on TrBlazeUI (`.dark` class) in Phase 9 | [view](#d-req-ui-033) |
| REQ-UI-034 | Media library page with category tabs (Phase 5/8) | Done (pre-existing) | 100% | FIX-006 + EPIC-IRM-001 Stream I.1 (ManageImages.razor). ⚠ DevGuide 2026-08-02: page is `AdminOnly` while `ImagePicker` uploads from `AuthorOrAbove` pages — Authors create uploads they can never browse or delete (static; design question, not a break) | [view](#d-req-ui-034) |
| REQ-UI-035 | Reusable ImagePicker component (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream H | [view](#d-req-ui-035) |
| REQ-UI-036 | Public resume page + hero/experience/skills/awards/contact (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Streams J.1–J.4; QA gate EPIC-IRM-001.stream-L PASS | [view](#d-req-ui-036) |
| REQ-UI-037 | Manage experience page (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream I.2 | [view](#d-req-ui-037) |
| REQ-UI-038 | Manage skills page (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream I.3 | [view](#d-req-ui-038) |
| REQ-UI-039 | Manage awards page (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream I.4 | [view](#d-req-ui-039) |
| REQ-UI-040 | Manage profile page incl. resume fields (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream K.2 | [view](#d-req-ui-040) |
| REQ-UI-041 | Authors listing page (Phase 8) | N/A (removed 2026-08-06) | — | ~~BRD-53~~ retired — personal site, no public author index. `AuthorsPage.razor` + `/authors` route to be removed | [view](#d-req-ui-041) |
| REQ-UI-042 | Author profile page (Phase 8) | N/A (removed 2026-08-06) | — | ~~BRD-54/55~~ retired. `AuthorProfilePage.razor` + `/author/{username}` to be removed; bylines become plain text | [view](#d-req-ui-042) |
| REQ-UI-043 | Newsletter composer UI (Phase 5) | Not Started | 0% | Story 5.5 "NOT DONE" per MVP-EXECUTION-PLAN §Epic 5 | [view](#d-req-ui-043) |
| REQ-UI-044 | Analytics dashboard UI — charts + date range (Phase 5) | Not Started | 0% | Story 5.7 "NOT DONE" | [view](#d-req-ui-044) |
| REQ-UI-045 | Shared components — PostCard, Pagination, Breadcrumb, Sidebar (Phase 1) | Done (pre-existing) | 100% | Story 1.6/1.7 | [view](#d-req-ui-045) |
| REQ-UI-046 | RSS feed page and auto-discovery link (Phase 6) | Done (pre-existing) | 100% | Story 6.1 DONE — "RssFeed.razor exists" | [view](#d-req-ui-046) |
| REQ-UI-047 | Admin layout with grouped navigation (Phase 1) | Done (pre-existing) | 100% | Story 1.11/1.12; AdminLayout.razor | [view](#d-req-ui-047) |
| REQ-UI-048 | Migrate all BlogUI pages, components and layouts from Fluent UI to TrBlazeUI (Phase 9) | Not Started | 0% | [BRD-92] Added 2026-08-06. Owner supplies GitHub Packages credentials in nuget.config before build; add `<PortalHost />` to root layouts; retire fluent-dark-mode.css; removes both FluentUI packages (also clears REQ-FN-043's cause) | [view](#d-req-ui-048) |
| REQ-UI-049 | Portfolio-style home page — hero, stats, about, latest articles, contact (Phase 9) | Not Started | 0% | [BRD-30 rev] Added 2026-08-06; supersedes the featured+grid home (REQ-UI-006); driven by site-owner resume data (F-RESUME). Mockup: docs/mockups/01-home.html (TrBlazeUI set, *mockups 2026-08-06 — the repo-root mockups/ set is superseded) | [view](#d-req-ui-049) |
| REQ-UI-050 | Remove public login/admin entry points; contextual sign-in prompts; README admin-URL doc (Phase 9) | Not Started | 0% | [BRD-93] Added 2026-08-06 | [view](#d-req-ui-050) |
| REQ-UI-051 | BlogApp login screen + admin shell (Phase 10) | Not Started | 0% | [BRD-95] Added 2026-08-06 | [view](#d-req-ui-051) |
| REQ-UI-052 | Full admin surface available in BlogApp (Phase 10) | Not Started | 0% | [BRD-97] Added 2026-08-06 | [view](#d-req-ui-052) |
| REQ-UI-053 | Public newsletter archive page `/newsletters` + subscribe form (Phase 9) | Not Started | 0% | [BRD-100] Added 2026-08-06 — the reader-facing counterpart to the admin composer. Mockup: 42-newsletter-archive.html | [view](#d-req-ui-053) |
| REQ-UI-054 | Public newsletter issue view `/newsletter/{slug}` + prev/next (Phase 9) | Not Started | 0% | [BRD-101] Added 2026-08-06. Mockup: 43-newsletter-view.html | [view](#d-req-ui-054) |
| REQ-UI-055 | Email confirmation landing page `/verify/{token}` (Phase 9) | Not Started | 0% | [BRD-98] Added 2026-08-06 — success / expired / already-verified / subscription states. Mockup: 44-verify-email.html | [view](#d-req-ui-055) |
| REQ-UI-056 | Captcha widget on every public write surface (Phase 9) | Not Started | 0% | [BRD-99] Added 2026-08-06 — challenge image + reload + input on comment, rating and subscribe forms. Mockups: 02-post-view.html, 42-newsletter-archive.html | [view](#d-req-ui-056) |
| REQ-FN-001 | Migrate all projects to .NET 10 LTS (Phase 1) | Done (pre-existing) | 100% | Story 1.1 DONE; QA gate 1.1 PASS; all 5 csproj target net10.0 | [view](#d-req-fn-001) |
| REQ-FN-002 | Remove BlogSvc API project; UI calls services via DI (Phase 1) | Done (pre-existing) | 100% | Story 1.2 DONE; QA gate 1.2 PASS | [view](#d-req-fn-002) |
| REQ-FN-003 | PostgreSQL schema + DbUp migration runner (Phase 1) | Done (pre-existing) | 100% | Story 1.3 DONE; 12 scripts 001–013 (011 skipped) | [view](#d-req-fn-003) |
| REQ-FN-004 | Replace Blazorise with Fluent UI Blazor (Phase 1) | Done (pre-existing) | 100% | Story 1.4 DONE; QA gate 1.4 PASS. **Floating `4.*` now breaks restore — see REQ-FN-043.** [BRD-92] 2026-08-06: historical — Fluent UI is itself replaced by TrBlazeUI in Phase 9 (REQ-UI-048) | [view](#d-req-fn-004) |
| REQ-FN-005 | AuthSvc login + JWT issuance + login logging (Phase 2) | Done (pre-existing) | 100% | Story 2.1 Complete; QA gate 2.1 PASS | [view](#d-req-fn-005) |
| REQ-FN-006 | Password strength validation on staff account creation + reset (Phase 2) | Needs re-verify | 60% | Story 2.2; FIX-001 wired AuthService.RegisterUserAsync. [BRD-1 retired / BRD-3 rev] 2026-08-06: remove the **public signup path**; keep `PasswordValidator` on admin-created accounts (BRD-10) and password reset (BRD-5) | [view](#d-req-fn-006) |
| REQ-FN-007 | Password reset request / validate / reset (Phase 2) | Done (pre-existing) | 100% | FIX-002 — ResetPasswordAsync (130–141), SendPasswordResetEmailAsync (148–160) | [view](#d-req-fn-007) |
| REQ-FN-008 | Token refresh (Phase 2) | Done (pre-existing) | 100% | FIX-003 — RefreshTokenAsync validates via GetUserByToken (lines 72–93) | [view](#d-req-fn-008) |
| REQ-FN-009 | 5-role model + 5 authorization policies (Phase 2) | Needs re-verify | 80% | Story 2.4 Complete; QA gate 2.4 PASS. ⚠ DevGuide 2026-08-02: `ContributorOrAbove` (`Program.cs:96`) is referenced by no page, so Contributor grants nothing beyond Reader; and the post-login redirect ignores role (see REQ-UI-001) (static) | [view](#d-req-fn-009) |
| REQ-FN-010 | Admin user management backend (Phase 2) | Done (pre-existing) | 100% | Story 2.6 DONE | [view](#d-req-fn-010) |
| REQ-FN-011 | Profile read/update + change password (Phase 2) | Done (pre-existing) | 100% | Story 2.5 Completed; QA gate 2.5 PASS | [view](#d-req-fn-011) |
| REQ-FN-012 | Post CRUD service + repository (Phase 3) | Done (pre-existing) | 100% | Story 3.1 Complete; QA gate 3.1 PASS; BlogPostRepo 467 lines (FIX-012) | [view](#d-req-fn-012) |
| REQ-FN-013 | Slug generation, uniqueness and slug-based routing (Phase 3) | Done (pre-existing) | 100% | Story 3.1 + 3.8; SlugGenerator.cs | [view](#d-req-fn-013) |
| REQ-FN-014 | Markdown rendering via Markdig (Phase 3) | Done (pre-existing) | 100% | Story 3.2 Completed; MarkdownRenderer singleton | [view](#d-req-fn-014) |
| REQ-FN-015 | Draft / Published state handling (Phase 3) | Done (pre-existing) | 100% | Story 3.5 Completed; QA gate 3.5 PASS | [view](#d-req-fn-015) |
| REQ-FN-016 | Post scheduling + background publisher (Phase 3) | Done (pre-existing) | 100% | Story 3.6 Complete; QA gate 3.6 PASS; ScheduledPostPublisher hosted service | [view](#d-req-fn-016) |
| REQ-FN-017 | Category CRUD + single-category assignment (Phase 3) | Done (pre-existing) | 100% | Story 3.3 Completed ✅; QA gate 3.3 PASS | [view](#d-req-fn-017) |
| REQ-FN-018 | Tag CRUD, post-tag junction, autocomplete, accurate counts (Phase 3/7) | Done (pre-existing) | 100% | Story 3.4 Completed; Story 7.5 DONE — "Fixed COUNT query in BlogTagRepo.cs" | [view](#d-req-fn-018) |
| REQ-FN-019 | Series CRUD, ordering, prev/next navigation (Phase 3) | Done (pre-existing) | 100% | Story 3.7 Complete; QA gate 3.7 PASS | [view](#d-req-fn-019) |
| REQ-FN-020 | Published listings, featured post, related posts, reading time (Phase 3) | Done (pre-existing) | 100% | Story 3.8 Complete; QA gate 3.8 PASS; ReadingTimeCalculator.cs | [view](#d-req-fn-020) |
| REQ-FN-021 | Search service — ILIKE across title/abstract/body/tags, paging (Phase 3) | Done (pre-existing) | 100% | FIX-004 — BlogPostRepo.SearchPosts (424–465), BlogSvc.SearchPosts (555–580) | [view](#d-req-fn-021) |
| REQ-FN-022 | Comment CRUD, approval workflow, counts (Phase 4/9) | Needs re-verify | 60% | FIX-005 — CommentSvc 313 lines; BlogCommentRepo complete, but keyed to a signed-in user. [BRD-36 rev] 2026-08-06: accept anonymous name+email (schema change — commenter name/email columns), default to moderation, add spam protection | [view](#d-req-fn-022) |
| REQ-FN-023 | Rating service — one per **email** per post, changeable, aggregates (Phase 4/9) | Needs re-verify | 60% | FIX-013 — PostRating model, PostRatingRepo, RatingSvc keyed to user id. [BRD-40/41 rev] 2026-08-06: re-key to email (schema change) | [view](#d-req-fn-023) |
| REQ-FN-024 | Favourites service — add/remove/toggle/list/count (Phase 4) | N/A (removed 2026-08-06) | — | ~~BRD-43/44~~ retired. `UserFavorite`, `UserFavoriteRepo`, `FavoriteSvc` + script 009 table to be retired | [view](#d-req-fn-024) |
| REQ-FN-025 | Image upload service with per-category validation (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream F; 7 categories with size/format limits | [view](#d-req-fn-025) |
| REQ-FN-026 | BlogImage metadata + category schema migration (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 Stream A → script 012-ResumeAndImageManagement.sql | [view](#d-req-fn-026) |
| REQ-FN-027 | Resume data model + repositories (skills, awards, stats, experience) (Phase 8) | PARTIAL | 85% | EPIC-IRM-001 Streams B, E; UserSkills/UserAwards/UserStats tables + repos. ⚠ DevGuide 2026-08-02: `IUserStatsRepo` is registered (`BlogSvcInitializer.cs:69`) but **no admin page maintains UserStats** — the resume's About/Community statistics can only be populated by direct SQL (static) | [view](#d-req-fn-027) |
| REQ-FN-028 | CV upload and download (Phase 8) | Done (pre-existing) | 100% | EPIC-IRM-001 — CVFilePath + cv upload category | [view](#d-req-fn-028) |
| REQ-FN-029 | Username uniqueness + site-owner flag (Phase 8) | Needs re-verify | 75% | EPIC-IRM-001 Stream G; scripts 012 + 013. Scope narrowed 2026-08-06: `IsSiteOwner` + username stay (F-RESUME); the **author-lookup-by-username** and author-listing queries retire with ~~BRD-53/54/55~~ | [view](#d-req-fn-029) |
| REQ-FN-030 | Subscriber capture with validation + duplicate handling (Phase 5) | Done (pre-existing) | 100% | Story 5.3 DONE via Story 7.6 | [view](#d-req-fn-030) |
| REQ-FN-031 | Subscriber list / search / status / remove / export (Phase 5) | Done (pre-existing) | 100% | Story 5.4 DONE via Story 7.7 | [view](#d-req-fn-031) |
| REQ-FN-032 | Newsletter compose, send, history, unsubscribe link (Phase 5) | Not Started | 0% | Story 5.5 "NOT DONE" | [view](#d-req-fn-032) |
| REQ-FN-033 | Real SMTP IEmailService replacing ConsoleEmailService (Phase 5) | Not Started | 0% | Only ConsoleEmailService exists — password-reset mail is logged, not sent | [view](#d-req-fn-033) |
| REQ-FN-034 | Post view tracking — total and unique (Phase 5) | Not Started | 0% | Story 5.6 "NOT DONE"; PostViews table exists, nothing writes to it | [view](#d-req-fn-034) |
| REQ-FN-035 | Popular posts + per-post engagement statistics (Phase 5) | Not Started | 0% | Story 5.7 "NOT DONE" | [view](#d-req-fn-035) |
| REQ-FN-036 | Admin dashboard counts service (Phase 5) | Needs re-verify | 50% | `CommentSvc.GetAdminCounts` + `AdminCounts` model exist. ⚠ DevGuide 2026-08-02: the dashboard never calls them — user/comment/subscriber counts are constants in `AdminDashboard.razor.cs:63-68` (static) | [view](#d-req-fn-036) |
| REQ-FN-037 | RSS feed generation (Phase 6) | Done (pre-existing) | 100% | Story 6.1 DONE | [view](#d-req-fn-037) |
| REQ-FN-038 | Sitemap.xml + robots.txt endpoints (Phase 6) | Done (pre-existing) | 100% | FIX-015 — SitemapSvc.GenerateSitemap, endpoint at Program.cs line 169 | [view](#d-req-fn-038) |
| REQ-FN-039 | ThemeService, ThemeProvider, CSS-variable theme system (Phase 1/6) | PARTIAL | 85% | Story 1.5 DONE (QA gate 1.5 PASS) + 6.3 DONE; 3 themes + _variables.css. ⚠ DevGuide 2026-08-02: theme + dark-mode state live only in browser local storage, so there is no site-wide default an admin can set (static). [BRD-67 rev/BRD-92] 2026-08-06: theme files re-expressed as TrBlazeUI/shadcn variable sets in Phase 9 | [view](#d-req-fn-039) |
| REQ-FN-040 | Site settings persistence (Phase 6) | Needs re-verify | 15% | ⚠ DevGuide 2026-08-02: **there is no settings persistence layer at all** — no `SiteSettings` table in any migration, no settings repo/service in BlogEngine; `Settings.razor:337` says `// TODO: Implement actual save to database`. Only the pagination word count is stored, in browser local storage (static) | [view](#d-req-fn-040) |
| REQ-FN-041 | Seed / sample data set for immediate evaluation (Phase 6) | Not Started | 0% | Story 6.6 "NOT DONE" — only an admin row + 5 categories are seeded | [view](#d-req-fn-041) |
| REQ-FN-042 | Configurable storage-provider abstraction (Phase 5) | Not Started | 0% | FR19 — BlogImageService writes to local disk directly, no IFileStorage | [view](#d-req-fn-042) |
| REQ-FN-043 | Fix NU1605 restore failure and pin all package references (Phase 6) | FAIL | 0% | 2026-08-02: `dotnet build TechieBlog.slnx` FAILS on rungs #2 and #4. BlogUI pins Components.Web 10.0.0; FluentUI `4.*`→4.14.4 requires ≥10.0.9. **Blocks every runtime check.** 2026-08-06: strategic fix = TrBlazeUI migration (REQ-UI-048) removes the FluentUI packages; a tactical pin is still worthwhile for any runtime work before Phase 9. Pin-all-packages rule unchanged | [view](#d-req-fn-043) |
| REQ-FN-044 | GitHub template packaging + rename script (Phase 6) | Done (pre-existing) | 100% | scripts/ rename script; docs/packaging-strategy-brainstorm.md decision "Clone & Own" | [view](#d-req-fn-044) |
| REQ-FN-045 | Adopter documentation set (Phase 6) | Done (pre-existing) | 100% | README, GETTING_STARTED, customization, deployment, 2 migration guides, release checklist | [view](#d-req-fn-045) |
| REQ-FN-046 | BlogApp MAUI Blazor Hybrid project scaffold reusing BlogUI + BlogEngine (Phase 10) | Not Started | 0% | [BRD-94] Added 2026-08-06; new `source/BlogApp` project (Windows + macOS); MAUI workload needed on the Windows build host | [view](#d-req-fn-046) |
| REQ-FN-047 | BlogApp connection-setup screen + secure connection-string storage, direct PostgreSQL (Phase 10) | Not Started | 0% | [BRD-96] Added 2026-08-06; platform secure storage; no local DB, no sync; site DB must accept remote connections | [view](#d-req-fn-047) |
| REQ-FN-048 | Double opt-in email verification — token issue, email, consume, expiry (Phase 9) | Not Started | 0% | [BRD-98] Added 2026-08-06. Persisted tokens (not in-memory — cf. REQ-NFR-019), 24 h expiry, single use; verified-address registry so repeat visitors skip the step. **Depends on REQ-FN-033 (real SMTP)** | [view](#d-req-fn-048) |
| REQ-FN-049 | Self-hosted captcha — generate, render, validate (Phase 9) | Not Started | 0% | [BRD-99] Added 2026-08-06. **.NET BCL only — no third-party package, no external service.** SVG-rendered challenge, answer held server-side (IDataProtector-signed token or cache) with short expiry, `RandomNumberGenerator` for the code | [view](#d-req-fn-049) |
| REQ-FN-050 | Newsletter publishing + public archive queries (Phase 9) | Not Started | 0% | [BRD-100/101] Added 2026-08-06 — sent issues become public records with slug, list/paging and prev/next resolution. Extends REQ-FN-032 | [view](#d-req-fn-050) |
| REQ-NFR-001 | Performance — page load < 2 s, ≥ 100 concurrent users (Phase 6) | Not Started | 0% | Never measured; no load-test harness | [view](#d-req-nfr-001) |
| REQ-NFR-002 | Password hashing with an industry-standard salted algorithm (Phase 2) | PARTIAL | 50% | ⚠ SECURITY — AppEncrypt.CreateHash is hand-rolled, not BCrypt/PBKDF2/Argon2 | [view](#d-req-nfr-002) |
| REQ-NFR-003 | Parameterised queries everywhere (Phase 3) | Done (pre-existing) | 100% | Dapper DynamicParameters throughout DbAccess | [view](#d-req-nfr-003) |
| REQ-NFR-004 | HTTPS enforced outside Development (Phase 6) | Done (pre-existing) | 100% | UseHttpsRedirection + UseHsts in Program.cs | [view](#d-req-nfr-004) |
| REQ-NFR-005 | Rate limiting on authentication endpoints (Phase 2) | Not Started | 0% | ⚠ SECURITY — NFR10 unmet; failed attempts tracked but not throttled | [view](#d-req-nfr-005) |
| REQ-NFR-006 | Input validation + XSS prevention on all user input (Phase 3) | PARTIAL | 60% | Form validation present; Markdown → HTML sanitisation not audited | [view](#d-req-nfr-006) |
| REQ-NFR-007 | WCAG 2.1 AA conformance (Phase 6) | Not Started | 0% | Architecture §11.4 contract exists; never audited (no axe / keyboard / SR pass) | [view](#d-req-nfr-007) |
| REQ-NFR-008 | Clean architecture + XML docs on public members (Phase 6) | PARTIAL | 60% | Story 6.5 "NOT DONE"; 5-project separation holds, XML docs uneven | [view](#d-req-nfr-008) |
| REQ-NFR-009 | Browser and host compatibility (Phase 6) | Done (pre-existing) | 100% | Standard Blazor Server; deployment paths documented for Docker/Azure/Linux | [view](#d-req-nfr-009) |
| REQ-NFR-010 | Responsive layouts across 4 breakpoints (Phase 1) | Done (pre-existing) | 100% | Story 1.6 AC5; mockup responsive CSS carried into layout.css | [view](#d-req-nfr-010) |
| REQ-NFR-011 | PostgreSQL primary DB + numbered DbUp migrations (Phase 1) | Done (pre-existing) | 100% | Story 1.3 DONE | [view](#d-req-nfr-011) |
| REQ-NFR-012 | Resilience — retry, circuit breaker, graceful degradation (Phase 6) | Not Started | 0% | Architecture §8 design; no Polly reference in any csproj | [view](#d-req-nfr-012) |
| REQ-NFR-013 | Observability — Serilog rolling file in every head (Phase 1) | PARTIAL | 80% | Console + daily rolling file wired at startup, CloseAndFlush on exit. Missing: AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException handlers | [view](#d-req-nfr-013) |
| REQ-NFR-014 | Health endpoint verifying DB + critical services (Phase 6) | Not Started | 0% | Story 6.7 "NOT DONE" — no AddHealthChecks call anywhere | [view](#d-req-nfr-014) |
| REQ-NFR-015 | Correlation IDs in logs for request tracing (Phase 6) | Not Started | 0% | Story 6.7 AC6 "NOT DONE" | [view](#d-req-nfr-015) |
| REQ-NFR-016 | Automated test project — xUnit + bUnit (Phase 1) | Not Started | 0% | Story 1.14 DEFERRED; no test csproj in the solution | [view](#d-req-nfr-016) |
| REQ-NFR-017 | CI pipeline — build, test, artifacts on push and PR (Phase 1) | Not Started | 0% | Story 1.13 DEFERRED; .github/ contains no workflow | [view](#d-req-nfr-017) |
| REQ-NFR-018 | Caching layer — settings, taxonomy, listings, output cache (Phase 6) | Not Started | 0% | Architecture §8 design; no IMemoryCache registration | [view](#d-req-nfr-018) |
| REQ-NFR-019 | Persist password-reset tokens (survive restart / scale-out) (Phase 2) | Not Started | 0% | PasswordResetTokenRepo is an in-memory singleton by design (FIX-PLAN) | [view](#d-req-nfr-019) |
| REQ-NFR-020 | Remove legacy artifacts — duplicate AccessDenied, FluentDemo, MySql.Data (Phase 6) | Not Started | 0% | Story 6.5 "NOT DONE"; MySqlScripts/ retained for reference. ⚠ DevGuide 2026-08-02: also found orphan code-behinds `BlogHome.razor.cs` and `BlogPage.razor.cs` with no matching `.razor`, and an 11-line empty `ManageComments.razor.cs` scaffold (static) | [view](#d-req-nfr-020) |
| REQ-NFR-021 | Field-naming standards remediation — 32 underscore-prefixed fields (Phase 6) | Not Started | 0% | Standards drift across 17 files; remediate incrementally, not big-bang | [view](#d-req-nfr-021) |
| REQ-NFR-022 | Enable nullable reference types across all projects (Phase 6) | Not Started | 0% | All 5 csproj set `<Nullable>disable</Nullable>`, contradicting the standard | [view](#d-req-nfr-022) |
| REQ-NFR-023 | Hash the seeded admin credential and force first-login change (Phase 6) | Not Started | 0% | ⚠ SECURITY — 003-SeedData.sql inserts LoginPass = 'admin_password' in plain text | [view](#d-req-nfr-023) |

**Status values:** `Not Started` · `In Progress` · `Implemented` (code done, not yet verified) ·
`Verified` (self-smoke or verifier PASS) · `Done (pre-existing)` (migrated from an earlier dev plan as
already complete — build agents must NOT rebuild; terminal like `Verified`) · `Needs re-verify` ·
`PARTIAL` (some acceptance unmet — say what in Remarks) · `FAIL` (verifier ran and failed — bug in
Remarks) · `Blocked` · `N/A`.

**% guide:** `0` not started · `25` scaffolded · `50` in progress · `75` implemented-unverified · `100` verified.

**Remarks:** date + what was done / what is missing / bug reference. Visual-gate failures are prefixed
`⚠ visual:`; security findings `⚠ SECURITY`.

**Counts:** 56 `REQ-UI-*` · 50 `REQ-FN-*` · 0 `REQ-RAG-*` · 23 `REQ-NFR-*` = **129 requirements**.
Terminal: 69 (61 `Done (pre-existing)` + 8 `N/A (removed)`). Open: 60 (1 `FAIL`, 17 `Needs re-verify`,
6 `PARTIAL`, 36 `Not Started`). *(Counted from the table on 2026-08-06; earlier revisions of this
line carried forward an inherited estimate that did not match the rows.)*
*(2026-08-06 design review: 7 rows retired as `N/A` — REQ-UI-013/014/015/028/041/042, REQ-FN-024 —
because the authors pages, favourites and reader accounts left scope; 5 rows dropped to
`Needs re-verify` — REQ-UI-027/029, REQ-FN-022/023/029 — because comments and ratings became
anonymous and email-keyed. The retired rows stay in the table for traceability; build agents must
**remove** the corresponding built code, not rebuild it.)*
*(2026-08-06 second pass: registration retired — REQ-UI-002 `N/A`, REQ-FN-006 narrowed to staff
account creation + reset; 7 new rows added — REQ-UI-053…056 and REQ-FN-048…050 — for email
verification, the self-hosted captcha, and the public newsletter archive/issue pages.)*
*(8 `Needs re-verify` rows were downgraded on 2026-08-02 by the day-1 DevGuide pass — see
`docs/devguides/TechieBlog-DevGuide.md` §6. They were `Done` in the migrated plan; code reading showed
stub data, missing persistence or a policy mismatch. On 2026-08-06 the docs amendment added 7 new
rows — REQ-UI-048…052, REQ-FN-046/047, from BRD-92…97 + the revised BRD-30/67 — and downgraded
REQ-UI-005/006/033 to `Needs re-verify` because BRD-93, the revised BRD-30, and the TrBlazeUI swap
change what those rows must satisfy.)*

## UI / Pages

<!-- Each REQ carries an explicit `<a id="d-REQ-ID">` anchor so the Details column links straight to it.
     This project has no docs/mockups/ set produced by *mockups; the visual contract is the 28 HTML
     mockups in mockups/ at the repo root, referenced per page below. -->

### Page: Authentication (`/login`, `/register`, `/forgot-password`, `/reset-password`, `/access-denied`)

<a id="d-req-ui-001"></a>
- **REQ-UI-001** — Login page with email/password form, remember-me and forgot-password link (BRD-2). *Mockup:* `mockups/08-login.html`.
  - *Acceptance:* valid credentials authenticate and redirect to the intended page; invalid credentials show an inline error; controls do not overlap at desktop + mobile widths (visual gate).

<a id="d-req-ui-002"></a>
- **REQ-UI-002** — Registration page with email, password, confirm-password and terms checkbox (BRD-1). *Mockup:* `mockups/09-register.html`.
  - *Acceptance:* a new account is created with the Reader role; validation errors render inline; duplicate email is rejected with a clear message.

<a id="d-req-ui-003"></a>
- **REQ-UI-003** — Forgot-password and reset-password pages (BRD-4, BRD-5). *Mockups:* `mockups/10-forgot-password.html`, `mockups/11-reset-password.html`.
  - *Acceptance:* submitting an email issues a token; a valid token accepts a new password and redirects to login; an expired/invalid token shows an error.

<a id="d-req-ui-004"></a>
- **REQ-UI-004** — Access-denied page shown when an authorization policy rejects the user (BRD-9).
  - *Acceptance:* navigating to a policy-protected route as an under-privileged user lands here, not a raw 403.

### Page: Shell and shared components

<a id="d-req-ui-005"></a>
- **REQ-UI-005** — Main layout: header with logo, nav links, search, theme toggle and user menu; responsive sidebar; footer; mobile drawer nav (BRD-30, BRD-70). *Mockup:* layout extracted from `mockups/01-home.html`.
  - *Acceptance:* every public page renders inside the shell; nav collapses to a drawer below 768 px; no horizontal scroll at 320 px (visual gate).

<a id="d-req-ui-045"></a>
- **REQ-UI-045** — Shared components: PostCard (title, excerpt, author, date, category badge, rating), Pagination, Breadcrumb, Sidebar widgets (BRD-30, BRD-33).
  - *Acceptance:* PostCard renders identically in home, archive and search listings; pagination reflects real counts.

<a id="d-req-ui-047"></a>
- **REQ-UI-047** — Admin layout with grouped navigation (dashboard, content, taxonomy, users, resume, media, subscribers, settings) (BRD-70).
  - *Acceptance:* every management page renders inside `AdminLayout`; the active item is highlighted; role-gated groups are hidden for users who lack the policy.

### Page: Public reading (`/`, `/post/{slug}`, `/category/{slug}`, `/tag/{slug}`, `/series/{slug}`, `/search`, `/about`)

<a id="d-req-ui-006"></a>
- **REQ-UI-006** — Home page with featured post, recent-posts grid and sidebar (BRD-30). *Mockup:* `mockups/01-home.html`.
  - *Acceptance:* published posts appear newest-first with real data (data-render gate — the grid is non-empty when posts exist).

<a id="d-req-ui-007"></a>
- **REQ-UI-007** — Post view page: article body, author info, publish date, category, tags, reading time, related posts, series navigation, rating, favourite toggle, comments (BRD-31, BRD-32). *Mockup:* `mockups/02-blog-post.html`.
  - *Acceptance:* Markdown renders to formatted HTML; every metadata field is populated from the post record, not placeholder text.

<a id="d-req-ui-008"></a>
- **REQ-UI-008** — Category archive listing published posts in a category (BRD-25). *Mockup:* `mockups/03-category-archive.html`.

<a id="d-req-ui-009"></a>
- **REQ-UI-009** — Tag archive listing published posts with a tag, showing correct category names and post counts (BRD-26). *Mockup:* `mockups/04-tag-archive.html`.
  - *Acceptance:* the count shown equals the number of posts actually listed (the Story 7.5 regression).

<a id="d-req-ui-010"></a>
- **REQ-UI-010** — Series view listing all parts in reading order (BRD-29). *Mockup:* `mockups/05-series-view.html`.

<a id="d-req-ui-011"></a>
- **REQ-UI-011** — Search results page with query box, dynamic category filter, paging and term highlighting (BRD-34, BRD-35). *Mockup:* `mockups/06-search-results.html`.
  - *Acceptance:* results come from the database, not hardcoded placeholders; the category dropdown is populated from `CategorySvc`.

<a id="d-req-ui-012"></a>
- **REQ-UI-012** — About page and 404 page (BRD-30).

<a id="d-req-ui-046"></a>
- **REQ-UI-046** — RSS feed page and `<link rel="alternate">` auto-discovery in the head (BRD-63).

### Page: Portfolio home & public shell — Phase 9 (added 2026-08-06)

<a id="d-req-ui-048"></a>
- **REQ-UI-048** — **OPEN** — Migrate every `BlogUI` page, component and layout from Microsoft Fluent UI Blazor to TrBlazeUI (BRD-92). Re-express the three site themes as TrBlazeUI/shadcn CSS-variable sets (BRD-67 rev); add `<PortalHost />` to root layouts; retire `fluent-dark-mode.css`; remove both FluentUI package references.
  - *Acceptance:* no `Microsoft.FluentUI.*` package or `<Fluent*>` component remains in the solution; every page renders correctly in all three themes × light/dark; the build is green with all packages pinned.
- **Prerequisite:** owner adds GitHub Packages feed credentials to `nuget.config` (BRD-92).

<a id="d-req-ui-049"></a>
- **REQ-UI-049** — **OPEN** — Portfolio-style home page at `/` (BRD-30 revised): full-viewport hero (photo, "Hi, I'm {FirstName}", title, tagline, Get-In-Touch + Download-CV CTAs, social links), headline stats, about summary, latest-articles section from recent published posts, contact block — all from the site-owner's resume data. *Mockup:* `docs/mockups/01-home.html` (TrBlazeUI set, 2026-08-06 — the full 41-screen contract is `docs/TechieBlog-UIDesign.md`; the repo-root `mockups/` set is superseded).
  - *Acceptance:* every section renders from the `IsSiteOwner` user's data (data-render gate); latest articles link to real posts; the page is usable at mobile width; no login/user-menu entry appears (see REQ-UI-050).

<a id="d-req-ui-050"></a>
- **REQ-UI-050** — **OPEN** — Remove all public login/admin entry points (BRD-93): no header login link or user menu on public pages; engagement features (comment, rate, favourite) keep contextual sign-in prompts linking to `/login`; README documents the direct `/login` admin URL and first-time setup.
  - *Acceptance:* no public page renders a login/admin affordance for an anonymous visitor; `/login` still works by direct URL; a signed-in admin can still reach `/admin`; the README section exists.

### Page: BlogApp desktop — Phase 10 (added 2026-08-06)

<a id="d-req-ui-051"></a>
- **REQ-UI-051** — **OPEN** — BlogApp login screen + admin shell (BRD-95): the desktop app opens at login (after first-run connection setup) and hosts the shared `AdminLayout` navigation; the five role policies apply unchanged.
  - *Acceptance:* valid credentials open the admin shell; a Reader-role login is refused admin surfaces; logout returns to the login screen.

<a id="d-req-ui-052"></a>
- **REQ-UI-052** — **OPEN** — Full admin surface in BlogApp (BRD-97): posts, taxonomy, series, comments, media, resume data, users, subscribers, settings and themes — the same `BlogUI` admin pages as the web.
  - *Acceptance:* a post authored and published in BlogApp is immediately visible on the public website; every admin page family opens and operates against the live site database.

### Page: Newsletter & verification — Phase 9 (added 2026-08-06)

<a id="d-req-ui-053"></a>
- **REQ-UI-053** — **OPEN** — Public newsletter archive at `/newsletters` (BRD-100): page header, prominent subscribe card (email + captcha + double-opt-in note), list of past issues newest-first with issue number, title, date and excerpt, and paging. *Mockup:* `docs/mockups/42-newsletter-archive.html`.
  - *Acceptance:* only *sent* issues appear; each links to its issue page; subscribing creates a **pending** subscriber until confirmed; no issues → TbEmpty.

<a id="d-req-ui-054"></a>
- **REQ-UI-054** — **OPEN** — Public newsletter issue view at `/newsletter/{slug}` (BRD-101): issue number, title, sent date, rendered body, previous/next issue navigation and a compact subscribe CTA. *Mockup:* `docs/mockups/43-newsletter-view.html`.
  - *Acceptance:* an unknown or unsent slug returns 404; prev/next resolve by send order and hide at the ends.

<a id="d-req-ui-055"></a>
- **REQ-UI-055** — **OPEN** — Email confirmation landing page at `/verify/{token}` (BRD-98) with four states: confirmed, expired/invalid (offering a fresh link), already verified, and subscription-confirmed. *Mockup:* `docs/mockups/44-verify-email.html`.
  - *Acceptance:* a valid token confirms exactly once and the page states what was confirmed (comment, rating or subscription); a reused or expired token never confirms and explains why.

<a id="d-req-ui-056"></a>
- **REQ-UI-056** — **OPEN** — Captcha widget on every public write surface (BRD-99): challenge image, reload button and answer input, on the comment form, the rating step and the subscribe form. *Mockups:* `02-post-view.html`, `42-newsletter-archive.html`.
  - *Acceptance:* a wrong answer blocks submission with an inline error and issues a fresh challenge; the answer is never present in the page source; the control is keyboard reachable and labelled for screen readers.

### Page: Reader account (`/profile`, `/my-favorites`)

<a id="d-req-ui-013"></a>
- **REQ-UI-013** — User profile page showing current details with edit affordances (BRD-11). *Mockup:* `mockups/12-user-profile.html`.

<a id="d-req-ui-014"></a>
- **REQ-UI-014** — My Favourites page listing the reader's bookmarked posts (BRD-44). *Mockup:* `mockups/13-my-favorites.html`.

<a id="d-req-ui-015"></a>
- **REQ-UI-015** — **OPEN** — My Comments page listing the reader's comment history with links to each post (BRD-13). *Mockup:* `mockups/14-my-comments.html`.
  - *Acceptance:* a signed-in user sees their own comments newest-first with post title, date and status; an empty state renders when there are none.

### Page: Authoring (`/ManagePost`, `/BlogsList`, `/admin/preview/{id}`, `/admin/series`)

<a id="d-req-ui-016"></a>
- **REQ-UI-016** — Post editor: title, Markdown editor with live preview and formatting toolbar, metadata sidebar (category, tags, series, featured image, scheduling) (BRD-14, BRD-16, BRD-17). *Mockup:* `mockups/17-post-editor.html`.
  - *Acceptance:* preview updates as the author types; saving persists every metadata field.

<a id="d-req-ui-017"></a>
- **REQ-UI-017** — Post list with status filters and row actions (BRD-14). *Mockup:* `mockups/18-my-posts.html`.

<a id="d-req-ui-018"></a>
- **REQ-UI-018** — Draft preview rendering an unpublished post in full (BRD-19). *Mockup:* `mockups/20-draft-preview.html`.

<a id="d-req-ui-024"></a>
- **REQ-UI-024** — Series list and manage-series form with part ordering (BRD-27).

### Page: Administration (`/admin`, `/users`, `/CommentsList`, `/CategoriesList`, `/admin/tags`, `/admin/subscribers`, `/settings`)

<a id="d-req-ui-019"></a>
- **REQ-UI-019** — Admin dashboard with statistic tiles (posts, users, comments, subscribers) and quick actions (BRD-62). *Mockup:* `mockups/21-admin-dashboard.html`.
  - *Acceptance:* tiles show live counts, not placeholders (data-render gate).

<a id="d-req-ui-020"></a>
- **REQ-UI-020** — User management list with role badges, search and actions, plus an add-user form (BRD-10). *Mockup:* `mockups/23-admin-users.html`.

<a id="d-req-ui-021"></a>
- **REQ-UI-021** — Comment moderation queue with approve/reject/edit and bulk actions (BRD-39). *Mockup:* `mockups/24-admin-comments.html`.

<a id="d-req-ui-022"></a>
- **REQ-UI-022** — Category management list and add/edit form (BRD-22). *Mockup:* `mockups/25-admin-categories.html`.

<a id="d-req-ui-023"></a>
- **REQ-UI-023** — Tag management list and add/edit form (BRD-24). *Mockup:* `mockups/26-admin-tags.html`.

<a id="d-req-ui-025"></a>
- **REQ-UI-025** — Subscriber management with list, search, status filter, remove and export (BRD-57, BRD-58). *Mockup:* `mockups/27-admin-subscribers.html`.

<a id="d-req-ui-026"></a>
- **REQ-UI-026** — Site settings page: title, tagline, posts-per-page, comment moderation, theme selector, SMTP, storage (BRD-68, BRD-69). *Mockup:* `mockups/28-admin-settings.html`.

<a id="d-req-ui-043"></a>
- **REQ-UI-043** — **OPEN** — Newsletter composer: subject, Markdown/rich body, preview, recipient selection, send button with progress (BRD-59).
  - *Acceptance:* an admin can compose, preview and dispatch a newsletter; send progress and outcome are visible; history is listed.

<a id="d-req-ui-044"></a>
- **REQ-UI-044** — **OPEN** — Analytics dashboard: popular posts, view trends, engagement averages, date-range filter (BRD-61).
  - *Acceptance:* charts render from real view data; the date range filters every panel.

### Page: Engagement components

<a id="d-req-ui-027"></a>
- **REQ-UI-027** — Star rating widget, interactive for signed-in users and read-only otherwise, showing average and count (BRD-40, BRD-42).

<a id="d-req-ui-028"></a>
- **REQ-UI-028** — Favourite toggle on the post page and post cards with a visual favourited state (BRD-43).

<a id="d-req-ui-029"></a>
- **REQ-UI-029** — Comment form and comment list on the post page, signed-in only, with own-comment edit/delete (BRD-36, BRD-37).

<a id="d-req-ui-030"></a>
- **REQ-UI-030** — Subscribe form component placeable in sidebar, footer or a dedicated page (BRD-56).

### Page: Theming

<a id="d-req-ui-031"></a>
- **REQ-UI-031** — Light/dark toggle in the header on every page, persisted across sessions (BRD-66).
  - *Acceptance:* the choice survives a reload; the toggle is reachable by keyboard and exposes `role="switch"`.

<a id="d-req-ui-032"></a>
- **REQ-UI-032** — Site-theme selector in Site Settings offering the three shipped themes (BRD-68).

<a id="d-req-ui-033"></a>
- **REQ-UI-033** — Dark-mode corrections across sidebar, public pages, search, about and admin dialogs/checkboxes (BRD-66).
  - *Acceptance:* no unreadable text or invisible control in dark mode on any page (visual gate).

### Page: Media (`/admin/images`)

<a id="d-req-ui-034"></a>
- **REQ-UI-034** — Media library: category tabs, upload, delete, copy URL, paging (BRD-47). *Mockup:* `mockups/19-media-library.html`.

<a id="d-req-ui-035"></a>
- **REQ-UI-035** — Reusable ImagePicker component with "choose from library" and "upload new", filtered by category, two-way bound to an image path (BRD-48).

### Page: Resume and authors (`/resume`, `/authors`, `/author/{username}`, `/admin/profile`, `/admin/experience`, `/admin/skills`, `/admin/awards`)

<a id="d-req-ui-036"></a>
- **REQ-UI-036** — Public resume page for the site owner with hero, about + stats, experience timeline, skills grid, awards, community stats and contact, plus anchor navigation and CV download (BRD-49, BRD-50, BRD-51, BRD-52).
  - *Acceptance:* all sections render for the `IsSiteOwner` user; anchor navigation scrolls smoothly; the page is usable at mobile width.

<a id="d-req-ui-037"></a>
- **REQ-UI-037** — Manage experience: list, add/edit/delete, display-order control, company-logo picker; admins can switch user (BRD-50).

<a id="d-req-ui-038"></a>
- **REQ-UI-038** — Manage skills grouped by category with add/edit/delete, new categories and in-category ordering (BRD-51).

<a id="d-req-ui-039"></a>
- **REQ-UI-039** — Manage awards: list, add/edit/delete, badge-image picker, ordering (BRD-51).

<a id="d-req-ui-040"></a>
- **REQ-UI-040** — Manage profile: basic info, social links, username, resume settings, CV picker and jump-offs to experience/skills/awards (BRD-11, BRD-12).
  - *Acceptance:* authors see only their own data; admins get a user selector.

<a id="d-req-ui-041"></a>
- **REQ-UI-041** — Authors listing with avatar, name, username, title and article count (BRD-53). *Mockup:* `mockups/07-author-profile.html` (profile styling reference).

<a id="d-req-ui-042"></a>
- **REQ-UI-042** — Author profile page with compact header, article list and optional resume sections (BRD-54).
  - *Acceptance:* an unknown username returns 404; resume sections appear only when `ResumeEnabled` is true.

## Functional requirements

<a id="d-req-fn-001"></a>
- **REQ-FN-001** — All five projects target `net10.0` and the solution restores and builds (BRD-85).

<a id="d-req-fn-002"></a>
- **REQ-FN-002** — The `BlogSvc` REST project is removed; `BlogUI` resolves `BlogEngine` services from DI with no HTTP hop (BRD-85).

<a id="d-req-fn-003"></a>
- **REQ-FN-003** — PostgreSQL schema created by numbered DbUp scripts, executed automatically at host startup (BRD-88).

<a id="d-req-fn-004"></a>
- **REQ-FN-004** — Fluent UI Blazor replaces Blazorise as the only component library (BRD-65).

<a id="d-req-fn-005"></a>
- **REQ-FN-005** — `AuthSvc.AppLogin` validates credentials and issues a JWT with `PrimarySid`, `Name`, `Email`, `Role`, recording the login (BRD-2).

<a id="d-req-fn-006"></a>
- **REQ-FN-006** — Registration creates a Reader-role account after enforcing email uniqueness and password strength (BRD-1, BRD-3).

<a id="d-req-fn-007"></a>
- **REQ-FN-007** — Password reset issues, validates and consumes a time-limited token (BRD-4, BRD-5).

<a id="d-req-fn-008"></a>
- **REQ-FN-008** — An expiring session token is refreshed without forcing re-login (BRD-6).

<a id="d-req-fn-009"></a>
- **REQ-FN-009** — Five roles and five authorization policies are registered and applied to every protected page (BRD-7, BRD-8).

<a id="d-req-fn-010"></a>
- **REQ-FN-010** — Admin user management: list, search, view, change role, enable/disable, delete (BRD-10).

<a id="d-req-fn-011"></a>
- **REQ-FN-011** — Profile read/update and password change with current-password verification (BRD-11, BRD-12).

<a id="d-req-fn-012"></a>
- **REQ-FN-012** — Post create, read, update and delete through `BlogSvc` returning `Result<BlogPost>` (BRD-14).

<a id="d-req-fn-013"></a>
- **REQ-FN-013** — Slugs are generated from the title, unique, overridable, and used for all public routes (BRD-15, BRD-33).

<a id="d-req-fn-014"></a>
- **REQ-FN-014** — Post bodies are rendered from Markdown to HTML via Markdig (BRD-16).

<a id="d-req-fn-015"></a>
- **REQ-FN-015** — Draft posts are excluded from every public query; publish/unpublish transitions the state (BRD-18).

<a id="d-req-fn-016"></a>
- **REQ-FN-016** — Scheduled posts are published automatically at their scheduled time by a hosted background service (BRD-20, BRD-21).

<a id="d-req-fn-017"></a>
- **REQ-FN-017** — Category CRUD and single-primary-category assignment per post (BRD-22, BRD-23).

<a id="d-req-fn-018"></a>
- **REQ-FN-018** — Tag CRUD, many-to-many post-tag junction, autocomplete, inline creation, and accurate per-tag post counts (BRD-24, BRD-26).

<a id="d-req-fn-019"></a>
- **REQ-FN-019** — Series CRUD, part ordering, and previous/next navigation resolution (BRD-27, BRD-28).

<a id="d-req-fn-020"></a>
- **REQ-FN-020** — Published-post listings, featured-post selection, related posts and reading-time calculation (BRD-30, BRD-31, BRD-32).

<a id="d-req-fn-021"></a>
- **REQ-FN-021** — Keyword search across title, abstract, body and tags with paging and result counts (BRD-34, BRD-35).

<a id="d-req-fn-022"></a>
- **REQ-FN-022** — Comment add/edit/delete, approval workflow, pending queue and per-post counts (BRD-36, BRD-37, BRD-38, BRD-39).

<a id="d-req-fn-023"></a>
- **REQ-FN-023** — One rating per user per post, changeable, with average and count aggregates and top-rated queries (BRD-40, BRD-41, BRD-42).

<a id="d-req-fn-024"></a>
- **REQ-FN-024** — Favourite add/remove/toggle, per-user listing and per-post counts (BRD-43, BRD-44).

<a id="d-req-fn-025"></a>
- **REQ-FN-025** — Image upload validated server-side against per-category size and format limits, stored with a collision-proof filename (BRD-45, BRD-46).

<a id="d-req-fn-026"></a>
- **REQ-FN-026** — `BlogImage` carries category, alt text, MIME type and dimensions; migration `012` applies the schema (BRD-46).

<a id="d-req-fn-027"></a>
- **REQ-FN-027** — Resume data model and repositories for experience (`UserEvents` extensions), skills, awards and stats (BRD-50, BRD-51).

<a id="d-req-fn-028"></a>
- **REQ-FN-028** — CV file upload (PDF, ≤ 10 MB) and public download from the resume page (BRD-52).

<a id="d-req-fn-029"></a>
- **REQ-FN-029** — Unique URL-safe usernames, single-site-owner enforcement, author lookup by username and author listing by published posts (BRD-53, BRD-54, BRD-55).

<a id="d-req-fn-030"></a>
- **REQ-FN-030** — Subscriber capture with email validation and duplicate handling (BRD-56).

<a id="d-req-fn-031"></a>
- **REQ-FN-031** — Subscriber list, search, status update, removal and export projection (BRD-57, BRD-58).

<a id="d-req-fn-032"></a>
- **REQ-FN-032** — **OPEN** — Newsletter composition, SMTP dispatch to all or a filtered segment, send-history log and an unsubscribe link in every message (BRD-59).
  - *Acceptance:* a newsletter reaches a test subscriber; the send is logged; the unsubscribe link removes the subscriber.

<a id="d-req-fn-033"></a>
- **REQ-FN-033** — **OPEN** — A real SMTP `IEmailService` replaces `ConsoleEmailService`, configured from Site Settings (BRD-4, BRD-59).
  - *Acceptance:* a password-reset email is actually delivered in a configured environment; failures are logged, not swallowed.

<a id="d-req-fn-034"></a>
- **REQ-FN-034** — **OPEN** — Track total and unique post views per visit, writing to the existing `PostViews` table (BRD-60).

<a id="d-req-fn-035"></a>
- **REQ-FN-035** — **OPEN** — Popular-post ranking and per-post engagement statistics (comments, ratings, views) (BRD-61).

<a id="d-req-fn-036"></a>
- **REQ-FN-036** — Aggregate counts for the admin dashboard (posts, users, comments, subscribers) (BRD-62).

<a id="d-req-fn-037"></a>
- **REQ-FN-037** — RSS 2.0 feed of recent published posts with title, link, description, pubDate and author (BRD-63).

<a id="d-req-fn-038"></a>
- **REQ-FN-038** — `sitemap.xml` covering published posts, categories and tags, referenced from a generated `robots.txt` (BRD-64).

<a id="d-req-fn-039"></a>
- **REQ-FN-039** — `ThemeService` + `ThemeProvider` apply the site theme and light/dark mode from persisted preferences; all values come from CSS custom properties (BRD-65, BRD-66, BRD-67).

<a id="d-req-fn-040"></a>
- **REQ-FN-040** — Site settings persist and take effect without a restart where possible (BRD-69).
  - *Acceptance:* changing posts-per-page or the theme is reflected on the public site after save.

<a id="d-req-fn-041"></a>
- **REQ-FN-041** — **OPEN** — Seed/sample data: sample posts demonstrating Markdown, images and series; one user per role; categories, tags, comments and ratings (BRD-73).

<a id="d-req-fn-042"></a>
- **REQ-FN-042** — **OPEN** — `IFileStorage` abstraction with local, network and cloud implementations behind the image service (BRD-45, BRD-46).

<a id="d-req-fn-043"></a>
- **REQ-FN-043** — **OPEN, BLOCKER** — Restore the solution to a green build and pin all package references (BRD-91).
  - *Failure (2026-08-02):* `NU1605` — `BlogUI` references `Microsoft.AspNetCore.Components.Web 10.0.0`; `Microsoft.FluentUI.AspNetCore.Components 4.*` resolves to 4.14.4 requiring ≥ 10.0.9. Reproduced on ladder rung #2 (`~/.dotnet/dotnet`) and rung #4 (`cmd.exe /c dotnet`).
  - *Acceptance:* `dotnet build TechieBlog.slnx` succeeds with 0 errors; no `PackageReference` uses a floating version (`4.*`).

<a id="d-req-fn-044"></a>
- **REQ-FN-044** — The repository is consumable as a GitHub template and the rename script re-brands a clone (BRD-71).

<a id="d-req-fn-045"></a>
- **REQ-FN-045** — Adopter documentation covers getting started, customization, deployment and database migration (BRD-72).

<a id="d-req-fn-046"></a>
- **REQ-FN-046** — **OPEN** — `source/BlogApp` MAUI Blazor Hybrid project (Windows + macOS) referencing BlogUI, BlogEngine and BlogModels; registers the same DI graph as the web host (BRD-94). *Prerequisite:* MAUI workload on the Windows build host; runtime verification opts in via `core-config.yaml` `runtimeVerification` once the head exists.
  - *Acceptance:* the solution builds with the sixth project; BlogApp launches to the connection-setup/login flow on Windows.

<a id="d-req-fn-047"></a>
- **REQ-FN-047** — **OPEN** — BlogApp first-run connection-setup: capture + test the site's PostgreSQL connection string, store it in platform secure storage, and reconfigure from a settings surface; all data access goes directly to the site database — no local DB, no sync (BRD-96).
  - *Acceptance:* an invalid connection string is rejected with a clear error; a valid one persists across app restarts and is not stored in plain text; deleting it returns the app to the setup screen.

<a id="d-req-fn-048"></a>
- **REQ-FN-048** — **OPEN** — Double opt-in email verification (BRD-98): issue a single-use, 24-hour token for an anonymous comment, rating or subscription; email it; consume it on `/verify/{token}`; record the address as verified so later submissions from it skip the step. Tokens are **persisted**, not in-memory (contrast ADR-008 / REQ-NFR-019).
  - *Acceptance:* an unconfirmed comment never appears publicly; a confirmed one enters the moderation queue; a token works exactly once and expires; a verified address posts without re-confirming.
  - *Depends on:* REQ-FN-033 (real SMTP `IEmailService`) — with the console stub, verification cannot complete outside development.

<a id="d-req-fn-049"></a>
- **REQ-FN-049** — **OPEN** — Self-hosted captcha (BRD-99): generate a random code with `RandomNumberGenerator`, render it as a distorted **SVG** server-side, and validate the answer against a server-held value (an `IDataProtector`-signed token or a short-lived cache entry). **.NET base class library only — no third-party package and no external service.**
  - *Acceptance:* the expected answer never reaches the client; a challenge is single-use and expires in minutes; reload issues a new one; validation failure blocks the write and re-challenges.
  - *Note:* avoid `System.Drawing.Common` — it is Windows-only and unsupported cross-platform; SVG keeps the renderer portable and dependency-free.

<a id="d-req-fn-050"></a>
- **REQ-FN-050** — **OPEN** — Newsletter publishing and public archive queries (BRD-100, BRD-101): a sent issue becomes a public record with a slug; list published issues newest-first with paging; resolve previous/next by send order. Extends the composer/send pipeline (REQ-FN-032).
  - *Acceptance:* a draft or unsent issue is never publicly reachable; the archive count matches the issues listed; prev/next omit missing neighbours at the ends.

## RAG / AI requirements (→ /techierag)

*None. TechieBlog has no AI, LLM, embedding or vector-search features, and does not reference
TechieRag. This section exists for schema completeness only.*

## Non-functional

<a id="d-req-nfr-001"></a>
- **REQ-NFR-001** — **OPEN** — Public pages load in under 2 s on broadband and the app serves ≥ 100 concurrent users (BRD-78).

<a id="d-req-nfr-002"></a>
- **REQ-NFR-002** — **OPEN** — Passwords are stored with an industry-standard salted hash (BRD-79). ⚠ SECURITY — replace `AppEncrypt.CreateHash` with PBKDF2/BCrypt/Argon2 and re-hash on next login.

<a id="d-req-nfr-003"></a>
- **REQ-NFR-003** — All database access uses parameterised queries (BRD-80).

<a id="d-req-nfr-004"></a>
- **REQ-NFR-004** — HTTPS redirection and HSTS are enforced outside Development (BRD-81).

<a id="d-req-nfr-005"></a>
- **REQ-NFR-005** — **OPEN** — Authentication endpoints are rate-limited (BRD-82). ⚠ SECURITY.

<a id="d-req-nfr-006"></a>
- **REQ-NFR-006** — **OPEN** — All user input is validated and encoded against XSS and injection, including rendered Markdown (BRD-83).

<a id="d-req-nfr-007"></a>
- **REQ-NFR-007** — **OPEN** — WCAG 2.1 AA conformance audited: heading hierarchy, 4.5:1 contrast, keyboard operability, visible focus, ARIA labelling, 44×44 px targets, 200% zoom (BRD-84).

<a id="d-req-nfr-008"></a>
- **REQ-NFR-008** — **OPEN** — Clean-architecture separation upheld and XML documentation present on all public members (BRD-85).

<a id="d-req-nfr-009"></a>
- **REQ-NFR-009** — Runs on current Chrome, Firefox, Edge and Safari and deploys to any .NET-capable host (BRD-86).

<a id="d-req-nfr-010"></a>
- **REQ-NFR-010** — Layouts adapt across mobile, tablet, desktop and wide breakpoints (BRD-87).

<a id="d-req-nfr-011"></a>
- **REQ-NFR-011** — PostgreSQL is the primary database and all schema changes ship as numbered DbUp scripts (BRD-88).

<a id="d-req-nfr-012"></a>
- **REQ-NFR-012** — **OPEN** — Retry with exponential backoff and a circuit breaker protect database, email and storage calls, with the defined graceful-degradation behaviour (BRD-89).

<a id="d-req-nfr-013"></a>
- **REQ-NFR-013** — Serilog writes to console and a daily rolling file under `logs/`, wired before anything can fail, with `Log.CloseAndFlush()` on exit and libraries logging via `ILogger<T>` (BRD-90).
  - *Remaining:* add `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers.

<a id="d-req-nfr-014"></a>
- **REQ-NFR-014** — **OPEN** — `/health` and `/health/ready` endpoints verify database and critical-service availability (BRD-74).

<a id="d-req-nfr-015"></a>
- **REQ-NFR-015** — **OPEN** — Logs carry a correlation ID per request for cross-component tracing (BRD-75).

<a id="d-req-nfr-016"></a>
- **REQ-NFR-016** — **OPEN** — `TechieBlog.Tests` (xUnit + bUnit) covers engine services and key components; 80% target for `BlogEngine` (BRD-76).

<a id="d-req-nfr-017"></a>
- **REQ-NFR-017** — **OPEN** — A GitHub Actions workflow builds, tests and produces artifacts on push to main/dev and on pull requests (BRD-77).

<a id="d-req-nfr-018"></a>
- **REQ-NFR-018** — **OPEN** — In-memory caching for site settings, taxonomy and listings, plus output caching for public listings and RSS, with the defined invalidation events (BRD-78).

<a id="d-req-nfr-019"></a>
- **REQ-NFR-019** — **OPEN** — Password-reset tokens are persisted so they survive a restart and work across instances (BRD-5).

<a id="d-req-nfr-020"></a>
- **REQ-NFR-020** — **OPEN** — Remove legacy artifacts: duplicate `AccessDenied` (page vs component), `FluentDemo.razor`, the `MySql.Data` package reference, and decide the fate of `MySqlScripts/` (BRD-85).

<a id="d-req-nfr-021"></a>
- **REQ-NFR-021** — **OPEN** — Rename the 32 underscore-prefixed instance fields across 17 files to the project's no-prefix convention, incrementally as each file is touched (BRD-85).

<a id="d-req-nfr-022"></a>
- **REQ-NFR-022** — **OPEN** — Enable `<Nullable>enable</Nullable>` project by project and resolve the resulting warnings (BRD-85).

<a id="d-req-nfr-023"></a>
- **REQ-NFR-023** — **OPEN** — Hash the seeded admin credential in `003-SeedData.sql` and require a password change on first login (BRD-79). ⚠ SECURITY.

---
Last updated: 2026-08-06
Traceability: every live BRD-1 … BRD-101 in `docs/TechieBlog-BRD.md` maps to at least one REQ above.
Retired BRD IDs (2026-08-06): BRD-1, BRD-13, BRD-37, BRD-43, BRD-44, BRD-53, BRD-54, BRD-55 — their REQ rows
are marked `N/A (removed)` and kept for traceability; IDs are never reused.
