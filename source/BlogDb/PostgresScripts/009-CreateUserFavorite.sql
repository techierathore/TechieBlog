-- ============================================================================
-- Script: 009-CreateUserFavorite.sql
-- Purpose: Creates UserFavorite table for user bookmarks/favorites feature
-- Author: James (Dev Agent)
-- Created: 2025-12-30
-- Epic: Epic 4 - Engagement & Social Features
-- Story: FIX-014 - Favorites/Bookmarks (FR17)
-- ============================================================================

-- ============================================================================
-- TABLE: UserFavorite
-- Purpose: Stores user-post favorite/bookmark relationships
--
-- Relationships:
--   - BlogUser (UserId) - The user who favorited the post
--   - Post (PostId) - The favorited post
--
-- Business Rules:
--   - Each user can only favorite a post once (unique constraint)
--   - Favorites are deleted when user or post is deleted (CASCADE)
--   - CreatedOn tracks when the favorite was added for sorting
-- ============================================================================
CREATE TABLE IF NOT EXISTS UserFavorite (
    -- Primary identifier, auto-generated
    FavoriteId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogPost - the favorited post
    PostId BIGINT NOT NULL REFERENCES BlogPost(PostId) ON DELETE CASCADE,

    -- Foreign key to BlogUser - the user who favorited
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId) ON DELETE CASCADE,

    -- When the favorite was created
    CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Unique constraint to prevent duplicate favorites
    CONSTRAINT UQ_UserFavorite_User_Post UNIQUE (PostId, UserId)
);

-- Index for post favorite lookups (e.g., count favorites for a post)
CREATE INDEX IF NOT EXISTS IX_UserFavorite_PostId ON UserFavorite(PostId);

-- Index for user's favorites list
CREATE INDEX IF NOT EXISTS IX_UserFavorite_UserId ON UserFavorite(UserId);

-- Index for user's favorites sorted by date (most recent first)
CREATE INDEX IF NOT EXISTS IX_UserFavorite_CreatedOn ON UserFavorite(UserId, CreatedOn DESC);
