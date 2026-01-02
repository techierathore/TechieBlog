-- ============================================================================
-- Script: 004-FixPostTable.sql
-- Purpose: Adds missing columns and renames Post to BlogPost to match app code
-- ============================================================================

-- Add missing columns to Post table
ALTER TABLE Post ADD COLUMN IF NOT EXISTS Slug VARCHAR(300);
ALTER TABLE Post ADD COLUMN IF NOT EXISTS IsDeleted BOOLEAN DEFAULT FALSE;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS DeletedOn TIMESTAMP;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS PublishedOn TIMESTAMP;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS ScheduledPublishOn TIMESTAMP;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS CategoryId BIGINT;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS SeriesId BIGINT;
ALTER TABLE Post ADD COLUMN IF NOT EXISTS SeriesPartNumber INT;

-- Update dependent tables to reference the new table name
-- First drop the foreign key constraint on BlogComment
ALTER TABLE BlogComment DROP CONSTRAINT IF EXISTS blogcomment_postid_fkey;
ALTER TABLE PostCategory DROP CONSTRAINT IF EXISTS postcategory_postid_fkey;
ALTER TABLE PostViews DROP CONSTRAINT IF EXISTS postviews_postid_fkey;

-- Rename Post to BlogPost
ALTER TABLE Post RENAME TO BlogPost;

-- Recreate foreign key constraints with new table name
ALTER TABLE BlogComment ADD CONSTRAINT blogcomment_postid_fkey
    FOREIGN KEY (PostId) REFERENCES BlogPost(PostId);
ALTER TABLE PostCategory ADD CONSTRAINT postcategory_postid_fkey
    FOREIGN KEY (PostId) REFERENCES BlogPost(PostId);
ALTER TABLE PostViews ADD CONSTRAINT postviews_postid_fkey
    FOREIGN KEY (PostId) REFERENCES BlogPost(PostId);

-- Rename indexes to match new table name
ALTER INDEX IF EXISTS IdxPostUserId RENAME TO IdxBlogPostUserId;
ALTER INDEX IF EXISTS IdxPostPublished RENAME TO IdxBlogPostPublished;

-- Generate slugs for existing posts that don't have them
UPDATE BlogPost
SET Slug = LOWER(REGEXP_REPLACE(REGEXP_REPLACE(Title, '[^a-zA-Z0-9\s-]', '', 'g'), '\s+', '-', 'g'))
WHERE Slug IS NULL OR Slug = '';

-- Create unique index on slug
CREATE UNIQUE INDEX IF NOT EXISTS IdxBlogPostSlug ON BlogPost(Slug);

-- Create BlogSeries table if it doesn't exist (referenced by repo)
CREATE TABLE IF NOT EXISTS BlogSeries (
    SeriesId BIGSERIAL PRIMARY KEY,
    Name VARCHAR(255) NOT NULL,
    Slug VARCHAR(300) NOT NULL UNIQUE,
    Description TEXT,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UpdatedOn TIMESTAMP,
    IsActive BOOLEAN DEFAULT TRUE
);

-- Add foreign key for SeriesId if table exists
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'blogpost_seriesid_fkey'
    ) THEN
        ALTER TABLE BlogPost ADD CONSTRAINT blogpost_seriesid_fkey
            FOREIGN KEY (SeriesId) REFERENCES BlogSeries(SeriesId) ON DELETE SET NULL;
    END IF;
EXCEPTION
    WHEN others THEN NULL;
END $$;

-- Add foreign key for CategoryId to Category table
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_name = 'blogpost_categoryid_fkey'
    ) THEN
        ALTER TABLE BlogPost ADD CONSTRAINT blogpost_categoryid_fkey
            FOREIGN KEY (CategoryId) REFERENCES Category(CategoryId) ON DELETE SET NULL;
    END IF;
EXCEPTION
    WHEN others THEN NULL;
END $$;

-- Create index on CategoryId for faster lookups
CREATE INDEX IF NOT EXISTS IdxBlogPostCategoryId ON BlogPost(CategoryId);
