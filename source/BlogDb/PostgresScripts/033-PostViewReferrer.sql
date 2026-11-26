-- ============================================================================
-- Script: 033-PostViewReferrer.sql
-- Purpose: Record the referring SITE of each post view, so the Analytics page can
--          answer "where did readers come from" (BRD-111, REQ-UI-071).
-- Author: flow-master (build phase, 2026-11-21)
-- Created: 2026-11-21
--
-- Requirements:
--   REQ-UI-071 - Every recorded post view keeps the name of the referring site
--                only; Analytics shows "Where readers came from" for the range.
--
-- Changes:
--   1. PostViews.ReferrerHost VARCHAR(253) NULL - the lower-cased host name the
--      browser's Referer header named (253 = the longest legal DNS name). Only
--      the host is ever stored: never the path, never the query. NULL means no
--      header, an unparseable one, or a visit from the site itself; the page
--      shows those as "Direct or unknown".
--   2. IdxPostViewsViewedOnReferrerHost - the range-scoped GROUP BY the Analytics
--      page runs filters on ViewedOn and groups on ReferrerHost.
--
-- Safety on a live database:
--   A nullable column with no default is a catalogue-only change in PostgreSQL:
--   no table rewrite, no backfill. Existing views read as NULL, i.e. "Direct or
--   unknown"; nothing is guessed for them.
--
-- Dependencies:
--   001-CreateTables.sql            - creates PostViews.
--   015-NewsletterAndAnalytics.sql  - adds PostViews.VisitorHash (used beside it).
--
-- Idempotency: yes - ADD COLUMN IF NOT EXISTS and CREATE INDEX IF NOT EXISTS.
--
-- Rollback:
--   DROP INDEX IF EXISTS IdxPostViewsViewedOnReferrerHost;
--   ALTER TABLE PostViews DROP COLUMN IF EXISTS ReferrerHost;
-- ============================================================================

ALTER TABLE PostViews ADD COLUMN IF NOT EXISTS ReferrerHost VARCHAR(253) NULL;

CREATE INDEX IF NOT EXISTS IdxPostViewsViewedOnReferrerHost
    ON PostViews (ViewedOn, ReferrerHost);
