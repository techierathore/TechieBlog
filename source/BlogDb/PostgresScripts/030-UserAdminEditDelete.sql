-- ============================================================================
-- Script: 030-UserAdminEditDelete.sql
-- Purpose: Give the Users administration screen a working edit, a real delete
--          and an activation flag that is actually persisted and actually
--          enforced.
-- Author: flow-master (*fix-issues, owner UAT round 2)
-- Created: 2026-08-22
-- Requirements: UAT-002 (no edit / delete on /users), UAT-003 (activate and
--               deactivate are decorative)
-- Depends on:   001-CreateTables.sql (BlogUser),
--               002-CreateStoredFunctions.sql (InsertBlogUser, UpdateBlogUser,
--               SelectBlogUserById),
--               020-CaseInsensitiveEmailLookup.sql (GetUserByEmail)
--
-- ----------------------------------------------------------------------------
-- WHY THIS EXISTS
-- ----------------------------------------------------------------------------
-- Owner UAT found that /users offers no way to edit a user's details and no way
-- to delete one. Reading the screen to route that fix turned up a second, larger
-- defect underneath it: the Activate / Deactivate button that IS on the screen
-- has never done anything.
--
-- Three separate links in that chain were broken, and fixing any one alone would
-- still leave the feature inert:
--
--   1. PERSISTENCE. UsersList.ToggleUserStatus flips AppUser.IsConfirmed and
--      calls BlogUserRepo.Update, which calls UpdateBlogUser(...). That function
--      has thirteen parameters and IsConfirmed is not among them, so the flag was
--      never written. The page then reloaded from the database and the badge
--      silently reverted to its previous value.
--
--   2. ENFORCEMENT. AuthSvc.AuthenticateAsync never consulted IsConfirmed, so
--      even a correctly persisted "Inactive" account could still sign in. The
--      flag was decorative on both sides of the wire.
--
--   3. CREATION. InsertBlogUser does not set IsConfirmed either, so it fell to
--      the column default of FALSE. Every account an administrator has ever
--      created through /AddUser is therefore stored as Inactive while remaining
--      able to sign in — the exact inverse of what the screen displays.
--
-- Because of (1) and (3) the stored value cannot be trusted as a record of
-- anyone's intent: no account was ever deliberately deactivated, because the
-- button that would have done it never wrote. PART C therefore backfills every
-- existing row to TRUE before PART D starts enforcing the flag at sign-in.
-- Skipping that backfill would lock out every administrator-created account on
-- the next deployment, including, on a site whose seed rows predate this script,
-- possibly the only Admin.
--
-- DELETE is a SOFT delete, and that is not a shortcut. BlogUser is the target of
-- sixteen foreign keys — BlogPost, Comment, PostRating, UserLogin, the three
-- resume tables and more — and only four of them declare ON DELETE CASCADE. A
-- hard DELETE would be refused outright for any user who has ever written a post
-- or left a comment, which is precisely the user an administrator wants to
-- remove. Worse, the four cascading references mean a hard delete that DID
-- succeed would silently take that user's ratings and favourites with it. A
-- flag keeps referential integrity intact and keeps authored posts attributed,
-- which is what an administrator actually wants when an author leaves.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- PART A - The soft-delete flag
-- ----------------------------------------------------------------------------
-- NOT NULL with a default so every existing row becomes explicitly not-deleted
-- rather than NULL; the read predicates below can then be a plain "= FALSE"
-- without a COALESCE, unlike BlogPost.IsDeleted which was added nullable and
-- has needed "(IsDeleted = false OR IsDeleted IS NULL)" at every call site since.
ALTER TABLE BlogUser
    ADD COLUMN IF NOT EXISTS IsDeleted BOOLEAN NOT NULL DEFAULT FALSE;

-- Partial index: every production read filters to the not-deleted rows, and
-- deleted users are expected to stay a small minority, so indexing only the
-- live ones keeps the index small while still serving the common predicate.
CREATE INDEX IF NOT EXISTS IdxBlogUserNotDeleted
    ON BlogUser (UserId)
    WHERE IsDeleted = FALSE;

-- ----------------------------------------------------------------------------
-- PART B - Persist the activation flag (defect 1)
-- ----------------------------------------------------------------------------
-- A dedicated single-purpose function rather than widening UpdateBlogUser's
-- signature. Widening it would force every existing caller of Update(AppUser) to
-- carry a correct IsConfirmed, and a caller that loaded a projection WITHOUT
-- that column would then write FALSE back over a live account's activation on an
-- unrelated profile save. Narrow write, narrow blast radius.
CREATE OR REPLACE FUNCTION SetBlogUserActive(
    pUserId BIGINT,
    pIsConfirmed BOOLEAN
)
RETURNS BOOLEAN AS $$
DECLARE
    vRowsAffected INT;
BEGIN
    UPDATE BlogUser
    SET IsConfirmed = pIsConfirmed,
        UpdatedOn   = NOW()
    WHERE UserId = pUserId
      AND IsDeleted = FALSE;

    GET DIAGNOSTICS vRowsAffected = ROW_COUNT;
    RETURN vRowsAffected > 0;
END;
$$ LANGUAGE plpgsql;

-- ----------------------------------------------------------------------------
-- PART C - Backfill, so PART D cannot lock anyone out
-- ----------------------------------------------------------------------------
-- Read the WHY block above before changing this. Every row is set active because
-- no stored FALSE can be shown to represent a deliberate deactivation: the only
-- UI that writes the flag never reached the database, and account creation
-- defaulted it to FALSE regardless of intent. Deactivating an account remains
-- available from /users the moment PART B lands — and from that point forward
-- the stored value IS trustworthy, which is why this backfill is a one-off in a
-- numbered migration and not a recurring job.
UPDATE BlogUser
SET IsConfirmed = TRUE
WHERE IsConfirmed IS DISTINCT FROM TRUE
  AND IsDeleted = FALSE;

-- ----------------------------------------------------------------------------
-- PART D - New accounts are created active (defect 3)
-- ----------------------------------------------------------------------------
-- Public registration was retired (REQ-UI-002 is N/A), so the only path into
-- this function is an administrator deliberately creating a staff account from
-- /AddUser. There is no confirmation mail to wait for, so leaving the account
-- inactive would mean every new account had to be activated by a second manual
-- step that the administrator has no reason to expect. The signature is
-- unchanged, so CREATE OR REPLACE rebinds it with no caller recompiled.
CREATE OR REPLACE FUNCTION InsertBlogUser(
    pFirstName VARCHAR(100),
    pLastName VARCHAR(100),
    pEmailId VARCHAR(255),
    pLoginPass VARCHAR(255),
    pUserRole VARCHAR(51),
    pProfileImagePath VARCHAR(255) DEFAULT NULL,
    pProfileDescription TEXT DEFAULT NULL,
    pTwitterUrl VARCHAR(255) DEFAULT NULL,
    pLinkedInUrl VARCHAR(255) DEFAULT NULL,
    pGitHubUrl VARCHAR(255) DEFAULT NULL,
    pPodDescription VARCHAR(1050) DEFAULT NULL,
    pSpeakDescription VARCHAR(1050) DEFAULT NULL
)
RETURNS BIGINT AS $$
DECLARE
    vUserId BIGINT;
BEGIN
    INSERT INTO BlogUser (
        FirstName, LastName, EmailId, LoginPass, CreatedOn, UpdatedOn,
        UserRole, IsConfirmed, IsDeleted, ProfileImagePath, ProfileDescription,
        TwitterUrl, LinkedInUrl, GitHubUrl, PodDescription, SpeakDescription
    )
    VALUES (
        pFirstName, pLastName, pEmailId, pLoginPass, NOW(), NOW(),
        pUserRole, TRUE, FALSE, pProfileImagePath, pProfileDescription,
        pTwitterUrl, pLinkedInUrl, pGitHubUrl, pPodDescription, pSpeakDescription
    )
    RETURNING UserId INTO vUserId;

    RETURN vUserId;
END;
$$ LANGUAGE plpgsql;

-- ----------------------------------------------------------------------------
-- PART E - The soft delete itself
-- ----------------------------------------------------------------------------
-- Deactivates as well as deletes, so a single flag check is enough anywhere that
-- only cares "may this account act". Deliberately refuses to delete the site
-- owner: BlogUser.IsSiteOwner drives the entire public home page and /resume
-- through GetSiteOwner, and removing that row would blank the landing page for
-- every visitor with no error anywhere to explain it. The UI blocks this too;
-- this is the backstop for any other caller.
--
-- Returns FALSE for "refused or no such row" and TRUE for "deleted", matching
-- the bool-returning convention the other IBlogUserRepo write members follow.
CREATE OR REPLACE FUNCTION SoftDeleteBlogUser(pUserId BIGINT)
RETURNS BOOLEAN AS $$
DECLARE
    vRowsAffected INT;
BEGIN
    UPDATE BlogUser
    SET IsDeleted   = TRUE,
        IsConfirmed = FALSE,
        UpdatedOn   = NOW()
    WHERE UserId = pUserId
      AND IsDeleted = FALSE
      AND IsSiteOwner = FALSE;

    GET DIAGNOSTICS vRowsAffected = ROW_COUNT;
    RETURN vRowsAffected > 0;
END;
$$ LANGUAGE plpgsql;

-- ----------------------------------------------------------------------------
-- PART F - Keep deleted users out of the identity lookups
-- ----------------------------------------------------------------------------
-- A soft delete that the sign-in path ignores is not a delete. Both projections
-- keep their existing column lists and signatures exactly — callers map these by
-- position into AppUser — and gain only the IsDeleted predicate.
CREATE OR REPLACE FUNCTION GetUserByEmail(pLoginMail VARCHAR(550))
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.UserRole, u.CreatedOn, u.UpdatedOn
    FROM BlogUser u
    WHERE LOWER(u.EmailId) = LOWER(pLoginMail)
      AND u.IsDeleted = FALSE;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION GetLoginUser(pLoginMail VARCHAR(550), pLoginPassword VARCHAR(255))
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.UserRole, u.CreatedOn, u.UpdatedOn
    FROM BlogUser u
    WHERE u.EmailId = pLoginMail
      AND u.LoginPass = pLoginPassword
      AND u.IsDeleted = FALSE;
END;
$$ LANGUAGE plpgsql;

-- SelectBlogUserById is deliberately left ALONE, and the reason is worth stating
-- because the obvious change here is a trap. It is the projection AuthSvc loads
-- after the password check, so the instinct is to add IsDeleted to it and test
-- that flag at sign-in. But PostgreSQL cannot CREATE OR REPLACE a function whose
-- RETURNS TABLE column list changes — that is a return-type change and is
-- refused — so adding the column means DROP then CREATE, and the drop window
-- would break every profile read on a live site mid-deployment.
--
-- It is also unnecessary. SoftDeleteBlogUser above sets IsConfirmed = FALSE in
-- the same statement that sets IsDeleted = TRUE, precisely so that the single
-- IsConfirmed test AuthSvc now performs refuses deleted accounts and deactivated
-- accounts alike. One flag check on the hot path, two states covered, no
-- signature churn. If a future change ever needs to tell the two apart at
-- sign-in, add the column in its own numbered migration with an explicit
-- DROP FUNCTION and update BlogUserRepo's mapping in the same commit.
