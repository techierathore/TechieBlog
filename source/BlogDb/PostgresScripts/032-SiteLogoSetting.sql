-- ============================================================================
-- Script: 032-SiteLogoSetting.sql
-- Purpose: Seed the 'General.SiteLogo' site setting so the owner can configure a
--          site logo from /settings (UAT-022) without a blank row breaking the
--          new-database render-truth expectation the Settings screen relies on.
-- Author: tf-builder (fix-issues, UAT-021 / UAT-022)
-- Created: 2026-08-23
--
-- Requirements:
--   UAT-022 - There is no way to change the site logo in Settings. Adds the
--             persisted key SiteSettingsMapper now reads/writes:
--             BlogModels.SiteSettingKeys.SiteLogo = "General.SiteLogo".
--
-- Changes:
--   Seed one SiteSetting row, blank by default (no logo shipped; the header,
--   admin sidebar and auth shell keep rendering the built-in glyph until an
--   administrator picks one on /settings).
--
-- Dependencies:
--   016-SiteSettings.sql - creates the SiteSetting table this script inserts into.
--
-- Idempotency: yes - ON CONFLICT (SettingKey) DO NOTHING. DbUp journals by
--              filename, so this guard is what makes a re-run against a database
--              that already has the row (or that never dropped SiteSetting)
--              harmless, matching 016's own seed block.
--
-- Rollback:
--   DELETE FROM SiteSetting WHERE SettingKey = 'General.SiteLogo';
-- ============================================================================

INSERT INTO SiteSetting (SettingKey, SettingValue, SettingGroup, IsSecret) VALUES
    ('General.SiteLogo', '', 'General', FALSE)
ON CONFLICT (SettingKey) DO NOTHING;
