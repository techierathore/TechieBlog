-- ============================================================================
-- Script: 020-CaseInsensitiveEmailLookup.sql
-- Purpose: Makes user lookup by email address case-insensitive, and enforces
--          case-insensitive uniqueness on the address.
-- Author: flow-master (orchestrator, *build-phase)
-- Created: 2026-08-07
--
-- Requirements:
--   REQ-NFR-019 - Password-reset tokens must persist. Discovered while proving
--                 the reset flow end to end: the flow never reached the token
--                 store at all for the seeded site owner.
--   REQ-FN-006  - Password strength on staff account creation. The same defect
--                 silently disabled the duplicate-account guard (see PART B).
--   REQ-FN-029  - Username uniqueness + site-owner flag; this is the email-side
--                 sibling of that constraint work.
--
-- Background:
--   GetUserByEmail was created by 002-CreateStoredFunctions.sql with an exact,
--   case-sensitive predicate:
--
--       WHERE u.EmailId = pLoginMail
--
--   AuthSvc, however, normalises the caller's input to lower case before every
--   call (AuthSvc.cs:351 and :402, `email.ToLowerInvariant().Trim()`). Any account
--   whose stored address contains an upper-case character therefore could never be
--   found through this function. The seeded site owner, `Ravi@techieblog.com`, is
--   exactly such an account.
--
--   Two user-visible defects followed, both silent:
--
--     1. PASSWORD RESET WAS IMPOSSIBLE for those accounts. RequestPasswordReset
--        treats "not found" as a deliberate no-op and returns success without a
--        token, so that no attacker can enumerate which addresses have accounts.
--        The legitimate owner sees the same reassuring "if an account exists we
--        have sent a link" message and simply never receives mail.
--
--     2. THE DUPLICATE-ACCOUNT GUARD DID NOT HOLD. ValidateNewAccount calls the
--        same function to reject an address that already exists, so
--        `RAVI@techieblog.com` was accepted alongside `Ravi@techieblog.com`.
--        Neither unique index caught it: bloguser_emailid_key and idxbloguseremail
--        are both plain btrees over the raw column, so the two spellings occupy
--        different index entries.
--
--   GetUserCredentialByEmail, added later by 017-SecurityAndTokenPersistence.sql,
--   already uses `LOWER(u.EmailId) = LOWER(pLoginMail)`. That is why signing in
--   worked while resetting a password did not, and why this script adopts that
--   same predicate rather than inventing a third convention.
--
-- Changes:
--   PART A - Replace GetUserByEmail with a case-insensitive predicate.
--   PART B - Add a case-insensitive unique index so the guard is enforced by the
--            database rather than only by application code.
--
-- Idempotent: PART A uses CREATE OR REPLACE; PART B uses IF NOT EXISTS.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- PART A - Case-insensitive lookup
-- ----------------------------------------------------------------------------
-- The signature is unchanged, so CREATE OR REPLACE rebinds the existing function
-- and no caller needs recompiling.
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
    WHERE LOWER(u.EmailId) = LOWER(pLoginMail);
END;
$$ LANGUAGE plpgsql;

-- ----------------------------------------------------------------------------
-- PART B - Enforce case-insensitive uniqueness
-- ----------------------------------------------------------------------------
-- This index also serves the PART A predicate: without a functional index on
-- LOWER(EmailId), the new WHERE clause cannot use the existing btrees and would
-- fall back to a sequential scan on every sign-in and every reset request.
--
-- If this statement fails with a uniqueness violation, the database already holds
-- two accounts whose addresses differ only by case — a condition that was
-- reachable until now. Resolve the duplicates by hand before re-running; the
-- script deliberately does NOT merge or delete accounts on its own.
CREATE UNIQUE INDEX IF NOT EXISTS IdxBlogUserEmailLower
    ON BlogUser (LOWER(EmailId));

-- Note for a future cleanup pass (deliberately not done here, as it is unrelated
-- to the defect): bloguser_emailid_key and idxbloguseremail are duplicate unique
-- btrees over the same raw column. One of them is redundant.
