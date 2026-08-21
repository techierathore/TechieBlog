-- ============================================================================
-- Script: 028-PostViewCountRollup.sql
-- Purpose: Give the per-post readership figures a maintained rollup row, so the
--          public /post/{slug} render reads them with a primary-key lookup
--          instead of aggregating the unbounded PostViews table on every hit.
-- Author: flow-master (build phase, Cluster B)
-- Created: 2026-08-11
-- Requirements: REQ-NFR-034 (PostViews INSERT + COUNT(DISTINCT) on every post
--               render — an unbounded per-render write and scan)
-- Depends on:   001-CreateTables.sql  (PostViews table)
--               004-FixPostTable.sql  (Post renamed to BlogPost; PostViews FK
--                                      re-pointed at BlogPost)
--               015-NewsletterAndAnalytics.sql (PostViews.VisitorHash column
--                                      and IdxPostViewsPostIdVisitorHash)
--
-- ----------------------------------------------------------------------------
-- WHY THIS EXISTS
-- ----------------------------------------------------------------------------
-- Statement-level query capture of a warm /post/{slug} render showed four
-- queries, two of them view tracking:
--
--   INSERT INTO PostViews (...) SELECT ... WHERE NOT EXISTS (...)   -- a WRITE
--   SELECT COUNT(*), COUNT(DISTINCT VisitorHash) FROM PostViews
--     WHERE PostId = $1                                             -- a SCAN
--
-- and EXPLAIN (ANALYZE) on the second one reported
--
--   Aggregate -> Sort (Sort Key: visitorhash) -> Seq Scan on postviews
--
-- PostViews holds a couple of dozen rows today, so the aggregate is free and
-- the site is NOT slow. That is precisely the problem: the cost is invisible
-- now and grows linearly with readership forever, because PostViews is an
-- append-only fact table with no retention policy. At a million rows the same
-- render still asks PostgreSQL to sort every visitor hash the post ever had.
--
-- The per-post RATING aggregates avoided this by being cached under
-- CacheTags.Content. View counts deliberately are not cached, because they have
-- to move — a reader who refreshes expects the number to have changed. So a
-- cache is the wrong instrument here and a MAINTAINED COUNTER is the right one.
--
-- ----------------------------------------------------------------------------
-- SHAPE CHOSEN, AND THE TWO SHAPES REJECTED
-- ----------------------------------------------------------------------------
-- CHOSEN — a one-row-per-post rollup table, PostViewCount, keyed by PostId.
--   The render reads it with `WHERE PostId = $1` against the primary key: an
--   index lookup of one row, constant work no matter how large PostViews grows.
--   The write path maintains it in the SAME statement that inserts the view, so
--   the counter cannot drift from its source without the insert also failing.
--
-- REJECTED — counter columns on BlogPost. Functionally equivalent, but it would
--   force every BlogPost projection (BlogPostRepo has several hand-written
--   column lists) to carry two more columns, widening the row every listing
--   page already reads in order to serve a number only the detail page shows.
--   A separate rollup keeps the hot listing queries exactly as narrow as they
--   are today.
--
-- REJECTED — a short-TTL cache in front of the existing aggregate. Cheapest to
--   write, but it does not remove the scan, it only makes it less frequent: the
--   Seq Scan still happens, now unpredictably, and it still grows without
--   bound. It also fights the row's own constraint that view counts must move.
--
-- WHY UNIQUE VIEWS CAN BE MAINTAINED INCREMENTALLY AT ALL. COUNT(DISTINCT ...)
-- is not incrementally maintainable in general — you cannot know whether "+1
-- row" means "+1 distinct visitor" without asking. The write path therefore
-- asks, with an EXISTS probe on (PostId, VisitorHash). That probe is NOT a
-- scan: IdxPostViewsPostIdVisitorHash (PostId, VisitorHash, ViewedOn DESC) from
-- script 015 already covers it exactly, so it is an index point lookup that
-- stops at the first matching tuple. One indexed probe on the (queued, off-
-- render) write path buys a primary-key lookup on the (synchronous, per-render)
-- read path. That is the trade this script makes, and it is stated here rather
-- than left for a reader to infer.
--
-- ----------------------------------------------------------------------------
-- CHANGES
-- ----------------------------------------------------------------------------
--   1. CREATE TABLE IF NOT EXISTS PostViewCount — PostId (PK, FK to BlogPost),
--      TotalViews, UniqueViews, UpdatedOn.
--   2. BACKFILL it from the existing PostViews rows, using the EXACT aggregate
--      expressions the old per-render query used, so the numbers the site shows
--      do not change by a single view at deployment. Without this every post
--      would read zero the moment the new code went live.
--   3. Re-runnable: the backfill is an UPSERT that re-syncs an existing row, so
--      applying the script twice converges on the same answer rather than
--      double-counting. DbUp journals it after the first run; the idempotency
--      is for the operator who has to repair drift by hand.
--
-- No index beyond the primary key is created: the only access paths are "one
-- row by PostId" (the PK) and "sum over all rows" (a bounded scan of one row
-- per post, orders of magnitude smaller than PostViews).
--
-- ----------------------------------------------------------------------------
-- ROLLBACK
-- ----------------------------------------------------------------------------
--   DROP TABLE IF EXISTS PostViewCount;
--
-- PostViews itself is neither altered nor pruned by this script, so the rollup
-- can always be rebuilt from it with the same SELECT used in step 2. Reverting
-- the code without dropping the table is also safe — the old aggregate query
-- ignores PostViewCount entirely.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. The rollup table
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS PostViewCount (
    -- The post these figures describe. Primary key AND foreign key: exactly one
    -- rollup row can exist per post, which is what makes the render's lookup a
    -- unique index probe rather than a scan-and-aggregate.
    PostId BIGINT PRIMARY KEY REFERENCES BlogPost(PostId) ON DELETE CASCADE,

    -- Rows in PostViews for this post, all time. Session-like, not hit-like:
    -- the tracker writes at most one PostViews row per visitor per window.
    TotalViews INTEGER NOT NULL DEFAULT 0,

    -- Distinct VisitorHash values for this post, all time. Bounded above by
    -- TotalViews; equal to it when every visitor read the post exactly once.
    UniqueViews INTEGER NOT NULL DEFAULT 0,

    -- When the counters last moved. Diagnostic only — nothing reads it to make
    -- a decision, but it is the first thing to look at if drift is suspected.
    UpdatedOn TIMESTAMP NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

-- ----------------------------------------------------------------------------
-- 2. Backfill from the existing view log
-- ----------------------------------------------------------------------------
-- COUNT(*) and COUNT(DISTINCT VisitorHash) are copied verbatim from the query
-- this rollup replaces (PostViewRepo.SelectCountsSql as it stood before this
-- change), INCLUDING the fact that COUNT(DISTINCT ...) skips NULL hashes. Using
-- the same expressions rather than a "better" one is deliberate: the acceptance
-- test for this migration is that no post's displayed numbers change, and a
-- COALESCE added here would silently bump the unique count of any post carrying
-- pre-015 rows that have no hash at all.
--
-- Posts with no views get NO row. GetCountsAsync returns a zeroed PostViewCounts
-- when the lookup misses, and the write path's UPSERT creates the row on the
-- first real view, so an absent row and a zero row mean the same thing.
INSERT INTO PostViewCount (PostId, TotalViews, UniqueViews, UpdatedOn)
SELECT v.PostId,
       COUNT(*)::int,
       COUNT(DISTINCT v.VisitorHash)::int,
       (now() AT TIME ZONE 'utc')
FROM PostViews v
GROUP BY v.PostId
ON CONFLICT (PostId) DO UPDATE
    SET TotalViews  = EXCLUDED.TotalViews,
        UniqueViews = EXCLUDED.UniqueViews,
        UpdatedOn   = EXCLUDED.UpdatedOn;
