-- ============================================================================
-- Script: 017-SecurityAndTokenPersistence.sql
-- Purpose: Security hardening and password-reset token persistence
-- Author: flow-master (Cluster E)
-- Created: 2026-08-06
--
-- Requirements:
--   REQ-NFR-002 - Passwords stored with an industry-standard salted hash (BRD-79).
--                 Verification moves out of SQL and into BlogModels.PasswordHasher,
--                 so the login functions now return the stored hash instead of
--                 comparing it; UpdateUserPassword rotates only the credential
--                 columns so a silent re-hash cannot clobber profile fields.
--   REQ-NFR-023 - The seeded administrator credential is hashed and the account is
--                 flagged so the first sign-in must set a new password (BRD-79).
--   REQ-NFR-019 - Password-reset tokens persist in the database so a mailed link
--                 survives a restart and works on any instance (BRD-5).
--
-- Changes:
--   PART A - BlogUser.MustChangePassword column
--   PART B - Repair databases seeded with the plaintext bootstrap credential
--   PART C - PasswordResetToken table and indexes
--   PART D - Credential access functions
--   PART E - Password-reset token functions
--
-- Dependencies:
--   001-CreateTables.sql (BlogUser), 003-SeedData.sql (bootstrap administrator)
--
-- Related: REQ-FN-048 introduces a SEPARATE anonymous email-verification token
--          store. The two tables share this shape deliberately but stay distinct -
--          different owners, lifetimes and expiry rules.
--
-- Idempotent: yes - every statement is guarded (IF NOT EXISTS / CREATE OR REPLACE /
--             conditional UPDATE), so DbUp re-running the script is harmless.
--
-- Rollback:
--   DROP FUNCTION IF EXISTS DeleteExpiredPasswordResetToken();
--   DROP FUNCTION IF EXISTS MarkPasswordResetTokenUsed(BIGINT);
--   DROP FUNCTION IF EXISTS GetPasswordResetTokenByUser(BIGINT);
--   DROP FUNCTION IF EXISTS GetPasswordResetTokenById(BIGINT);
--   DROP FUNCTION IF EXISTS GetPasswordResetTokenByToken(VARCHAR);
--   DROP FUNCTION IF EXISTS InsertPasswordResetToken(BIGINT, VARCHAR, TIMESTAMP, TIMESTAMP);
--   DROP FUNCTION IF EXISTS UpdateUserPassword(BIGINT, VARCHAR, BOOLEAN);
--   DROP FUNCTION IF EXISTS GetUserCredentialById(BIGINT);
--   DROP FUNCTION IF EXISTS GetUserCredentialByEmail(VARCHAR);
--   DROP TABLE IF EXISTS PasswordResetToken;
--   ALTER TABLE BlogUser DROP COLUMN IF EXISTS MustChangePassword;
-- ============================================================================

-- ============================================================================
-- PART A: Forced password change flag  [REQ-NFR-023]
-- Purpose: Marks accounts that must set their own password before continuing
--
-- Business Rules:
--   - The seeded bootstrap administrator is always flagged
--   - Admin-created staff accounts are flagged at creation (AuthSvc.CreateStaffAccount)
--   - ChangePassword and ResetPassword clear the flag
-- ============================================================================
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS MustChangePassword BOOLEAN NOT NULL DEFAULT FALSE;

-- ============================================================================
-- PART B: Repair the bootstrap administrator credential  [REQ-NFR-023]
-- Purpose: Databases created before this migration hold LoginPass = 'admin_password'
--          in plain text. Replace it with the same PBKDF2 hash 003-SeedData.sql now
--          seeds, so the documented password keeps working while nothing readable is
--          left at rest.
--
-- Business Rules:
--   - Only rows that still hold the known plaintext are touched
--   - The flag is set for the bootstrap account regardless of hash state, so the
--     well-known password cannot survive the first sign-in
-- ============================================================================
UPDATE BlogUser
SET LoginPass = 'PBKDF2-SHA256$210000$VGVjaGllQmxvZ1NlZWQwMQ==$m3BUDC+/QWc38+4jGaLfRF6VDV/ksim4+JCoOJJZjw4=',
    UpdatedOn = NOW()
WHERE LOWER(EmailId) = 'ravi@techieblog.com'
  AND LoginPass = 'admin_password';

UPDATE BlogUser
SET MustChangePassword = TRUE
WHERE LOWER(EmailId) = 'ravi@techieblog.com'
  AND MustChangePassword = FALSE
  AND LoginPass = 'PBKDF2-SHA256$210000$VGVjaGllQmxvZ1NlZWQwMQ==$m3BUDC+/QWc38+4jGaLfRF6VDV/ksim4+JCoOJJZjw4=';

-- ============================================================================
-- PART C: PasswordResetToken table  [REQ-NFR-019]
-- Purpose: Persists single-use password-reset tokens
--
-- Relationships:
--   - BlogUser (UserId) - the account the token resets
--
-- Business Rules:
--   - Token is globally unique and URL-safe (256 bits of entropy, base64url)
--   - ExpiresAt is set 24 hours after issue by AuthSvc.RequestPasswordReset
--   - IsUsed makes redemption single-shot; the row is kept until cleanup runs
-- ============================================================================
CREATE TABLE IF NOT EXISTS PasswordResetToken (
    -- Primary identifier, auto-generated
    TokenId BIGSERIAL PRIMARY KEY,

    -- Account the reset link belongs to
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId) ON DELETE CASCADE,

    -- Opaque URL-safe token carried in the emailed link
    Token VARCHAR(255) NOT NULL UNIQUE,

    -- When the token was issued (UTC, supplied by the application)
    CreatedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- When the token stops being redeemable (UTC, 24 hours after issue)
    ExpiresAt TIMESTAMP NOT NULL,

    -- Whether the token has already been redeemed
    IsUsed BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS IdxPasswordResetTokenToken ON PasswordResetToken(Token);
CREATE INDEX IF NOT EXISTS IdxPasswordResetTokenUserId ON PasswordResetToken(UserId);
CREATE INDEX IF NOT EXISTS IdxPasswordResetTokenExpiresAt ON PasswordResetToken(ExpiresAt);

-- ============================================================================
-- PART D: Credential access functions  [REQ-NFR-002]
-- ============================================================================

-- ============================================================================
-- FUNCTION: GetUserCredentialByEmail
-- Purpose: Returns the stored password hash for an email address
--
-- Parameters:
--   pLoginMail - the login email address (matched case-insensitively)
--
-- Returns: One credential row, or nothing when the account does not exist
--
-- Called By: UserCredentialRepo.GetByEmail() -> AuthSvc.AppLogin()
-- ============================================================================
DROP FUNCTION IF EXISTS GetUserCredentialByEmail(VARCHAR);
CREATE FUNCTION GetUserCredentialByEmail(pLoginMail VARCHAR(550))
RETURNS TABLE (
    UserId BIGINT,
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    MustChangePassword BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT u.UserId, u.EmailId, u.LoginPass, u.UserRole, u.MustChangePassword
    FROM BlogUser u
    WHERE LOWER(u.EmailId) = LOWER(pLoginMail);
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetUserCredentialById
-- Purpose: Returns the stored password hash for a user identifier
--
-- Parameters:
--   pUserId - the user identifier
--
-- Returns: One credential row, or nothing when the account does not exist
--
-- Called By: UserCredentialRepo.GetByUserId() -> AuthSvc.ChangePassword()
-- ============================================================================
DROP FUNCTION IF EXISTS GetUserCredentialById(BIGINT);
CREATE FUNCTION GetUserCredentialById(pUserId BIGINT)
RETURNS TABLE (
    UserId BIGINT,
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    MustChangePassword BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT u.UserId, u.EmailId, u.LoginPass, u.UserRole, u.MustChangePassword
    FROM BlogUser u
    WHERE u.UserId = pUserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: UpdateUserPassword
-- Purpose: Rotates the stored password hash and the forced-change flag
--
-- Parameters:
--   pUserId             - the account to update
--   pLoginPass          - the new PBKDF2 hash
--   pMustChangePassword - whether the user must change it at next sign-in
--
-- Returns: Number of rows updated (0 or 1)
--
-- Called By: UserCredentialRepo.UpdatePasswordHash()
--
-- Note: deliberately narrow - a silent re-hash during login must not overwrite
--       profile columns the caller never loaded.
-- ============================================================================
CREATE OR REPLACE FUNCTION UpdateUserPassword(
    pUserId BIGINT,
    pLoginPass VARCHAR(255),
    pMustChangePassword BOOLEAN
)
RETURNS INTEGER AS $$
DECLARE
    vRowCount INTEGER;
BEGIN
    UPDATE BlogUser
    SET LoginPass = pLoginPass,
        MustChangePassword = pMustChangePassword,
        UpdatedOn = NOW()
    WHERE UserId = pUserId;

    GET DIAGNOSTICS vRowCount = ROW_COUNT;
    RETURN vRowCount;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- PART E: Password-reset token functions  [REQ-NFR-019]
-- ============================================================================

-- ============================================================================
-- FUNCTION: InsertPasswordResetToken
-- Purpose: Persists a newly issued reset token
--
-- Parameters:
--   pUserId    - account the token resets
--   pToken     - opaque URL-safe token string
--   pCreatedAt - issue timestamp (UTC)
--   pExpiresAt - expiry timestamp (UTC)
--
-- Returns: The generated TokenId
--
-- Called By: PasswordResetTokenRepo.InsertToGetId()
-- ============================================================================
CREATE OR REPLACE FUNCTION InsertPasswordResetToken(
    pUserId BIGINT,
    pToken VARCHAR(255),
    pCreatedAt TIMESTAMP,
    pExpiresAt TIMESTAMP
)
RETURNS BIGINT AS $$
DECLARE
    vTokenId BIGINT;
BEGIN
    INSERT INTO PasswordResetToken (UserId, Token, CreatedAt, ExpiresAt, IsUsed)
    VALUES (pUserId, pToken, pCreatedAt, pExpiresAt, FALSE)
    RETURNING TokenId INTO vTokenId;

    RETURN vTokenId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPasswordResetTokenByToken
-- Purpose: Resolves a reset token by its opaque string
--
-- Parameters:
--   pToken - the token taken from the reset link
--
-- Returns: The token row regardless of expiry or used state, so the caller can
--          tell "expired" from "already used" from "unknown"
--
-- Called By: PasswordResetTokenRepo.GetByToken()
-- ============================================================================
DROP FUNCTION IF EXISTS GetPasswordResetTokenByToken(VARCHAR);
CREATE FUNCTION GetPasswordResetTokenByToken(pToken VARCHAR(255))
RETURNS TABLE (
    TokenId BIGINT,
    UserId BIGINT,
    Token VARCHAR(255),
    CreatedAt TIMESTAMP,
    ExpiresAt TIMESTAMP,
    IsUsed BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT t.TokenId, t.UserId, t.Token, t.CreatedAt, t.ExpiresAt, t.IsUsed
    FROM PasswordResetToken t
    WHERE t.Token = pToken;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPasswordResetTokenById
-- Purpose: Loads a reset token by its primary key
--
-- Parameters:
--   pTokenId - the token identifier
--
-- Returns: The token row, or nothing
--
-- Called By: PasswordResetTokenRepo.GetSingle()
-- ============================================================================
DROP FUNCTION IF EXISTS GetPasswordResetTokenById(BIGINT);
CREATE FUNCTION GetPasswordResetTokenById(pTokenId BIGINT)
RETURNS TABLE (
    TokenId BIGINT,
    UserId BIGINT,
    Token VARCHAR(255),
    CreatedAt TIMESTAMP,
    ExpiresAt TIMESTAMP,
    IsUsed BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT t.TokenId, t.UserId, t.Token, t.CreatedAt, t.ExpiresAt, t.IsUsed
    FROM PasswordResetToken t
    WHERE t.TokenId = pTokenId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPasswordResetTokenByUser
-- Purpose: Lists every reset token issued to one account, newest first
--
-- Parameters:
--   pUserId - the owning account
--
-- Returns: The user's token rows
--
-- Called By: PasswordResetTokenRepo.GetAllById()
-- ============================================================================
DROP FUNCTION IF EXISTS GetPasswordResetTokenByUser(BIGINT);
CREATE FUNCTION GetPasswordResetTokenByUser(pUserId BIGINT)
RETURNS TABLE (
    TokenId BIGINT,
    UserId BIGINT,
    Token VARCHAR(255),
    CreatedAt TIMESTAMP,
    ExpiresAt TIMESTAMP,
    IsUsed BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT t.TokenId, t.UserId, t.Token, t.CreatedAt, t.ExpiresAt, t.IsUsed
    FROM PasswordResetToken t
    WHERE t.UserId = pUserId
    ORDER BY t.TokenId DESC;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: MarkPasswordResetTokenUsed
-- Purpose: Consumes a reset token so it cannot be replayed
--
-- Parameters:
--   pTokenId - the token to consume
--
-- Returns: Number of rows updated (0 or 1)
--
-- Called By: PasswordResetTokenRepo.MarkUsed()
-- ============================================================================
CREATE OR REPLACE FUNCTION MarkPasswordResetTokenUsed(pTokenId BIGINT)
RETURNS INTEGER AS $$
DECLARE
    vRowCount INTEGER;
BEGIN
    UPDATE PasswordResetToken
    SET IsUsed = TRUE
    WHERE TokenId = pTokenId;

    GET DIAGNOSTICS vRowCount = ROW_COUNT;
    RETURN vRowCount;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: DeleteExpiredPasswordResetToken
-- Purpose: Removes tokens whose expiry has passed
--
-- Returns: Number of rows deleted
--
-- Called By: PasswordResetTokenRepo.DeleteExpiredTokens()
-- ============================================================================
CREATE OR REPLACE FUNCTION DeleteExpiredPasswordResetToken()
RETURNS INTEGER AS $$
DECLARE
    vRowCount INTEGER;
BEGIN
    DELETE FROM PasswordResetToken
    WHERE ExpiresAt < NOW();

    GET DIAGNOSTICS vRowCount = ROW_COUNT;
    RETURN vRowCount;
END;
$$ LANGUAGE plpgsql;
