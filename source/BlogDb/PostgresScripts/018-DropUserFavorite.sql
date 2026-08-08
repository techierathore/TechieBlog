-- ============================================================================
-- Script: 018-DropUserFavorite.sql
-- Purpose: Retires the user favourites / bookmarks feature at the schema level
-- Author: flow-master (Cluster G)
-- Created: 2026-08-07
--
-- Requirements:
--   REQ-FN-024 - Favourites service (add/remove/toggle/list/count) is retired.
--                BRD-43 and BRD-44 were withdrawn by the 2026-08-06 design
--                review, which removed reader accounts and favourites from the
--                product. The UserFavorite model, repository and service are
--                deleted from the codebase; this script removes the table so
--                the schema matches.
--   REQ-UI-014 - My Favourites page removed.
--   REQ-UI-028 - Favourite toggle component removed from the post page and cards.
--
-- Changes:
--   PART A - Drop the UserFavorite indexes (redundant with the table drop, but
--            stated explicitly so a partially applied 009 also cleans up)
--   PART B - Drop the UserFavorite table
--
-- Dependencies:
--   009-CreateUserFavorite.sql created UserFavorite plus its three indexes and
--   the UQ_UserFavorite_User_Post unique constraint. Script 009 is left intact -
--   DbUp journals every script, so it must never be edited or deleted; this
--   script supersedes it. No other table references UserFavorite, and its own
--   foreign keys to BlogPost and BlogUser disappear with the table, so nothing
--   else in the schema is affected.
--
-- Idempotency:
--   Every statement uses IF EXISTS. DbUp runs at every startup and the script is
--   safe to re-run against a database that never had the table (a fresh install
--   applies 009 then 018 in order and ends with no UserFavorite table).
--
-- Rollback:
--   Re-run the CREATE TABLE / CREATE INDEX statements from
--   009-CreateUserFavorite.sql. No rollback path is provided for the row data -
--   the feature is retired, and the favourites recorded against it are
--   deliberately discarded.
-- ============================================================================

-- ============================================================================
-- PART A: Drop indexes created by 009-CreateUserFavorite.sql
-- ============================================================================
DROP INDEX IF EXISTS IX_UserFavorite_CreatedOn;
DROP INDEX IF EXISTS IX_UserFavorite_UserId;
DROP INDEX IF EXISTS IX_UserFavorite_PostId;

-- ============================================================================
-- PART B: Drop the UserFavorite table
-- Nothing depends on this table, so CASCADE is not needed; a plain drop keeps
-- the blast radius visible and fails loudly if a future object ever does.
-- ============================================================================
DROP TABLE IF EXISTS UserFavorite;
