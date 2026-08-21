-- ============================================================================
-- Script: 029-NormalizeSeriesStatus.sql
-- Purpose: Collapse BlogSeries.Status onto the two canonical literals held by
--          BlogModels.SeriesStatus ('In Progress', 'Completed') and stop any
--          other spelling from being stored again.
-- Author: flow-master (build phase, Cluster B)
-- Created: 2026-08-11
-- Requirements: REQ-UI-024 (Series list + manage series)
-- Depends on:   004-FixPostTable.sql (BlogSeries table),
--               007-FixBlogSeriesAndPostTag.sql (Status VARCHAR(50) DEFAULT
--               'In Progress'), 019-SampleData.sql (seeds SeriesId 2 as
--               'Completed')
--
-- ----------------------------------------------------------------------------
-- WHY THIS EXISTS
-- ----------------------------------------------------------------------------
-- Status is free text: no enum, no lookup table, no constraint. Two spellings of
-- "finished" therefore grew up on opposite sides of the wire. 019-SampleData.sql
-- seeds SeriesId 2 ('PostgreSQL for .NET Developers') as 'Completed', while the
-- C# side compared BlogSeries.IsComplete against the literal 'Complete' and the
-- admin editor's status picker wrote that same never-matching value. The result
-- was a completed series rendering as "In Progress" in the admin grid and the
-- Complete filter tab counting 0 while psql counted 1 — a completed series was
-- invisible under its own tab.
--
-- The code side is fixed by BlogModels.SeriesStatus, which every producer and
-- consumer now routes through; 'Completed' won because that is what the database
-- and the seed script already store, so no live row changes meaning. This script
-- is the data-side half of that decision:
--
--   1. Rewrites any row still carrying the superseded spelling. Only rows saved
--      through the pre-fix editor can hold 'Complete'; the UPDATE is a no-op on a
--      clean database, which is what makes it safe to replay.
--   2. Trims accidental whitespace and repairs a NULL, so the column really does
--      hold one of exactly two strings.
--   3. Adds CkBlogSeriesStatus so a future literal typo fails the INSERT instead
--      of failing silently the way this one did.
--
-- The column default from 007 ('In Progress') already satisfies the constraint
-- and is deliberately left alone.
--
-- ----------------------------------------------------------------------------
-- CHANGES
-- ----------------------------------------------------------------------------
--   - UPDATE BlogSeries SET Status = 'Completed'   WHERE Status ~* legacy spelling
--   - UPDATE BlogSeries SET Status = 'In Progress' WHERE Status IS NULL / unknown
--   - ADD CONSTRAINT CkBlogSeriesStatus CHECK (Status IN ('In Progress','Completed'))
--
-- Idempotence: both UPDATEs are predicated on the value being wrong, so a replay
-- touches zero rows, and the constraint is created inside a guarded DO block that
-- checks pg_constraint first (ALTER TABLE ... ADD CONSTRAINT has no IF NOT EXISTS
-- form in PostgreSQL). The normalisation runs BEFORE the constraint is added, so
-- the ALTER cannot fail on pre-existing data.
--
-- ----------------------------------------------------------------------------
-- ROLLBACK
-- ----------------------------------------------------------------------------
--   ALTER TABLE BlogSeries DROP CONSTRAINT IF EXISTS CkBlogSeriesStatus;
--   -- The value rewrites are not reversible row-by-row (the old spelling is not
--   -- recorded), but 'Complete' and 'Completed' carry the same meaning, so
--   -- reverting the constraint alone restores the pre-script freedom.
--   -- Also revert BlogModels.SeriesStatus and its call sites if backing this out.
-- ============================================================================

-- 1. Legacy 'Complete' (and any casing variant of it) -> 'Completed'.
UPDATE BlogSeries
   SET Status = 'Completed'
 WHERE Status IS NOT NULL
   AND BTRIM(Status) ILIKE 'complete';

-- 2a. A canonical value wearing stray whitespace keeps its meaning; just trim it.
UPDATE BlogSeries
   SET Status = BTRIM(Status)
 WHERE Status IS NOT NULL
   AND BTRIM(Status) IN ('In Progress', 'Completed')
   AND Status <> BTRIM(Status);

-- 2b. Anything still unrecognised (including NULL) -> the column default meaning.
UPDATE BlogSeries
   SET Status = 'In Progress'
 WHERE Status IS NULL
    OR Status NOT IN ('In Progress', 'Completed');

-- 3. Pin the invariant in the schema.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conname = 'ckblogseriesstatus'
           AND conrelid = 'blogseries'::regclass
    ) THEN
        ALTER TABLE BlogSeries
            ADD CONSTRAINT CkBlogSeriesStatus
            CHECK (Status IN ('In Progress', 'Completed'));
    END IF;
END
$$;

-- ============================================================================
-- End of Migration
-- ============================================================================
