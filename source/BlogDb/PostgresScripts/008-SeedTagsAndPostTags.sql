-- ============================================================================
-- Script: 008-SeedTagsAndPostTags.sql
-- Purpose: Seeds initial tags and creates PostTag associations for existing posts
-- Author: Dev Agent
-- Created: 2025-12-25
-- ============================================================================

-- ============================================================================
-- SEED: Tags
-- Purpose: Create common technology blog tags
-- ============================================================================

-- Insert tags if they don't exist
INSERT INTO Tag (TagName, Slug)
SELECT 'Blazor', 'blazor'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Blazor');

INSERT INTO Tag (TagName, Slug)
SELECT 'ASP.NET Core', 'aspnet-core'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'ASP.NET Core');

INSERT INTO Tag (TagName, Slug)
SELECT 'C#', 'csharp'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'C#');

INSERT INTO Tag (TagName, Slug)
SELECT '.NET', 'dotnet'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = '.NET');

INSERT INTO Tag (TagName, Slug)
SELECT 'Architecture', 'architecture'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Architecture');

INSERT INTO Tag (TagName, Slug)
SELECT 'Tutorial', 'tutorial'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Tutorial');

INSERT INTO Tag (TagName, Slug)
SELECT 'Best Practices', 'best-practices'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Best Practices');

INSERT INTO Tag (TagName, Slug)
SELECT 'Database', 'database'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Database');

INSERT INTO Tag (TagName, Slug)
SELECT 'PostgreSQL', 'postgresql'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'PostgreSQL');

INSERT INTO Tag (TagName, Slug)
SELECT 'FluentUI', 'fluentui'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'FluentUI');

INSERT INTO Tag (TagName, Slug)
SELECT 'Conference', 'conference'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Conference');

INSERT INTO Tag (TagName, Slug)
SELECT 'Docker', 'docker'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Docker');

INSERT INTO Tag (TagName, Slug)
SELECT 'Azure', 'azure'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Azure');

INSERT INTO Tag (TagName, Slug)
SELECT 'Performance', 'performance'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Performance');

INSERT INTO Tag (TagName, Slug)
SELECT 'Security', 'security'
WHERE NOT EXISTS (SELECT 1 FROM Tag WHERE TagName = 'Security');

-- ============================================================================
-- SEED: PostTag Associations
-- Purpose: Link existing published posts to tags based on their content/category
-- This creates sample associations - adjust based on actual post content
-- ============================================================================

-- Associate Blazor-related posts with Blazor tag
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
WHERE t.TagName = 'Blazor'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (p.Title ILIKE '%blazor%' OR p.PostContent ILIKE '%blazor%' OR p.Tags ILIKE '%blazor%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- Associate .NET related posts with .NET tag  
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
WHERE t.TagName = '.NET'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (p.Title ILIKE '%.net%' OR p.PostContent ILIKE '%.net%' OR p.Tags ILIKE '%dotnet%' OR p.Tags ILIKE '%.net%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- Associate C# related posts with C# tag
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
WHERE t.TagName = 'C#'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (p.Title ILIKE '%c#%' OR p.PostContent ILIKE '%c#%' OR p.Tags ILIKE '%csharp%' OR p.Tags ILIKE '%c#%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- Associate ASP.NET Core posts  
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
WHERE t.TagName = 'ASP.NET Core'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (p.Title ILIKE '%asp.net%' OR p.PostContent ILIKE '%asp.net%' OR p.Tags ILIKE '%aspnet%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- Associate Database posts
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
WHERE t.TagName = 'Database'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (p.Title ILIKE '%database%' OR p.PostContent ILIKE '%database%' OR p.Tags ILIKE '%database%' 
       OR p.PostContent ILIKE '%postgresql%' OR p.PostContent ILIKE '%sql%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- Associate Tutorial posts (posts in Programming category often are tutorials)
INSERT INTO PostTag (PostId, TagId)
SELECT DISTINCT p.PostId, t.TagId
FROM BlogPost p
CROSS JOIN Tag t
JOIN Category c ON p.CategoryId = c.CategoryId
WHERE t.TagName = 'Tutorial'
  AND p.Published = TRUE
  AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
  AND (c.CategoryName = 'Programming' OR p.Tags ILIKE '%tutorial%' OR p.Title ILIKE '%how to%' OR p.Title ILIKE '%getting started%')
  AND NOT EXISTS (
    SELECT 1 FROM PostTag pt WHERE pt.PostId = p.PostId AND pt.TagId = t.TagId
  );

-- ============================================================================
-- FALLBACK: If no posts matched, assign first few published posts to common tags
-- This ensures the tag cloud has visible counts for demonstration
-- ============================================================================
DO $$
DECLARE
    v_post_count INT;
    v_posttag_count INT;
BEGIN
    -- Check if we have any PostTag entries
    SELECT COUNT(*) INTO v_posttag_count FROM PostTag;
    
    -- If no entries, create some default associations
    IF v_posttag_count = 0 THEN
        -- Get count of published posts
        SELECT COUNT(*) INTO v_post_count 
        FROM BlogPost 
        WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL);
        
        -- If we have posts, associate them with tags
        IF v_post_count > 0 THEN
            -- Associate first post with multiple tags
            INSERT INTO PostTag (PostId, TagId)
            SELECT p.PostId, t.TagId
            FROM (SELECT PostId FROM BlogPost WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL) ORDER BY PostId LIMIT 1) p
            CROSS JOIN (SELECT TagId FROM Tag WHERE TagName IN ('Blazor', '.NET', 'Tutorial') LIMIT 3) t
            ON CONFLICT DO NOTHING;
            
            -- Associate second post if exists
            INSERT INTO PostTag (PostId, TagId)
            SELECT p.PostId, t.TagId
            FROM (SELECT PostId FROM BlogPost WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL) ORDER BY PostId OFFSET 1 LIMIT 1) p
            CROSS JOIN (SELECT TagId FROM Tag WHERE TagName IN ('C#', 'Best Practices') LIMIT 2) t
            ON CONFLICT DO NOTHING;
            
            -- Associate third post if exists
            INSERT INTO PostTag (PostId, TagId)
            SELECT p.PostId, t.TagId
            FROM (SELECT PostId FROM BlogPost WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL) ORDER BY PostId OFFSET 2 LIMIT 1) p
            CROSS JOIN (SELECT TagId FROM Tag WHERE TagName IN ('ASP.NET Core', 'Architecture') LIMIT 2) t
            ON CONFLICT DO NOTHING;
        END IF;
    END IF;
END $$;

-- Verify the seed data
-- SELECT t.TagName, COUNT(pt.PostId) as PostCount
-- FROM Tag t
-- LEFT JOIN PostTag pt ON t.TagId = pt.TagId
-- GROUP BY t.TagId, t.TagName
-- ORDER BY PostCount DESC;
