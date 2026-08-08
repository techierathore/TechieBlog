-- ============================================================================
-- Script:  019-SampleData.sql
-- Purpose: Seeds an evaluation-ready sample data set so a freshly migrated
--          database renders every public surface with real content
--          (REQ-FN-041, BRD-73).
-- Author:  flow-master (Cluster H builder)
-- Created: 2026-08-07
--
-- Requirements:
--   REQ-FN-041 - Sample posts demonstrating Markdown, images and series;
--                one user per (surviving) role; categories, tags, comments
--                and ratings, for immediate local evaluation.
--   REQ-NFR-023 - No credential is ever stored in plain text. Every seeded
--                password is a PBKDF2-HMAC-SHA256 hash produced by
--                BlogModels.PasswordHasher (same format as 003-SeedData.sql).
--   REQ-FN-018  - PostTag / PostCategory junction rows are written for every
--                seeded post so per-tag and per-category counts match the
--                listings exactly.
--   REQ-FN-022  - Comments are seeded in the ANONYMOUS shape (name + email,
--                ModerationStatus = 'Approved', IsEmailVerified = TRUE).
--   REQ-FN-023  - Ratings are seeded EMAIL-keyed with IsEmailVerified = TRUE,
--                because the public average counts verified rows only.
--   REQ-FN-028/029 - Resume rows (skills, experience, awards, stats) for the
--                site owner so the portfolio home page and /resume render.
--
-- Changes:
--   PART A - Staff accounts: one per surviving role (Editor, Author,
--            Contributor). NOTE: the 'Reader' role is NOT seeded - the
--            2026-08-06 design review retired reader accounts and public
--            registration (BRD-1/13/43/44 retired), so a Reader account would
--            be a dead credential. Admin already exists from 003-SeedData.sql.
--   PART B - Category descriptions (existing five categories keep their ids).
--   PART C - Two blog series with ordered parts.
--   PART D - Ten sample posts (eight published, one scheduled "coming soon"
--            part, one contributor draft) demonstrating Markdown headings,
--            lists, fenced code, blockquotes, tables, links and images.
--   PART E - PostCategory and PostTag junction rows.
--   PART F - Verified anonymous identities, comments (with one threaded
--            reply) and ratings.
--   PART G - Site-owner resume data: profile fields, skills, experience,
--            awards and stats.
--
-- Dependencies:
--   001-CreateTables.sql            (BlogUser, BlogPost, Category, BlogComment)
--   003-SeedData.sql                (bootstrap admin, five categories)
--   004-FixPostTable.sql            (BlogPost.Slug, SeriesId, SeriesPartNumber)
--   005-FixCategoryAndTagTables.sql (Category.Slug, Tag.Slug)
--   007-FixBlogSeriesAndPostTag.sql (BlogSeries, PostTag)
--   008-SeedTagsAndPostTags.sql     (the fifteen tags referenced by slug here)
--   010-CreatePostRatingTable.sql   (PostRating)
--   012-ResumeAndImageManagement.sql(UserSkills, UserAwards, UserStats, resume columns)
--   014-AnonymousEngagement.sql     (anonymous comment / rating columns, VerifiedEmail)
--   017-SecurityAndTokenPersistence.sql (BlogUser.MustChangePassword)
--
-- Idempotency:
--   DbUp replays are harmless. Every INSERT is guarded by NOT EXISTS or
--   ON CONFLICT DO NOTHING against a natural business key (email, slug,
--   post+email, user+name), and every UPDATE is COALESCE-based or guarded so
--   it never overwrites data an operator or another script already wrote.
--   Running this script twice leaves identical row counts.
--
-- Rollback:
--   DELETE FROM PostRating   WHERE Email LIKE '%@example.com';
--   DELETE FROM BlogComment  WHERE Email LIKE '%@example.com';
--   DELETE FROM VerifiedEmail WHERE Email LIKE '%@example.com';
--   DELETE FROM PostTag      WHERE PostId IN (SELECT PostId FROM BlogPost WHERE Slug IN (...));
--   DELETE FROM PostCategory WHERE PostId IN (SELECT PostId FROM BlogPost WHERE Slug IN (...));
--   DELETE FROM BlogPost     WHERE Slug IN ('blazor-render-modes-explained', ...);
--   DELETE FROM BlogSeries   WHERE Slug IN ('blazor-server-in-production','postgres-for-dotnet-developers');
--   DELETE FROM UserSkills / UserEvents / UserAwards / UserStats WHERE UserId = 1;
--   DELETE FROM BlogUser     WHERE EmailId IN ('editor@techieblog.test','author@techieblog.test','contributor@techieblog.test');
-- ============================================================================


-- ============================================================================
-- PART A: STAFF ACCOUNTS - ONE PER SURVIVING ROLE  [REQ-FN-041, REQ-NFR-023]
--
-- Roles come from BlogModels.AppRoles and the five policies built in
-- Program.cs from AppPolicies.PolicyRoleMap:
--   Admin       - seeded by 003-SeedData.sql (Ravi@techieblog.com)
--   Editor      - seeded here
--   Author      - seeded here
--   Contributor - seeded here (exercises ContributorOrAbove)
--   Reader      - DELIBERATELY NOT SEEDED. Reader accounts and public
--                 registration were retired by the 2026-08-06 design review,
--                 so the role constant survives only for backwards
--                 compatibility; seeding one would create a credential that
--                 no surface can use.
--
-- SECURITY: LoginPass holds a PBKDF2-HMAC-SHA256 hash, never plain text.
--   format     PBKDF2-SHA256$<iterations>$<base64 salt>$<base64 subkey>
--   iterations 210000 (BlogModels.PasswordHasher.IterationCount, OWASP)
--   salt       a fixed 16-byte per-account seed salt so the migration is
--              deterministic and replay-safe; runtime accounts always get a
--              fresh random 128-bit salt instead.
-- To regenerate a literal:
--   BlogModels.PasswordHasher.HashPasswordWithSalt(
--       "<password>", Encoding.UTF8.GetBytes("<16-char salt>"))
--
-- The plaintext passwords matching these hashes are recorded ONLY in
-- docs/TechieBlog-UsageGuide.md (the canonical test-user registry). Each
-- account carries MustChangePassword = TRUE, exactly like the bootstrap admin.
-- ============================================================================

-- Editor: manages all posts and moderates comments.
-- Password: see docs/TechieBlog-UsageGuide.md, test user 2. Salt "TechieBlogEdt001".
INSERT INTO BlogUser (
    FirstName, LastName, EmailId, LoginPass, UserName, CreatedOn, UserRole,
    IsConfirmed, MustChangePassword, Title, ProfileDescription
)
SELECT
    'Maya', 'Sharma', 'editor@techieblog.test',
    'PBKDF2-SHA256$210000$VGVjaGllQmxvZ0VkdDAwMQ==$9RnsRoBglf/lxeZkHQi84ZesiEh3YwsMPuOVyYEd5iQ=',
    'maya', NOW(), 'Editor', TRUE, TRUE,
    'Managing Editor',
    'Sample Editor account. Reviews the moderation queue and keeps the publishing calendar honest.'
WHERE NOT EXISTS (SELECT 1 FROM BlogUser WHERE LOWER(EmailId) = 'editor@techieblog.test');

-- Author: creates and edits their own posts and series.
-- Password: see docs/TechieBlog-UsageGuide.md, test user 3. Salt "TechieBlogAut001".
INSERT INTO BlogUser (
    FirstName, LastName, EmailId, LoginPass, UserName, CreatedOn, UserRole,
    IsConfirmed, MustChangePassword, Title, ProfileDescription
)
SELECT
    'Arun', 'Nair', 'author@techieblog.test',
    'PBKDF2-SHA256$210000$VGVjaGllQmxvZ0F1dDAwMQ==$joHlWDDxpRvn5Uq0KhCodudlXtNUCz41ML9fyZwYq7w=',
    'arun', NOW(), 'Author', TRUE, TRUE,
    'Staff Writer',
    'Sample Author account. Writes the PostgreSQL series and owns its drafts.'
WHERE NOT EXISTS (SELECT 1 FROM BlogUser WHERE LOWER(EmailId) = 'author@techieblog.test');

-- Contributor: submits drafts for review; has no staff surface of its own.
-- Password: see docs/TechieBlog-UsageGuide.md, test user 4. Salt "TechieBlogCon001".
INSERT INTO BlogUser (
    FirstName, LastName, EmailId, LoginPass, UserName, CreatedOn, UserRole,
    IsConfirmed, MustChangePassword, Title, ProfileDescription
)
SELECT
    'Priya', 'Menon', 'contributor@techieblog.test',
    'PBKDF2-SHA256$210000$VGVjaGllQmxvZ0NvbjAwMQ==$u8kT/YQl7trxKB9bEeAyZa/mEKXTvfsEIxf+DW1BazY=',
    'priya', NOW(), 'Contributor', TRUE, TRUE,
    'Guest Contributor',
    'Sample Contributor account. Submits drafts that an Editor reviews before publication.'
WHERE NOT EXISTS (SELECT 1 FROM BlogUser WHERE LOWER(EmailId) = 'contributor@techieblog.test');


-- ============================================================================
-- PART B: CATEGORY DESCRIPTIONS
-- The five categories already exist (003-SeedData.sql) with slugs added by
-- 005-FixCategoryAndTagTables.sql. Only the empty description is filled in, so
-- an operator-authored description is never overwritten.
-- ============================================================================
UPDATE Category SET Description = 'Front-end and full-stack work: Blazor, HTML, CSS and the browser platform.'
WHERE Slug = 'web-development' AND (Description IS NULL OR Description = '');

UPDATE Category SET Description = 'Languages, frameworks and the day-to-day craft of writing code.'
WHERE Slug = 'programming' AND (Description IS NULL OR Description = '');

UPDATE Category SET Description = 'Speaking, writing, interviewing and growing as an engineer.'
WHERE Slug = 'career' AND (Description IS NULL OR Description = '');

UPDATE Category SET Description = 'Build pipelines, containers, cloud infrastructure and running things in production.'
WHERE Slug = 'devops' AND (Description IS NULL OR Description = '');

UPDATE Category SET Description = 'Industry news, tooling and everything that does not fit a narrower box.'
WHERE Slug = 'technology' AND (Description IS NULL OR Description = '');


-- ============================================================================
-- PART C: BLOG SERIES  [REQ-FN-019]
-- Two series so /series lists more than one card and /series/{slug} has real
-- ordered parts with working previous/next navigation.
-- ============================================================================
INSERT INTO BlogSeries (Name, Slug, Description, CreatedOn, IsActive, AuthorId, Status)
SELECT
    'Blazor Server in Production',
    'blazor-server-in-production',
    'A four-part walk through everything that changes once a Blazor Server app leaves your laptop: render modes, circuit state, SignalR scale-out and the telemetry that tells you it is healthy.',
    NOW() - INTERVAL '46 days',
    TRUE,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'In Progress'
WHERE NOT EXISTS (SELECT 1 FROM BlogSeries WHERE Slug = 'blazor-server-in-production');

INSERT INTO BlogSeries (Name, Slug, Description, CreatedOn, IsActive, AuthorId, Status)
SELECT
    'PostgreSQL for .NET Developers',
    'postgres-for-dotnet-developers',
    'Two focused parts on the PostgreSQL knowledge a .NET developer actually needs: how indexes are chosen, and how to read the plan the planner hands back.',
    NOW() - INTERVAL '25 days',
    TRUE,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'author@techieblog.test'),
    'Completed'
WHERE NOT EXISTS (SELECT 1 FROM BlogSeries WHERE Slug = 'postgres-for-dotnet-developers');


-- ============================================================================
-- PART D: SAMPLE POSTS  [REQ-FN-041]
--
-- Ten posts. The three posts that may already exist on a developer database
-- (getting-started-with-blazor-server, dapper-patterns-that-scale,
-- theming-with-css-custom-properties) are NOT touched - every INSERT is keyed
-- on its own slug, so nothing is duplicated or rewritten.
--
-- The Tags column is the free-text list the post page and cards read; its FIRST
-- entry doubles as the displayed category (PostView.GetCategory), so it always
-- starts with the category name. Only tag names whose UI slug matches the Tag
-- table slug are listed there, so every chip links to a live archive.
-- Body text is dollar-quoted ($md$) because DbUp runs with variable
-- substitution disabled (BlogDbSvc.UpgradeDatabase), which makes $...$ safe.
-- ============================================================================

-- --- Series A, part 1 ------------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeriesId, SeriesPartNumber,
    SeoTitle, SeoDescription
)
SELECT
    'Blazor Render Modes Explained',
    'blazor-render-modes-explained',
    'Server, WebAssembly, Auto and static SSR - what each render mode actually costs you, and how to pick one on purpose.',
    $md$## Four modes, one component model

.NET unified the Blazor hosting models behind a single component model, which is
wonderful right up to the moment you have to choose one. Here is the short version.

| Mode | First paint | Interactivity | Where state lives |
| --- | --- | --- | --- |
| Static SSR | Fastest | None | Nowhere |
| Server | Fast | Over SignalR | Server memory |
| WebAssembly | Slow first visit | Local | Browser |
| Auto | Fast, then local | Server then WASM | Both |

### Static server rendering

Static SSR renders the component once and ships plain HTML. No circuit, no
download, no interactivity. For a blog listing page this is exactly right.

```razor
@attribute [RenderModeStatic]

<ul>
    @foreach (var post in Posts)
    {
        <li><a href="/post/@post.Slug">@post.Title</a></li>
    }
</ul>
```

### Interactive server

Add a circuit and the component becomes interactive. Every event round-trips to
the server, and the server holds your state between renders.

```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
```

> The circuit is the unit of failure. If you cannot describe what happens to a
> page when its circuit drops, you have not finished designing the page.

### Picking one

- **Content pages** - static SSR. There is nothing to interact with.
- **Admin screens behind a login** - interactive server. The latency is fine on
  a LAN and you keep full .NET on the server.
- **Offline or high-latency clients** - WebAssembly, and budget for the download.
- **Not sure yet** - Auto, and measure before you commit.

Read the [official render modes documentation](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes)
for the full matrix, then come back for [part two](/post/blazor-circuits-and-state),
where the circuit stops being an implementation detail.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Web Development,Blazor,Tutorial',
    (SELECT CategoryId FROM Category WHERE Slug = 'web-development'),
    '/_content/BlogUI/images/HomeBg.jpg',
    NOW() - INTERVAL '45 days', TRUE, NOW() - INTERVAL '45 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'blazor-server-in-production'), 1,
    'Blazor Render Modes Explained - Server, WebAssembly, Auto and Static SSR',
    'A practical comparison of the four Blazor render modes, with guidance on which to choose for content pages, admin screens and offline clients.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'blazor-render-modes-explained');

-- --- Series A, part 2 ------------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeriesId, SeriesPartNumber,
    SeoTitle, SeoDescription
)
SELECT
    'Circuits, State and Reconnection in Blazor Server',
    'blazor-circuits-and-state',
    'What a circuit really holds, why a dropped connection is not a lost session, and how to keep state that survives both.',
    $md$## A circuit is a conversation

A Blazor Server circuit is the server-side object graph backing one browser tab.
It holds your component instances, their fields, and the render tree used to
diff the next update. It does **not** hold anything the browser owns.

### What lives where

1. **Circuit memory** - component fields, injected scoped services, cascading values.
2. **Browser memory** - form input the user is typing, scroll position, focus.
3. **Durable storage** - anything you cannot afford to lose.

The mistake is assuming category two and three are the same as category one.

```csharp
public sealed class DraftState
{
    private readonly ProtectedLocalStorage storage;

    public DraftState(ProtectedLocalStorage storage) => this.storage = storage;

    public ValueTask SaveAsync(string key, string body)
        => storage.SetAsync(key, body);
}
```

### Reconnection

When the WebSocket drops, Blazor shows the reconnect overlay and tries to
reattach to the *same* circuit. If the server still has it, the page resumes
mid-edit. If the server recycled, restarted or evicted it, the user gets a full
reload and everything in circuit memory is gone.

> Treat circuit memory as a cache with an unpredictable eviction policy, because
> that is precisely what it is.

### Practical rules

- Persist anything a user typed to `ProtectedLocalStorage` on a debounce.
- Keep per-circuit memory small; you pay for it per open tab.
- Set `DisconnectedCircuitRetentionPeriod` deliberately instead of accepting the default.
- Never store a `DbContext` or an open connection in a component field.

Next: [scaling SignalR](/post/scaling-signalr-for-blazor-server), where one
server stops being enough.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Web Development,Blazor,Architecture',
    (SELECT CategoryId FROM Category WHERE Slug = 'web-development'),
    '/_content/BlogUI/images/Aboutbg.jpg',
    NOW() - INTERVAL '38 days', TRUE, NOW() - INTERVAL '38 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'blazor-server-in-production'), 2,
    'Circuits, State and Reconnection in Blazor Server',
    'Understand what a Blazor Server circuit holds, what happens on reconnection, and where to keep state so a dropped connection never loses a users work.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'blazor-circuits-and-state');

-- --- Series A, part 3 ------------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeriesId, SeriesPartNumber,
    SeoTitle, SeoDescription
)
SELECT
    'Scaling SignalR for Blazor Server',
    'scaling-signalr-for-blazor-server',
    'Sticky sessions, backplanes, connection budgets and the load test that tells you which one you actually need.',
    $md$## The scale-out question

Blazor Server keeps one long-lived connection per open tab. That single fact
drives every scaling decision you will make.

![Server room](/_content/BlogUI/images/SpeakingBg.jpg)

### Sticky sessions come first

A circuit lives on exactly one server. If your load balancer moves a client
mid-session, the circuit is gone. Turn on session affinity before you tune
anything else.

```yaml
# Azure Container Apps
ingress:
  stickySessions:
    affinity: sticky
```

### Do you need a backplane?

A Redis backplane distributes SignalR *hub messages* between servers. Blazor
Server circuits are not shared between servers, so a backplane does **not**
let a circuit roam. You need one when you push server-initiated updates to
clients that may be connected anywhere:

- Live dashboards fed by a background service
- Notification fan-out
- Presence and typing indicators

If none of those apply, skip it.

### Budgeting connections

| Concurrent tabs | Memory per circuit | Rough server memory |
| --- | --- | --- |
| 500 | 250 KB | ~125 MB |
| 2 000 | 250 KB | ~500 MB |
| 10 000 | 250 KB | ~2.5 GB |

Measure your own number with `dotnet-counters`; 250 KB is a starting guess, not
a promise.

> Load test with real browsers, not with a script that opens sockets. The render
> tree diff is the expensive part, and only a real client produces it.

Part four covers the telemetry that makes all of this visible.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Web Development,Blazor,Performance',
    (SELECT CategoryId FROM Category WHERE Slug = 'web-development'),
    '/_content/BlogUI/images/SpeakingBg.jpg',
    NOW() - INTERVAL '31 days', TRUE, NOW() - INTERVAL '31 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'blazor-server-in-production'), 3,
    'Scaling SignalR for Blazor Server',
    'Sticky sessions, Redis backplanes and per-circuit memory budgets - how to scale a Blazor Server app past one machine without losing circuits.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'scaling-signalr-for-blazor-server');

-- --- Series A, part 4: scheduled, renders as "Coming Soon" on the series page
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, ScheduledPublishOn, IsDeleted,
    SeriesId, SeriesPartNumber, SeoTitle, SeoDescription
)
SELECT
    'Observability for Blazor Server',
    'observability-for-blazor-server',
    'Circuit metrics, structured logs and traces that survive a production incident at 3am.',
    $md$## Coming soon

The final part of this series covers the telemetry that turns "the site feels
slow" into a number you can act on:

- Circuit open, closed and evicted counters
- Render batch duration histograms
- Correlating a SignalR disconnect with the request that caused it
- Serilog enrichers that carry the circuit id into every log line

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("Microsoft.AspNetCore.Components.Server.Circuits"));
```

Subscribe to be notified when it publishes.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Web Development,Blazor,Best Practices',
    (SELECT CategoryId FROM Category WHERE Slug = 'web-development'),
    '/_content/BlogUI/images/HomeBg.jpg',
    NOW() - INTERVAL '2 days', FALSE, NULL, NOW() + INTERVAL '14 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'blazor-server-in-production'), 4,
    'Observability for Blazor Server',
    'Circuit metrics, structured logging and distributed traces for a Blazor Server application in production.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'observability-for-blazor-server');

-- --- Series B, part 1 ------------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeriesId, SeriesPartNumber,
    SeoTitle, SeoDescription
)
SELECT
    'Indexing Basics for .NET Developers',
    'postgres-indexing-for-dotnet-developers',
    'B-tree, partial and expression indexes explained through the queries a Dapper repository actually issues.',
    $md$## Indexes are for predicates, not for tables

The single most useful reframing: you do not index a table, you index the
*shape of a predicate*. Start from the SQL your repository emits.

```csharp
const string sql = @"
    SELECT PostId, Title, Slug
    FROM BlogPost
    WHERE Published = TRUE AND IsDeleted = FALSE
    ORDER BY CreatedOn DESC
    LIMIT @PageSize OFFSET @Offset";
```

That query wants one index:

```sql
CREATE INDEX IdxBlogPostPublished
    ON BlogPost (Published, CreatedOn DESC);
```

### Partial indexes

When a predicate is nearly always the same constant, push it into the index and
shrink it dramatically.

```sql
CREATE UNIQUE INDEX IdxBlogUserUserName
    ON BlogUser (UserName)
    WHERE UserName IS NOT NULL;
```

### Expression indexes

Case-insensitive lookups need the expression indexed, not the column.

```sql
CREATE UNIQUE INDEX IdxVerifiedEmailEmail
    ON VerifiedEmail (LOWER(Email));
```

Query it the same way or the index is ignored:

- `WHERE LOWER(Email) = LOWER(@Email)` - index used
- `WHERE Email ILIKE @Email` - sequential scan

> Every index you add is paid for on every write. Add them because a plan told
> you to, not because a column "looks searchable".

### A checklist

1. Find the slow query.
2. Read its plan (that is [part two](/post/reading-postgres-query-plans)).
3. Add the narrowest index that removes the sequential scan.
4. Re-measure. Delete the index if nothing improved.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'author@techieblog.test'),
    'Programming,PostgreSQL,Database',
    (SELECT CategoryId FROM Category WHERE Slug = 'programming'),
    '/_content/BlogUI/images/Podcastbg.jpg',
    NOW() - INTERVAL '24 days', TRUE, NOW() - INTERVAL '24 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'postgres-for-dotnet-developers'), 1,
    'PostgreSQL Indexing Basics for .NET Developers',
    'B-tree, partial and expression indexes explained through the SQL a Dapper repository actually issues, with a four-step tuning checklist.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'postgres-indexing-for-dotnet-developers');

-- --- Series B, part 2 ------------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeriesId, SeriesPartNumber,
    SeoTitle, SeoDescription
)
SELECT
    'Reading PostgreSQL Query Plans',
    'reading-postgres-query-plans',
    'EXPLAIN ANALYZE from the top down: which numbers matter, which are noise, and the three shapes that mean trouble.',
    $md$## Read the plan from the inside out

`EXPLAIN (ANALYZE, BUFFERS)` prints a tree. Execution starts at the deepest
node and bubbles upward, so read it bottom-up even though it prints top-down.

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT p.PostId, p.Title
FROM BlogPost p
JOIN PostTag pt ON pt.PostId = p.PostId
WHERE pt.TagId = 3 AND p.Published = TRUE
ORDER BY p.CreatedOn DESC
LIMIT 10;
```

### The numbers that matter

- **actual time** - real milliseconds. `cost` is a unitless guess; ignore it.
- **rows** vs **actual rows** - a large gap means the statistics are stale.
- **loops** - the per-row cost is `actual time x loops`, which is easy to misread.
- **Buffers: shared read** - pages fetched from disk rather than cache.

### Three shapes that mean trouble

1. **Seq Scan on a large table with a selective filter.** A missing index, or a
   predicate written so the index cannot be used.
2. **Nested Loop with thousands of loops.** The planner expected a handful of
   rows and got thousands; run `ANALYZE`.
3. **Sort spilling to disk.** Visible as `Sort Method: external merge`. Either
   raise `work_mem` for that session or index the sort order.

> A plan is a hypothesis about your data. When the plan is wrong, the fix is
> usually better statistics, not a bigger machine.

### Making it a habit

Add a `LogSlowQuery` hook in development and print the plan for anything over
200 ms. You will find the problems long before a user does.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'author@techieblog.test'),
    'Programming,PostgreSQL,Performance',
    (SELECT CategoryId FROM Category WHERE Slug = 'programming'),
    '/_content/BlogUI/images/Aboutbg.jpg',
    NOW() - INTERVAL '17 days', TRUE, NOW() - INTERVAL '17 days', FALSE,
    (SELECT SeriesId FROM BlogSeries WHERE Slug = 'postgres-for-dotnet-developers'), 2,
    'Reading PostgreSQL Query Plans with EXPLAIN ANALYZE',
    'How to read an EXPLAIN ANALYZE tree, which numbers matter, and the three plan shapes that reliably indicate a performance problem.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'reading-postgres-query-plans');

-- --- Standalone: the Markdown showcase -------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeoTitle, SeoDescription
)
SELECT
    'The Markdown Kitchen Sink',
    'the-markdown-kitchen-sink',
    'Every Markdown construct this site renders, on one page - headings, lists, tables, quotes, images, links and fenced code.',
    $md$# The Markdown Kitchen Sink

This post exists to prove the renderer. If something here looks wrong, the
Markdown pipeline needs attention.

## Headings

### Third level

#### Fourth level

## Emphasis and inline code

Text can be *italic*, **bold**, ***both***, ~~struck through~~ or `inline code`.
A [link to the about page](/about) and an
[external link](https://learn.microsoft.com/dotnet/) should both render.

## Lists

An unordered list:

- First item
- Second item
  - A nested item
  - Another nested item
- Third item

An ordered list:

1. Clone the repository
2. Start PostgreSQL
3. Run the app - migrations apply themselves at startup

A task list:

- [x] Seed sample data
- [x] Hash every seeded credential
- [ ] Write the release notes

## Blockquote

> The best documentation is the sample database that ships with the product.
>
> - Every developer who has ever evaluated a CMS

## Table

| Component | Package | Notes |
| --- | --- | ---: |
| Data access | Dapper | 2.1.35 |
| Migrations | DbUp | 6.0.3 |
| Logging | Serilog | Structured |
| Markdown | Markdig | Advanced pipeline |

## Image

![TechieBlog logo](/_content/BlogUI/images/FullLogo.png)

## Fenced code

```csharp
public sealed record PostSummary(long PostId, string Title, string Slug)
{
    public string Url => $"/post/{Slug}";
}
```

```sql
SELECT t.TagName, COUNT(pt.PostId) AS PostCount
FROM Tag t
LEFT JOIN PostTag pt ON pt.TagId = t.TagId
GROUP BY t.TagId, t.TagName
ORDER BY PostCount DESC;
```

```bash
docker exec techieblog-pg psql -U PgVectorAdmin -d TechieBlog -c "SELECT COUNT(*) FROM BlogPost;"
```

## Horizontal rule

---

That is the whole vocabulary.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Technology,Tutorial,Best Practices',
    (SELECT CategoryId FROM Category WHERE Slug = 'technology'),
    '/_content/BlogUI/images/FullLogo.png',
    NOW() - INTERVAL '10 days', TRUE, NOW() - INTERVAL '10 days', FALSE,
    'The Markdown Kitchen Sink - Every Construct TechieBlog Renders',
    'A single page exercising every Markdown construct the site renders: headings, emphasis, lists, tables, blockquotes, images, links and fenced code blocks.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'the-markdown-kitchen-sink');

-- --- Standalone: DevOps ----------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeoTitle, SeoDescription
)
SELECT
    'Shipping .NET with Docker and GitHub Actions',
    'shipping-dotnet-with-docker-and-github-actions',
    'A small, honest pipeline: build once, test the artefact you built, and promote the same image to production.',
    $md$## Build once, promote everywhere

The cardinal rule of a deployment pipeline is that the artefact you tested is
the artefact you ship. Everything below follows from it.

### The Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish source/TechieBlog/TechieBlog.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "TechieBlog.dll"]
```

### The workflow

```yaml
name: build
on:
  push:
    branches: [ main ]

jobs:
  container:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: docker build -t techieblog:${{ github.sha }} .
      - run: docker run --rm techieblog:${{ github.sha }} --version
```

### Things worth doing early

1. Tag images with the commit SHA, never with `latest`.
2. Run migrations from the application at startup, so a rollback of the image is
   a rollback of the schema expectation too.
3. Keep secrets in the platform's secret store; a connection string in an
   environment file will eventually be committed.
4. Fail the build on a failing smoke test, not on a warning count.

> A pipeline nobody trusts gets bypassed. Make it fast enough that bypassing it
> is never tempting.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'editor@techieblog.test'),
    'DevOps,Docker,Azure',
    (SELECT CategoryId FROM Category WHERE Slug = 'devops'),
    '/_content/BlogUI/images/SpeakingBg.jpg',
    NOW() - INTERVAL '6 days', TRUE, NOW() - INTERVAL '6 days', FALSE,
    'Shipping .NET with Docker and GitHub Actions',
    'A minimal build-once-promote-everywhere pipeline for a .NET application using Docker and GitHub Actions, with the practices worth adopting on day one.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'shipping-dotnet-with-docker-and-github-actions');

-- --- Standalone: Career ----------------------------------------------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeoTitle, SeoDescription
)
SELECT
    'Writing a Technical Talk That Lands',
    'writing-a-technical-talk-that-lands',
    'One idea, three demos, zero bullet-point walls - how to build a conference talk people remember on the train home.',
    $md$## Start from the one sentence

Before you open a slide editor, write the sentence you want an attendee to
repeat to a colleague a week later. If you cannot write it, you do not have a
talk yet - you have a topic.

### The shape that works

1. **The problem, felt.** Two minutes of something the audience has lived.
2. **The idea.** One sentence, on one slide.
3. **Three demonstrations.** Each one earns the idea a little more trust.
4. **The catch.** What this approach costs. Skipping this loses the room.
5. **The one sentence again.**

### Demos

- Record a backup video. The Wi-Fi will fail.
- Type nothing live that takes more than fifteen seconds.
- Increase the font size until it looks absurd on your laptop.

> Nobody has ever complained that a code font was too large.

### Slides

Slides are a backdrop, not a document. If your slide is readable as a handout,
it is unreadable as a slide. Write the handout separately and link to it.

### Rehearsal

Run it three times: once alone out loud, once for a friendly colleague, once at
a meetup. The third run is where a talk stops being a draft.
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'),
    'Career,Conference,Best Practices',
    (SELECT CategoryId FROM Category WHERE Slug = 'career'),
    '/_content/BlogUI/images/SpeakingBg.jpg',
    NOW() - INTERVAL '3 days', TRUE, NOW() - INTERVAL '3 days', FALSE,
    'Writing a Technical Talk That Lands',
    'How to structure a conference talk around a single idea and three demonstrations, plus the rehearsal and demo practices that keep it from falling apart.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'writing-a-technical-talk-that-lands');

-- --- Contributor draft: exercises the submit-for-review path ----------------
INSERT INTO BlogPost (
    Title, Slug, Abstract, PostContent, UserId, Tags, CategoryId, FeaturedImage,
    CreatedOn, Published, PublishedOn, IsDeleted, SeoTitle, SeoDescription
)
SELECT
    'Testing Dapper Repositories Without a Database',
    'testing-dapper-repositories-without-a-database',
    'Draft awaiting editorial review - a look at where the seam belongs when your data access is raw SQL.',
    $md$## Draft - submitted for review

Raw SQL is not a mockable interface, which is exactly why Dapper repositories
are pleasant to work with and awkward to unit test.

### Where the seam belongs

Put the seam at the repository boundary, not inside it. A repository is thin
enough to be covered by an integration test against a real PostgreSQL container,
and everything above it can be tested against the repository interface.

```csharp
public interface IBlogPostRepo
{
    BlogPost GetBySlug(string slug);
}
```

### What to test where

- **Service logic** - unit tests, repository interface substituted.
- **SQL correctness** - integration tests against a throwaway container.
- **Migrations** - apply them to an empty database in CI and assert the schema.

*Editor note: needs a worked example and a section on Testcontainers before this
can be published.*
$md$,
    (SELECT UserId FROM BlogUser WHERE LOWER(EmailId) = 'contributor@techieblog.test'),
    'Programming,C#,Best Practices',
    (SELECT CategoryId FROM Category WHERE Slug = 'programming'),
    NULL,
    NOW() - INTERVAL '1 day', FALSE, NULL, FALSE,
    'Testing Dapper Repositories Without a Database',
    'Where to place the testing seam when your data access layer is raw SQL executed through Dapper.'
WHERE NOT EXISTS (SELECT 1 FROM BlogPost WHERE Slug = 'testing-dapper-repositories-without-a-database');


-- ============================================================================
-- PART E: JUNCTION ROWS  [REQ-FN-017, REQ-FN-018]
--
-- PostCategory mirrors BlogPost.CategoryId, and PostTag drives the tag cloud
-- and every /tag/{slug} archive. Both tables have a composite primary key, so
-- ON CONFLICT DO NOTHING makes the inserts replay-safe.
--
-- Only the ten posts seeded by THIS script are linked; any post created by an
-- operator or by another script keeps whatever links it already has.
-- ============================================================================
INSERT INTO PostCategory (PostId, CategoryId)
SELECT p.PostId, p.CategoryId
FROM BlogPost p
WHERE p.CategoryId IS NOT NULL
  AND p.Slug IN (
    'blazor-render-modes-explained',
    'blazor-circuits-and-state',
    'scaling-signalr-for-blazor-server',
    'observability-for-blazor-server',
    'postgres-indexing-for-dotnet-developers',
    'reading-postgres-query-plans',
    'the-markdown-kitchen-sink',
    'shipping-dotnet-with-docker-and-github-actions',
    'writing-a-technical-talk-that-lands',
    'testing-dapper-repositories-without-a-database')
ON CONFLICT DO NOTHING;

-- PostTag: one statement per post, listing tag slugs from 008-SeedTagsAndPostTags.sql.
INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'blazor-render-modes-explained'
  AND t.Slug IN ('blazor', 'dotnet', 'tutorial', 'aspnet-core')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'blazor-circuits-and-state'
  AND t.Slug IN ('blazor', 'architecture', 'dotnet')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'scaling-signalr-for-blazor-server'
  AND t.Slug IN ('blazor', 'performance', 'aspnet-core', 'azure')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'observability-for-blazor-server'
  AND t.Slug IN ('blazor', 'best-practices')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'postgres-indexing-for-dotnet-developers'
  AND t.Slug IN ('postgresql', 'database', 'dotnet', 'tutorial')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'reading-postgres-query-plans'
  AND t.Slug IN ('postgresql', 'database', 'performance')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'the-markdown-kitchen-sink'
  AND t.Slug IN ('tutorial', 'best-practices', 'fluentui')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'shipping-dotnet-with-docker-and-github-actions'
  AND t.Slug IN ('docker', 'azure', 'dotnet', 'security')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'writing-a-technical-talk-that-lands'
  AND t.Slug IN ('conference', 'best-practices')
ON CONFLICT DO NOTHING;

INSERT INTO PostTag (PostId, TagId)
SELECT p.PostId, t.TagId
FROM BlogPost p CROSS JOIN Tag t
WHERE p.Slug = 'testing-dapper-repositories-without-a-database'
  AND t.Slug IN ('csharp', 'database', 'best-practices')
ON CONFLICT DO NOTHING;


-- ============================================================================
-- PART F: ANONYMOUS ENGAGEMENT  [REQ-FN-022, REQ-FN-023]
--
-- Comments and ratings are keyed to a NAME + EMAIL, not to an account
-- (BRD-36/40 revised, 014-AnonymousEngagement.sql). Seeded rows are already
-- past the double opt-in, so they display:
--   comments  ModerationStatus = 'Approved', Published = TRUE, IsEmailVerified = TRUE
--   ratings   IsEmailVerified = TRUE   (PostRatingRepo averages verified rows only)
--
-- Addresses use example.com, reserved for documentation by RFC 2606, so no
-- real mailbox can ever be contacted by seeded data.
-- ============================================================================

-- --- Verified identities ---------------------------------------------------
INSERT INTO VerifiedEmail (Email, DisplayName, VerifiedOn, LastUsedOn)
SELECT v.Email, v.DisplayName, NOW() - INTERVAL '40 days', NOW() - INTERVAL '5 days'
FROM (VALUES
    ('dana.wells@example.com',   'Dana Wells'),
    ('samir.iqbal@example.com',  'Samir Iqbal'),
    ('lena.fischer@example.com', 'Lena Fischer'),
    ('tomas.novak@example.com',  'Tomas Novak'),
    ('grace.oduya@example.com',  'Grace Oduya'),
    ('hiro.tanaka@example.com',  'Hiro Tanaka'),
    ('nina.petrov@example.com',  'Nina Petrov')
) AS v(Email, DisplayName)
WHERE NOT EXISTS (
    SELECT 1 FROM VerifiedEmail ex WHERE LOWER(ex.Email) = LOWER(v.Email)
);

-- --- Comments: top level ---------------------------------------------------
-- One row per (post, email) pair, which is also the idempotency key.
INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '44 days', 'Dana Wells', 'dana.wells@example.com',
    'The static SSR versus interactive server table finally made this click for me. We had every page interactive by default and wondered why memory climbed all day.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '44 days'
FROM BlogPost p
WHERE p.Slug = 'blazor-render-modes-explained'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'dana.wells@example.com'
);

INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '43 days', 'Samir Iqbal', 'samir.iqbal@example.com',
    'Does Auto mode make sense for an admin area that is only ever used on a fast internal network? Feels like paying the download cost for nothing.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '43 days'
FROM BlogPost p
WHERE p.Slug = 'blazor-render-modes-explained'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'samir.iqbal@example.com'
);

INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '29 days', 'Lena Fischer', 'lena.fischer@example.com',
    'The point about backplanes not letting circuits roam is the thing everyone gets wrong. We added Redis and were baffled that failover still dropped sessions.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '29 days'
FROM BlogPost p
WHERE p.Slug = 'scaling-signalr-for-blazor-server'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'lena.fischer@example.com'
);

INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '22 days', 'Tomas Novak', 'tomas.novak@example.com',
    'Partial indexes were the missing piece for us. One WHERE clause moved into the index definition and the table shrank by two thirds.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '22 days'
FROM BlogPost p
WHERE p.Slug = 'postgres-indexing-for-dotnet-developers'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'tomas.novak@example.com'
);

INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '9 days', 'Grace Oduya', 'grace.oduya@example.com',
    'Bookmarking this as the reference for what our renderer is expected to handle. The nested list and task list cases catch most pipelines out.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '9 days'
FROM BlogPost p
WHERE p.Slug = 'the-markdown-kitchen-sink'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'grace.oduya@example.com'
);

INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn
)
SELECT
    p.PostId, NOW() - INTERVAL '2 days', 'Hiro Tanaka', 'hiro.tanaka@example.com',
    'The catch slide is the part I always skip and always regret. Adding it to the next talk.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '2 days'
FROM BlogPost p
WHERE p.Slug = 'writing-a-technical-talk-that-lands'
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'hiro.tanaka@example.com'
);

-- --- Comment: one threaded reply ------------------------------------------
-- Answers Samir Iqbal on the render-modes post, so the reply rendering has data.
INSERT INTO BlogComment (
    PostId, GivenOn, GivenBy, Email, Comment, Published,
    IsEmailVerified, ModerationStatus, VerifiedOn, ParentCommentId
)
SELECT
    p.PostId, NOW() - INTERVAL '42 days', 'Nina Petrov', 'nina.petrov@example.com',
    'Agreed - on a fast internal network plain interactive server is simpler and the state stays where your data is. Auto earns its keep when clients are remote.',
    TRUE, TRUE, 'Approved', NOW() - INTERVAL '42 days',
    (SELECT c.CommentId FROM BlogComment c
     WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'samir.iqbal@example.com'
     ORDER BY c.CommentId LIMIT 1)
FROM BlogPost p
WHERE p.Slug = 'blazor-render-modes-explained'
  AND EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'samir.iqbal@example.com'
  )
  AND NOT EXISTS (
    SELECT 1 FROM BlogComment c
    WHERE c.PostId = p.PostId AND LOWER(c.Email) = 'nina.petrov@example.com'
);

-- --- Ratings ---------------------------------------------------------------
-- IdxPostRatingPostEmail makes (PostId, LOWER(Email)) unique for non-null
-- emails; the NOT EXISTS guard keeps a replay from violating it.
INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '40 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('dana.wells@example.com',   5::SMALLINT),
    ('samir.iqbal@example.com',  4::SMALLINT),
    ('nina.petrov@example.com',  5::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'blazor-render-modes-explained'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '35 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('lena.fischer@example.com', 5::SMALLINT),
    ('tomas.novak@example.com',  4::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'blazor-circuits-and-state'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '28 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('lena.fischer@example.com', 5::SMALLINT),
    ('grace.oduya@example.com',  4::SMALLINT),
    ('hiro.tanaka@example.com',  4::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'scaling-signalr-for-blazor-server'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '20 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('tomas.novak@example.com', 5::SMALLINT),
    ('nina.petrov@example.com', 5::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'postgres-indexing-for-dotnet-developers'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '15 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('dana.wells@example.com', 4::SMALLINT),
    ('samir.iqbal@example.com', 5::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'reading-postgres-query-plans'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '8 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('grace.oduya@example.com',  5::SMALLINT),
    ('hiro.tanaka@example.com',  5::SMALLINT),
    ('lena.fischer@example.com', 4::SMALLINT),
    ('dana.wells@example.com',   5::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'the-markdown-kitchen-sink'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '4 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('samir.iqbal@example.com', 4::SMALLINT),
    ('nina.petrov@example.com', 5::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'shipping-dotnet-with-docker-and-github-actions'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);

INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn, Email, IsEmailVerified)
SELECT p.PostId, NULL, r.Rating, NOW() - INTERVAL '2 days', r.Email, TRUE
FROM BlogPost p
CROSS JOIN (VALUES
    ('hiro.tanaka@example.com', 5::SMALLINT),
    ('grace.oduya@example.com', 4::SMALLINT)
) AS r(Email, Rating)
WHERE p.Slug = 'writing-a-technical-talk-that-lands'
  AND NOT EXISTS (
    SELECT 1 FROM PostRating ex
    WHERE ex.PostId = p.PostId AND LOWER(ex.Email) = LOWER(r.Email)
);


-- ============================================================================
-- PART G: SITE-OWNER RESUME DATA  [REQ-FN-028, REQ-FN-029]
--
-- The portfolio home page and /resume render from BlogUser (hero and contact),
-- UserEvents where Type = 'Experience' (timeline), UserSkills, UserAwards and
-- UserStats. Another agent may already have inserted some of these directly
-- into a running database, so every statement is guarded:
--   - profile columns use COALESCE, so an existing value is never overwritten
--   - child rows are keyed on (UserId, natural name)
--
-- CVFilePath is deliberately left to whatever is already stored; this script
-- does not invent a path to a PDF that is not in the repository.
-- ============================================================================
UPDATE BlogUser
SET Title              = COALESCE(NULLIF(Title, ''), 'Senior .NET Architect'),
    Tagline            = COALESCE(NULLIF(Tagline, ''),
                            'I build cloud-native products with .NET, Blazor and PostgreSQL, and write about what I learn on the way.'),
    Location           = COALESCE(NULLIF(Location, ''), 'Hyderabad, India'),
    PhoneNumber        = COALESCE(NULLIF(PhoneNumber, ''), '+91 98765 43210'),
    ProfileDescription = COALESCE(NULLIF(ProfileDescription, ''),
                            'Hands-on architect on the Microsoft stack: Blazor, ASP.NET Core, PostgreSQL and Azure. I spend my days turning tangled systems into ones a small team can actually operate, and my evenings writing about the parts that surprised me.'),
    LinkedInUrl        = COALESCE(NULLIF(LinkedInUrl, ''), 'https://www.linkedin.com/in/techierathore'),
    GitHubUrl          = COALESCE(NULLIF(GitHubUrl, ''), 'https://github.com/techierathore'),
    TwitterUrl         = COALESCE(NULLIF(TwitterUrl, ''), 'https://x.com/techierathore'),
    ProfileImagePath   = COALESCE(NULLIF(ProfileImagePath, ''),
                            '/uploads/profiles/profiles_1_20260103181729_4dc89d68.png'),
    ResumeEnabled      = TRUE,
    UpdatedOn          = COALESCE(UpdatedOn, NOW())
WHERE LOWER(EmailId) = 'ravi@techieblog.com';

-- Exactly one row may carry IsSiteOwner (IdxSingleSiteOwner is a partial unique
-- index), so only claim it when nobody else holds it.
UPDATE BlogUser
SET IsSiteOwner = TRUE
WHERE LOWER(EmailId) = 'ravi@techieblog.com'
  AND IsSiteOwner IS DISTINCT FROM TRUE
  AND NOT EXISTS (
    SELECT 1 FROM BlogUser other
    WHERE other.IsSiteOwner = TRUE AND LOWER(other.EmailId) <> 'ravi@techieblog.com'
  );

-- --- Skills ----------------------------------------------------------------
INSERT INTO UserSkills (UserId, Category, SkillName, DisplayOrder)
SELECT u.UserId, s.Category, s.SkillName, s.DisplayOrder
FROM BlogUser u
CROSS JOIN (VALUES
    ('Languages',        'C#',              1),
    ('Languages',        'SQL',             2),
    ('Languages',        'TypeScript',      3),
    ('Frameworks',       'ASP.NET Core',    4),
    ('Frameworks',       'Blazor',          5),
    ('Frameworks',       'Dapper',          6),
    ('Data',             'PostgreSQL',      7),
    ('Data',             'Redis',           8),
    ('Cloud and DevOps', 'Azure',           9),
    ('Cloud and DevOps', 'Docker',         10),
    ('Cloud and DevOps', 'GitHub Actions', 11),
    ('Practices',        'Domain modelling', 12),
    ('Practices',        'Observability',  13)
) AS s(Category, SkillName, DisplayOrder)
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserSkills ex
    WHERE ex.UserId = u.UserId AND ex.SkillName = s.SkillName
);

-- --- Experience timeline (UserEvents, Type = 'Experience') ------------------
-- NOTE: UserEvent.EventDate is a non-nullable DateTime on the model, so the
-- current role stores CURRENT_DATE as its end date; IsCurrent = TRUE is what
-- makes ResumeExperience render it as "Present".
INSERT INTO UserEvents (UserId, Type, EventTitle, SessionTitle, EventUrl, StartDate, EventDate, Description, DisplayOrder, IsCurrent)
SELECT
    u.UserId, 'Experience', 'Contoso Cloud', 'Principal Architect', 'https://example.com/contoso',
    DATE '2021-04-01', CURRENT_DATE,
    'Own the architecture of a multi-tenant .NET platform serving 40k daily users. Led the move from a single deployment to regional Azure Container Apps, cut p95 latency by 38 percent and introduced the observability stack the on-call rota now runs on.',
    1, TRUE
FROM BlogUser u
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserEvents ex
    WHERE ex.UserId = u.UserId AND ex.Type = 'Experience' AND ex.EventTitle = 'Contoso Cloud'
);

INSERT INTO UserEvents (UserId, Type, EventTitle, SessionTitle, EventUrl, StartDate, EventDate, Description, DisplayOrder, IsCurrent)
SELECT
    u.UserId, 'Experience', 'Northwind Systems', 'Lead Software Engineer', NULL,
    DATE '2016-08-01', DATE '2021-03-31',
    'Rebuilt an order pipeline that had grown into 200k lines of stored procedures. Introduced Dapper repositories, a migration-first schema workflow and the first automated test suite the team had ever had.',
    2, FALSE
FROM BlogUser u
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserEvents ex
    WHERE ex.UserId = u.UserId AND ex.Type = 'Experience' AND ex.EventTitle = 'Northwind Systems'
);

INSERT INTO UserEvents (UserId, Type, EventTitle, SessionTitle, EventUrl, StartDate, EventDate, Description, DisplayOrder, IsCurrent)
SELECT
    u.UserId, 'Experience', 'Adventure Works', 'Senior Developer', NULL,
    DATE '2012-01-01', DATE '2016-07-31',
    'Shipped the first ASP.NET MVC product in a WebForms shop, then spent two years helping four other teams do the same. Learned that migrations are a people problem wearing a technical costume.',
    3, FALSE
FROM BlogUser u
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserEvents ex
    WHERE ex.UserId = u.UserId AND ex.Type = 'Experience' AND ex.EventTitle = 'Adventure Works'
);

-- --- Awards ----------------------------------------------------------------
INSERT INTO UserAwards (UserId, AwardTitle, AwardDescription, AwardUrl, AwardYear, DisplayOrder)
SELECT u.UserId, a.AwardTitle, a.AwardDescription, a.AwardUrl, a.AwardYear, a.DisplayOrder
FROM BlogUser u
CROSS JOIN (VALUES
    ('Microsoft MVP - Developer Technologies',
     'Awarded for community contributions across .NET and Blazor: talks, articles and open-source samples.',
     'https://example.com/mvp', '2019 - 2026', 1),
    ('Speaker of the Year - Regional .NET Conference',
     'Voted by attendees across a twelve-session track for the talk on pragmatic architecture.',
     'https://example.com/speaker-award', '2024', 2),
    ('Open Source Contributor Award',
     'Recognised for sustained contributions to community data-access and UI component libraries.',
     NULL, '2022', 3)
) AS a(AwardTitle, AwardDescription, AwardUrl, AwardYear, DisplayOrder)
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserAwards ex
    WHERE ex.UserId = u.UserId AND ex.AwardTitle = a.AwardTitle
);

-- --- Stats -----------------------------------------------------------------
INSERT INTO UserStats (UserId, StatLabel, StatValue, StatCategory, DisplayOrder)
SELECT u.UserId, s.StatLabel, s.StatValue, s.StatCategory, s.DisplayOrder
FROM BlogUser u
CROSS JOIN (VALUES
    ('Years of experience', '20+',  'Career',    1),
    ('Articles published',  '200+', 'Writing',   2),
    ('Conference talks',    '45',   'Speaking',  3),
    ('Products shipped',    '12',   'Delivery',  4)
) AS s(StatLabel, StatValue, StatCategory, DisplayOrder)
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (
    SELECT 1 FROM UserStats ex
    WHERE ex.UserId = u.UserId AND ex.StatLabel = s.StatLabel
);
