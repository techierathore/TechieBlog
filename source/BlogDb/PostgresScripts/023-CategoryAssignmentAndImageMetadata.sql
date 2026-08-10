-- ============================================================================
-- 023-CategoryAssignmentAndImageMetadata.sql
--
-- Purpose:
--   PART A [REQ-FN-017] - stop the "no category selected" sentinel from being
--   written into BlogPost.CategoryId as the literal 0, which no Category row
--   can ever satisfy.
--   PART B [REQ-FN-026] - make BlogImage.AltText carry a value on every row so
--   the accessible-name obligation (REQ-NFR-007 / WCAG 1.1.1) has something to
--   render, and confirm the descriptive columns migration 012 added are all
--   present before the repository starts writing Width/Height.
--
-- Changes:
--   PART A - Adds the trigger function NormaliseBlogPostCategory() and the
--            BEFORE INSERT OR UPDATE trigger TrgNormaliseBlogPostCategory on
--            BlogPost. CategoryId = 0 is rewritten to NULL. Existing rows that
--            already hold 0 are repaired by the same rule.
--   PART B - Backfills BlogImage.AltText from ImageName wherever it is NULL or
--            blank, and re-asserts the 012 columns with IF NOT EXISTS.
--
-- Business Rules / why this is a defect, not a tidy-up:
--   - BlogPost.CategoryId is a NULLABLE BIGINT with the foreign key
--     blogpost_categoryid_fkey -> Category(CategoryId) ON DELETE SET NULL, so
--     "this post has no category" is a supported, first-class state and its
--     only correct representation is NULL.
--   - The category picker offers "-- Select Category --" with the value "0",
--     and BlogPost.CategoryId is a non-nullable C# int, so an unselected
--     picker reaches the write path as 0 rather than as "absent". PostgreSQL
--     then rejects the whole INSERT with
--         23503: insert or update on table "blogpost" violates foreign key
--         constraint "blogpost_categoryid_fkey"
--     and the author loses the post they just wrote.
--   - The fix is placed in the database, not in one screen, deliberately: the
--     defect was reproduced independently from the Blazor Server head AND from
--     the BlogApp desktop head, and the same 0 can arrive from any future
--     write path. Normalising at the column keeps every head correct at once
--     and cannot be bypassed.
--   - 0 is never a legitimate CategoryId: Category.CategoryId is BIGSERIAL and
--     its sequence starts at 1, so nothing is lost by treating 0 as "none".
--   - BlogImage.AltText being permanently NULL means every media-library image
--     falls back to its stored file name for its accessible name, which is a
--     generated, collision-proofed string - not a description. Seeding the
--     column from ImageName is not a description either, but it is a real,
--     editable value rather than a NULL, and the upload path now captures a
--     typed alternative text on top of it.
--
-- Dependencies:
--   - 004-FixPostTable.sql  - created BlogPost.CategoryId and the foreign key.
--   - 012-ResumeAndImageManagement.sql - created the BlogImage descriptive
--     columns Category, AltText, MimeType, Width, Height.
--
-- Rollback:
--   DROP TRIGGER IF EXISTS TrgNormaliseBlogPostCategory ON BlogPost;
--   DROP FUNCTION IF EXISTS NormaliseBlogPostCategory();
--   (The AltText backfill is data, not schema; it is not rolled back.)
--
-- Idempotent: yes. CREATE OR REPLACE FUNCTION, DROP/CREATE TRIGGER, ADD COLUMN
-- IF NOT EXISTS and a WHERE-guarded UPDATE all tolerate a repeat run.
-- ============================================================================

-- ============================================================================
-- PART A [REQ-FN-017]: BlogPost.CategoryId = 0 means "unassigned" -> NULL
-- ============================================================================

CREATE OR REPLACE FUNCTION NormaliseBlogPostCategory()
RETURNS TRIGGER AS $$
BEGIN
    -- Category.CategoryId is BIGSERIAL starting at 1, so 0 can only be the
    -- "-- Select Category --" sentinel arriving from a non-nullable int.
    IF NEW.CategoryId = 0 THEN
        NEW.CategoryId := NULL;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION NormaliseBlogPostCategory() IS
    'REQ-FN-017: rewrites the unassigned-category sentinel 0 to NULL before it reaches the foreign key.';

DROP TRIGGER IF EXISTS TrgNormaliseBlogPostCategory ON BlogPost;

CREATE TRIGGER TrgNormaliseBlogPostCategory
    BEFORE INSERT OR UPDATE ON BlogPost
    FOR EACH ROW
    EXECUTE FUNCTION NormaliseBlogPostCategory();

-- Repair any row that predates the trigger. The foreign key means a stored 0
-- should be impossible, but a row written before the constraint existed would
-- still be reported to the UI as "category 0".
UPDATE BlogPost
SET CategoryId = NULL
WHERE CategoryId = 0;

-- ============================================================================
-- PART B [REQ-FN-026]: BlogImage descriptive columns carry values
-- ============================================================================

-- Re-assert the 012 columns. A database restored from a snapshot taken before
-- 012 would otherwise fail the repository's new Width/Height writes.
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Category VARCHAR(50) DEFAULT 'general';
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS AltText  VARCHAR(255);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS MimeType VARCHAR(100);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Width    INT;
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Height   INT;

-- Seed an accessible name for every legacy row. LEFT() keeps the value inside
-- the VARCHAR(255) column even for a very long original file name.
UPDATE BlogImage
SET AltText = LEFT(ImageName, 255)
WHERE AltText IS NULL OR TRIM(AltText) = '';
