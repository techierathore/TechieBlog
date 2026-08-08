-- ============================================================================
-- Script: 021-LoginAuditAndForcedChange.sql
-- Purpose: Makes the sign-in audit trail able to record a failed attempt, and
--          makes the forced-password-change flag visible to the application on
--          every page load rather than only at the moment of sign-in.
-- Author: flow-master (Cluster E)
-- Created: 2026-08-08
--
-- Requirements:
--   REQ-FN-051 - The login audit trail could not record a failed sign-in.
--                LoginLogRepo hard-coded success = true and attemptedemail = '',
--                and the LoginLog model exposed neither column, so a brute-force
--                run left no evidence. The repository and model are repaired in
--                code; this script guarantees the columns those statements bind
--                actually exist, and that UserId is nullable so an attempt against
--                an address with no account is recorded rather than rejected by
--                the foreign key.
--   REQ-NFR-023 - Hash the seeded admin credential and force a change at first
--                 login (BRD-79). The hashing half was already done in
--                 003-SeedData.sql and the flag already exists (017 PART A), but
--                 SelectBlogUserById did NOT project MustChangePassword, so every
--                 profile load after the initial sign-in reported the flag as
--                 false and any enforcement built on it would be bypassed by a
--                 page refresh. PART B repairs the projection.
--
-- Changes:
--   PART A - LoginLog audit columns and nullable user key
--   PART B - SelectBlogUserById projects MustChangePassword
--
-- Dependencies:
--   001-CreateTables.sql (LoginLog, BlogUser)
--   002-CreateStoredFunctions.sql (SelectBlogUserById)
--   017-SecurityAndTokenPersistence.sql (BlogUser.MustChangePassword)
--
-- Idempotent: yes - PART A uses ADD COLUMN IF NOT EXISTS / DROP NOT NULL, which
--             are no-ops on a database that already has the 001 shape; PART B
--             drops and recreates one function by exact signature. DbUp runs at
--             every host startup, and re-running this script changes nothing.
--
-- Rollback:
--   ALTER TABLE LoginLog DROP COLUMN IF EXISTS UserAgent;
--   ALTER TABLE LoginLog DROP COLUMN IF EXISTS Success;
--   ALTER TABLE LoginLog DROP COLUMN IF EXISTS AttemptedEmail;
--   -- and re-run the SelectBlogUserById definition from
--   -- 002-CreateStoredFunctions.sql to drop the MustChangePassword column again.
-- ============================================================================

-- ============================================================================
-- PART A: LoginLog audit columns  [REQ-FN-051]
-- Purpose: Guarantee the columns the repaired INSERT binds are present
--
-- Business Rules:
--   - Success is NOT NULL: an attempt with no recorded outcome is worthless to an
--     investigation, so the column must never be silently absent. Existing rows
--     predate the repair and were all written by the hard-coded 'true' branch, so
--     backfilling them as TRUE is factually what that code recorded.
--   - UserId stays nullable: a failed attempt frequently names an address that
--     matches no account, and there is no user row to point at. A sentinel id
--     would either break the foreign key or fabricate an attribution.
--   - AttemptedEmail is nullable at the column level but never written NULL by the
--     application; the repository COALESCEs it on read so the model never sees one.
-- ============================================================================
ALTER TABLE LoginLog ADD COLUMN IF NOT EXISTS AttemptedEmail VARCHAR(255);

ALTER TABLE LoginLog ADD COLUMN IF NOT EXISTS Success BOOLEAN;

ALTER TABLE LoginLog ADD COLUMN IF NOT EXISTS UserAgent VARCHAR(500);

-- Backfill any row written before the outcome column existed, then make the
-- column mandatory. Both statements are no-ops on a database created from 001.
UPDATE LoginLog SET Success = TRUE WHERE Success IS NULL;

ALTER TABLE LoginLog ALTER COLUMN Success SET NOT NULL;

-- A failed attempt has no user to point at. DROP NOT NULL is accepted even when
-- the column is already nullable, which is the case on a 001-created database.
ALTER TABLE LoginLog ALTER COLUMN UserId DROP NOT NULL;

-- Investigations read the trail by the address that was tried, because that is the
-- only key that spans a run of attempts against an account that may not exist.
CREATE INDEX IF NOT EXISTS IdxLoginLogAttemptedEmail ON LoginLog(LOWER(AttemptedEmail));

-- ============================================================================
-- PART B: SelectBlogUserById projects MustChangePassword  [REQ-NFR-023]
-- Purpose: Make the forced-change flag survive a page refresh
--
-- Business Rules:
--   - AuthSvc.AppLogin copies the flag from the credential row, so the flag was
--     correct for exactly one render. Every later profile load goes through
--     GetUserByToken -> BlogUserRepo.GetSingle -> this function, which did not
--     return the column, so AppUser.MustChangePassword silently reverted to false
--     and a flagged user could escape the change screen by pressing F5.
--   - The return type changes, so CREATE OR REPLACE is not enough - PostgreSQL
--     refuses to change a function's OUT parameters in place (42P13). The function
--     is dropped by exact signature first, which is safe and idempotent.
-- ============================================================================
DROP FUNCTION IF EXISTS SelectBlogUserById(BIGINT);

CREATE OR REPLACE FUNCTION SelectBlogUserById(pUserId BIGINT)
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    UserRole VARCHAR(51),
    IsConfirmed BOOLEAN,
    ProfileImagePath VARCHAR(255),
    ProfileDescription TEXT,
    TwitterUrl VARCHAR(255),
    LinkedInUrl VARCHAR(255),
    GitHubUrl VARCHAR(255),
    PodDescription VARCHAR(1050),
    SpeakDescription VARCHAR(1050),
    MustChangePassword BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.CreatedOn, u.UpdatedOn, u.UserRole, u.IsConfirmed,
        u.ProfileImagePath, u.ProfileDescription,
        u.TwitterUrl, u.LinkedInUrl, u.GitHubUrl,
        u.PodDescription, u.SpeakDescription,
        u.MustChangePassword
    FROM BlogUser u
    WHERE u.UserId = pUserId;
END;
$$ LANGUAGE plpgsql;
