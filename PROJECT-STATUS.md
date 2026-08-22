---
project: TechieBlog
stack: .NET 10 / Blazor Server / TrBlazeUI / PostgreSQL + Dapper + DbUp / Serilog / BlogApp MAUI (Windows)
last_updated: 2026-08-22
current_phase: UAT round 2 VERIFIED — scoped verify run 2026-08-22: 4 Verified, 1 demoted to Needs re-verify (REQ-FN-020, unobservable with 0 posts); DEPLOYED (prod on migration 031)
last_verified_build: PASS
last_verified_date: 2026-08-22
---
## Where I am

**Phase: UAT round 2 complete, and DEPLOYED.** The owner ran a second UAT pass against the live
website (the first covered the BlogApp desktop head). **Eighteen items** were raised or found —
`UAT-001…018` — and **all eighteen are closed**. No `REQ-*` row was added: at the owner's instruction
UAT findings are logged as `UAT-nnn` in `docs/TechieBlog-Checklist.md` → **UAT Bugs**, because a
defect is evidence a shipped requirement did not hold, not a new requirement. Requirement counts are
unchanged: **160 terminal / 4 open**.

### Production — verified live, 2026-08-22

Not inferred from the working tree; each row was checked against `https://techierathore.com`.

| Signal | State |
|---|---|
| Schema | `/healthz` → *"DbUp journal records all **30** migration scripts"* — **030 and 031 are live** |
| `/speaker-profile` | **HTTP 200**, banner rendering, Past Sessions showing its empty state |
| Primary nav | `Speaking` present · **`About` removed** (UAT-009 is live) |
| Home page | Blog-Archive link live and pointing at C# Corner (UAT-005) |
| Speaking data | ⏳ **NOT loaded** — 0 rows, count badge `0` |

### Health

| Signal | State |
|---|---|
| Build (rung #4, whole solution) | **0 Error(s), 7 / 7 projects** |
| Tests | **1 512** — 1 509 pass · 0 fail · 3 skip |
| Live smokes this round | 13/13 · 14/14 · 28/28 — all green |
| Mockup fidelity sweep | **CLEAN** — 27 web screens vs `docs/mockups/`, 0 outstanding defects |
| Executed verify run | **2026-08-22** — 5 REQs graded · 11/11 Playwright · gates: acceptance + data-render + visual-truth |
| Dev servers left running | none (port 5399 released) |
| Seeded test users | restored to the documented state (`MustChangePassword = TRUE`) |

### What changed

| ID | Item | Outcome |
|---|---|---|
| UAT-001 | Deleted articles still shown publicly | Cache-coherence gap; added **Settings → Maintenance → Clear cached content** |
| UAT-002 | `/users` had no edit and no delete | Edit dialog + **soft** delete, guarded against self / site-owner / last-admin |
| UAT-003 | Activate/Deactivate had **never** worked | Three breaks fixed in migration 030 + `AuthSvc`, with a lock-out-proof backfill |
| UAT-004 | Desktop Statistics died on a missing column | **My regression.** Fixed; produced the ship-order rule below |
| UAT-005 | Blog-Archive link | Added, off-site-safe — **live** |
| UAT-006 | **Speaker Profile page + admin screen** | Built on existing `UserEvents`; migration 031 adds one column |
| UAT-007 | Demo data mistaken for real figures | Seed PARTS C–G removed; test accounts kept |
| UAT-008 | "Looks different from the mockups" | Full sweep; 3 real gaps fixed, rest proved empty-DB artifacts |
| UAT-009 | About in the primary nav | Removed — **live** |
| UAT-010 | Data script should be deletable | Moved to `docs/data/` |
| UAT-011 | Two folders named `mockups` | Repo-root set **deleted** (36 files) |
| UAT-012 | A wrong claim I made about the mockups | Corrected everywhere; provenance not falsified |
| UAT-013 | Speaker Profile banner | Owner's conference photo, 1.51 MB → **141 KB** — **live** |
| UAT-014 | `README.md` stale | Rewritten; wrong tech, wrong architecture, retired features, dead theming advice |
| UAT-015 | `GETTING_STARTED.md` stale | Rewritten; **three errors would have stopped a fresh clone dead** |
| UAT-016 | Docs could drift back | **Build-time guard**, 6 checks, all mutation-tested |
| UAT-017 | Stale rate-limit entries | `/register` and `/logout` removed — neither was ever a route |
| UAT-018 | Logout redirect | `NavigateTo("/")` was **dead code**; split clear-from-notify, regression-pinned |

### ⚠ Owner actions outstanding

1. **Load the speaking data.** This is the only thing left. Migration 031 is on production, but the
   21 sessions are not — the page is live and empty. Run
   `docs/data/speaking-engagements.sql` against production; it is re-runnable, and the file is
   disposable once the rows are in.
2. *Optional:* 21 sessions have no description and 7 no session title, because no C# Corner event page
   publishes them. Fill them in at `/admin/speaking`.
3. *Optional:* enter real `UserStats` — the home stats band is hidden only because there are no rows,
   and returns the moment there are.

### Settled decisions (do not "fix" these back)

- **Dark by default is intentional** (migration 025 / BRD-66). The mockups render light; the owner
  confirmed on 2026-08-22 that he is happy with the site dark and the mockups light. A verifier
  finding "site does not match the mockup's light theme" is **not** a defect.
- **About is not in the primary nav** (UAT-009) even though the mockup lists it.
- **The home contact block is headed "Contact"**, not the home mockup's "Get In Touch" — REQ-UI-049
  reuses `ResumeContact`, and `10-resume.html` heads that section "Contact" too.
- **`docs/mockups/` is the ONLY mockup set.** The repo-root folder is deleted; do not re-create it.
- **`ClearPersistedSessionAsync` and `MarkUserAsLoggedOut` are deliberately separate** (UAT-018).
  Re-merging them silently restores the wrong logout destination; `LogoutStateTests` catches it.

### Standing engineering rules earned this round

- **Ship order: website first, BlogApp second.** BlogApp never runs DbUp — the website owns the
  schema — so a desktop build newer than the database breaks on the missing column (UAT-004).
- **Prefer schema-tolerant reads in shared code.** `SELECT *` plus a C# predicate degrades safely on
  an older schema; `WHERE new_column = …` cannot (UAT-004).
- **No Tailwind arbitrary values.** The build ships TrBlazeUI's **prebuilt** CSS with no JIT pass, so
  `text-[clamp(...)]` / `max-w-[1100px]` are never generated and silently do nothing (UAT-008/013).
- **Guard claims by executing them, and mutation-test the guard.** The longest-lived bugs this round
  — dead `NavigateTo`, a stale `/register` entry, docs describing an imaginary project — all shared
  one property: **no test failed and the wrong behaviour looked plausible.** Reading cannot catch
  that. Every guard added here was mutation-tested, and two of them were blind on first write.
- **Smoke is not verify.** Everything above is `Fixed (self-smoked)`; only an executed verify-phase
  run may write `Verified`.

### Next command

```
/TechieFlow:agents:verifier *verify REQ-FN-020
```

**That run has been executed** (2026-08-22): `REQ-UI-005`, `REQ-UI-020`, `REQ-UI-049` and
`REQ-FN-058` are now **`Verified`** — graded against the running app with the acceptance,
data-render and visual-truth gates, not a self-smoke.

**`REQ-FN-020` was DEMOTED to `Needs re-verify`**, and deliberately not passed. Its data-bearing half
— published listings, featured-post selection, related posts, reading-time rendering — cannot be
exercised while the database holds **0 published posts** (UAT-007 retired the demo content). The
empty-state contract verified clean and 19 unit assertions cover the logic, but passing a REQ on unit
tests alone would breach the strict gate: `Verified` means its controls were *seen* to render their
data. **No code defect is implied.** Re-run the command above once the site has published content.

---

## History — 2026-08-22 UAT round 2 (detail)

## Where I am

**2026-08-22c — SECOND OWNER UAT ROUND (website this time, not the desktop head). Two defects
reported, a third found while triaging them; all three fixed and smoke-proven live.** `*fix-issues`
(the owner asked for analysis *and* a fix). **Logged as `UAT-001…003` in the new
`docs/TechieBlog-Checklist.md` → UAT Bugs section, NOT as new `REQ-*` rows** — the owner's explicit
instruction this round: a UAT defect is evidence that a shipped requirement did not hold, so filing
it as a new requirement would restate a bug as a feature and inflate the requirement count. The
owning REQ is named on each row instead, so the requirement count is unchanged at **160 terminal /
4 open**. Build rung #4 **0 Error(s), 7/7 projects**. Live smoke **13/13 PASS** against the web host
on `:5399` (Playwright, screenshots under `tests/.artifacts/harness/uat-users/`); host shut down and
port released afterwards; seeded test users restored to their documented state.

- **UAT-001 — deleted articles kept showing on the public home page. Not a data bug, and the site had
  already self-corrected before it was investigated.** The owner emptied the production tables from
  the BlogApp desktop client, saw zero rows, and still saw the Featured article. Fetching
  `techierathore.com` during triage returned `home-articles-empty` with no featured block at all —
  proving both the delete and the query were right. The public pages read through a **10-minute
  in-memory cache that lives in the web host's process**; a delete made from the desktop client goes
  straight to the database and never enters that process, so nothing evicted
  `content:posts:featured` and it was served until it aged out. Fixed by adding a
  **Clear cached content** control to `/settings` → General → Maintenance. ⚠ The gap is inherent to
  caching in a separate process, not a coding error — the control closes it on demand rather than
  making the cache coherent with outside writers.
- **UAT-002 — `/users` had no edit and no delete.** Confirmed exactly as reported. Added a full Edit
  User dialog (name, email, role, with validation) and a Delete action. **Delete is a soft delete** —
  `BlogUser` is the target of 16 foreign keys, only 4 of which cascade — so posts and comments stay
  attributed while the account vanishes from every list and every identity lookup. Guarded against
  deleting yourself, the site owner, or the last active administrator, with the reason shown on the
  button rather than a silently greyed control.
- **UAT-003 — found during triage, not reported: the Activate/Deactivate button that already existed
  had never worked.** Three independent breaks — the flag was never persisted (`UpdateBlogUser` has
  no such parameter), never enforced at sign-in, and never set at account creation, so every
  admin-created account is stored *Inactive* while still able to sign in. All three fixed in
  migration `030-UserAdminEditDelete.sql` + `AuthSvc`, with an explicit backfill so that switching
  enforcement on cannot lock anyone out.

**Second batch, same session — two owner requests and one regression I had just caused:**

- **UAT-004 — the desktop Statistics page died with a missing-column error, and it was my own
  `UAT-002` change four hours earlier.** Adding `WHERE IsDeleted = FALSE` to the user projections
  bound them to migration 030, but **BlogApp deliberately never runs DbUp** (`MauiProgram.cs:14`), so
  a desktop build is routinely newer than the database it points at — production was journalling
  28/28 scripts (confirmed via live `/healthz`) while the new binary demanded a 30th. Reproduced
  exactly by dropping the column in a rolled-back transaction. Statistics was just the first page
  hit: **all six admin screens with a user picker** shared the defect, plus the public home page and
  `/resume` via `GetSiteOwner`. Fixed by moving the predicate out of SQL into C#, which degrades
  correctly on an older schema. **The general hazard is now a documented standing rule: deploy the
  website first, and prefer schema-tolerant reads in shared code.**
- **UAT-005 — Blog-Archive link** added to the home page's Latest-Articles intro, pointing at the
  owner's C# Corner writing; renders even with zero posts, and carries the full off-site treatment
  (`rel="noopener noreferrer"`, external-link icon, destination-naming `aria-label`).
- **Profile statistics** were gathered from the two C# Corner pages the owner supplied and handed
  back as proposed values; **nothing was written to `UserStats`** — those are the owner's numbers to
  approve, and three of the four current values are contradicted by the source (see the report).

**Third batch — a new feature and a data cleanup:**

- **UAT-006 — Speaker Profile page + its admin screen.** Public `/speaker-profile` renders **Past
  Sessions** (Date · Event Title → event page · Session Title · Details) and **Future Sessions**
  (same, plus Registration Link); `/admin/speaking` adds, edits and deletes the rows. Built on the
  **existing `UserEvents` table**, which was already the generic timeline store discriminated by a
  `Type` column that had only ever held `'Experience'` — so migration 031 adds exactly one column
  (`RegistrationUrl`) and an index, not a new table. **Past vs Future is derived from the date, not
  stored**, so it cannot go stale. Loaded with the owner's **real 21 sessions across 18 events**,
  scraped from both pages of his C# Corner speaking list and from each linked event page.
- **UAT-007 — demo data retired.** `019-SampleData.sql` PARTS C–G removed (series, ten posts,
  junctions, comments/ratings, and the resume block including the four fabricated `UserStats`). The
  three staff accounts and the category descriptions stay — the owner chose that scope once the
  conflict was put to them, because the whole test harness resolves its credentials from those
  accounts. Local database purged of the same content. **BRD-73 is deliberately narrowed, and said
  so, rather than quietly unmet.**

⚠ **Two things the owner must do, both because BlogApp does not migrate (UAT-004's standing rule):**
deploy the website so migrations **030 and 031** reach production, and *then* run
`docs/data/speaking-engagements.sql` against it. That file is deliberately **outside**
`PostgresScripts/` — TechieBlog ships as a clone-and-own template, and a numbered migration would
hand one person's speaking history to everyone who clones it. It is re-runnable (proved: a second run
inserted 0 rows).

**Fourth batch — mockup fidelity, and a misdirected comparison:**

- **UAT-008 — "the site looks very different from the mockups". TWO folders are named `mockups`, and
  an `@mockups/` reference resolves to the WRONG one.** `docs/mockups/` is the current authoritative
  set — newest file **2026-08-06**, **39 of 39** files reference TrBlazeUI — and is what
  `docs/TechieBlog-UIDesign.md` names as the visual contract. The repo-root `mockups/` is a different,
  older set: newest file **2025-12-16** (249 days), **0 of 35** files mention TrBlazeUI, `#0078D4` +
  Segoe UI + 4px radius. **The owner has confirmed `docs/mockups/` is the set to follow strictly**, and
  every comparison below was made against it. Against that contract the token layer already matches exactly
  (`--primary` ≡ `#2563eb`, `--radius` `0.625rem`). **But three real gaps were found and fixed:** the
  hero headline (now one `<h1>` at `clamp(30px,5vw,46px)/800` with the name in `--primary`, as both
  hero mockups render it), centred section titles, and social links as 44×44 bordered circles.
  ⚠ **Recorded trap:** the build consumes TrBlazeUI's **prebuilt** CSS with no Tailwind JIT, so
  `text-[clamp(...)]` is never generated — the first attempt silently collapsed the headline to the
  inherited size and was caught only by measuring `getComputedStyle` on the running page. Bespoke
  values must be real CSS rules.
- **Two items left as OWNER DECISIONS, not fixed:** the dark-vs-light default (a stored setting,
  deliberately set to dark by migration 025 for BRD-66 — changeable in Settings → Theme without a
  deploy) and the brand/footer wording (owner-controlled site title). Changing either silently would
  undo a documented requirement or rewrite live public content on a guess.
- **UAT-009 — About removed from the primary nav** (footer still links it; the `/about` route is
  untouched). A deliberate divergence from the mockup, recorded so a verifier does not revert it.
- **UAT-010 — the speaking data script moved to `docs/data/`** so it can be deleted after loading;
  nothing under `docs/` is referenced by a build, a test or DbUp.


**Fifth batch — YOLO: full mockup sweep + the legacy folder deleted.**

**The sweep is CLEAN.** All 38 contract mockups audited against the running app
(`tests/.artifacts/harness/uat-users/sweep.mjs`): 27 web screens compared, 11 skipped and named
(3 describe the MAUI desktop head, 8 need a slug/id/token an empty database cannot supply).
Structure and computed typography were diffed rather than pixels, because UAT-007 emptied the
database on purpose and every content region therefore differs by design.

- **First pass: 14 screens with gaps; 3 were real and are fixed.** (a) **Public page titles rendered
  at 48px against the mockups' `clamp(24px,4vw,32px)`/800** — every public title in `docs/mockups/`
  carries that identical inline style, while TrBlazeUI's `TypographyH1` bakes in
  `text-4xl`/`lg:text-5xl`. Fixed with `h1.page-title` in `layout.css`, whose element+class
  specificity (`0,1,1`) deliberately outranks the library's bare utility (`0,1,0`) so it wins
  regardless of stylesheet order. (b) `/about`'s title was left-aligned inside a header the mockup
  centres. (c) Four headings said something different from the design (`Page not found` →
  `This page wandered off`, `My Profile` → `Manage Profile`, `Comments Management` →
  `Comment moderation`, `All Series` → `Article Series`).
- **Second pass: 11 gaps remain and every one was PROVED to be an empty-database artifact or a
  documented decision.** The five "mockup has a table, page has 0" rows were verified to render a
  proper empty state with no error — and the control is that `/users` and `/CategoriesList`, the two
  admin lists that still have rows, both pass. The rest are mockup *content* the deleted seed data
  used to supply, plus two matcher artifacts (the mockup persona is "Ravi Rathore", the owner is
  "S Ravi Kumar"; and the home contact block is headed "Contact" because REQ-UI-049 explicitly
  reuses `ResumeContact`, and `10-resume.html` heads that section "Contact" too).
- **UAT-011 — repo-root `mockups/` DELETED** (36 files). `docs/mockups/` is now the only folder of
  that name, so an `@mockups/` reference cannot resolve to the wrong set again. Historical
  references in `docs/OldDocs/` and `docs/stories/` were deliberately left pointing at the dead path
  — those are read-only archives of Phase 1 as it actually happened, and rewriting them would
  falsify the record.
- **UAT-012 — a claim I made in this session, corrected.** I told the owner his mockups were
  pre-TrBlazeUI. Measured: `docs/mockups/` is 16 days old with TrBlazeUI in 39 of 39 files; the
  repo-root set was 249 days old with it in 0 of 35. His description matched his folder exactly. The
  error was treating the `@mockups/` path as authoritative for what he meant. The provenance of the
  2025 set was **not** rewritten — that would have put something untrue in the record.

**Still an owner decision, unchanged:** the site ships **dark** by default (migration 025) while
every mockup renders light. It is the largest remaining visual difference and is a Settings toggle,
not code.

**Owner question answered: the BlogApp upload work does NOT affect the website's admin section.**
Verified by file-level evidence, not assertion — the only shared-project files modified today are the
three *skills* files from the previous round; every upload/SFTP file is under `source/BlogApp/`, and
`BlogEngine/Storage/*` was last touched 2026-08-10. `DesktopFileStorageFactory` replaces
`IFileStorageFactory` inside **BlogApp's own DI container only** (`MauiProgram.cs:290-298`); the web
host still resolves `Storage.FileStorageFactory` from `BlogSvcInitializer.cs:237`.

---

**2026-08-22 — OWNER UAT of the BlogApp desktop head found three defects. All three are fixed,
smoke-proven live and documented; none of them touches the website's behaviour.** `*fix-issues`
(analysis + fix, both explicitly asked for). New rows **REQ-UI-063 / REQ-FN-062 / REQ-UI-064**, all
`Implemented` — capped there deliberately, because the ceiling for a builder's own smoke is
`Implemented` and no verify-phase run has graded them. Build rung #4 **0 Error(s) 7/7**; suite
**1 490 → 1 496** (1 493 pass / 0 fail / 3 skip, +6 mutation-tested Facts). Live evidence came from
BlogApp.exe launched by the harness and driven over its own WebView2 CDP — **14/14 smoke assertions
PASS** (`C:\Users\srkra\blogapp-smoke\uat-fix-smoke.mjs`, screenshots in `test-results-blogapp/`).

- **REQ-UI-063 — the desktop head opened the public blog on every warm start.** *The first diagnosis
  was wrong and the running app corrected it,* which is the part worth remembering: the base href was
  blamed, a component was built on that theory, and instrumentation showed the app was already on
  `/login` at its first render. The real chain was `MainPage.StartPath = "/login"` → `LoginPage`'s
  already-signed-in branch → `RoleLandingRoutes.PublicHome`. Fixed with BlogApp's own entry point
  `/blogapp/start`, which resolves the role. Now lands on `/admin`, `h1 "Dashboard"`, remembered
  session intact.
- **REQ-FN-062 — images uploaded from the desktop never reached the server. ⚠ THE FIRST FIX WAS
  WRONG AND FAILED IN THE OWNER'S HANDS; rebuilt the same day on SSH/SFTP — see 2026-08-22b below.**
- **REQ-UI-064 — skill ordering.** Half the report did not reproduce (the per-skill chevrons exist
  and work); the real gaps were that CATEGORY order was hard-coded alphabetical on both the admin
  screen and the public resume, that no order was visible, and — found while fixing — that the
  per-skill swap was a silent no-op on tied `DisplayOrder` values.

**Prior state (unchanged below): all other agent-actionable REQs are terminal; awaiting UAT per
`docs/TechieBlog-UsageGuide.md`.**
`*build-phase` (FIX) + a chained scoped `*verify` + `*handoff-phase`. Terminal **159 → 160**; open
**5 → 4**. Build GREEN rung #4 `0 Error(s)` 7/7; suite **1 482 → 1 490** (1 487 pass / 0 fail / 3 skip).
**REQ-NFR-041 CLOSED (`Verified`) — the three-occurrence regex-blindness cycle is over.** Pattern 2
was blind a third time (one underscore only). Both halves fixed: the pattern allows N underscores
plus generics, **and patterns 1-4 are gated at BUILD time** by `tests/unit/Ops/SourceConventionTests.cs`,
whose fifth Fact asserts each regex matches a synthetic violation — so a dead regex fails the build
instead of reading as a pass. Proved by **mutation test**: 3 violations injected into
`source/BlogModel/` failed 3 of 4 scans, each naming file, line, text.

**REQ-NFR-038 — five manual owner steps moved into `deploy.yml`** (`chown 1654`, a writability probe,
the DDL-rights probe as a *gate*, server-side GHCR login, a hard `/_framework/blazor.web.js` check),
**then the REAL VPS was inspected** via an owner-run write-free recon script. **Three would-be
first-deploy failures found and closed:** (1) the server was never authenticated to GHCR, so
`compose pull` would have been denied; (2) **PostgreSQL was not listening on `172.17.0.1`** — runbook
Step 7 edits 1-2 never applied, so the container could not have reached the database and `/healthz`
would have sat at 503; owner fixed it, `pg_isready` now answers `accepting connections`;
(3) **`SEQ_API_KEY` was required but no `seq` container exists** — now optional, `SeqUrl` rendered
blank, both branches tested. Also confirmed `ensure-db` makes `appuser` the DB **owner**, so on
PG 18.4 the §8 GRANTs are very unlikely to be needed. Stays `Implemented`: server *state* is known,
but no pipeline step has *executed* there.

**2026-08-16 — two of those findings CORRECTED, and the deploy docs merged into one file.**
(a) **Seq IS running** at `https://seq.techierathore.com` (Seq 2026.1.17083, same VPS IP as the
apex, same Caddy) — probed directly over HTTPS. The 2026-08-14 remedy therefore over-corrected:
blanking `SeqUrl` whenever `SEQ_API_KEY` was unset would have **silently disabled log shipping on
every deploy**, since nothing gave the owner a reason to set that secret. `SeqUrl` is now rendered
**unconditionally** (default `http://seq:5341`, at the time overridable by a repo **variable**
`SEQ_URL` — **that override was removed hours later, see 2026-08-16b**), and a new warning-only
deploy step *Report the Seq endpoint* observes on the server which address is right. `SEQ_API_KEY` stays optional and genuinely is: that Seq accepts anonymous ingestion
(`POST /ingest/clef` → 201; reads 401). All three render branches executed, `docker compose config`
valid. (b) **`appuser` DDL/database-creation rights owner-confirmed** — no longer an inference from
`ensure-db`'s text; the §8 gate stays as a guard, not an expected step. (c) **`docs/Server-Setup.md`
+ `.html` merged into `docs/Prod-Deploy-Checklist.md` §2 and DELETED** — two files described one
procedure and the short one had already drifted stale on both facts above.

**2026-08-16b — SERVER BUILD COMPLETE. Deploy docs realigned to runbook v5 + deployment-brief v3.**
The owner finished `docs/bluehost-vps-runbook-v5.md` end to end and its acceptance script
`sudo /srv/checkup.sh` (Part 9) reports **0 failed** — swap, ufw, fail2ban, cron, Postgres +
pgvector, Docker, the `web` network, Caddy valid, **`seq` container up and answering**, the whole
`/srv` tree, and backups landing on **both** OneDrive and Google Drive. The server is also **logged
in to GHCR** with a stored PAT. **Only the app container is left to deploy — that is CI's job.**
Consequences: the last open unknown (`http://seq:5341` — is the container named `seq` on `web`?) is
**closed**, so the `SEQ_URL` repo-variable override added earlier the same day was **removed** —
brief v3 §0 forbids the public Seq URL in app config, and it existed only while the address was
unconfirmed. `SeqUrl` is now the hardcoded contract constant. The GHCR credential check will now
always take its **leave-it-alone** branch, which is what the brief requires. `SEQ_API_KEY` was
promoted from "optional, skip it" to **§2 step 3** — one of the two genuinely manual per-app tasks.
**Two upstream contradictions found and adjudicated in the doc rather than silently picked:**
(1) runbook §6.3 names the secret `SEQ_APIKEY_BLOG`, brief §2 names it `SEQ_API_KEY` — **this repo
follows the brief** (the workflow reads `SEQ_API_KEY`; a mis-named secret fails silently, so it is
called out three times); (2) brief §0 mandates a **root** container with no `USER` directive, while
this repo runs **non-root UID 1654** — **deviation kept deliberately**, because the pipeline already
chowns the bind mounts to 1654 and probes them, which is exactly the premise the brief's rule
assumes absent. Both are documented in Prod-Deploy-Checklist §0 with the reasoning and the exact
change needed to switch. `sudo /srv/checkup.sh` added to §7 (post-deploy) and §11 (routine ops).
Evidence: `deploy.yml` parses (3 jobs / 21 steps), 14/14 shell blocks pass `bash -n`, the render
step **executed** in both Seq branches → `SeqUrl: "http://seq:5341"` in each, 237 lines, zero
surviving placeholders, `docker compose config` **valid**; HTML re-rendered, **58/58 anchors
resolve**, no unbalanced tags.

**2026-08-16c — `ciuser` CI account (runbook v5.2 / brief v3.2). THREE DEFECTS FOUND, all in the
pipeline, all fixed and measured.** The owner created a dedicated CI account and supplied the two
updated specs. Auditing this pipeline against them found: **(1) THE GHCR CREDENTIAL DOES NOT COVER
`ciuser` — this would have failed the first deploy.** Brief v3.2 §0 still asserts "the server is
already authenticated… the workflow must not run `docker login` over SSH", but that answer predates
the account change: registry logins are **per-user** (`~/.docker/config.json`), the `docker` group
grants daemon access and *nothing* at the registry, and runbook Step 12 ran as `ravi` before
`ciuser` existed. So `docker compose pull` of a private image as `ciuser` would fail `unauthorized`.
**The deploy survives only because the ephemeral-login fallback was deliberately retained on
2026-08-16b** — it is now the LIVE path, not a dormant one. Documented, with the one-command fix
(`sudo -u ciuser -H … docker login`) that restores spec compliance. **(2) The ownership fix would
have cut `ciuser` out of its own directories.** `chown -R 1654:1654` replaces group `webops` with
`1654`, silently breaking §8's documented `rsync` uploads-migration path, which connects as
`VPS_USER` (= `ciuser`). Now `chown -R 1654` (owner only) + `chmod g+ws` on the directory, restoring
the setgid/group-write model runbook v5.2 §2.1 sets on `/srv`. **Measured on both a Debian base (the
app image) and Alpine:** mode `drwxrwsr-x`, owner 1654, group unchanged; container UID 1654 writes;
a `webops` user writes; setgid inheritance confirmed; idempotent over 3 runs; parent untouched;
files stay `-rw-r--r--`. **(3) A silent-false-pass removed:** the old `find … ! -uid 1654 | wc -l`
guard reports "nothing to do" on any BusyBox/musl base — `-uid` is GNU-only, and a failed `find`
piped to `wc -l` yields `0`, which `set -e` never sees. Reproduced on `alpine:latest`; latent today
(Debian image), gone now. **Two quoting bugs in the new inline comments were caught by the
`bash -n` gate before they shipped** — one stray apostrophe inside the `sh -c '…'` inner script
(step dies `syntax error near unexpected token done`), then an *even* number of them, which
re-balances so `bash -n` passes while exposing the enclosed text to the outer shell. An
editors-note now states the rule in the file. **Compliance audit:** the workflow executes exactly
one `sudo` — `/usr/local/bin/ensure-db`, the only command in ciuser's allowlist — uses no
`sudo docker`, and never hardcodes the SSH username. **Evidence:** `deploy.yml` parses (21 steps);
**14/14** blocks pass `bash -n`; 0 apostrophes in the inner script; render executed → 245 lines,
zero placeholders, `docker compose config` VALID; checklist HTML **60/60 anchors**, no unbalanced
tags. Zero files under `source/` touched.

## 2026-08-22b — REQ-FN-062 round two (the first fix was wrong)

**The owner followed the instructions, got a green "Media folder OK", uploaded five logos, and none
of them reached the site.** Round 1 offered a folder box and assumed the server's uploads directory
could be mounted from Windows. It cannot: measured from this machine the VPS answers on **443 and 22
only**. Asked for "the folder your site serves /uploads from", the owner reasonably typed the
SERVER's path with a drive letter in front — `C:\srv\data\techieblog\uploads` — Windows created
it, the probe called it writable, and five uploads went to the laptop.

**Two defects, both mine.** (1) The transport was never verified to be *achievable* in this
deployment; a session note recording that this VPS is reachable over HTTPS rather than as a
filesystem was on file and was not applied. (2) **The probe converted a mistake into confidence** —
it asked "can I write here?", which is true of any folder on one's own C: drive. Writability is not
reachability. Same silent-false-pass class as REQ-NFR-039 and REQ-NFR-041.

**Round 2 uses the transport that actually exists between these two machines: SSH.** Port 22 is
open, and is the same access already used to reach the site's database. (`localhost:5433` **is** the
server database, exposed locally — owner-corrected; the database half was never at fault, and an
earlier note here claiming the desktop was editing a local DB was wrong.) New in `source/BlogApp`:
`MediaTransports` makes the destination an explicit choice rather than something inferred from a
path; `SftpFileStorage` writes over SSH.NET into the server's uploads directory; `UploadsUrlRewriter`
resolves stored `/uploads/…` paths against the site address **at display time only**, which is what
fixes "the Experience page was not showing the images" without changing stored data. The probe now
proves a real round trip against the actual destination and **refuses a local fixed drive outright,
before creating anything**.

**Follow-up 2026-08-22c — the two frictions the owner hit next, both removed.** The `scp` recovery
command was bad advice: it carried a literal `you@techierathore.com` placeholder, was run verbatim,
and produced a password prompt for an account that does not exist. (Being connected to the site
database on `localhost:5433` is a forwarded port, not an SSH session `scp` can reuse — a second,
separate reason that command was never going to be frictionless.) The real defect was reaching for a
shell at all: the app already holds proven credentials and the server path. A **Send to server**
button now pushes a folder of stranded images over that same connection, preserving the
`uploads/{category}/{file}` layout and writing nothing to the database. The SSH private key and the
migration folder are now chosen with **Browse** buttons rather than typed. **Smoke 7/7** — three
stranded files arrived at the matching server paths with no shell involved, and a second run left the
count unchanged. *(Native picker dialogs are OS modals outside the WebView, so automation asserts the
buttons render and are wired, not that the dialogs open.)*

**Smoke 11/11 PASS against a real SSH server** (disposable container on the same
`/srv/data/techieblog/uploads` path, verified through `docker exec`): the upload arrived on the
server, 70 bytes matching, **zero** new files on this machine, and the rendered `src` came back
absolute so a server image can display. **Negative control:** the owner's exact path
`C:\srv\data\techieblog\uploads` is now **REFUSED**, and a fresh local path is refused *and not
created*. Build **0 Error(s) 7/7**; suite **1 496** (1 493 pass / 0 fail / 3 skip).

## Next command to run

```
/TechieFlow:agents:verifier *verify REQ-UI-063 REQ-FN-062 REQ-UI-064 REQ-UI-020 REQ-FN-020
```
The first three are `Implemented` on the builder's own smoke; a verify-phase run is what promotes
them to `Verified`. `REQ-UI-020` and `REQ-FN-020` are added to that scope because the `UAT-002` /
`UAT-003` / `UAT-001` fixes changed what those two requirements now have to satisfy — the users
screen gained edit and delete, and account activation became a real gate on sign-in.
Then continue manual UAT per `docs/TechieBlog-UsageGuide.md`.

**⚠ Deployment note for the UAT round (migration 030).** `030-UserAdminEditDelete.sql` **backfills
every `BlogUser` row to `IsConfirmed = TRUE`** before `AuthSvc` begins enforcing that flag at
sign-in. That backfill is deliberate and must not be dropped: activation was never persisted and
account creation defaulted it to `FALSE`, so no stored `FALSE` represents anyone's actual intent, and
enforcing the flag without the backfill would lock out every administrator-created account —
possibly including the only Admin. Applied and verified on the local database (DbUp journal shows
`030-UserAdminEditDelete.sql` applied 2026-08-22 17:36).

**⚠ TWO OWNER ACTIONS, both from REQ-FN-062 (revised 2026-08-22b):**
1. On *Change connection → Media storage*, choose **"Send to the server over SSH (SFTP)"** and enter
   the SSH host, username, password or private-key file, and `/srv/data/techieblog/uploads`. Press
   **Test** — it now writes a file to the server, reads it back and deletes it, so a pass means the
   bytes genuinely crossed. The folder option remains for a real mapped drive and now **refuses** a
   local path.
2. **Six existing image rows are stranded — use the in-app button, no scp needed** (revised
   2026-08-22c). On the same Media storage panel, **Send to server** pushes a folder of images over
   the SSH connection you just tested. Run it once for
   `%LOCALAPPDATA%\TechieBlog\BlogApp\wwwroot\uploads` (the default) and once for
   `C:\srv\data\techieblog\uploads`. Their names already match what the database rows point at,
   so landing the files repairs the existing rows — nothing is re-uploaded and nothing is written to
   the database. *(The earlier `scp` advice here was wrong: it carried a literal `you@…` placeholder
   and produced a password prompt for an account that does not exist.)*

## Open requirements

- [ ] **Implemented — awaiting a verifier run (3)** — REQ-UI-063, REQ-FN-062, REQ-UI-064 *(2026-08-22 UAT fixes; builder smoke 14/14 PASS)*
- [ ] **Implemented — not agent-observable (1)** — REQ-NFR-038 *(needs a real VPS)*
- [ ] **PARTIAL (2)** — REQ-NFR-017 *(owner: CI repo secret)*, REQ-NFR-026 *(stage 4 deferred)*
- [ ] **In Progress (1)** — REQ-NFR-025 *(owner: git-history decision)*

Counts: 167 rows. Terminal 160 (148 `Verified` + 4 `N/A` + 8 `N/A removed`); open 7 — 4 owner-gated,
3 awaiting verification.

## Known blockers

- **⚠⚠ OWNER — ROTATE A LEAKED PAT, TODAY.** A live TrBlazeUI PAT sits in **clear text** at
  `C:\Users\srkra\AppData\Roaming\NuGet\NuGet.Config` and was exposed in a 2026-08-14 transcript. Not
  used after discovery. Rotate, then re-store.
- **⚠ OWNER — six GitHub items gate a real deploy:** `TrBlazeUiPackagesToken`, `JWT_SIGNING_KEY`,
  `APP_ENCRYPTION_KEY`, `ANALYTICS_VISITOR_SALT`, **the per-app `SEQ_API_KEY`** (created in the Seq
  UI — note the name, NOT runbook §6.3's `SEQ_APIKEY_BLOG`) — **owner reports all secrets added
  2026-08-16**. DNS A records are **already live** (apex + www → 50.6.45.249, verified). Server-side
  steps (ownership, DDL probe, GHCR auth) need nothing from you. See `docs/Prod-Deploy-Checklist.md`
  §2 — now the ONLY deploy setup doc; `docs/Server-Setup.md` was merged into it and deleted
  2026-08-16.
- **⚠ OWNER — OPTIONAL BUT RECOMMENDED: give `ciuser` its own GHCR login.** Registry credentials are
  per-user; runbook Step 12 logged in as `ravi`, so `ciuser` has none and every pull currently relies
  on the pipeline's ephemeral `GITHUB_TOKEN` login. It works, but it ties each pull to
  `packages: read` on the workflow. One command on the server as `ravi`:
  `sudo -u ciuser -H bash -c 'echo <PAT> | docker login ghcr.io -u techierathore --password-stdin'`
- **`deploy.yml` triggers on `main` only** — one production branch, no second name to keep in sync.
  **Correction 2026-08-16:** an intermediate revision claimed this repo used `master` and widened the
  trigger to both names; that came from a stale session snapshot, not the repository. Owner confirmed
  `main` and removed the fallback. No deploy was ever at risk — both names were listed at the time —
  but the written instructions pointed at the wrong branch for part of a day.
- **✅ The VPS build is COMPLETE (2026-08-16)** — the whole of `docs/bluehost-vps-runbook-v5.md`,
  confirmed by its own acceptance script `sudo /srv/checkup.sh` reporting **0 failed**. The earlier
  "runbook was NOT fully applied" warning is retired. Historical note worth keeping: two recon
  conclusions from 2026-08-14 had to be reversed (Postgres reachability, and "no Seq container") —
  **do not treat a one-off recon as ground truth when a repeatable acceptance script exists.**
  Verified state: `docs/Prod-Deploy-Checklist.md` §0.
- **⚠ `/srv/caddy/sites/techieblog.caddy` already exists and is AUTHORITATIVE** — the pipeline is
  create-only and will never replace it. Content checked and correct; it just lacks the repo
  version's compression directive.
- **⚠ A green deploy is still not proof** — every probe has been green while the site was broken three
  times. Two classes are now gated (`_framework` 404, empty schema); the Caddy hop is not.
- **⚠ `dotnet run` ignores `ASPNETCORE_ENVIRONMENT`** unless `--no-launch-profile` is passed.
  **`CLAUDE.md` says "No TrBlazeUI" — stale**; 2.0.2 is referenced by three projects.
- **⚠ OWNER — dead PAT still in git history.** Revoked (401). Recommendation: accept the history.
- **⚠ Admin WCAG rests on a workaround** (TR-054/063/064); no screen-reader pass ever run. *(The
  `mustchangepassword` contradiction is RESOLVED — dev-DB drift, not a doc error. See UsageGuide.)*

## Verification log

| Date | Phase | Result | Status table |
|------|-------|--------|--------------|
| 2026-08-02…08-06 | Day-1 docs, amendment, mockups | Docs only — BRD-92…97, 38 screens | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-07…08-08 | *build-phase x2 + build repair | 383 tests; PASS 7/7 · no verifier run | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09 | *verify all* (first executed run) | 131 graded · 98 Verified, 17 FAIL, 8 Needs re-verify | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-09…08-10 | *build-phase FIX + *build-phase | 1055 → 1291 tests · coverage 82.5% | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | *build-phase + local Docker verification | 1355 tests · BLOCKING `_framework` defect found and fixed | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-11 | *build-phase (7 clusters) + chained *verify all* | **51 rows graded: 45 Verified** · terminal 108 → 149 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | *build-phase (REQ-FN-061) + chained *verify* | 1411 → 1482 tests · **11 graded, 9 → `Verified`** · REQ-NFR-041 raised · terminal 149 → 158 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | *verify* REQ-NFR-001/038/041 (scoped) | **REQ-NFR-001 → `Verified`** (both perf budgets, 0 errors in 30 000 req @ c100); REQ-NFR-041 → `Needs re-verify` (pattern 2 blind, 3rd time) · ⚠ leaked PAT found · terminal 158 → 159 | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | ***handoff-phase*** (owner-invoked; a prior handoff ran 2026-08-11) | **READY FOR UAT.** UsageGuide finalised: test-user table **reconciled against the live DB** (all 4 exist, ids 1-4), and the long-open `MustChangePassword` contradiction RESOLVED — `019-SampleData.sql` seeds users 2-4 `TRUE`, so the doc was right and the **dev DB had drifted** (re-arm step skipped after a smoke). Stale sections corrected against evidence: "solution does not build", "no tests / no CI", "reset tokens in memory", "seeded admin password is plaintext" were all false. DevGuide re-map skipped — **zero files under `source/` changed this session**. TrBlazeUI feedback re-confirmed, no new entries (next id TR-067) | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-14 | ***build-phase* FIX + chained *verify* REQ-NFR-041** | PASS 0 errors 7/7 · 1482 → **1490** tests · **REQ-NFR-041 → `Verified`** (pattern 2 fixed AND patterns 1-4 promoted to a build-time gate; mutation-tested by the verifier) · **REQ-NFR-038: 5 manual server steps moved into CI, then the REAL VPS inspected (owner-run read-only recon) — 3 would-be first-deploy failures found and closed: server-side GHCR auth, Postgres not listening on 172.17.0.1 (runbook Step 7 never applied), `SEQ_API_KEY` required with no Seq container** · deploy docs rewritten · terminal **159 → 160**, open **5 → 4** | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22b | ***fix-issues*** round 2 (REQ-FN-062 failed UAT) | **THE FIRST FIX WAS WRONG AND FAILED IN THE OWNER'S HANDS.** A green "Media folder OK" for `C:\srv\data\techieblog\uploads` — a folder on the operator's own drive — sent five uploads to the laptop. Round 1 assumed the Linux VPS's uploads directory could be mounted from Windows; it answers on **443 and 22 only**. Rebuilt on **SSH/SFTP**: explicit `MediaTransports` choice, `SftpFileStorage` over SSH.NET, `UploadsUrlRewriter` so server images display in the desktop app, and a probe that proves a real round trip and **refuses a local fixed drive before creating anything**. **Follow-up 2026-08-22c — the two frictions the owner hit next, both removed.** The `scp` recovery
command was bad advice: it carried a literal `you@techierathore.com` placeholder, was run verbatim,
and produced a password prompt for an account that does not exist. (Being connected to the site
database on `localhost:5433` is a forwarded port, not an SSH session `scp` can reuse — a second,
separate reason that command was never going to be frictionless.) The real defect was reaching for a
shell at all: the app already holds proven credentials and the server path. A **Send to server**
button now pushes a folder of stranded images over that same connection, preserving the
`uploads/{category}/{file}` layout and writing nothing to the database. The SSH private key and the
migration folder are now chosen with **Browse** buttons rather than typed. **Smoke 7/7** — three
stranded files arrived at the matching server paths with no shell involved, and a second run left the
count unchanged. *(Native picker dialogs are OS modals outside the WebView, so automation asserts the
buttons render and are wired, not that the dialogs open.)*

**Smoke 11/11 PASS against a real SSH server** (upload arrived on the server, 70 bytes matching, 0 files written locally); **negative control: the owner's exact path is now REFUSED and a fresh local path is refused and not created**. Build 0 errors 7/7; suite 1 496 (1 493 pass / 0 fail). Owner correction recorded: `localhost:5433` **is** the server database, so the DB half was never at fault | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22 | ***fix-issues*** (owner UAT, BlogApp desktop head) | **3 defects reproduced live, fixed, smoke-proven: REQ-UI-063 / REQ-FN-062 / REQ-UI-064, all `Implemented`.** Build rung #4 **0 Error(s) 7/7**; suite **1 490 → 1 496** (1 493 pass / 0 fail / 3 skip). Live smoke **14/14 PASS** over CDP against a harness-launched BlogApp.exe. **REQ-UI-063:** first diagnosis (base href) was WRONG and was killed by instrumenting the running app — real cause `MainPage.StartPath = "/login"` → `LoginPage`'s already-signed-in branch → `PublicHome`; fixed with a BlogApp-owned `/blogapp/start` entry point; now lands on `/admin`. **REQ-FN-062:** the owner's failed upload was found still on the desktop at `%LOCALAPPDATA%\…\wwwroot\uploads\logos\`; the head had no media connection at all. Added one (media folder + probe + `DesktopFileStorageFactory`), proven end to end — bytes landed in the configured folder, **0** new files under `%LOCALAPPDATA%`, stored path still the site-relative `/uploads/…`. Also fixed the prefill gap that made the settings screen unusable without re-typing both site secrets. **REQ-UI-064:** half the report did not reproduce (the per-skill chevrons work); the real gaps were alphabetical CATEGORY order on BOTH the admin screen and the public resume, no visible order, and a per-skill swap that was a silent no-op on tied values. 6 new Facts, **mutation-tested** (revert the resume only → 2 fail; revert the admin helper only → 3 fail; restored → 6/6). Zero changes to `source/TechieBlog`; the only non-BlogApp changes are the two skills surfaces. DB and settings left as found | docs/TechieBlog-Checklist.md#requirements-status |
| 2026-08-22c | ***fix-issues*** (owner UAT round 2 — **website**, not the desktop head) | **2 reported defects + 1 found while triaging; all 3 fixed and smoke-proven. Logged as `UAT-001…003` in the new checklist "UAT Bugs" section, NOT as new `REQ-*` rows** (owner's instruction — a UAT defect is a broken requirement, not a new one; requirement counts unchanged at 160 terminal / 4 open). Build rung #4 **0 Error(s) 7/7**. Live smoke **13/13 PASS** (Playwright vs the web host on `:5399`, screenshots `tests/.artifacts/harness/uat-users/`); host killed by PID afterwards and the four seeded users restored to their documented state. **UAT-001 — the featured article surviving a database purge was a CACHE artifact, and the site had already self-corrected:** fetching the live home page during triage returned `home-articles-empty` with no featured block, proving delete and query were both right. The public pages read a **10-minute in-process cache** (`MemoryCacheService.cs:96,152`, keys `content:posts:featured` / `content:posts:published:*`) that a delete made from the **desktop client straight to the database** can never invalidate, because it never enters the web host's process. Fixed with a `Clear cached content` control on `/settings` → Maintenance; the underlying gap is inherent to per-process caching, not a coding error. **UAT-002 — `/users` had no edit and no delete** (confirmed at `UsersList.razor:151-185`): added a full Edit dialog and a **soft** Delete (16 FKs point at `BlogUser`, only 4 cascade, so a hard delete would be refused for any author who has posted), guarded against deleting yourself, the site owner or the last active admin, each refusal explained on the button. **UAT-003 — found while triaging, not reported: the existing Activate/Deactivate button had NEVER worked** — three independent breaks (never persisted, since `UpdateBlogUser` has no `IsConfirmed` parameter; never enforced at sign-in; never set at creation, so every admin-created account is stored *Inactive* yet can still sign in). Fixed in migration `030-UserAdminEditDelete.sql` + `AuthSvc`, **with an explicit backfill so switching enforcement on cannot lock anyone out**. Owner question answered from file evidence: the BlogApp upload work does **not** touch the website admin — `DesktopFileStorageFactory` replaces `IFileStorageFactory` in BlogApp's own container only | docs/TechieBlog-Checklist.md#uat-bugs |
| 2026-08-22d | ***fix-issues*** rounds 3-6 (owner UAT, website) | **UAT-001…018 all closed; DEPLOYED to production and verified live.** Prod `/healthz` now reports all **30** migration scripts (was 28), `/speaker-profile` returns 200 with its banner, the nav shows Speaking and no About, and the Blog-Archive link is live — each checked against `https://techierathore.com`, not inferred. Highlights: **UAT-004** a regression I introduced four hours earlier (schema-coupled read broke all six desktop admin screens — produced the website-first ship rule); **UAT-006** Speaker Profile + admin screen on the existing `UserEvents` table; **UAT-007** demo seed data retired; **UAT-008** full 27-screen mockup sweep, 3 real gaps fixed, remainder proved to be empty-DB artifacts; **UAT-011/012** the duplicate `mockups/` folder deleted and a wrong claim of mine corrected without falsifying the 2025 set's provenance; **UAT-014/015** both onboarding docs rewritten (three errors in GETTING_STARTED would have stopped a fresh clone dead); **UAT-016** a mutation-tested build-time docs guard, blind on 2 of 4 faults when first written; **UAT-018** logout's `NavigateTo("/")` was dead code — the documented destination and the real one had disagreed silently. Build 0 errors 7/7; suite 1 490 → **1 512**, 0 failures. ⏳ Only the production speaking-data load remains | docs/TechieBlog-Checklist.md#uat-bugs |
| 2026-08-22e | ***verify*** REQ-UI-005 · REQ-UI-020 · REQ-UI-049 · REQ-FN-020 · REQ-FN-058 (executed run) | **4 Verified · 1 Needs re-verify.** Booted rung #4 on `:5099`; `tests/verify/req-list-ui.spec.ts` **11/11 passed**; `dotnet test` **1 509 passed / 0 failed**; gates applied = acceptance + data-render + visual-truth (**no `perf-budget:` declared on any scoped REQ, so §4c did not run** — not a gap). **REQ-UI-005** PASS incl. the previously-failing *no horizontal scroll at 320px* criterion, now clean on 6 routes. **REQ-UI-020** PASS — 4 rows, non-empty cells in all four columns, count badge agrees with visible rows, Add/Edit/Delete present, search narrows 4→1. **REQ-UI-049** PASS — hero two-tone heading, social circles, centred sections, clean at 1280+390; ⚠ the stats band and Download-CV CTA were **not observable** (no `UserStats` rows, no `CVFilePath`) and are explicitly not claimed. **REQ-FN-058** PASS — deep links to `/admin/speaking` and `/users` keep the session. **REQ-FN-020 DEMOTED** — 0 published posts, so listings/featured/related/reading-time could not be observed; empty-state contract verified and 19 unit assertions cover the logic, but a runtime observation nobody made will not be claimed. No gate telemetry emitted for it: it was unobservable, not a gate catch, and a `render` record would have inflated that gate's catch rate. Ledger `docs/.last-verify.json`; Admin DevGuide runtime-stamped | docs/TechieBlog-Checklist.md#requirements-status |

## Library feedback summary

- **TrBlazeUI:** next free **TR-067**. Nothing new — this pass was app-side. Prior: TR-057…066.
  **TechieRag:** not used (no AI/RAG features).

## Standards compliance (2026-08-14)

- **All six enforcement checks are now gated by the build.** Patterns 1-4 joined 5-6 via
  `SourceConventionTests`; the exposure flagged on every prior pass is closed.
- **Source and tests clean**, each zero validated by a control in the same invocation: underscore
  fields 0 (4/4 synthetic, liveness **259**) · test-method underscores 0 (4/5 — correctly rejecting
  the non-violation; liveness **1 228**) · Hungarian 0 (2/2) · `a`/`v` 0 (both caught) · `ex.Message` 0.
- **Mutation-tested, not merely green** — 3 injected violations failed 3 of 4 scans; a passing suite
  alone cannot tell a live gate from a dead one. Run the doc greps with `command grep` (this shell
  aliases `grep` to `ugrep -G`).

## Deferred / future

- **The 137 rows `Verified` on 2026-08-11 were NOT re-confirmed** — a full `*verify all` re-sweep is owed.
- **No pipeline step has EXECUTED on the real VPS.** Server *state* is verified (§0); its behaviour
  under the pipeline is not — SSH-from-CI, GHCR push/pull, TLS issuance, the Caddy hop. §13 is the ledger.
- **Optional on the server:** delete `/srv/caddy/sites/techieblog.caddy` to adopt the repo's
  compression directive. Swap and Seq are both **done** — the whole runbook is complete and
  `checkup.sh` passes.
- **Not re-tested** — `/category/{slug}` featured-image `renders-empty`. **Not re-exercised** —
  REQ-NFR-005, REQ-NFR-023 (both keep `Verified` on unit coverage).
- **Never driven** — profile save (REQ-FN-011 update half, REQ-FN-053), newsletter Send, subscriber
  toggles, comment/rating/subscribe submits. **The "BlogApp (MAUI) is build-verified only" note is
  RETIRED (2026-08-22):** the 2026-08-22 UAT pass drove the real head over CDP through the landing
  route, the connection-settings screen (probe + save + restart), an end-to-end image upload with
  byte-level assertions, `/admin/skills`, `/admin/experience`, `/admin/images` and `/resume`. What
  is still undriven there is the rest of the admin surface, not the head itself.
- REQ-UI-059 needs a back-dated seed post; `newsletter` has 0 rows (REQ-UI-054 / REQ-FN-050
  unexercised); a real screen-reader pass; Firefox + WebKit for REQ-NFR-009; macOS BlogApp head.
- `.buildout/*/logs` ~133 MB of leftover agent logs (gitignored, safe to delete). Docker cleanup
  2026-08-14 reclaimed 18.05 GB; `WinPostgre` untouched.
