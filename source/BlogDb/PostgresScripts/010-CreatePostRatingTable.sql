-- ============================================================================
-- Script: 009-CreatePostRatingTable.sql
-- Purpose: Creates PostRating table for star rating system (Epic 4, FR15-16)
-- Author: James (Dev Agent)
-- Created: 2025-12-30
-- Story: FIX-013 - Star Ratings Implementation
-- ============================================================================

-- ============================================================================
-- TABLE: PostRating
-- Purpose: Stores user ratings for blog posts (1-5 stars)
--
-- Relationships:
--   - Post (PostId) - The rated post
--   - BlogUser (UserId) - The user who rated
--
-- Business Rules:
--   - Each user can rate each post once (unique constraint)
--   - Rating must be between 1 and 5
--   - Users can update their existing rating
-- ============================================================================
CREATE TABLE IF NOT EXISTS PostRating (
    -- Primary identifier, auto-generated
    RatingId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogPost - the rated post
    PostId BIGINT NOT NULL REFERENCES BlogPost(PostId) ON DELETE CASCADE,

    -- Foreign key to BlogUser - the user who rated
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId) ON DELETE CASCADE,

    -- Rating value (1-5 stars)
    Rating SMALLINT NOT NULL CHECK (Rating >= 1 AND Rating <= 5),

    -- When the rating was first created
    CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- When the rating was last updated (null if never updated)
    UpdatedOn TIMESTAMP,

    -- Unique constraint: one rating per user per post
    CONSTRAINT UQ_PostRating_User_Post UNIQUE (PostId, UserId)
);

-- Index for post rating lookups (get all ratings for a post)
CREATE INDEX IF NOT EXISTS IX_PostRating_PostId ON PostRating(PostId);

-- Index for user rating lookups (get all ratings by a user)
CREATE INDEX IF NOT EXISTS IX_PostRating_UserId ON PostRating(UserId);

-- Index for average rating calculations (grouped by post)
CREATE INDEX IF NOT EXISTS IX_PostRating_PostId_Rating ON PostRating(PostId, Rating);
