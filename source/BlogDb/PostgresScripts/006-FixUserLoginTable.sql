-- ============================================================================
-- Script: 006-FixUserLoginTable.sql
-- Purpose: Recreate UserLogin table to match application model
-- Author: James (Dev Agent)
-- Created: 2025-12-23
-- ============================================================================

-- Drop existing tables (both old and potential new)
DROP TABLE IF EXISTS userlogin CASCADE;
DROP TABLE IF EXISTS userlogins CASCADE;

-- ============================================================================
-- TABLE: userlogins
-- Purpose: Tracks active login sessions and JWT tokens
-- Columns match the BlogModels.UserLogin class
-- ============================================================================
CREATE TABLE userlogins (
    loginid BIGSERIAL PRIMARY KEY,
    userid BIGINT NOT NULL REFERENCES bloguser(userid),
    logindate TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    logintoken TEXT NOT NULL,
    tokenstatus VARCHAR(50) NOT NULL DEFAULT 'ValidToken',
    exiprydate TIMESTAMP NOT NULL,
    issuedate TIMESTAMP NOT NULL
);

-- Index for user session lookups
CREATE INDEX idx_userlogins_userid ON userlogins(userid);

-- Index for token validation
CREATE INDEX idx_userlogins_token ON userlogins(logintoken);

-- ============================================================================
-- Verify table creation
-- ============================================================================
SELECT 'userlogins table created successfully' AS status;
