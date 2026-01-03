-- ============================================================================
-- Script: 011-PopulateUsernames.sql
-- Purpose: Populate empty Username fields for existing users
-- Author: Quinn (QA Agent)
-- Created: 2026-01-02
-- Issue: /author/{username} route returns 404 due to empty usernames
-- ============================================================================

-- ============================================================================
-- PART A: Generate usernames from email addresses
-- Strategy: Use the part before @ in email, make it URL-friendly
--           If collision occurs, append user ID
-- ============================================================================

-- Create a function to generate a URL-friendly slug from text
CREATE OR REPLACE FUNCTION generate_username_slug(input_text TEXT)
RETURNS TEXT AS $$
BEGIN
    RETURN LOWER(
        REGEXP_REPLACE(
            REGEXP_REPLACE(
                COALESCE(input_text, ''),
                '[^a-zA-Z0-9]+', '-', 'g'  -- Replace non-alphanumeric with hyphens
            ),
            '^-+|-+$', '', 'g'  -- Trim leading/trailing hyphens
        )
    );
END;
$$ LANGUAGE plpgsql;

-- Update users with empty usernames using email prefix
UPDATE BlogUser
SET Username = generate_username_slug(SPLIT_PART(EmailId, '@', 1))
WHERE Username IS NULL OR Username = '';

-- Handle any collisions by appending user ID
WITH duplicates AS (
    SELECT UserId, Username,
           ROW_NUMBER() OVER (PARTITION BY LOWER(Username) ORDER BY UserId) as rn
    FROM BlogUser
    WHERE Username IS NOT NULL AND Username != ''
)
UPDATE BlogUser bu
SET Username = bu.Username || '-' || bu.UserId::TEXT
FROM duplicates d
WHERE bu.UserId = d.UserId AND d.rn > 1;

-- ============================================================================
-- PART B: Set site owner if none exists
-- The first user (lowest UserId) becomes site owner by default
-- ============================================================================

UPDATE BlogUser
SET IsSiteOwner = TRUE
WHERE UserId = (SELECT MIN(UserId) FROM BlogUser)
  AND NOT EXISTS (SELECT 1 FROM BlogUser WHERE IsSiteOwner = TRUE);

-- ============================================================================
-- PART C: Enable resume for site owner by default
-- ============================================================================

UPDATE BlogUser
SET ResumeEnabled = TRUE
WHERE IsSiteOwner = TRUE AND (ResumeEnabled IS NULL OR ResumeEnabled = FALSE);

-- ============================================================================
-- Cleanup: Drop the helper function
-- ============================================================================
DROP FUNCTION IF EXISTS generate_username_slug(TEXT);

-- ============================================================================
-- End of Migration
-- ============================================================================
