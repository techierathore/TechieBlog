-- ============================================================================
-- Script: 015-NewsletterAndAnalytics.sql
-- Purpose: Newsletter publishing + public archive, per-subscriber unsubscribe
--          tokens, newsletter send history, and privacy-conscious post-view
--          tracking.
-- Author: flow-master (build phase, Cluster C)
-- Created: 2026-08-06
-- Requirements: REQ-FN-032 (newsletter send + unsubscribe link)
--               REQ-FN-034 (post view tracking, total and unique)
--               REQ-FN-035 (popular posts + engagement statistics)
--               REQ-FN-036 (admin dashboard counts)
--               REQ-FN-050 (newsletter publishing + public archive)
-- BRD: BRD-59, BRD-60, BRD-61, BRD-62, BRD-100, BRD-101
--
-- Changes:
--   PART A - Newsletter: publication columns (Slug, Summary, SentOn, IsPublic,
--            RecipientCount, UpdatedOn) + unique slug index + archive index.
--   PART B - SubscriberNewsletter: send-history columns (SendStatus,
--            ErrorMessage) + newsletter lookup index.
--   PART C - Subscriber: UnsubscribeToken column, per-row backfill, column
--            default so future inserts self-provision, unique index.
--   PART D - PostViews: VisitorHash column + de-duplication and aggregation
--            indexes.
--
-- Dependencies:
--   - 001-CreateTables.sql (Newsletter, SubscriberNewsletter, Subscriber,
--     PostViews tables)
--   - 004-FixPostTable.sql (Post renamed to BlogPost; PostViews FK re-pointed)
--
-- Idempotency: every statement uses IF NOT EXISTS or a guarded DO block, since
--              DbUp runs at every host startup and this script may be re-applied
--              against a partially migrated database.
--
-- Rollback:
--   ALTER TABLE Newsletter DROP COLUMN IF EXISTS Slug, DROP COLUMN IF EXISTS Summary,
--     DROP COLUMN IF EXISTS SentOn, DROP COLUMN IF EXISTS IsPublic,
--     DROP COLUMN IF EXISTS RecipientCount, DROP COLUMN IF EXISTS UpdatedOn;
--   ALTER TABLE SubscriberNewsletter DROP COLUMN IF EXISTS SendStatus,
--     DROP COLUMN IF EXISTS ErrorMessage;
--   ALTER TABLE Subscriber ALTER COLUMN UnsubscribeToken DROP DEFAULT;
--   ALTER TABLE Subscriber DROP COLUMN IF EXISTS UnsubscribeToken;
--   ALTER TABLE PostViews DROP COLUMN IF EXISTS VisitorHash;
--   DROP INDEX IF EXISTS IdxNewsletterSlug, IdxNewsletterSentOn,
--     IdxSubscriberNewsletterNewsletterId, IdxSubscriberUnsubscribeToken,
--     IdxPostViewsVisitorHash, IdxPostViewsPostIdVisitorHash;
-- ============================================================================

-- ============================================================================
-- PART A: Newsletter publication (REQ-FN-050, BRD-100/101)
-- Purpose: A sent issue becomes a public archive record addressed by slug.
--
-- Business Rules:
--   - Slug is assigned only at send time; a draft has no slug and is therefore
--     unreachable through the public /newsletter/{slug} route.
--   - IsPublic is the final gate: an issue may be sent privately (announcement
--     mail) without joining the archive.
--   - SentOn defines archive ordering and previous/next navigation.
--   - RecipientCount records how many subscribers the issue actually reached.
-- ============================================================================
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS Slug VARCHAR(300);
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS Summary VARCHAR(500);
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS SentOn TIMESTAMP;
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS IsPublic BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS RecipientCount INT NOT NULL DEFAULT 0;
ALTER TABLE Newsletter ADD COLUMN IF NOT EXISTS UpdatedOn TIMESTAMP;

-- Unique slug across published issues only; drafts (NULL slug) are exempt.
CREATE UNIQUE INDEX IF NOT EXISTS IdxNewsletterSlug
    ON Newsletter(Slug)
    WHERE Slug IS NOT NULL;

-- Archive listing and previous/next navigation both order by send time.
CREATE INDEX IF NOT EXISTS IdxNewsletterSentOn
    ON Newsletter(SentOn DESC);

-- ============================================================================
-- PART B: Newsletter send history (REQ-FN-032, BRD-59)
-- Purpose: One auditable row per delivery attempt, success or failure.
--
-- Business Rules:
--   - SendStatus is 'sent' or 'failed'; a failure keeps its SMTP error text so
--     the problem is logged rather than swallowed.
--   - The history is read back per newsletter, so NewsletterId needs an index
--     (SubscriberId already had one from script 001).
-- ============================================================================
ALTER TABLE SubscriberNewsletter ADD COLUMN IF NOT EXISTS SendStatus VARCHAR(20) NOT NULL DEFAULT 'sent';
ALTER TABLE SubscriberNewsletter ADD COLUMN IF NOT EXISTS ErrorMessage TEXT;

CREATE INDEX IF NOT EXISTS IdxSubscriberNewsletterNewsletterId
    ON SubscriberNewsletter(NewsletterId);

-- ============================================================================
-- PART C: Per-subscriber unsubscribe token (REQ-FN-032, BRD-59)
-- Purpose: Every newsletter message must carry a working unsubscribe link.
--
-- Business Rules:
--   - The token is opaque and per-subscriber, so a link cannot be used to
--     enumerate or unsubscribe anyone else.
--   - Existing rows are backfilled row by row (the UPDATE re-evaluates the
--     volatile expression per row, unlike an ADD COLUMN default).
--   - A DEFAULT is then attached so inserts made by SubscriberRepo, which does
--     not know about this column, still self-provision a token.
--   - md5() is used rather than sha256()/gen_random_uuid() so the script runs on
--     PostgreSQL versions before 11/13 and needs no pgcrypto extension.
-- ============================================================================
ALTER TABLE Subscriber ADD COLUMN IF NOT EXISTS UnsubscribeToken VARCHAR(64);

-- Backfill any row still missing a token (also repairs a partially applied run).
UPDATE Subscriber
SET UnsubscribeToken = md5(random()::text || clock_timestamp()::text || SubscriberId::text)
                    || md5(clock_timestamp()::text || random()::text || SubscriberId::text)
WHERE UnsubscribeToken IS NULL OR UnsubscribeToken = '';

-- Future inserts get a token without SubscriberRepo having to supply one.
ALTER TABLE Subscriber
    ALTER COLUMN UnsubscribeToken
    SET DEFAULT (md5(random()::text || clock_timestamp()::text)
              || md5(clock_timestamp()::text || random()::text));

CREATE UNIQUE INDEX IF NOT EXISTS IdxSubscriberUnsubscribeToken
    ON Subscriber(UnsubscribeToken)
    WHERE UnsubscribeToken IS NOT NULL;

-- ============================================================================
-- PART D: Post view tracking (REQ-FN-034/035, BRD-60/61)
-- Purpose: Wire the PostViews table, which has existed since script 001 with
--          nothing writing to it.
--
-- Business Rules:
--   - VisitorHash is SHA-256(siteSalt | ipAddress | userAgent) computed in the
--     application. It is the ONLY visitor identifier persisted; the legacy
--     ViewerIp column is deliberately left NULL by the tracker so no raw IP
--     address is stored.
--   - "Total views"  = number of rows for the post. The application writes at
--     most one row per visitor per post per de-duplication window (24 h by
--     default), so a refresh inside one reading session is not double counted.
--   - "Unique views" = number of distinct VisitorHash values for the post.
--   - IdxPostViewsPostIdVisitorHash serves both the de-duplication probe
--     (PostId + VisitorHash + recent ViewedOn) and the unique-count aggregate.
-- ============================================================================
ALTER TABLE PostViews ADD COLUMN IF NOT EXISTS VisitorHash VARCHAR(64);

CREATE INDEX IF NOT EXISTS IdxPostViewsVisitorHash
    ON PostViews(VisitorHash);

CREATE INDEX IF NOT EXISTS IdxPostViewsPostIdVisitorHash
    ON PostViews(PostId, VisitorHash, ViewedOn DESC);

-- ============================================================================
-- End of Migration
-- ============================================================================
