-- ============================================================================
-- Script: 014-AnonymousEngagement.sql
-- Purpose: Re-keys comments and ratings from a signed-in user id to an
--          anonymous name + email identity, and adds the persisted
--          double opt-in email-verification store.
-- Author:  flow-master (Cluster B builder)
-- Created: 2026-08-06
-- REQs:    REQ-FN-022 (anonymous comments + moderation workflow)
--          REQ-FN-023 (one rating per EMAIL per post)
--          REQ-FN-048 (persisted double opt-in verification tokens)
--          REQ-FN-049 (self-hosted captcha - no schema, listed for traceability)
--
-- Changes:
--   PART A - BlogComment: UserId (nullable), IsEmailVerified, ModerationStatus,
--            VerifiedOn, AuthorIpAddress, AuthorUserAgent + supporting indexes.
--   PART B - PostRating: Email, IsEmailVerified, UserId made nullable, legacy
--            (PostId, UserId) unique constraint replaced by (PostId, LOWER(Email)).
--   PART C - EmailVerificationToken: new table, single-use 24 h tokens.
--   PART D - VerifiedEmail: new table, registry of confirmed addresses.
--   PART E - Stored functions for atomic token consumption, verified-address
--            upsert, email-keyed rating upsert and comment verification.
--
-- Dependencies:
--   001-CreateTables.sql   (BlogComment, BlogUser)
--   004-FixPostTable.sql   (BlogPost)
--   010-CreatePostRatingTable.sql (PostRating)
--
-- Idempotency:
--   Every statement is guarded (IF NOT EXISTS / CREATE OR REPLACE / NULL-only
--   back-fills), because DbUp scripts may be replayed against a live database.
--
-- Rollback:
--   DROP FUNCTION IF EXISTS ConsumeEmailVerificationToken(VARCHAR);
--   DROP FUNCTION IF EXISTS RecordVerifiedEmail(VARCHAR, VARCHAR);
--   DROP FUNCTION IF EXISTS UpsertPostRatingByEmail(BIGINT, VARCHAR, SMALLINT, BIGINT, BOOLEAN);
--   DROP FUNCTION IF EXISTS MarkCommentEmailVerified(BIGINT);
--   DROP TABLE IF EXISTS EmailVerificationToken;
--   DROP TABLE IF EXISTS VerifiedEmail;
--   DROP INDEX IF EXISTS IdxPostRatingPostEmail;
--   ALTER TABLE PostRating DROP COLUMN IF EXISTS Email, DROP COLUMN IF EXISTS IsEmailVerified;
--   ALTER TABLE BlogComment DROP COLUMN IF EXISTS UserId, DROP COLUMN IF EXISTS IsEmailVerified,
--       DROP COLUMN IF EXISTS ModerationStatus, DROP COLUMN IF EXISTS VerifiedOn,
--       DROP COLUMN IF EXISTS AuthorIpAddress, DROP COLUMN IF EXISTS AuthorUserAgent;
-- ============================================================================


-- ============================================================================
-- PART A: BlogComment - anonymous identity and moderation workflow
--
-- Business Rules:
--   - GivenBy / Email already hold the commenter's name and address; they are
--     now the PRIMARY identity. UserId is an OPTIONAL back-link for the case
--     where the commenter happened to be signed in.
--   - ModerationStatus drives visibility and is the single source of truth:
--       PendingVerification -> the address has not been confirmed yet.
--                              NEVER visible publicly, NEVER in the queue.
--       PendingApproval     -> address confirmed; sitting in the moderation queue.
--       Approved            -> visible publicly (Published is kept in sync).
--       Rejected / Spam     -> never visible.
--   - Published stays for backward compatibility and mirrors Approved.
--   - AuthorIpAddress / AuthorUserAgent are retained for abuse forensics.
-- ============================================================================
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS UserId BIGINT;
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS VerifiedOn TIMESTAMP;
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS AuthorIpAddress VARCHAR(45);
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS AuthorUserAgent VARCHAR(500);

-- IsEmailVerified: added nullable, back-filled TRUE for pre-existing rows
-- (they predate double opt-in), then locked down to NOT NULL DEFAULT FALSE.
-- Re-running the script is a no-op because no NULLs remain.
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS IsEmailVerified BOOLEAN;
UPDATE BlogComment SET IsEmailVerified = TRUE WHERE IsEmailVerified IS NULL;
ALTER TABLE BlogComment ALTER COLUMN IsEmailVerified SET DEFAULT FALSE;
ALTER TABLE BlogComment ALTER COLUMN IsEmailVerified SET NOT NULL;

-- ModerationStatus: same guarded pattern. Legacy rows map from Published.
ALTER TABLE BlogComment ADD COLUMN IF NOT EXISTS ModerationStatus VARCHAR(30);
UPDATE BlogComment
   SET ModerationStatus = CASE WHEN Published THEN 'Approved' ELSE 'PendingApproval' END
 WHERE ModerationStatus IS NULL;
ALTER TABLE BlogComment ALTER COLUMN ModerationStatus SET DEFAULT 'PendingVerification';
ALTER TABLE BlogComment ALTER COLUMN ModerationStatus SET NOT NULL;

-- Legacy inserts wrote 0 instead of NULL for top-level comments; normalise.
UPDATE BlogComment SET ParentCommentId = NULL WHERE ParentCommentId = 0;

CREATE INDEX IF NOT EXISTS IdxBlogCommentModerationStatus ON BlogComment(ModerationStatus);
CREATE INDEX IF NOT EXISTS IdxBlogCommentEmail ON BlogComment(LOWER(Email));
CREATE INDEX IF NOT EXISTS IdxBlogCommentUserId ON BlogComment(UserId);
CREATE INDEX IF NOT EXISTS IdxBlogCommentPostStatus ON BlogComment(PostId, ModerationStatus);


-- ============================================================================
-- PART B: PostRating - re-key from UserId to Email
--
-- Business Rules:
--   - One rating per EMAIL per post; the rating is changeable.
--   - UserId becomes optional (anonymous raters have none).
--   - Only rows with IsEmailVerified = TRUE contribute to the public
--     average / count aggregates.
-- ============================================================================
ALTER TABLE PostRating ADD COLUMN IF NOT EXISTS Email VARCHAR(320);

ALTER TABLE PostRating ADD COLUMN IF NOT EXISTS IsEmailVerified BOOLEAN;
UPDATE PostRating SET IsEmailVerified = TRUE WHERE IsEmailVerified IS NULL;
ALTER TABLE PostRating ALTER COLUMN IsEmailVerified SET DEFAULT FALSE;
ALTER TABLE PostRating ALTER COLUMN IsEmailVerified SET NOT NULL;

-- Back-fill Email from the signed-in user that produced each legacy rating.
-- BlogUser stores the address in EmailId, not Email.
UPDATE PostRating r
   SET Email = u.EmailId
  FROM BlogUser u
 WHERE r.UserId = u.UserId
   AND r.Email IS NULL;

-- Anonymous raters have no user id.
ALTER TABLE PostRating ALTER COLUMN UserId DROP NOT NULL;

-- Replace the legacy per-user uniqueness with per-email uniqueness.
ALTER TABLE PostRating DROP CONSTRAINT IF EXISTS UQ_PostRating_User_Post;
CREATE UNIQUE INDEX IF NOT EXISTS IdxPostRatingPostEmail
    ON PostRating (PostId, LOWER(Email))
    WHERE Email IS NOT NULL;
CREATE INDEX IF NOT EXISTS IdxPostRatingEmail ON PostRating (LOWER(Email));


-- ============================================================================
-- PART C: EmailVerificationToken - persisted double opt-in tokens
--
-- Purpose: Replaces the in-memory token store used for password reset with a
--          DURABLE one, so a verification link survives an application restart.
--
-- Business Rules:
--   - Token is a cryptographically random, URL-safe string; unique.
--   - ExpiresOn is IssuedOn + 24 hours.
--   - A token works EXACTLY ONCE - consumption flips IsUsed inside the same
--     statement that selects it (see ConsumeEmailVerificationToken).
--   - Purpose is one of: Comment, Rating, Subscription.
--   - TargetId points at the pending BlogComment / PostRating / Subscriber row.
-- ============================================================================
CREATE TABLE IF NOT EXISTS EmailVerificationToken (
    -- Primary identifier, auto-generated
    TokenId BIGSERIAL PRIMARY KEY,

    -- URL-safe random token handed to the recipient in the verification link
    Token VARCHAR(128) NOT NULL,

    -- Address being confirmed
    Email VARCHAR(320) NOT NULL,

    -- What the confirmation unlocks: Comment | Rating | Subscription
    Purpose VARCHAR(30) NOT NULL,

    -- Row awaiting confirmation (BlogComment.CommentId, PostRating.RatingId, ...)
    TargetId BIGINT,

    -- Name supplied with the submission, echoed back into the email
    DisplayName VARCHAR(150),

    -- When the token was issued
    IssuedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Hard expiry (issue time + 24 hours)
    ExpiresOn TIMESTAMP NOT NULL,

    -- When the token was consumed (null while unused)
    ConsumedOn TIMESTAMP,

    -- Single-use flag
    IsUsed BOOLEAN NOT NULL DEFAULT FALSE,

    -- Origin of the request, for abuse forensics
    RequestIpAddress VARCHAR(45)
);

CREATE UNIQUE INDEX IF NOT EXISTS IdxEmailVerificationTokenToken ON EmailVerificationToken(Token);
CREATE INDEX IF NOT EXISTS IdxEmailVerificationTokenEmail ON EmailVerificationToken(LOWER(Email));
CREATE INDEX IF NOT EXISTS IdxEmailVerificationTokenExpiresOn ON EmailVerificationToken(ExpiresOn);


-- ============================================================================
-- PART D: VerifiedEmail - registry of confirmed addresses
--
-- Purpose: Once an address has completed double opt-in, later submissions from
--          it skip the confirmation step entirely.
--
-- Business Rules:
--   - Email is unique, case-insensitively.
--   - IsBlocked lets an administrator ban an abusive address without deleting
--     its history; a blocked address is treated as NOT verified.
-- ============================================================================
CREATE TABLE IF NOT EXISTS VerifiedEmail (
    -- Primary identifier, auto-generated
    VerifiedEmailId BIGSERIAL PRIMARY KEY,

    -- The confirmed address
    Email VARCHAR(320) NOT NULL,

    -- Most recent display name seen for this address
    DisplayName VARCHAR(150),

    -- When the address was first confirmed
    VerifiedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- When the address last submitted something
    LastUsedOn TIMESTAMP,

    -- Administrative ban flag
    IsBlocked BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE UNIQUE INDEX IF NOT EXISTS IdxVerifiedEmailEmail ON VerifiedEmail(LOWER(Email));


-- ============================================================================
-- PART E: Stored functions
-- ============================================================================

-- ----------------------------------------------------------------------------
-- ConsumeEmailVerificationToken
-- Purpose: Atomically redeem a verification token exactly once.
-- Business Logic: The UPDATE ... RETURNING both checks and flips the state in a
--   single statement, so two concurrent requests cannot both redeem the token.
--   Returns zero rows when the token is unknown, already used or expired.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION ConsumeEmailVerificationToken(pToken VARCHAR)
RETURNS TABLE (
    TokenId BIGINT,
    Token VARCHAR,
    Email VARCHAR,
    Purpose VARCHAR,
    TargetId BIGINT,
    DisplayName VARCHAR,
    IssuedOn TIMESTAMP,
    ExpiresOn TIMESTAMP,
    ConsumedOn TIMESTAMP,
    IsUsed BOOLEAN
) AS $$
BEGIN
    -- The data-modifying CTE is what makes this atomic: the row is selected and
    -- flipped by one statement, so two concurrent clicks cannot both redeem it.
    RETURN QUERY
    WITH Consumed AS (
        UPDATE EmailVerificationToken t
           SET IsUsed = TRUE,
               ConsumedOn = CURRENT_TIMESTAMP
         WHERE t.Token = pToken
           AND t.IsUsed = FALSE
           AND t.ExpiresOn > CURRENT_TIMESTAMP
        RETURNING t.TokenId, t.Token, t.Email, t.Purpose, t.TargetId,
                  t.DisplayName, t.IssuedOn, t.ExpiresOn, t.ConsumedOn, t.IsUsed
    )
    SELECT Consumed.TokenId, Consumed.Token, Consumed.Email, Consumed.Purpose,
           Consumed.TargetId, Consumed.DisplayName, Consumed.IssuedOn,
           Consumed.ExpiresOn, Consumed.ConsumedOn, Consumed.IsUsed
      FROM Consumed;
END;
$$ LANGUAGE plpgsql;


-- ----------------------------------------------------------------------------
-- RecordVerifiedEmail
-- Purpose: Insert or refresh the verified-address registry entry.
-- Business Logic: Case-insensitive match on Email; refreshes LastUsedOn and the
--   display name when the address is already known. Returns the row id.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION RecordVerifiedEmail(pEmail VARCHAR, pDisplayName VARCHAR)
RETURNS BIGINT AS $$
DECLARE
    vVerifiedEmailId BIGINT;
BEGIN
    SELECT v.VerifiedEmailId INTO vVerifiedEmailId
      FROM VerifiedEmail v
     WHERE LOWER(v.Email) = LOWER(pEmail);

    IF vVerifiedEmailId IS NULL THEN
        INSERT INTO VerifiedEmail (Email, DisplayName, VerifiedOn, LastUsedOn)
        VALUES (pEmail, pDisplayName, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
        RETURNING VerifiedEmail.VerifiedEmailId INTO vVerifiedEmailId;
    ELSE
        UPDATE VerifiedEmail
           SET LastUsedOn = CURRENT_TIMESTAMP,
               DisplayName = COALESCE(pDisplayName, DisplayName)
         WHERE VerifiedEmail.VerifiedEmailId = vVerifiedEmailId;
    END IF;

    RETURN vVerifiedEmailId;
END;
$$ LANGUAGE plpgsql;


-- ----------------------------------------------------------------------------
-- UpsertPostRatingByEmail
-- Purpose: Enforce "one rating per email per post, changeable" in one round trip.
-- Business Logic: Looks the rating up case-insensitively by (PostId, Email).
--   Inserts when absent, otherwise updates the score and stamps UpdatedOn.
--   Verification is sticky - an address that has been verified once stays so.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION UpsertPostRatingByEmail(
    pPostId BIGINT,
    pEmail VARCHAR,
    pRating SMALLINT,
    pUserId BIGINT,
    pIsEmailVerified BOOLEAN)
RETURNS BIGINT AS $$
DECLARE
    vRatingId BIGINT;
BEGIN
    SELECT r.RatingId INTO vRatingId
      FROM PostRating r
     WHERE r.PostId = pPostId
       AND LOWER(r.Email) = LOWER(pEmail);

    IF vRatingId IS NULL THEN
        INSERT INTO PostRating (PostId, UserId, Email, Rating, IsEmailVerified, CreatedOn)
        VALUES (pPostId, pUserId, pEmail, pRating, pIsEmailVerified, CURRENT_TIMESTAMP)
        RETURNING PostRating.RatingId INTO vRatingId;
    ELSE
        UPDATE PostRating
           SET Rating = pRating,
               UpdatedOn = CURRENT_TIMESTAMP,
               UserId = COALESCE(pUserId, UserId),
               IsEmailVerified = (IsEmailVerified OR pIsEmailVerified)
         WHERE PostRating.RatingId = vRatingId;
    END IF;

    RETURN vRatingId;
END;
$$ LANGUAGE plpgsql;


-- ----------------------------------------------------------------------------
-- MarkCommentEmailVerified
-- Purpose: Move a comment out of PendingVerification into the moderation queue.
-- Business Logic: Only a PendingVerification row is promoted, so replaying a
--   consumed link can never resurrect a rejected comment. The comment stays
--   unpublished - an administrator still has to approve it.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION MarkCommentEmailVerified(pCommentId BIGINT)
RETURNS BIGINT AS $$
DECLARE
    vAffected BIGINT;
BEGIN
    UPDATE BlogComment
       SET IsEmailVerified = TRUE,
           VerifiedOn = CURRENT_TIMESTAMP,
           ModerationStatus = 'PendingApproval'
     WHERE CommentId = pCommentId
       AND ModerationStatus = 'PendingVerification';

    GET DIAGNOSTICS vAffected = ROW_COUNT;
    RETURN vAffected;
END;
$$ LANGUAGE plpgsql;


-- ----------------------------------------------------------------------------
-- MarkRatingEmailVerified
-- Purpose: Make a pending anonymous rating count towards the public aggregates.
-- ----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION MarkRatingEmailVerified(pRatingId BIGINT)
RETURNS BIGINT AS $$
DECLARE
    vAffected BIGINT;
BEGIN
    UPDATE PostRating
       SET IsEmailVerified = TRUE,
           UpdatedOn = CURRENT_TIMESTAMP
     WHERE RatingId = pRatingId
       AND IsEmailVerified = FALSE;

    GET DIAGNOSTICS vAffected = ROW_COUNT;
    RETURN vAffected;
END;
$$ LANGUAGE plpgsql;
