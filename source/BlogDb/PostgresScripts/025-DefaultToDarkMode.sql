-- ============================================================================
-- Script: 025-DefaultToDarkMode.sql
-- Purpose: Make DARK the shipped site-wide light/dark default, so a first-time
--          visitor with an empty LocalStorage receives dark on the very first
--          server-rendered paint.
-- Author: flow-master (build phase, Cluster G)
-- Created: 2026-08-10
-- Requirements: REQ-UI-033 / BRD-66 (dark mode), REQ-UI-032 / REQ-FN-039 /
--               BRD-68 (admin-selected default rendered server-side)
-- Depends on:   016-SiteSettings.sql (SiteSetting table + the seeded
--                                     'Theme.IsDarkModeDefault' row)
--
-- ----------------------------------------------------------------------------
-- WHY THIS EXISTS
-- ----------------------------------------------------------------------------
-- The mechanism was already correct and is NOT changed here. App.razor renders
--   <html ... class="@(isDarkModeDefault ? "dark" : null)">
-- server-side from SiteSettings.IsDarkModeDefault, and the inline head script
-- then lets a returning visitor's own LocalStorage preference override it. The
-- site opened light for one reason only: the VALUE was False in all three
-- places that can supply it.
--
-- The three places must agree or a fresh database and an established one open
-- in different modes:
--   1. the seeded row  ................ this script
--   2. SiteSettings.IsDarkModeDefault . property initialiser, now `= true`
--   3. ThemeService fallback .......... ThemeService.ShippedDarkMode, now true
--
-- 016-SiteSettings.sql is deliberately left untouched: DbUp has already
-- journalled it on every existing database, so editing it would fix nothing and
-- would break the "applied scripts are immutable" rule. A fresh database runs
-- 016 (False) and then this script (True) and lands in the same state as an
-- upgraded one.
--
-- ----------------------------------------------------------------------------
-- CHANGES
-- ----------------------------------------------------------------------------
--   - UPDATE the existing 'Theme.IsDarkModeDefault' row to 'True'.
--   - INSERT it with 'True' if it is somehow absent (a database whose settings
--     rows were pruned), so this script is complete on its own.
--
-- The value written is the literal 'True' because SiteSettingsMapper persists
-- flags with bool.ToString(), and reads them with bool.TryParse — which is
-- case-insensitive, so an existing 'true'/'TRUE' still parses. Any row already
-- holding a dark value is left alone, which is what makes the script idempotent
-- and safe if it is ever replayed against a restored database.
--
-- ----------------------------------------------------------------------------
-- ROLLBACK
-- ----------------------------------------------------------------------------
--   UPDATE SiteSetting SET SettingValue = 'False', UpdatedOn = CURRENT_TIMESTAMP
--    WHERE SettingKey = 'Theme.IsDarkModeDefault';
--   -- and revert the two code-side defaults named above.
--
-- NOTE: site settings are cached under the 'settings:effective' key for ten
-- minutes (REQ-NFR-018) and the cache is evicted by service mutations only.
-- A direct SQL change like this one is not visible until the cache expires or
-- the host restarts. That is expected; DbUp runs this at startup, before the
-- cache is populated, so a normal deployment shows the new value immediately.
-- ============================================================================

-- ============================================================================
-- PART A: Flip the seeded default to dark
-- ============================================================================
UPDATE SiteSetting
   SET SettingValue = 'True',
       UpdatedOn    = CURRENT_TIMESTAMP
 WHERE SettingKey = 'Theme.IsDarkModeDefault'
   AND lower(coalesce(SettingValue, '')) IS DISTINCT FROM 'true';

-- ============================================================================
-- PART B: Supply the row if it is missing
-- ON CONFLICT DO NOTHING so PART A stays the single writer on a normal database.
-- ============================================================================
INSERT INTO SiteSetting (SettingKey, SettingValue, SettingGroup, IsSecret)
VALUES ('Theme.IsDarkModeDefault', 'True', 'Theme', FALSE)
ON CONFLICT (SettingKey) DO NOTHING;

-- ============================================================================
-- End of Migration
-- ============================================================================
