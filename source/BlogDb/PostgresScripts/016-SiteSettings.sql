-- ============================================================================
-- Script: 016-SiteSettings.sql
-- Purpose: Server-side persistence for site-wide configuration. Replaces the
--          browser local-storage preferences that previously stood in for site
--          settings, so an administrator's choices apply to every visitor.
-- Author: flow-master (build phase, Cluster A)
-- Created: 2026-08-07
--
-- Requirements:
--   REQ-FN-040 - Site settings persist and take effect without a restart (BRD-69).
--   REQ-UI-026 - Every section of the admin Settings page round-trips to the
--                database, not just the pagination word count (BRD-68, BRD-69).
--   REQ-UI-032 - The admin's theme choice is the SITE theme, stored server-side
--                rather than per browser (BRD-68).
--   REQ-FN-039 - ThemeService / ThemeProvider read a site-wide default that an
--                administrator can set (BRD-65, BRD-66, BRD-67).
--
-- Changes:
--   PART A - SiteSetting key/value table + indexes
--   PART B - UpsertSiteSetting stored function (the single write path)
--   PART C - Seed the shipped defaults so a fresh database renders a site
--
-- Design note - why key/value and not a wide row:
--   Site configuration grows by one setting at a time. One row per key means a
--   new setting is a code change only, never a schema migration, and the admin
--   screen can group settings without a hard-coded column list. SettingGroup
--   carries the section name (General, Blog, Theme, Seo, Social, Smtp, Storage)
--   and IsSecret marks the values the service encrypts at rest.
--
-- Dependencies:
--   None. The table stands alone - settings have no owning entity.
--
-- Idempotency: yes - CREATE TABLE IF NOT EXISTS, CREATE INDEX IF NOT EXISTS,
--              CREATE OR REPLACE FUNCTION and ON CONFLICT DO NOTHING seeds, so
--              DbUp re-running the script is harmless.
--
-- Rollback:
--   DROP FUNCTION IF EXISTS UpsertSiteSetting(TEXT, TEXT, TEXT, BOOLEAN);
--   DROP INDEX IF EXISTS IdxSiteSettingGroup;
--   DROP TABLE IF EXISTS SiteSetting;
-- ============================================================================

-- ============================================================================
-- PART A: SiteSetting table  [REQ-FN-040]
-- Purpose: One row per configuration key.
--
-- Business Rules:
--   - SettingKey is unique and case-sensitive; the keys are the constants in
--     BlogModels.SiteSettingKeys, never free text from a user.
--   - SettingValue is always TEXT. Typed conversion (int, bool) happens in
--     SiteSettingsMapper so the storage shape never constrains a new setting.
--   - A NULL or absent value means "use the built-in default" - that is how a
--     setting is reset without deleting knowledge of it.
--   - IsSecret marks credentials; SiteSettingsService encrypts those values
--     before they reach this table and decrypts them on the way out.
--   - UpdatedOn is stamped by the upsert function, never by the caller.
-- ============================================================================
CREATE TABLE IF NOT EXISTS SiteSetting (
    -- Surrogate primary key, auto-generated
    SettingId BIGSERIAL PRIMARY KEY,

    -- Canonical key name, e.g. 'General.SiteTitle' - unique across the table
    SettingKey VARCHAR(150) NOT NULL UNIQUE,

    -- Stored value as text; NULL means "fall back to the built-in default"
    SettingValue TEXT,

    -- Section the key belongs to, used to group the admin settings screen
    SettingGroup VARCHAR(50) NOT NULL DEFAULT 'General',

    -- True when the value is encrypted at rest (SMTP password, cloud key)
    IsSecret BOOLEAN NOT NULL DEFAULT FALSE,

    -- Timestamp of the last write, maintained by UpsertSiteSetting
    UpdatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Group lookups drive the admin screen's section rendering.
CREATE INDEX IF NOT EXISTS IdxSiteSettingGroup ON SiteSetting (SettingGroup);

-- ============================================================================
-- PART B: UpsertSiteSetting  [REQ-FN-040]
-- Purpose: The single write path for a setting. Insert-or-update is decided by
--          the database so two concurrent saves cannot race into duplicate keys.
--
-- Business Rules:
--   - Matching is on SettingKey, the table's natural key.
--   - Group and secret flag are refreshed on every write, so a key that moves
--     section (or becomes a secret) corrects itself without a data migration.
--   - UpdatedOn is always stamped, which is what lets SiteSettings.UpdatedOn
--     report when the site was last reconfigured.
--
-- Returns: the affected row's SettingId.
-- ============================================================================
-- Every text argument is declared TEXT (not VARCHAR) because Npgsql sends a
-- plain string parameter with the text OID; matching the declaration removes
-- any dependence on PostgreSQL's overload-resolution casting rules.
CREATE OR REPLACE FUNCTION UpsertSiteSetting(
    pSettingKey TEXT,
    pSettingValue TEXT,
    pSettingGroup TEXT,
    pIsSecret BOOLEAN
)
RETURNS BIGINT AS $$
DECLARE
    vSettingId BIGINT;
BEGIN
    INSERT INTO SiteSetting (SettingKey, SettingValue, SettingGroup, IsSecret, UpdatedOn)
    VALUES (
        pSettingKey,
        pSettingValue,
        COALESCE(NULLIF(pSettingGroup, ''), 'General'),
        COALESCE(pIsSecret, FALSE),
        CURRENT_TIMESTAMP
    )
    ON CONFLICT (SettingKey) DO UPDATE
        SET SettingValue = EXCLUDED.SettingValue,
            SettingGroup = EXCLUDED.SettingGroup,
            IsSecret     = EXCLUDED.IsSecret,
            UpdatedOn    = CURRENT_TIMESTAMP
    RETURNING SettingId INTO vSettingId;

    RETURN vSettingId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- PART C: Seed the shipped defaults  [REQ-UI-026, REQ-UI-032]
-- Purpose: A fresh database renders a complete Settings screen with real values
--          rather than blank inputs (the RENDER-TRUTH gate).
--
-- Business Rules:
--   - ON CONFLICT DO NOTHING, so an existing site's configuration is never
--     overwritten by a re-run.
--   - Credentials are deliberately NOT seeded - no secret ships in a script.
--   - The seeded theme is the shipped default 'trblaze-modern' (REQ-UI-048).
-- ============================================================================
INSERT INTO SiteSetting (SettingKey, SettingValue, SettingGroup, IsSecret) VALUES
    ('General.SiteTitle',          'TechieBlog',                                            'General', FALSE),
    ('General.SiteTagline',        'Practical notes on .NET, Blazor and the web',           'General', FALSE),
    ('General.AdminEmail',         'Ravi@techieblog.com',                                   'General', FALSE),
    ('Blog.PostsPerPage',          '10',                                                    'Blog',    FALSE),
    ('Blog.PaginationWordCount',   '500',                                                   'Blog',    FALSE),
    ('Blog.AreCommentsAllowed',    'True',                                                  'Blog',    FALSE),
    ('Blog.AreCommentsModerated',  'True',                                                  'Blog',    FALSE),
    ('Blog.IsRegistrationAllowed', 'True',                                                  'Blog',    FALSE),
    ('Theme.SiteTheme',            'trblaze-modern',                                        'Theme',   FALSE),
    ('Theme.IsDarkModeDefault',    'False',                                                 'Theme',   FALSE),
    ('Seo.MetaDescription',        'TechieBlog - practical articles on .NET and Blazor.',   'Seo',     FALSE),
    ('Seo.MetaKeywords',           'dotnet, blazor, csharp, postgresql, web development',   'Seo',     FALSE),
    ('Social.TwitterUrl',          '',                                                      'Social',  FALSE),
    ('Social.LinkedInUrl',         '',                                                      'Social',  FALSE),
    ('Social.GitHubUrl',           '',                                                      'Social',  FALSE),
    ('Smtp.Host',                  '',                                                      'Smtp',    FALSE),
    ('Smtp.Port',                  '587',                                                   'Smtp',    FALSE),
    ('Smtp.IsSslEnabled',          'True',                                                  'Smtp',    FALSE),
    ('Smtp.UserName',              '',                                                      'Smtp',    FALSE),
    ('Smtp.FromAddress',           '',                                                      'Smtp',    FALSE),
    ('Smtp.FromName',              'TechieBlog',                                            'Smtp',    FALSE),
    ('Storage.ProviderName',       'Local',                                                 'Storage', FALSE),
    ('Storage.LocalRootPath',      '',                                                      'Storage', FALSE),
    ('Storage.NetworkRootPath',    '',                                                      'Storage', FALSE),
    ('Storage.CloudServiceUrl',    '',                                                      'Storage', FALSE),
    ('Storage.CloudContainerName', '',                                                      'Storage', FALSE),
    ('Storage.PublicBaseUrl',      '',                                                      'Storage', FALSE)
ON CONFLICT (SettingKey) DO NOTHING;
