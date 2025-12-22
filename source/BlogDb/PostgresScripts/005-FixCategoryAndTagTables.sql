-- ============================================================================
-- Script: 005-FixCategoryAndTagTables.sql
-- Purpose: Adds missing columns to Category and Tag tables
-- ============================================================================

-- Add missing columns to Category table
ALTER TABLE Category ADD COLUMN IF NOT EXISTS Slug VARCHAR(200);
ALTER TABLE Category ADD COLUMN IF NOT EXISTS Description TEXT;

-- Add missing column to Tag table
ALTER TABLE Tag ADD COLUMN IF NOT EXISTS Slug VARCHAR(200);

-- Generate slugs for existing categories
UPDATE Category
SET Slug = LOWER(REGEXP_REPLACE(REGEXP_REPLACE(CategoryName, '[^a-zA-Z0-9\s-]', '', 'g'), '\s+', '-', 'g'))
WHERE Slug IS NULL OR Slug = '';

-- Generate slugs for existing tags
UPDATE Tag
SET Slug = LOWER(REGEXP_REPLACE(REGEXP_REPLACE(TagName, '[^a-zA-Z0-9\s-]', '', 'g'), '\s+', '-', 'g'))
WHERE Slug IS NULL OR Slug = '';

-- Create indexes
CREATE UNIQUE INDEX IF NOT EXISTS IdxCategorySlug ON Category(Slug);
CREATE UNIQUE INDEX IF NOT EXISTS IdxTagSlug ON Tag(Slug);
