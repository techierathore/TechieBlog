# TechieBlog — UI Design Spec (TrBlazeUI)

<!-- Produced by *mockups on 2026-08-06 per the amended BRD (BRD-92 TrBlazeUI adoption, BRD-30 revised
     portfolio home, BRD-93 no public admin entry, BRD-94…97 BlogApp desktop). This spec + the rendered
     docs/mockups/*.html are the visual contract the build matches and the verifier's visual-truth gate
     diffs against — they are the ONLY mockup set, and every "Mockup:" link below is relative to this
     docs/ folder.

     A second, older set used to sit at repo-root mockups/ (the pre-REQ-UI-048 Fluent design, last
     touched 2025-12-16, no TrBlazeUI anywhere in it). It was DELETED on 2026-08-22 at the owner's
     instruction. The reason is worth keeping: an `@mockups/` reference resolves from the repo root,
     so a comparison meant for THIS set silently ran against that one, and the resulting "the site
     looks nothing like the mockups" was a folder mix-up rather than a defect (checklist UAT-008).
     There is now exactly one folder named mockups in the repository, and it is this one. Do not
     re-create the other. -->

**Component library:** TrBlazeUI (`TrBlazeUI.Components`, GitHub Packages feed) — shadcn/ui-compatible,
CSS-variable theming, dark mode via `.dark` on `<html>`, `<PortalHost />` required in root layouts.
**Catalog source:** the TrBlazeUI GitHub repository (github.com/techierathore/TrBlazeUI) — the local
`.trblazeui/TrBlazeUI-AI-Reference.md` is not yet deployed because the package is not installed
(REQ-UI-048 prerequisite); re-check component names against the AI reference when the feed is wired.

## Design system

- **Shells:** public pages use **TbResponsiveNav** (sticky top bar; brand + Home/Categories/Series/
  Resume/About + theme toggle — **no login/user-menu entry points**, BRD-93, and **no Authors entry**
  since the author-browsing screens were dropped). Admin pages (web + BlogApp) use the **TbSidebar**
  inset shell: grouped navigation (Content · Taxonomy · Media · Resume · Audience · System), topbar
  with a **collapse trigger** (icon-rail mode, `.collapsed`), TbBreadcrumb and theme toggle. The
  signed-in identity lives **in the topbar**, as a TbDropdownMenu on the avatar offering *My Profile*
  and *Log Out* — the sidebar has no footer identity block, because a name with no actions attached
  earns no permanent space. BlogApp screens wrap the same shell in desktop window chrome (MAUI head)
  and their menu logs out to the desktop login screen. Auth screens are a bare centered TbCard
  (reached by direct URL only).
- **Icons:** Lucide (bundled with TrBlazeUI), rendered as inline 24×24 stroke SVGs at 16 px
  (`.icon`) / 20 px (`.icon-lg`) — one identical icon set across every admin screen. Social links use
  the real brand marks (LinkedIn, GitHub, X, YouTube, RSS) as filled SVGs with brand-coloured hover.
  No emoji or text glyphs are used as UI icons anywhere.
- **Identity:** the site owner is shown with a real **profile photograph** (`profile-photo.svg`
  placeholder in the mockups), never initials. Other people (commenters, staff rows) use initials
  TbAvatars.
- **Anonymous engagement:** commenting and rating need **no account** — the visitor supplies a name
  and email (BRD-36, BRD-40/41 revised). No public screen offers sign-in, registration or a reader
  account area; public self-service registration is retired (~~BRD-1~~).
- **Rating is a real control:** TbRating renders five focusable star buttons (not display-only text).
  Choosing a star reveals the email + captcha step; an already-verified address rates in one click.
- **Anti-abuse on every public write surface** (comment, rating, subscribe): a **self-hosted captcha**
  — challenge image, reload button, answer input, generated and validated in-process with no
  third-party service (BRD-99) — plus **double opt-in email verification**, explained inline by the
  `.verify-note` panel and landing on `/verify/{token}` (BRD-98).
- **Dialogs:** every TbDialog is **closed on load** (`hidden`) and opened by its row action; list
  pages always render the list first.
- **Theme:** neutral shadcn palette, blue primary (`--primary`), radius `0.625rem`; the three site
  themes (TrBlaze Modern default · Developer Dark · Minimal Clean) are CSS-variable sets over the same
  tokens (BRD-67 revised). Light/dark via `.dark` class, toggle in every shell header (BRD-66).
- **Mockup stylesheet:** `docs/mockups/trblazeui.css` — a visual approximation of TrBlazeUI's tokens
  and component shapes shared by every mockup; the build uses the real library, not this file.
- **Controls inventory used:** TbResponsiveNav, TbSidebar, TbBreadcrumb, TbCard, TbButton, TbBadge,
  TbAvatar, TbInput, TbLabel, TbSelect, TbCombobox, TbMultiSelect, TbNumericInput, TbTextarea,
  TbCheckbox, TbSwitch, TbTabs, TbDataTable, TbPagination, TbDialog, TbDropdownMenu, TbAlert, TbToast,
  TbEmpty, TbSkeleton, TbProgress, TbSeparator, TbFileUpload, TbDatePicker, TbTimePicker,
  TbDateRangePicker, Rating, MarkdownEditor, PortalHost, Lucide icons.
- **Data conventions:** placeholder persona "Ravi Rathore" (site owner) demonstrates the resume-driven
  surfaces; all counts/lists are realistic non-zero samples so the data-render gate has a shape to
  compare against; every screen defines empty/loading/error states in its spec block.

## Table of Contents

1. [Design system](#design-system)
2. Screens — public: Home · Post view · Category archive · Tag archive · Series list · Series view · Search results · About · Not found
3. Screens — identity: Resume
4. Screens — auth (direct-URL only, BRD-93): Sign in · Forgot password · Reset password · Access denied
4b. Screens — newsletter & verification (public): Newsletter archive · Newsletter issue · Email confirmation
5. Screens — authoring: Post editor · Posts list · Draft preview · Series admin · Media library
6. Screens — admin console: Dashboard · Users · Comments · Categories · Tags · Subscribers · Settings · Newsletter composer · Analytics
7. Screens — resume management: Profile · Experience · Skills · Awards
8. Screens — BlogApp desktop (F-DESK): Connection setup · Login · Shell

## Screens

### Screen: Home — portfolio landing (`/`)

- **Mockup:** [mockups/01-home.html](mockups/01-home.html) · **Roles:** anonymous · **BRD:** BRD-30 (revised), BRD-93 · **REQ:** REQ-UI-049, REQ-UI-050
- **Layout:** single-column personal-brand landing — TbResponsiveNav, full-viewport hero, stats, about, latest articles, contact, footer.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Top nav | TbResponsiveNav | Brand + Home/Categories/Series/Resume/About links, search icon, theme TbToggle; **no login/user menu** (BRD-93), **no Authors entry** | mobile → burger + TbSheet drawer |
| Hero | TbAvatar (xl, **photo**) + Typography + TbButton | Site-owner photograph (not initials), "Hi, I'm {FirstName}", title, tagline; Get-In-Touch (primary) + Download-CV (outline) CTAs; brand-icon social row (LinkedIn/GitHub/X/YouTube/RSS) | no photo set → initials fallback; no site owner flagged → generic brand hero |
| Stats row | TbCard ×4 | Headline stats from `UserStats` | empty → section hidden |
| About summary | TbCard | Short bio from owner profile, link to `/resume` | empty → section hidden |
| Latest articles | TbCard ×3 (post card) | Recent published posts: featured image, category TbBadge, title, excerpt, author, date, read time, Rating (read-only) | none → TbEmpty "No posts yet"; loading → TbSkeleton ×3 |
| Contact | TbCard + TbButton | Email, location, socials, mail CTA | — |
| Footer | Typography + TbSeparator | © line, RSS/About/Resume links | — |

- **Interactions & states:** theme toggle persists (local storage); hero CTAs smooth-scroll to `#contact` / open CV file; article cards navigate to `/post/{slug}`; no admin affordance renders for any anonymous visitor (BRD-93 acceptance).

### Screen: Post view (`/post/{slug}`)

- **Mockup:** [mockups/02-post-view.html](mockups/02-post-view.html) · **Roles:** anonymous · **BRD:** BRD-31, BRD-32, BRD-36 *(revised)*, BRD-40/41 *(revised)* · **REQ:** REQ-UI-007, REQ-UI-027, REQ-UI-029
- **Layout:** Single centered column (max 820px): breadcrumb → title/meta → featured image → body → series card → engagement card → related grid → comments.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Breadcrumb | TbBreadcrumb | Home / Category / Post title | current = plain text |
| Title + meta | Typography + TbAvatar (sm) + TbBadge + tag chips | Author byline is plain text (public author profiles retired 2026-08-06); category badge → 03-category-archive.html; tags → 04-tag-archive.html | — |
| Featured image | styled placeholder div | Post.FeaturedImage 1200×630 | missing image → gradient placeholder |
| Article body | Typography; code → TbCard + `.mono` | Rendered markdown/HTML incl. H2s and code blocks | loading → TbSkeleton |
| Series navigation | TbCard + TbButton (outline prev / primary next) | "Part 2 of 5 · Blazor Deep Dives" → 06-series-view.html | hidden when post not in a series |
| Engagement row | Rating (interactive) | "Rate this article" — avg 4.2 / 31 ratings; **anonymous, keyed by email**, one per email per post, changeable (BRD-40/41 revised). **No favourite toggle** — F-FAV retired | click → email prompt → stored + thanks TbToast; already-rated → shows own rating |
| Related posts | TbCard ×3 (compact) | Same category/tags, 3 items | none → section hidden |
| Comments | TbAvatar + Typography per comment | Approved comments, newest last, flat (no threading) | 0 comments → TbEmpty "Be the first to comment" |
| Comment form | TbCard + TbInput ×2 + TbTextarea + TbButton | **Anonymous** — Name, Email ("never published — moderation and reply notification only"), Comment body, "Post comment"; note that comments appear after moderation (BRD-36 revised) | validation errors inline; submit → TbToast "Awaiting moderation"; **no sign-in prompt anywhere** |

- **Interactions & states:** Unknown slug → 09-404.html. Loading → TbSkeleton blocks for image/body/comments. Rating and commenting work for any visitor with **no account and no sign-in** (BRD-36, BRD-40 revised) — the email identifies the visitor for de-duplication and moderation and is never displayed. The page offers **no** sign-in, registration or favourite affordance. Because input is anonymous, comment moderation (BRD-38/39) and spam protection are load-bearing — see the Architecture open question.
- Author byline renders as plain text (public author profiles retired 2026-08-06).

### Screen: Category archive (`/category/{slug}`)

- **Mockup:** [mockups/03-category-archive.html](mockups/03-category-archive.html) · **Roles:** anonymous · **BRD:** BRD-25 · **REQ:** REQ-UI-052
- **Layout:** Full-width container: breadcrumb + header (name, description, count badge) → 3-col post-card grid → pagination → sibling-category chip row.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Page header | Typography + TbBadge | Category name, description, "14 posts" | unknown slug → 09-404.html |
| Post grid | TbCard ×6 (post card as 01-home) | Thumb, category TbBadge, date, read time, title → 02-post-view.html, excerpt, author TbAvatar, Rating read-only | empty → TbEmpty "No posts in this category yet" |
| Pagination | TbPagination | Page 1 of 3; prev/next disabled at bounds | single page → hidden |
| Other categories | chip row (TbBadge-style links) | Sibling categories with counts → same route, other slug | — |

- **Interactions & states:** Card click anywhere on title navigates to post. Loading → TbSkeleton card grid. Empty category still shows header + sibling chips so the reader can pivot. Pagination is server-driven (querystring page).

### Screen: Tag archive (`/tag/{slug}`)

- **Mockup:** [mockups/04-tag-archive.html](mockups/04-tag-archive.html) · **Roles:** anonymous · **BRD:** BRD-26 · **REQ:** REQ-UI-053
- **Layout:** Same shell as category archive: header ("#dotnet", description, count) → post-card grid ×6 → pagination → weighted tag-cloud chip row.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Page header | Typography + TbBadge | Tag name "#dotnet", accurate "6 posts" count | unknown slug → 09-404.html |
| Post grid | TbCard ×6 (post card as 01-home) | Posts carrying the tag, any category; cards link 02-post-view.html | empty → TbEmpty "Nothing tagged yet" |
| Pagination | TbPagination | Single page (1) shown; hidden when ≤1 page | — |
| Tag cloud | chip row | All tags with counts; current tag emphasized (bold) | — |

- **Interactions & states:** Identical behavior contract to category archive; only the filter dimension differs. Loading → TbSkeleton cards. Tag chips navigate within /tag/{slug}. Post count badge must match actual result count (BRD-26).

### Screen: Series list (`/series`)

- **Mockup:** [mockups/05-series-list.html](mockups/05-series-list.html) · **Roles:** anonymous · **BRD:** BRD-29 · **REQ:** REQ-UI-054
- **Layout:** Container: page header → 2-col grid of 4 series cards (title, part-count badge, description, progress bar, CTA).

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Page header | Typography | "Article Series" + explainer | — |
| Series card | TbCard (header + content + footer) | Name → 06-series-view.html; description | — |
| Part count | TbBadge (secondary) | "5 parts" etc. | — |
| Publish progress | TbProgress (`.progress`) | published/total parts as % + hint line ("4 of 6 · in progress") | complete → 100% |
| CTA | TbButton (primary) | "Start reading" → 06-series-view.html (part 1) | — |

- **Interactions & states:** No series → TbEmpty "No series published yet". Loading → TbSkeleton cards. Progress reflects published parts only (drafts excluded). Card title and CTA both navigate to series view.

### Screen: Series view (`/series/{slug}`)

- **Mockup:** [mockups/06-series-view.html](mockups/06-series-view.html) · **Roles:** anonymous · **BRD:** BRD-28, BRD-29 · **REQ:** REQ-UI-055
- **Layout:** Centered column (max 860px): breadcrumb → series hero (badges, title, description, progress) → ordered part cards 1–5 → back link.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Breadcrumb | TbBreadcrumb | Home / Series / name | unknown slug → 09-404.html |
| Series hero | Typography + TbBadge ×3 + TbProgress | Name, description, "5 parts", Complete/In-progress badge, total reading time | — |
| Parts list | TbCard ×5 (ordered, numbered TbAvatar chip) | Part number, title → 02-post-view.html, date, reading time | no published parts → TbEmpty "Parts coming soon" |
| Current part | TbCard with ring highlight + TbBadge "You are here" | Highlighted when navigated from a series post; primary "Continue" TbButton | default (direct visit) → no highlight |
| Part CTA | TbButton (outline "Read" / primary "Continue") | Each → 02-post-view.html | — |

- **Interactions & states:** Order is fixed by series part number, unpublished parts omitted. Loading → TbSkeleton rows. "You are here" highlight driven by referring post; falls back to plain list. Progress bar mirrors the series-list card value.

### Screen: Search results (`/search`)

- **Mockup:** [mockups/07-search-results.html](mockups/07-search-results.html) · **Roles:** anonymous · **BRD:** BRD-34, BRD-35 · **REQ:** REQ-UI-056
- **Layout:** Centered column (max 860px): search-bar card (input + category select + button) → results count → stacked result cards ×4 → pagination; TbEmpty variant documented below fold.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Search bar | TbInput (lg) + TbSelect + TbButton (primary) | Pre-filled query "blazor"; category filter (All + 6); submit re-queries | no query → prompt/recent posts |
| Results count | Typography | "17 results for 'blazor' · sorted by relevance" | — |
| Result card | TbCard ×4 | Category TbBadge, date, read time, title + excerpt with `<mark>` term highlight → 02-post-view.html | loading → TbSkeleton rows |
| Pagination | TbPagination | Pages 1–5, prev/next | ≤1 page → hidden |
| Empty state | TbEmpty | "No results for '…'" + Clear filters TbButton | replaces result list when 0 matches |

- **Interactions & states:** Enter or Search button submits; category select narrows server-side. Matched terms highlighted via `<mark>` in title and excerpt (plain HTML — no TrBlazeUI highlight component). Empty state offers clear-filters recovery. Error (search backend down) → TbAlert (destructive) with retry.

### Screen: About (`/about`)

- **Mockup:** [mockups/08-about.html](mockups/08-about.html) · **Roles:** anonymous · **BRD:** BRD-30 · **REQ:** REQ-UI-057
- **Layout:** Narrow centered column (max 760px): avatar + heading → single prose TbCard (site/author/stack) with chip row and footer link buttons.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header | TbAvatar (xl) + Typography | Site identity | — |
| Prose | TbCard + Typography | About the site, the author, "site as its own case study"; contact email | owner-editable content (BRD-30) |
| Stack chips | chip row | .NET 10, Blazor Server, TrBlazeUI, PostgreSQL, Dapper+DbUp, Serilog, Playwright | — |
| Links | TbButton (outline) ×2 | "View my resume" → 10-resume.html; "Source on GitHub" → external repo | — |

- **Interactions & states:** Static content page — no empty/error states beyond initial render; loading → TbSkeleton card. External GitHub link opens in new tab in the real build. Contact email mailto in the real build.

### Screen: Not found (`/404`)

- **Mockup:** [mockups/09-404.html](mockups/09-404.html) · **Roles:** anonymous · **BRD:** — (platform quality) · **REQ:** REQ-UI-058
- **Layout:** Vertically centered block: oversized mono "404" → heading + explanation → primary/outline CTA pair → secondary browse links.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| 404 code | Typography (`.mono`, primary color) | Static | — |
| Message | Typography | Friendly explanation (unpublished / stale link / bad slug) | — |
| CTAs | TbButton (primary lg + outline lg) | "Back to home" → 01-home.html; "Search articles" → 07-search-results.html | — |
| Browse links | Typography links | Categories → 03, Series → 05 | — |

- **Interactions & states:** Single static state; served as catch-all for unknown routes, deleted posts and bad slugs (post/category/tag/series views all redirect here on unknown slug). Full public nav + footer retained so the reader is never dead-ended.

### Screen: Resume (`/resume`)

- **Mockup:** [mockups/10-resume.html](mockups/10-resume.html) · **Roles:** anonymous (public) · **BRD:** BRD-49, BRD-50, BRD-51, BRD-52 · **REQ:** REQ-UI-036
- **Layout:** Single-column public page — sticky anchor-chip row under TbResponsiveNav, then hero / about+stats / experience timeline / skills / awards / community / contact sections, site footer.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Anchor row | Sticky chip row (TbButton ghost / chip pattern) | In-page anchors: About · Experience · Skills · Awards · Community · Contact; sticks below 56px nav | Always visible; horizontal scroll on narrow viewports |
| Hero | TbAvatar (xl) + Typography + TbButton ×2 + social TbButton(ghost) row | Site-owner name, title, tagline; "Get In Touch" → #contact, "⬇ Download CV" → PDF export | Loading → TbSkeleton |
| About + stats | TbCard + TbCard ×4 stat tiles | Bio paragraph + UserStats (years, articles, talks, MVP count) | Empty field → section hidden |
| Experience | Timeline list (.timeline) + logo-chip | 4 entries newest-first; role, company, date range ("Present" on current), 2 markdown bullets each | Empty → section hidden |
| Skills | TbCard ×4 in .skill-grid, chip items | 4 category cards (Backend / Frontend / Cloud & DevOps / Data), 4–6 chips each | Empty → section hidden |
| Awards | TbCard ×3 | Badge icon, award name, year, external link | Empty → section hidden |
| Community | TbCard ×3 stat tiles | 50+ college sessions, 10+ conferences, 50K+ developers mentored | Empty → section hidden |
| Contact | TbCard + social links | Email, phone, location, socials, "Send an email" TbButton (mailto) | Always shown |

- **Interactions & states:** Anchor chips smooth-scroll to sections (html scroll-behavior: smooth); sticky row stays below nav while scrolling.
- Any resume section with no data is hidden entirely — no empty placeholders on the public page.
- Loading → TbSkeleton hero + section blocks; data fetch error → TbAlert(destructive) with retry.
- Site-wide ResumeEnabled=false → route returns 404 and the "Resume" nav link is hidden.

<!-- REMOVED 2026-08-06 (design review): "Authors" (/authors) and "Author profile" (/author/{username}).
     TechieBlog instances are personal sites — no public author browsing. BRD-53/54/55 retired,
     REQ-UI-041/042 marked N/A, mockups 11-authors.html and 12-author-profile.html deleted, and the
     "Authors" entry removed from the public nav on every screen. Post bylines are plain text. -->

### Screen: Sign in (`/login`)

- **Mockup:** [mockups/13-login.html](mockups/13-login.html) · **Roles:** anonymous (direct URL only — no public link, BRD-93) · **BRD:** BRD-2, BRD-93 · **REQ:** REQ-UI-001
- **Layout:** Minimal auth layout — centered brand mark above a single centered TbCard (max-width 420px); fixed theme toggle top-right; no site nav.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Brand mark | Typography link | "● TechieBlog" → 01-home.html | static |
| Email / Password | TbInput + TbLabel | bound to LoginModel; email autofocus | default, focus ring, invalid |
| Remember me | TbCheckbox | persists auth cookie | checked / unchecked |
| Forgot link | Typography link | → 15-forgot-password.html | static |
| Sign in | TbButton (primary, full-width) | posts credentials | default, disabled+spinner while submitting |
| Error banner | TbAlert (destructive) | "Invalid email or password." | hidden by default; shown on failed sign-in |
| Theme toggle | TbButton (ghost icon, fixed) | toggles `html.dark` | light / dark |

- **Interactions & states:**
  - Failed sign-in shows the destructive TbAlert above the form; fields keep their values (password cleared).
  - Client validation: both fields required, email format checked before submit.
  - Submit disables the button and shows a spinner; success redirects to the return URL or home.
  - Footer hint links to 14-register.html; page itself is never linked from public chrome (BRD-93).

<!-- REMOVED 2026-08-06 (second design-review pass): "Create account" (/register). Public
     self-service registration is retired — there are no reader accounts; comments, ratings and
     subscriptions are anonymous + email-verified, and staff accounts are created by an admin.
     BRD-1 retired; REQ-UI-002 marked N/A; mockup 14-register.html deleted. -->
### Screen: Forgot password (`/forgot-password`)

- **Mockup:** [mockups/15-forgot-password.html](mockups/15-forgot-password.html) · **Roles:** anonymous (direct URL only, BRD-93) · **BRD:** BRD-4 · **REQ:** REQ-UI-003
- **Layout:** Minimal auth layout — centered brand mark above a centered TbCard (max-width 420px); fixed theme toggle; no site nav.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Explanation | Typography (card-desc) | one-line instructions | static |
| Success notice | TbAlert (info) | "If the address exists, a reset link has been sent." | hidden → shown after submit |
| Email | TbInput + TbLabel | account email | default, invalid format |
| Send reset link | TbButton (primary, full-width) | triggers reset email | default, disabled+spinner, disabled after success |
| Back link | Typography link | → 13-login.html | static |

- **Interactions & states:**
  - The success TbAlert wording is identical whether or not the address exists — no account enumeration.
  - Email format validated client-side; empty field blocks submit.
  - After success the form stays visible but the button is disabled for a cooldown period.

### Screen: Reset password (`/reset-password/{token}`)

- **Mockup:** [mockups/16-reset-password.html](mockups/16-reset-password.html) · **Roles:** anonymous (direct URL from reset email only, BRD-93) · **BRD:** BRD-5 · **REQ:** REQ-UI-003
- **Layout:** Minimal auth layout — centered brand mark above a centered TbCard (max-width 420px); fixed theme toggle; no site nav.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Token error | TbAlert (destructive) | "This reset link is invalid or has expired." + link to 15-forgot-password.html | hidden (valid token) / shown (form hidden) |
| New password | TbInput + TbLabel + hint | strength rules: min 8, mixed case, number | default, invalid |
| Confirm new password | TbInput + TbLabel | must match | default, mismatch error |
| Reset password | TbButton (primary, full-width) | consumes token, sets new hash | default, disabled+spinner |
| Back link | Typography link | → 13-login.html | static |

- **Interactions & states:**
  - Token is validated on page load; invalid/expired token replaces the form with the destructive TbAlert.
  - Same live strength validation as registration; mismatch blocks submit.
  - Success invalidates the token, redirects to 13-login.html with a "Password updated" TbToast.

### Screen: Access denied (`/access-denied`)

- **Mockup:** [mockups/17-access-denied.html](mockups/17-access-denied.html) · **Roles:** any user failing an authorization check (redirect target; direct URL only, BRD-93) · **BRD:** BRD-9 · **REQ:** REQ-UI-004
- **Layout:** Minimal auth layout — centered brand mark above a centered TbCard (max-width 420px) with icon, message and action; fixed theme toggle; no site nav.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Icon | Typography (🚫, 44px) | static glyph | static |
| Title + message | Typography | "Access denied" + contact-owner explanation | static |
| Go home | TbButton (primary) | → 01-home.html | static |
| Theme toggle | TbButton (ghost icon, fixed) | toggles `html.dark` | light / dark |

- **Interactions & states:**
  - Single static state; the auth framework redirects here whenever a signed-in user lacks the required role.
  - No sign-in link is offered (public chrome never advertises auth routes, BRD-93).

<!-- REMOVED 2026-08-06 (design review): "My profile" (/profile), "My favourites" (/my-favorites)
     and "My comments" (/my-comments). Reader accounts are dropped — visitors comment and rate
     anonymously with an email address, so there is no reader-facing account area. BRD-13/37/43/44
     retired; REQ-UI-013/014/015/028 + REQ-FN-024 marked N/A; mockups 18/19/20 deleted.
     Staff profile management lives at /admin/profile (see "Manage Profile" below). -->
### Screen: Post editor (`/ManagePost/{id}`)

- **Mockup:** [mockups/21-post-editor.html](mockups/21-post-editor.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-14..17, BRD-20 · **REQ:** REQ-UI-016
- **Layout:** Admin shell (TbSidebar inset); two-column page — main editor column (title, slug, MarkdownEditor split view) + 320px stacked-card sidebar (Publish, Category, Tags, Series, Featured image, SEO).

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Title | TbInput (large) | Post title; edits regenerate slug until slug manually touched | Required — validation on Save/Publish |
| Slug row | TbInput (mono) + TbButton ghost "↻ Auto" | `/post/{slug}`; auto-kebab from title, editable | Uniqueness check → inline destructive hint |
| Body | MarkdownEditor | Toolbar (B, I, H2, link, code, image, list, quote) over split markdown textarea + live preview pane | Preview re-renders on debounce; TbSkeleton while rendering |
| Publish card | TbBadge + TbButton ×2 + TbDatePicker + TbTimePicker | Status badge (Draft/Published/Scheduled); Save Draft (secondary), Publish (primary); optional schedule date+time | Schedule set → status Scheduled on Publish; save → TbToast "Draft saved" |
| Category card | TbSelect | Single category from taxonomy | Required before Publish |
| Tags card | TbMultiSelect (chips + input) | Existing-tag type-ahead; Enter adds; ✕ removes | Free-entry creates new tag |
| Series card | TbSelect + TbNumericInput | Optional series + part number | "— none —" hides part number |
| Featured image | ImagePicker (thumbnail + TbButton) | "Choose from library" opens media library picker; Authors can upload here without library-browse rights | Empty → placeholder tile; ✕ clears |
| SEO card | TbInput + TbTextarea | SEO title (60-char hint) + meta description (160-char hint) | Char counters as TbInput hints |

- **Interactions & states:** New post (`{id}` absent) starts empty in Draft; title is the only hard-required field for Save Draft.
- Publish with future schedule → Scheduled status; without → immediate publish + redirect to public post.
- Save feedback via TbToast; unsaved-changes guard on navigation.
- Slug collision and missing category surface inline destructive hints, blocking Publish only.

### Screen: Posts list (`/BlogsList`)

- **Mockup:** [mockups/22-posts-list.html](mockups/22-posts-list.html) · **Roles:** AuthorOrAbove (policy fixed from EditorOrAbove per REQ-UI-017) · **BRD:** BRD-14 · **REQ:** REQ-UI-017
- **Layout:** Admin shell; page header with "New post" primary action, filter row (status TbTabs + search), full-width TbDataTable with pager.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header action | TbButton primary | "＋ New post" → /ManagePost | — |
| Status filter | TbTabs | All 24 · Published 18 · Drafts 4 · Scheduled 2 (live counts) | Active tab filters table server-side |
| Search | TbInput | Title/slug contains, debounced | Clears to full list |
| Table | TbDataTable | Columns: title (link to editor), status TbBadge (success/secondary/warning), category, date, views (mono), rating stars | Loading → TbSkeleton rows |
| Row actions | TbButton ghost sm ×3 | Edit → editor; Preview → draft preview; Delete → confirm | Delete → TbDialog confirm then TbToast |
| Pager | TbPagination | 10/page, 24 total | Hidden when ≤1 page |
| Empty | TbEmpty | "No posts yet — write your first" + New post CTA | Shown when filter/search yields none |

- **Interactions & states:** Tabs and search combine; counts refresh after delete/publish.
- Scheduled rows show publish date+time and em-dash views/rating (no data yet).
- Delete is a two-step confirm TbDialog; success removes row and toasts.
- Empty and skeleton-loading variants defined; empty differs per active tab ("No drafts" etc.).

### Screen: Draft preview (`/admin/preview/{id}`)

- **Mockup:** [mockups/23-draft-preview.html](mockups/23-draft-preview.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-19 · **REQ:** REQ-UI-018
- **Layout:** Admin topbar only (no sidebar, no public nav — admin surface that must not leak reader chrome); sticky TbAlert preview banner above an article rendered exactly like the public post page.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Topbar | TbBreadcrumb + TbButton ghost + theme toggle | "Admin / Draft preview"; View site ↗ | — |
| Preview banner | TbAlert (info, sticky) + TbButton ×2 | "Draft preview — this post is not published"; Back to editor (outline), Publish (primary) | Already published → banner swaps to "This post is live" + View public |
| Article header | Typography + TbBadge + meta-row | Category badge, Draft badge, last-edited date, read time, author avatar | — |
| Featured image | Rendered image | Post's featured image at public dimensions | Missing → hidden (as on public page) |
| Body | MarkdownEditor read-only render | Same renderer as public post view (headings, quotes, code) | — |

- **Interactions & states:** Publish from banner → confirm TbDialog → publishes, redirects to public post URL, TbToast.
- Banner is sticky beneath the topbar so status stays visible while scrolling.
- `{id}` not found or not owned by the caller → destructive TbAlert with back link (no article body).
- Rendering path is shared with the public post page so preview is pixel-faithful.

### Screen: Series admin (`/admin/series`)

- **Mockup:** [mockups/24-series-admin.html](mockups/24-series-admin.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-27 · **REQ:** REQ-UI-024
- **Layout:** Admin shell; TbDataTable of series with "New series" action; Edit opens a TbDialog (mocked open) with fields + orderable parts list.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header action | TbButton primary | "＋ New series" opens the same dialog empty | — |
| Series table | TbDataTable | Name, slug (mono), parts count, updated; 3 seeded series | Loading → TbSkeleton rows |
| Row actions | TbButton ghost sm | Edit (opens dialog), Delete (destructive) | Delete w/ parts → confirm TbDialog; posts keep, series link cleared |
| Dialog: fields | TbDialog + TbInput ×2 + TbTextarea | Name, slug, description | Slug collision → inline destructive hint |
| Dialog: parts list | Drag list rows (⋮⋮ handle) + TbNumericInput per row | Reorder by drag or by editing order number; numbers rewrite after drag | Series w/o parts → row placeholder "No parts yet" |
| Dialog: actions | TbButton outline + primary | Cancel discards; Save persists name/slug/desc + order | Save → TbToast + table refresh |

- **Interactions & states:** Empty library of series → TbEmpty "No series yet" replacing the table.
- Part order edits are staged in the dialog and persisted only on Save.
- Parts are added from the post editor's Series card, not from this dialog (read/reorder only here).
- Validation: name and slug required; slug unique across series.

### Screen: Media library (`/admin/images`)

- **Mockup:** [mockups/25-media-library.html](mockups/25-media-library.html) · **Roles:** AdminOnly (Authors upload via ImagePicker but cannot browse here — REQ-UI-034 remark) · **BRD:** BRD-45..48 · **REQ:** REQ-UI-034, REQ-UI-035
- **Layout:** Admin shell; TbFileUpload dropzone strip, 7-category TbTabs, responsive tile grid (8/page) with per-tile actions, TbPagination.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Upload strip | TbFileUpload (dropzone) | Drag & drop or browse; uploads into the active category | Rejected (size/format per category) → destructive TbToast; in-flight → TbProgress on tile |
| Category tabs | TbTabs | profiles 3 · logos 6 · awards 5 · icons 12 · blog 34 (active) · cv 1 · general 4 | Counts live from image_store |
| Image grid | TbCard tiles | Thumb, filename (mono, ellipsized), size | Loading → TbSkeleton tiles |
| Tile action: copy URL | TbButton ghost sm (⧉) | Copies public image URL | → TbToast "URL copied" |
| Tile action: delete | TbButton ghost sm destructive (🗑) | Removes from store | Confirm TbDialog; blocked with destructive TbToast if referenced by a post |
| Pager | TbPagination | 8/page within active category (34 in "blog") | Hidden when ≤1 page |
| Empty | TbEmpty | "No images in this category yet" + upload CTA | Per-category |

- **Interactions & states:** Switching tabs resets pagination and re-scopes the dropzone's limits (blog: webp/png/jpg ≤ 2 MB; cv: pdf ≤ 5 MB).
- Upload success appends the tile to the grid head and toasts; failures name the violated limit.
- Delete is reference-checked: images used as featured/inline in posts cannot be removed.
- Empty, skeleton-loading, and upload-progress variants defined per category.

### Screen: Admin Dashboard (`/admin`)

- **Mockup:** [mockups/26-admin-dashboard.html](mockups/26-admin-dashboard.html) · **Roles:** EditorOrAbove · **BRD:** BRD-62 · **REQ:** REQ-UI-019
- **Layout:** TbSidebar inset admin shell; 6-tile stat grid, quick-actions card, then Popular posts and Recent comments cards side by side.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Admin shell | TbSidebar (inset) + TbBreadcrumb + theme TbToggle | Grouped nav (Content/Taxonomy/Media/Resume/Audience/System); Dashboard active | Active item highlighted |
| Live counts | TbCard ×6 stat tiles | Posts 24 / Published 18 / Users 132 / Subscribers 486 / Comments 291 / Pending 7 | Loading → TbSkeleton tiles; pending 0 → no badge |
| Pending tile badge | TbBadge (warning) | Count of comments awaiting moderation | Hidden when queue is empty |
| Quick actions | TbButton (primary + outline ×3) | New post → editor; Moderate comments; Manage images; Site settings | — |
| Popular posts | TbCard + list rows | Top 5 published posts by views, count right-aligned .mono | Empty → TbEmpty "No published posts" |
| Recent comments | TbCard + TbAvatar + TbButton(sm) | Latest 4 comments: author, excerpt, post link, Approve/Delete | Empty → TbEmpty "Moderation queue is clear" |

- **Interactions & states:** Approve on a recent-comment row approves inline and removes the row with a TbToast; Delete confirms via TbDialog first.
- Counts refresh on load from a single admin-stats query; tiles show TbSkeleton until it resolves; query failure → TbAlert (destructive) with retry.
- Pending tile deep-links to the Comments page's Pending tab.

### Screen: User Management (`/users`)

- **Mockup:** [mockups/27-admin-users.html](mockups/27-admin-users.html) · **Roles:** AdminOnly · **BRD:** BRD-10 · **REQ:** REQ-UI-020
- **Layout:** Admin shell; title + Add user button, search/role filter row, full-width users table with pagination; Change-role TbDialog closed on load, opened by a row action.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header actions | TbButton (primary) | "Add user" opens create-user dialog | — |
| Search | TbInput | Filters by name or email, debounced | No match → TbEmpty "No users found" |
| Role filter | TbSelect | All roles / Admin / Editor / Author / Contributor / Reader | Combines with search |
| Users table | TbDataTable | TbAvatar + name, email, role TbBadge (Admin=default, Editor=success, Author=warning, Reader=secondary), status dot, joined date | Loading → TbSkeleton rows |
| Row actions | TbButton (ghost sm) ×3 | Change role / Disable (Enable when disabled) / Delete | Self row (current admin) shows "(you)" — actions suppressed |
| Pagination | TbPagination | 8 rows/page of 132 users | — |
| Change-role dialog | TbDialog + TbSelect + TbButton | 5 roles (Admin/Editor/Author/Contributor/Reader), role hint text, Save/Cancel | Save → TbToast "Role updated" |

- **Interactions & states:** Delete always confirms via TbDialog naming the user and their content impact.
- Disable is immediate (row status flips, TbToast); a disabled user cannot sign in.
- The signed-in admin cannot disable/delete or demote their own account — actions hidden on the self row.
- Loading → skeleton rows; fetch error → TbAlert with retry.

### Screen: Comment Moderation (`/CommentsList`)

- **Mockup:** [mockups/28-admin-comments.html](mockups/28-admin-comments.html) · **Roles:** EditorOrAbove · **BRD:** BRD-38, BRD-39 · **REQ:** REQ-UI-021
- **Layout:** Admin shell; TbTabs queue switcher, bulk-action bar, comments table, pagination.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Queue tabs | TbTabs | Pending 7 (active, warning-badge count) · Approved 284 · All 291 | Tab switch reloads table server-side |
| Bulk bar | TbCheckbox + TbButton (primary sm / destructive sm) | Select-all checkbox; "Approve selected (n)" / "Delete selected"; selected-count hint | Buttons disabled until ≥1 row checked |
| Comments table | TbDataTable | Row: TbCheckbox, TbAvatar + author, excerpt, post link, date, status TbBadge, actions | Pending empty → TbEmpty "Moderation queue is clear" |
| Row actions | TbButton (outline/ghost sm) | Approve / Edit (inline excerpt edit) / Delete | Approve moves row to Approved + TbToast |
| Pagination | TbPagination | Per-tab paging | — |

- **Interactions & states:** Approve (row or bulk) publishes the comment immediately (BRD-39) and updates the Pending count in tab + sidebar-facing dashboard tile.
- Delete (row or bulk) requires TbDialog confirmation; bulk delete states the count.
- Edit opens the comment text in a small TbDialog with textarea — saving re-approves it as edited.
- Loading → TbSkeleton rows; error → TbAlert with retry.

### Screen: Category Management (`/admin/categories`)

- **Mockup:** [mockups/29-admin-categories.html](mockups/29-admin-categories.html) · **Roles:** AdminOnly · **BRD:** BRD-22 · **REQ:** REQ-UI-022
- **Layout:** Admin shell; title + New category button, single table of 6 categories; Edit TbDialog closed on load, opened by the row action or "New category".

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header actions | TbButton (primary) | "New category" opens the same dialog empty | — |
| Categories table | TbDataTable | Name, slug (.mono), description, post count (sums to total posts), actions | Empty → TbEmpty "No categories yet — create the first one" |
| Row actions | TbButton (ghost sm) | Edit / Delete | Delete of in-use category → confirm dialog with reassignment TbSelect |
| Edit dialog | TbDialog | Name TbInput, slug TbInput (.mono, auto-derived but editable), description TbTextarea, Save/Cancel | Duplicate slug → inline validation error |
| Save feedback | TbToast | "Category saved" | — |

- **Interactions & states:** Slug auto-derives from name on create; editing the slug shows the resulting URL hint (/category/{slug}).
- Deleting a category with posts forces reassignment to another category before confirming (a post always has exactly one category, BRD-22).
- Loading → TbSkeleton rows; save conflict/validation errors render inline under the field.

### Screen: Tag Management (`/admin/tags`)

- **Mockup:** [mockups/30-admin-tags.html](mockups/30-admin-tags.html) · **Roles:** AdminOnly · **BRD:** BRD-24, BRD-26 · **REQ:** REQ-UI-023
- **Layout:** Admin shell; title + New tag button, search input, tag table with per-row Edit/Delete/Merge.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header actions | TbButton (primary) | "New tag" opens create dialog (name + slug) | — |
| Search | TbInput | Filters tag list client-side | No match → TbEmpty "No tags match" |
| Tags table | TbDataTable | Tag chip, slug (.mono), post count, actions | Empty → TbEmpty "No tags yet" |
| Post count | plain .mono cell | MUST equal the number of posts on the tag's archive — recomputed on unpublish/delete, never stale (Story 7.5 regression guard, BRD-26) | — |
| Row actions | TbButton (ghost sm) ×3 | Edit / Merge / Delete | Merge opens TbDialog to pick target tag |
| Merge dialog | TbDialog + TbSelect | Re-tags all posts to the target, deletes the source, shows affected count | Confirm → TbToast "Tags merged" |

- **Interactions & states:** Delete on a tag in use shows a confirm dialog with the affected post count; unused tags delete immediately with a toast.
- Merge is the canonical de-duplication path (e.g. fluent-ui → trblazeui); counts recompute after the merge.
- Loading → TbSkeleton rows; counts are computed server-side from published posts only.

### Screen: Subscribers (`/admin/subscribers`)

- **Mockup:** [mockups/31-admin-subscribers.html](mockups/31-admin-subscribers.html) · **Roles:** AdminOnly · **BRD:** BRD-57, BRD-58 · **REQ:** REQ-UI-025
- **Layout:** Admin shell; title + Export CSV, 3-tile stat row, search/status filters, subscribers table, pagination.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header actions | TbButton (outline) | "Export CSV" downloads the currently filtered set (BRD-58) | Export in progress → button spinner |
| Stat row | TbCard ×3 stat tiles | Total 486 / Active 462 / Unsubscribed 24 | Loading → TbSkeleton |
| Search | TbInput | Filters by email | No match → TbEmpty "No subscribers found" |
| Status filter | TbSelect | All / Active / Unsubscribed | Combines with search |
| Table | TbDataTable | Email (.mono), status TbBadge (Active=success, Unsubscribed=outline), subscribed date, source (Footer form / Post CTA / Newsletter landing / Import), Remove | Empty list → TbEmpty "No subscribers yet" |
| Pagination | TbPagination | 8/page of 486 | — |

- **Interactions & states:** Remove confirms via TbDialog (permanent, GDPR-style erase) then TbToast "Subscriber removed"; stat tiles decrement.
- Unsubscribed rows are retained for suppression — Remove is the only way they leave the list.
- Loading → skeleton rows; export failure → TbAlert (destructive).

### Screen: Site Settings (`/settings`)

- **Mockup:** [mockups/32-admin-settings.html](mockups/32-admin-settings.html) · **Roles:** AdminOnly · **BRD:** BRD-68, BRD-69 · **REQ:** REQ-UI-026
- **Layout:** Admin shell; TbTabs section switcher (General active), 2-column card grid (General, Theme, Comments, SMTP), sticky Save bar, sample success toast.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Section tabs | TbTabs | General · Blog · Comments · Email (SMTP) · Theme · Storage | Active tab drives visible cards |
| General card | TbCard + TbInput ×2 + TbNumericInput | Site title, tagline, posts-per-page (1–50) | Numeric out of range → inline validation |
| Theme card | TbCard + TbSelect + swatch row | Site theme: TrBlaze Modern / Developer Dark / Minimal Clean; preview swatches, selected outlined | Selection updates swatch highlight |
| Comments card | TbCard + TbSwitch (on) | Require approval before publishing (BRD-38) | Toggle is part of the dirty form |
| SMTP card | TbCard + TbInput ×4 + TbButton (outline) | Host, port, username, password (masked); "Send test email" | Test → TbToast success/failure; invalid port → field error |
| Save bar | Sticky bar + TbButton (primary) | "Save changes" + persistence hint (BRD-69) | Saving → spinner; saved → TbToast "Settings saved" |

- **Interactions & states:** Dirty tracking — any edit highlights the sticky bar and navigating away (tab switch or route change) warns about unsaved changes.
- All settings persist to the PostgreSQL settings table and apply without an app restart (BRD-69); theme change re-renders the public site on next load.
- Send test email uses the currently entered (possibly unsaved) SMTP values so they can be verified before saving.
- Save failure → TbAlert (destructive) above the save bar; field-level validation errors block the save.

### Screen: Newsletter Composer (`/admin/newsletter`)

- **Mockup:** [mockups/33-newsletter-composer.html](mockups/33-newsletter-composer.html) · **Roles:** AdminOnly · **BRD:** BRD-59 · **REQ:** REQ-UI-043
- **Layout:** Admin shell; two-column — main column subject + split Markdown editor/preview, side column Recipients, Send, and History cards.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Subject | TbInput | Newsletter subject line | Empty → validation blocks send |
| Body | TbMarkdownEditor (split) | Markdown source left, rendered preview right, kept in sync | Preview renders same pipeline as email body |
| Recipients card | TbCard + radio rows + TbSelect | "All active subscribers (462)" (default) or a segment (Post CTA / Footer form / last 30 days) | Segment select disabled until its radio picked |
| Send card | TbButton (outline) + TbButton (primary) + TbProgress | "Send test → self", "Send newsletter" (TbDialog confirm — irreversible), live progress n/462 | Sending → buttons disabled + progress advances |
| History card | TbCard + rows + TbBadge | Last 3 sends: subject, date, sent count, badge Sent | Empty → TbEmpty "Nothing sent yet" |

- **Interactions & states:** "Send newsletter" always confirms via TbDialog stating the recipient count; a send cannot be undone.
- During a send the progress bar streams n/total; completion fires TbToast "Newsletter sent to N subscribers" and prepends a History row.
- "Send test" mails only the signed-in admin and never touches History.
- Draft (subject + body) is retained if the admin navigates away mid-compose; empty body or subject blocks sending with inline errors.

### Screen: Analytics Dashboard (`/admin/analytics`)

- **Mockup:** [mockups/34-analytics-dashboard.html](mockups/34-analytics-dashboard.html) · **Roles:** EditorOrAbove · **BRD:** BRD-60, BRD-61 · **REQ:** REQ-UI-044
- **Layout:** Admin shell; header with From/To date inputs + Apply, 4-tile stat grid, full-width Views-trend bar chart, then Popular-posts table beside Engagement-by-category bars.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Date range | TbInput (type=date) ×2 + TbButton — **GAP: no TbDateRangePicker** | From/To bound to the query; Apply reloads all widgets | From > To → inline error, Apply disabled |
| Stat row | TbCard ×4 stat tiles | Views 30d 12.4K · Unique 8.9K · Avg rating 4.3★ · Comments 291 | Loading → TbSkeleton tiles |
| Views trend | placeholder div bars — **GAP: no TrBlazeUI chart component** | 14 daily single-hue (--chart-1) bars, hover tooltip per bar, peak direct-labeled, sparse x-axis labels | No data in range → TbEmpty "No traffic recorded" |
| Popular posts | TbDataTable | Title, views, unique, comments, rating (stars + numeric) | Empty → TbEmpty |
| Engagement by category | TbCard + labeled .progress bars | 5 categories, chart colors --chart-1..5, text label + value on every bar (identity never color-alone) | Missing category data → row omitted |

- **Interactions & states:** Apply refetches all widgets for the selected range; widgets show skeletons independently while loading; a failed widget shows its own TbAlert with retry, others stay live.
- Trend bars expose per-day tooltips (date + views); the trend is a single series so it carries no legend — the card title names it.
- Table rows link to the public post view; ratings render read-only stars with the numeric average for accessibility.
- Build note: the chart areas are visual placeholders — the implementation needs a charting approach (custom SVG or a chart library) because TrBlazeUI ships none.

### Screen: Manage Profile (`/admin/profile`)

- **Mockup:** [mockups/35-manage-profile.html](mockups/35-manage-profile.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-11 · **REQ:** REQ-UI-040
- **Layout:** Admin inset shell (TbSidebar + topbar); single 860px column of stacked cards — user selector (admin only), Basic info, Social links, Resume settings — with a Save row at the bottom.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| User selector (admin only) | TbCombobox | Admins pick any author's profile to edit; Authors never see this row | hidden (Author role); searching; selected "(me)" |
| Photo row | TbAvatar(md) + TbButton "Change photo" (ImagePicker) | Opens media library picker; square min 400×400 | no photo → initials avatar |
| Basic info fields | TbInput ×4, TbTextarea (bio, markdown) | DisplayName, Title, Tagline, Bio, Phone, Location on BlogUser | required-field validation on Save |
| Social links | TbInput ×4 (mono) | LinkedIn / GitHub / X / Instagram URLs; blank = icon hidden on public site | invalid URL hint |
| Username | TbInput (mono) + hint | Public resume slug `/resume/{username}`; uniqueness checked async | available ✓ hint / taken → destructive hint + Save disabled |
| ResumeEnabled | TbSwitch | ON = resume page + Authors listing entry visible | off → public resume returns 404 |
| CV file row | TbFileUpload (Replace) | Current file name + size + upload date; PDF only | empty → dropzone instead of file row; uploading → TbProgress |
| Section jump-offs | TbButton(outline) ×3 | Navigate to /admin/experience, /admin/skills, /admin/awards | — |
| Save bar | TbButton primary + ghost | Persists all cards in one call | dirty → sticky; saving → loading; saved → TbToast |

- **Interactions & states:**
  - Username field validates uniqueness on blur; a taken name shows a destructive hint and disables Save.
  - "Change photo" and CV "Replace" both route through the shared media/file pickers; CV upload shows progress and replaces the file row on success.
  - Admin-only TbCombobox swaps the whole form's data context to the selected author; unsaved changes prompt a confirm dialog first.

### Screen: Manage Experience (`/admin/experience`)

- **Mockup:** [mockups/36-manage-experience.html](mockups/36-manage-experience.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-50 · **REQ:** REQ-UI-037
- **Layout:** Admin inset shell; page title + "Add experience" button, vertical list of experience cards ordered by DisplayOrder, edit dialog overlays the page.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header action | TbButton(primary) "Add experience" | Opens the dialog empty | — |
| Experience card | TbCard (logo-chip + role/company/dates) | UserExperience row; "Present" TbBadge(success) when IsCurrent | empty list → TbEmpty "No experience yet" |
| Reorder controls | TbButton(ghost) ↑ ↓ | Swaps DisplayOrder with neighbor, optimistic | first/last arrow disabled; persist → TbToast |
| Row actions | TbButton(outline) Edit · TbButton(ghost, destructive) Delete | Delete opens confirm TbDialog | deleting → loading |
| Dialog: role/company | TbInput ×2 | Required | validation hints |
| Dialog: company logo | ImagePicker row (logo-chip + "Choose image…") | Square min 80×80 from media library | none → placeholder chip |
| Dialog: dates | TbDatePicker ×2 + TbSwitch "Current role" | IsCurrent ON disables End date and shows "Present" publicly | end < start → destructive hint |
| Dialog: description | TbTextarea (markdown) | Rendered as markdown on resume timeline | — |
| Dialog: display order | TbNumericInput | Position in timeline (lowest first) | — |

- **Interactions & states:**
  - "Current role" switch clears and disables End date; turning it off re-enables and requires an end date.
  - ↑/↓ reorder is optimistic in the list and persisted immediately; a failed persist reverts with a destructive TbToast.
  - Delete always passes through a confirm TbDialog naming the role/company.

### Screen: Manage Skills (`/admin/skills`)

- **Mockup:** [mockups/37-manage-skills.html](mockups/37-manage-skills.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-51 · **REQ:** REQ-UI-038
- **Layout:** Admin inset shell; title + "New category"/"Add skill" buttons, one TbCard per skill category containing chip rows; Add-skill dialog overlays the page.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header actions | TbButton(primary) "Add skill" + TbButton(outline) "New category" | Dialog / inline category create | — |
| Category card | TbCard (title + count + Rename ghost) | UserSkill rows grouped by Category | empty category → TbEmpty row "No skills yet" |
| Skill chip | chip (Badge-style) + icon placeholder + ✎/× TbButton(ghost) | Chip order = in-category DisplayOrder | remove → confirm TbDialog |
| Dialog: name | TbInput | Required, unique within category | duplicate → destructive hint |
| Dialog: category | TbSelect with inline-create | Existing categories + "Create '<typed>'" option | creating new category inline |
| Dialog: icon | ImagePicker | Optional 32×32 SVG/PNG from media library | none → "?" placeholder |
| Dialog: order | TbNumericInput | Position within the category | — |

- **Interactions & states:**
  - TbSelect supports inline category creation: typing an unknown name surfaces a "Create '…'" option that adds the category on save.
  - Chip ✎ reopens the same dialog pre-filled; × asks for confirmation before removing.
  - Renaming a category updates every chip's grouping; deleting the last skill leaves an empty-state row rather than dropping the card.

### Screen: Manage Awards (`/admin/awards`)

- **Mockup:** [mockups/38-manage-awards.html](mockups/38-manage-awards.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-51 · **REQ:** REQ-UI-039
- **Layout:** Admin inset shell; title + "Add award" button above a TbDataTable of awards; Edit dialog overlays the page.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Header action | TbButton(primary) "Add award" | Opens dialog empty | — |
| Awards table | TbDataTable | Columns: badge thumb, name, year, link (mono), order, actions | empty → TbEmpty "No awards yet" |
| Badge thumb | image cell (logo-chip placeholder) | BadgeImage from media library | none → initials chip |
| Row actions | TbButton(outline) Edit · TbButton(ghost, destructive) Delete | Delete → confirm TbDialog | — |
| Dialog: name | TbInput | Required | validation hint |
| Dialog: year | TbNumericInput | 1990–current year | out of range → destructive hint |
| Dialog: link | TbInput (mono) | Optional; award name becomes a link when set | invalid URL hint |
| Dialog: badge image | ImagePicker | Square min 80×80 | — |

- **Interactions & states:**
  - Order column drives the resume Awards section sequence (lowest first); edited via the dialog.
  - Save closes the dialog and raises TbToast "Award updated"; table row updates in place.
  - Delete confirmation names the award to avoid accidental removal.

### Screen: BlogApp Connection Setup (`(desktop first-run)`)

- **Mockup:** [mockups/39-blogapp-connection-setup.html](mockups/39-blogapp-connection-setup.html) · **Roles:** local desktop user (pre-auth) · **BRD:** BRD-96 · **REQ:** REQ-FN-047
- **Layout:** Small desktop window (window--sm): centered brand + heading, PostgreSQL connection form, Test connection row with result alert, full-width Save & continue.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Host / Database / Username | TbInput (mono) | Direct PostgreSQL connection parameters — no local DB, no sync (BRD-96) | required |
| Port | TbNumericInput | Default 5432 | non-numeric rejected |
| Password | TbInput (password) | Stored only in OS credential store | — |
| SSL mode | TbSelect | Disable / Prefer / Require (default Require) | — |
| Test connection | TbButton(outline) | Read-only probe; verifies server + TechieBlog schema/migration level | testing → loading |
| Result alert | TbAlert | Success: "Connection OK — TechieBlog schema found" | failure → TbAlert(destructive) with Npgsql error; schema missing → destructive "database is not a TechieBlog instance" |
| Save & continue | TbButton(primary, lg) | Persists to credential store → login screen | disabled until a successful test |

- **Interactions & states:**
  - Save & continue stays disabled until Test connection succeeds; editing any field after a success invalidates the test.
  - Invalid connection error state: destructive TbAlert with the driver message (timeout, auth failed, host unreachable) replaces the success alert.
  - A reachable server without the TechieBlog schema is a distinct destructive state — connection is not saved.

### Screen: BlogApp Login (`(desktop login)`)

- **Mockup:** [mockups/40-blogapp-login.html](mockups/40-blogapp-login.html) · **Roles:** anonymous → web admin users · **BRD:** BRD-95 · **REQ:** REQ-UI-051
- **Layout:** Small desktop window (window--sm): centered brand, connected-to pill, email/password form with full-width Sign in.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Brand + heading | Typography | "Sign in to manage your blog" | — |
| Connected-to pill | TbBadge(success dot) + mono text | Shows saved target `blog.ravirathore.dev · PostgreSQL` | unreachable → red ● Disconnected |
| Change connection | TbButton(ghost) | Returns to connection setup (39) | — |
| Email / Password | TbInput ×2 | Same BlogUser credentials & roles as the web admin | required |
| Sign in | TbButton(primary, lg) | Authenticates directly against the site DB | signing in → loading |
| Error alert | TbAlert(destructive) | Bad credentials or role below Author | — |

- **Interactions & states:**
  - Offline/unreachable-DB state: pill turns red "● Disconnected", Sign in is disabled, and a destructive TbAlert links to Change connection.
  - Failed credentials show a destructive TbAlert without revealing which field was wrong.
  - Successful sign-in loads the shared admin shell (41) with the user's web role enforced.

### Screen: BlogApp Shell (`(desktop main window · /admin/dashboard)`)

- **Mockup:** [mockups/41-blogapp-shell.html](mockups/41-blogapp-shell.html) · **Roles:** AuthorOrAbove · **BRD:** BRD-94, BRD-97 · **REQ:** REQ-UI-052
- **Layout:** Full desktop window containing the SAME admin shell as the web (TbSidebar inset + topbar + page); topbar swaps "View site ↗" for a live connection chip; page shows the admin dashboard.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Window chrome | desktop mock chrome | MAUI Blazor Hybrid head hosting the shared BlogUI pages (one RCL, two heads) | — |
| Sidebar | TbSidebar (inset) | Identical groups/items to web admin; each item opens the shared BlogUI page in-window | active item highlight; nav disabled while disconnected |
| Topbar status chip | TbBadge-style chip | green ● "Connected · blog.ravirathore.dev" — replaces web "View site ↗" | red ● "Disconnected — retrying…" |
| Stat tiles | TbCard ×5 | Posts 24 · Users 132 · Subscribers 486 · Comments 291 · Pending 7 (live DB queries) | loading → TbSkeleton tiles |
| Quick actions | TbButton row | New post, Moderate comments (badge count), Upload image, Compose newsletter | — |
| Popular posts | TbDataTable | Title, views (mono), read-only rating — top by views, 30 days | empty → TbEmpty |
| Theme toggle + avatar | TbToggle + TbAvatar(sm) | Same dark-mode mechanism as web | — |

- **Interactions & states:**
  - Offline/unreachable-DB state: status chip turns red, a full-width destructive TbAlert "Connection lost — retrying…" appears, and sidebar navigation is disabled until the connection heals (TbToast "Connection restored").
  - All sidebar destinations are the identical shared BlogUI admin pages (mockups 21–38 apply verbatim inside this window).
  - No public-site navigation exists in this head; "View site ↗" is intentionally absent (BRD-94/97).

### Screen: Newsletter archive (`/newsletters`)

- **Mockup:** [mockups/42-newsletter-archive.html](mockups/42-newsletter-archive.html) · **Roles:** anonymous (public) · **BRD:** BRD-93, BRD-98, BRD-99, BRD-100 · **REQ:** REQ-UI-053
- **Layout:** Public nav, centred 900px column — page header, prominent subscribe card, then a single-column list of issue cards with pagination and the site footer.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Top nav | TbResponsiveNav | Home · Categories · Series · Newsletter (active) · Resume · About; theme TbToggle, search icon. No login/user menu (BRD-93) | sticky; burger under 768px |
| Page header | Typography + TbBreadcrumb | H1 "Newsletter" + subtitle (monthly note, no spam, unsubscribe any time) | static |
| Subscribe box | TbCard + TbInput (email) + TbButton (primary) | POST email → creates unconfirmed NewsletterSubscriber, sends opt-in mail | idle / submitting / pending-confirmation / duplicate-address info alert |
| Captcha | Self-hosted SVG challenge + reload TbButton + TbInput (`sub-captcha`) | Server-generated challenge token, checked server-side; no third-party service (BRD-99) | fresh / wrong answer inline error / reloaded |
| Verify note | TbAlert-style panel (`.verify-note`) | "We'll send a confirmation link — your subscription starts once you click it." (BRD-98) | always visible |
| Subscriber count | Typography (muted) + users icon | Confirmed-subscriber count ("1,240 subscribers") | hidden when count is 0 |
| Issue list | TbCard ×6 | NewsletterIssue published only, newest first: TbBadge "Issue #24", title, sent date, 2-line excerpt, "Read issue →" → `/newsletter/{slug}` | loaded / TbSkeleton loading / TbEmpty "No issues published yet" |
| Pagination | TbPagination | Page size 6, server-side paging over published issues | single page → hidden |

- **Interactions & states:** Subscribe validates the email format client-side and the captcha server-side; a wrong captcha re-renders a fresh challenge with an inline error and never reveals whether the address already exists.
- Successful submit swaps the card for a pending panel ("check your inbox") — the subscription only becomes active when the confirmation link is clicked (`/verify/{token}`, BRD-98); an already-confirmed address gets an info TbAlert instead of a second mail.
- Empty state: when no issue has been published, the list region is replaced by TbEmpty "No issues published yet" while the subscribe card stays visible.
- Loading renders TbSkeleton issue cards; a failed load renders a destructive TbAlert with a retry TbButton.

### Screen: Newsletter issue (`/newsletter/{slug}`)

- **Mockup:** [mockups/43-newsletter-view.html](mockups/43-newsletter-view.html) · **Roles:** anonymous (public) · **BRD:** BRD-93, BRD-98, BRD-100, BRD-101 · **REQ:** REQ-UI-054
- **Layout:** Public nav, centred 820px reading column — breadcrumb, issue header, rendered body, prev/next card, compact subscribe CTA, footer.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Breadcrumb | TbBreadcrumb | Home › Newsletter › Issue #24 | static |
| Issue header | TbBadge + Typography + TbAvatar (sm, photo) | Issue number badge, H1 title, author byline, "Sent Aug 1, 2026", "Sent to 1,240 subscribers" | recipient count hidden if not recorded |
| Issue body | Typography (15.5px, H2 at 22px) | Rendered newsletter HTML — paragraphs, one H2 subheading, bulleted list of article links → `/post/{slug}` | loaded / TbSkeleton loading |
| Article links | Typography links | 3 links into the post view; sanitised outbound HTML | dead link → 404 handled by target route |
| Prev/next nav | TbCard + TbButton (outline prev / primary next) | Adjacent issue numbers and slugs; "Issue N of M · Newsletter" plus "All issues" link | first issue → prev disabled; latest → next disabled |
| Subscribe CTA | TbCard (compact) + TbInput + TbButton (primary) | Email capture with one-line double-opt-in note and "See all issues" link → archive | idle / submitting / pending-confirmation |
| Footer | Typography + TbSeparator | Copyright + RSS · Newsletter · About · Resume | static |

- **Interactions & states:** Unknown slug or an issue not yet sent renders the 404 route — draft issues are never publicly addressable (BRD-101).
- The CTA reuses the archive's subscribe endpoint, so submitting produces the same pending-confirmation state and the same `/verify/{token}` landing (BRD-98); captcha is enforced server-side even though the compact form omits the visible challenge until first submit.
- Loading renders TbSkeleton header and body blocks; a body render failure falls back to a destructive TbAlert with a link back to the archive.
- Prev/next buttons render disabled (not hidden) at the ends of the range so the card keeps a stable shape.

### Screen: Email confirmation landing (`/verify/{token}`)

- **Mockup:** [mockups/44-verify-email.html](mockups/44-verify-email.html) · **Roles:** anonymous (public) · **BRD:** BRD-40, BRD-93, BRD-98, BRD-100 · **REQ:** REQ-UI-055
- **Layout:** Public nav and footer with a single vertically-centred TbCard (max-width 520px) holding icon, headline, body copy, two buttons and a muted footnote.

| Region | TrBlazeUI control | Data / behavior | States |
|--------|-------------------|-----------------|--------|
| Top nav / footer | TbResponsiveNav + footer | Full public shell — this is a public page, not an auth screen | static |
| Result icon | Lucide SVG (circle-check / circle-alert / info) | Colour follows outcome: green success, destructive expired, primary already-verified | one per outcome |
| Headline + body | Typography | "Email confirmed" + "your comment is now queued for moderation and will appear shortly" | success / expired / already-verified / subscribed |
| Actions | TbButton primary + TbButton outline | "Back to the article" → `/post/{slug}`; "Browse articles" → category archive | expired swaps primary for "Send a new link" |
| Expired alert | TbAlert destructive | Token expired (>24h) or already consumed; nothing published | alternate render |
| Already-verified alert | TbAlert info | Address verified earlier — idempotent, no state change | alternate render |
| Subscription variant | Success card, newsletter copy | "You're subscribed" + link → `/newsletters`; mentions one-click unsubscribe | alternate render |
| Footnote | Typography (muted `.hint`) | "Confirmation links expire after 24 hours and can be used once." | always visible |

- **Interactions & states:** The token is resolved server-side on first render; while it resolves the card shows a TbSkeleton, and the outcome (success / expired / already-verified / subscribed) picks exactly one card variant — the token's `purpose` field (comment, rating, subscription) selects the copy and the return link.
- Verification is single-use and idempotent: consuming a valid token flips the commenter/rating/subscriber row to verified, a second visit to the same URL lands on the already-verified info variant rather than an error.
- Expired or unknown tokens show the destructive TbAlert and offer "Send a new link", which re-issues a token to the stored address without disclosing whether that address exists.
- Success for a comment means "queued for moderation", never "published" — the copy must not promise immediate visibility (BRD-40); server errors fall back to a destructive TbAlert with a link home.
---
Generated by *mockups on 2026-08-06 · **38 screens** · visual contract for REQ-UI-048…056 and all rebuilt UI
Revised 2026-08-06 after owner design review (two passes): authors, reader-account and registration screens
removed (6 screens); anonymous comments/ratings with an interactive rating control; email verification +
self-hosted captcha; public newsletter archive, issue view and confirmation landing added (3 screens);
one canonical collapsible admin sidebar with Lucide icons; brand social icons; photo avatars;
dialogs closed on load
Legacy Fluent mockup set: mockups/ (repo root, 28 files) — superseded, read-only
