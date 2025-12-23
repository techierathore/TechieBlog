-- ============================================================================
-- Script: 007-FixBlogSeriesAndPostTag.sql
-- Purpose: Fixes BlogSeries table columns and adds PostTag junction table
-- Author: Claude Code
-- Created: 2025-12-23
-- ============================================================================

-- ============================================================================
-- FIX: BlogSeries table - Add missing columns required by BlogSeriesRepo
-- The repo expects: SeriesId, Name, Slug, Description, Status, AuthorId,
--                   CreatedOn, UpdatedOn
-- But the table only has: SeriesId, Name, Slug, Description, CreatedOn,
--                         UpdatedOn, IsActive
-- ============================================================================

-- Add AuthorId column (foreign key to BlogUser)
ALTER TABLE BlogSeries ADD COLUMN IF NOT EXISTS AuthorId BIGINT;

-- Add Status column (replaces IsActive for more descriptive status)
ALTER TABLE BlogSeries ADD COLUMN IF NOT EXISTS Status VARCHAR(50) DEFAULT 'In Progress';

-- Add foreign key constraint for AuthorId
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'blogseries_authorid_fkey'
    ) THEN
        ALTER TABLE BlogSeries ADD CONSTRAINT blogseries_authorid_fkey
            FOREIGN KEY (AuthorId) REFERENCES BlogUser(UserId) ON DELETE SET NULL;
    END IF;
EXCEPTION
    WHEN others THEN NULL;
END $$;

-- Create index on AuthorId for faster lookups
CREATE INDEX IF NOT EXISTS IdxBlogSeriesAuthorId ON BlogSeries(AuthorId);

-- ============================================================================
-- CREATE: PostTag junction table for many-to-many relationship between
-- BlogPost and Tag
-- ============================================================================
CREATE TABLE IF NOT EXISTS PostTag (
    -- Foreign key to BlogPost
    PostId BIGINT NOT NULL,

    -- Foreign key to Tag
    TagId BIGINT NOT NULL,

    -- Composite primary key prevents duplicate assignments
    PRIMARY KEY (PostId, TagId)
);

-- Add foreign key constraints if they don't exist
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'posttag_postid_fkey'
    ) THEN
        ALTER TABLE PostTag ADD CONSTRAINT posttag_postid_fkey
            FOREIGN KEY (PostId) REFERENCES BlogPost(PostId) ON DELETE CASCADE;
    END IF;
EXCEPTION
    WHEN others THEN NULL;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'posttag_tagid_fkey'
    ) THEN
        ALTER TABLE PostTag ADD CONSTRAINT posttag_tagid_fkey
            FOREIGN KEY (TagId) REFERENCES Tag(TagId) ON DELETE CASCADE;
    END IF;
EXCEPTION
    WHEN others THEN NULL;
END $$;

-- Create indexes for faster lookups
CREATE INDEX IF NOT EXISTS IdxPostTagPostId ON PostTag(PostId);
CREATE INDEX IF NOT EXISTS IdxPostTagTagId ON PostTag(TagId);
