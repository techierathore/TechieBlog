-- ============================================================================
-- Script:  019-SampleData.sql
-- Purpose: Seeds the three non-admin STAFF ACCOUNTS the test harness signs in
--          as, and fills in empty category descriptions.
--
--          *** IT NO LONGER SEEDS ANY CONTENT. *** Posts, series, comments,
--          ratings and the site owner's resume rows were removed on 2026-08-22
--          at the owner's request — see the "PARTS C-G REMOVED" block at the
--          foot of this file for what went and why.
--
-- Author:  flow-master (Cluster H builder; trimmed 2026-08-22)
-- Created: 2026-08-07
--
-- Requirements:
--   REQ-FN-041 - PARTIALLY served, deliberately. The "one user per surviving
--                role" half is here; the sample posts / comments / ratings half
--                was retired by the owner (UAT-007). BRD-73 is narrowed to
--                match, not silently unmet.
--   REQ-NFR-023 - No credential is ever stored in plain text. Every seeded
--                password is a PBKDF2-HMAC-SHA256 hash produced by
--                BlogModels.PasswordHasher (same format as 003-SeedData.sql).
--
-- NOTE: REQ-FN-018 (junction rows), REQ-FN-022 (anonymous comments),
--       REQ-FN-023 (email-keyed ratings) and REQ-FN-028/029 (resume rows) were
--       all exercised by the removed parts. Those requirements are unchanged and
--       still covered by the application and its tests; they simply no longer
--       have seeded example rows on a fresh database.
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
-- PARTS C-G REMOVED  (2026-08-22, at the owner's request)
-- ============================================================================
-- This script used to continue with:
--
--   PART C  Blog series
--   PART D  Ten sample posts
--   PART E  PostCategory / PostTag junction rows for those posts
--   PART F  Anonymous comments, verified identities and ratings
--   PART G  Site-owner resume data: skills, experience, awards and the four
--           headline UserStats ("20+ years", "200+ articles", "45 talks",
--           "12 products shipped")
--
-- All of it was DEMO content written to exercise the build, and in owner UAT it
-- was mistaken for real data — the four UserStats figures in particular, which
-- read as claims about the site owner and were contradicted by his actual
-- public record. The owner deleted the rows from his database and asked that
-- they stop being seeded. They are gone from here rather than merely deleted
-- downstream, so a fresh clone never creates them in the first place.
--
-- ----------------------------------------------------------------------------
-- WHAT THIS MEANS FOR THE REQUIREMENTS THAT CITED THEM
-- ----------------------------------------------------------------------------
-- BRD-73 / REQ-FN-041 asked for an evaluation-ready sample set, and this script
-- no longer provides the content half of it. That is a deliberate, owner-made
-- narrowing, not an oversight: a template that seeds invented biography into a
-- real person's portfolio site is worse than a template that starts empty. What
-- survives here is the part that is genuinely infrastructural rather than
-- editorial:
--
--   PART A  the three staff accounts, one per surviving role. KEPT because the
--           UsageGuide test-user table, every smoke script and the verifier all
--           resolve credentials from them; removing them would break the test
--           harness, not just the sample data.
--   PART B  category descriptions, which only fill in a description that is
--           already empty and therefore cannot overwrite operator-authored text.
--
-- A fresh database now renders the empty states — which is the honest starting
-- point for a new site, and is exactly what the public pages were built to
-- handle (home-articles-empty, speaker-past-empty, and the rest).
--
-- ----------------------------------------------------------------------------
-- NOTE ON ALREADY-MIGRATED DATABASES
-- ----------------------------------------------------------------------------
-- DbUp journals by FILE NAME. Any database that already ran 019 has it recorded
-- and will not re-run it, so editing this file changes nothing for existing
-- installations — including production, where the owner has already deleted the
-- rows by hand. This edit governs FRESH databases only. Do not "fix" that by
-- adding a cleanup migration: deleting content on an existing site because a
-- seed script changed is exactly the kind of surprise a migration must not
-- spring on an operator who may have edited those rows into real ones.
-- ============================================================================
