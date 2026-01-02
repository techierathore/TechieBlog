-- ============================================================================
-- Script: 005-ResumeAndImageManagement.sql
-- Purpose: Resume Page and Image Management Extensions
-- Author: James (Dev Agent)
-- Created: 2026-01-02
-- Epic: Image/Resume/Multi-Author Feature Implementation
-- ============================================================================

-- ============================================================================
-- PART A: Extend BlogImage for categorization
-- Purpose: Adds categorization, accessibility, and metadata fields to images
--
-- Business Rules:
--   - Category allows organizing images (general, profile, hero, resume, etc.)
--   - AltText provides accessibility for screen readers
--   - MimeType enables proper content-type headers
--   - Width/Height support responsive image handling
-- ============================================================================
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Category VARCHAR(50) DEFAULT 'general';
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS AltText VARCHAR(255);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS MimeType VARCHAR(100);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Width INT;
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Height INT;

-- Index for image category lookups
CREATE INDEX IF NOT EXISTS IdxBlogImageCategory ON BlogImage(Category);

-- ============================================================================
-- PART B: Extend BlogUser for Multi-Author and Resume
-- Purpose: Adds fields for multi-author support and resume page functionality
--
-- Business Rules:
--   - Username provides a unique, URL-friendly identifier for authors
--   - IsSiteOwner indicates the primary site administrator (only one allowed)
--   - Title/Tagline are for professional resume display
--   - InstagramUrl extends social media links
--   - PhoneNumber/Location for contact info on resume
--   - CVFilePath stores path to downloadable CV/resume file
--   - ResumeEnabled controls whether user's resume page is public
-- ============================================================================
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Username VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS IsSiteOwner BOOLEAN DEFAULT FALSE;
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Title VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Tagline VARCHAR(500);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS InstagramUrl VARCHAR(255);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS PhoneNumber VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Location VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS CVFilePath VARCHAR(550);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS ResumeEnabled BOOLEAN DEFAULT FALSE;

-- Unique index for username lookups (partial - only where username is set)
CREATE UNIQUE INDEX IF NOT EXISTS IdxBlogUserUsername ON BlogUser(Username) WHERE Username IS NOT NULL;

-- Unique partial index to ensure only one site owner exists
-- This constraint allows only a single row with IsSiteOwner = TRUE
CREATE UNIQUE INDEX IF NOT EXISTS IdxSingleSiteOwner ON BlogUser ((CASE WHEN IsSiteOwner = TRUE THEN 1 END)) WHERE IsSiteOwner = TRUE;

-- ============================================================================
-- PART C: Extend UserEvents for Experience Timeline
-- Purpose: Enhances UserEvents table to support resume experience timeline
--
-- Business Rules:
--   - StartDate captures when an experience/position began
--   - Description allows detailed text about the role/experience
--   - DisplayOrder controls the order shown on resume page
--   - IsCurrent indicates ongoing/present positions
-- ============================================================================
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS StartDate TIMESTAMP;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS Description TEXT;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS DisplayOrder INT DEFAULT 0;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS IsCurrent BOOLEAN DEFAULT FALSE;

-- ============================================================================
-- PART D: Create UserSkills Table
-- Purpose: Stores user skills organized by category for resume display
--
-- Relationships:
--   - BlogUser (UserId) - The user who has these skills
--
-- Business Rules:
--   - Skills are grouped by Category (e.g., Languages, Frameworks, Tools)
--   - SkillName is the display name of the skill
--   - IconPath allows for skill icons/logos
--   - DisplayOrder controls ordering within categories
-- ============================================================================
CREATE TABLE IF NOT EXISTS UserSkills (
    -- Primary identifier, auto-generated
    SkillId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- Category grouping (e.g., Languages, Frameworks, Databases)
    Category VARCHAR(100) NOT NULL,

    -- Name of the skill
    SkillName VARCHAR(150) NOT NULL,

    -- Optional path to skill icon/logo
    IconPath VARCHAR(350),

    -- Order for display within category
    DisplayOrder INT DEFAULT 0,

    -- When the skill record was created
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Index for user skills lookups
CREATE INDEX IF NOT EXISTS IdxUserSkillsUserId ON UserSkills(UserId);

-- Index for skills by category
CREATE INDEX IF NOT EXISTS IdxUserSkillsCategory ON UserSkills(Category);

-- ============================================================================
-- PART E: Create UserAwards Table
-- Purpose: Stores user awards, certifications, and achievements for resume
--
-- Relationships:
--   - BlogUser (UserId) - The user who earned the award
--
-- Business Rules:
--   - AwardTitle is the name of the award/certification
--   - AwardDescription provides details about the achievement
--   - BadgeImagePath allows displaying award badges/logos
--   - AwardUrl links to verification or award details
--   - AwardYear captures when the award was received
--   - DisplayOrder controls the order on the resume page
-- ============================================================================
CREATE TABLE IF NOT EXISTS UserAwards (
    -- Primary identifier, auto-generated
    AwardId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- Title of the award/certification
    AwardTitle VARCHAR(255) NOT NULL,

    -- Description of the award
    AwardDescription TEXT,

    -- Path to badge/certificate image
    BadgeImagePath VARCHAR(550),

    -- URL to award verification or details
    AwardUrl VARCHAR(350),

    -- Year the award was received
    AwardYear VARCHAR(50),

    -- Order for display
    DisplayOrder INT DEFAULT 0,

    -- When the record was created
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Index for user awards lookups
CREATE INDEX IF NOT EXISTS IdxUserAwardsUserId ON UserAwards(UserId);

-- ============================================================================
-- PART F: Create UserStats Table
-- Purpose: Stores user statistics for resume display (years of experience, etc.)
--
-- Relationships:
--   - BlogUser (UserId) - The user these stats belong to
--
-- Business Rules:
--   - StatLabel is the display label (e.g., "Years of Experience")
--   - StatValue is the value to display (e.g., "15+")
--   - StatCategory allows grouping stats
--   - DisplayOrder controls the order on the resume page
-- ============================================================================
CREATE TABLE IF NOT EXISTS UserStats (
    -- Primary identifier, auto-generated
    StatId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- Label for the stat (e.g., "Years of Experience")
    StatLabel VARCHAR(100) NOT NULL,

    -- Value of the stat (e.g., "15+")
    StatValue VARCHAR(50) NOT NULL,

    -- Optional category for grouping
    StatCategory VARCHAR(50),

    -- Order for display
    DisplayOrder INT DEFAULT 0
);

-- Index for user stats lookups
CREATE INDEX IF NOT EXISTS IdxUserStatsUserId ON UserStats(UserId);

-- ============================================================================
-- End of Migration
-- ============================================================================
