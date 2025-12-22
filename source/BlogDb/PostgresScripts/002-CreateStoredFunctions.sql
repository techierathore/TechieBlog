-- ============================================================================
-- Script: 002-CreateStoredFunctions.sql
-- Purpose: Creates all PostgreSQL functions for TechieBlog database operations
-- Author: James (Dev Agent)
-- Created: 2025-12-17
-- Modified: 2025-12-17 - Initial PostgreSQL migration from MySQL stored procedures
-- ============================================================================

-- ============================================================================
-- BLOG USER FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: InsertBlogUser
-- Purpose: Creates a new user account
--
-- Parameters:
--   pFirstName, pLastName - User's name
--   pEmailId - Login email (must be unique)
--   pLoginPass - Hashed password
--   pUserRole - Role name (Admin, Blogger, Subscriber)
--   pProfileImagePath - Optional profile image
--   pProfileDescription - Optional bio
--   pTwitterUrl, pLinkedInUrl, pGitHubUrl - Social links
--   pPodDescription, pSpeakDescription - Speaker info
--
-- Returns: The new UserId
--
-- Called By: BlogUserRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION InsertBlogUser(
    pFirstName VARCHAR(100),
    pLastName VARCHAR(100),
    pEmailId VARCHAR(255),
    pLoginPass VARCHAR(255),
    pUserRole VARCHAR(51),
    pProfileImagePath VARCHAR(255) DEFAULT NULL,
    pProfileDescription TEXT DEFAULT NULL,
    pTwitterUrl VARCHAR(255) DEFAULT NULL,
    pLinkedInUrl VARCHAR(255) DEFAULT NULL,
    pGitHubUrl VARCHAR(255) DEFAULT NULL,
    pPodDescription VARCHAR(1050) DEFAULT NULL,
    pSpeakDescription VARCHAR(1050) DEFAULT NULL
)
RETURNS BIGINT AS $$
DECLARE
    vUserId BIGINT;
BEGIN
    INSERT INTO BlogUser (
        FirstName, LastName, EmailId, LoginPass, CreatedOn, UpdatedOn,
        UserRole, ProfileImagePath, ProfileDescription,
        TwitterUrl, LinkedInUrl, GitHubUrl, PodDescription, SpeakDescription
    )
    VALUES (
        pFirstName, pLastName, pEmailId, pLoginPass, NOW(), NOW(),
        pUserRole, pProfileImagePath, pProfileDescription,
        pTwitterUrl, pLinkedInUrl, pGitHubUrl, pPodDescription, pSpeakDescription
    )
    RETURNING UserId INTO vUserId;

    RETURN vUserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: UpdateBlogUser
-- Purpose: Updates an existing user profile
--
-- Parameters: All user fields including UserId
--
-- Called By: BlogUserRepo.Update()
-- ============================================================================
CREATE OR REPLACE FUNCTION UpdateBlogUser(
    pUserId BIGINT,
    pFirstName VARCHAR(100),
    pLastName VARCHAR(100),
    pEmailId VARCHAR(255),
    pLoginPass VARCHAR(255),
    pUserRole VARCHAR(51),
    pProfileImagePath VARCHAR(255),
    pProfileDescription TEXT,
    pTwitterUrl VARCHAR(255),
    pLinkedInUrl VARCHAR(255),
    pGitHubUrl VARCHAR(255),
    pPodDescription VARCHAR(1050),
    pSpeakDescription VARCHAR(1050)
)
RETURNS VOID AS $$
BEGIN
    UPDATE BlogUser
    SET
        FirstName = pFirstName,
        LastName = pLastName,
        EmailId = pEmailId,
        LoginPass = pLoginPass,
        UpdatedOn = NOW(),
        UserRole = pUserRole,
        ProfileImagePath = pProfileImagePath,
        ProfileDescription = pProfileDescription,
        TwitterUrl = pTwitterUrl,
        LinkedInUrl = pLinkedInUrl,
        GitHubUrl = pGitHubUrl,
        PodDescription = pPodDescription,
        SpeakDescription = pSpeakDescription
    WHERE UserId = pUserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: SelectBlogUserById
-- Purpose: Retrieves a user by their ID
--
-- Parameters:
--   pUserId - The user's unique identifier
--
-- Returns: Single user record or empty if not found
--
-- Called By: BlogUserRepo.GetSingle()
-- ============================================================================
CREATE OR REPLACE FUNCTION SelectBlogUserById(pUserId BIGINT)
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    UserRole VARCHAR(51),
    IsConfirmed BOOLEAN,
    ProfileImagePath VARCHAR(255),
    ProfileDescription TEXT,
    TwitterUrl VARCHAR(255),
    LinkedInUrl VARCHAR(255),
    GitHubUrl VARCHAR(255),
    PodDescription VARCHAR(1050),
    SpeakDescription VARCHAR(1050)
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.CreatedOn, u.UpdatedOn, u.UserRole, u.IsConfirmed,
        u.ProfileImagePath, u.ProfileDescription,
        u.TwitterUrl, u.LinkedInUrl, u.GitHubUrl,
        u.PodDescription, u.SpeakDescription
    FROM BlogUser u
    WHERE u.UserId = pUserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetLoginUser
-- Purpose: Authenticates a user with email and password
--
-- Parameters:
--   pLoginMail - User's email address
--   pLoginPassword - Hashed password
--
-- Returns: User record if credentials match, empty otherwise
--
-- Called By: AuthSvc.AppLogin()
-- ============================================================================
CREATE OR REPLACE FUNCTION GetLoginUser(pLoginMail VARCHAR(550), pLoginPassword VARCHAR(255))
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.UserRole, u.CreatedOn, u.UpdatedOn
    FROM BlogUser u
    WHERE u.EmailId = pLoginMail AND u.LoginPass = pLoginPassword;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetUserByEmail
-- Purpose: Retrieves a user by email address
--
-- Parameters:
--   pLoginMail - User's email address
--
-- Returns: User record if found
--
-- Called By: AuthSvc for user lookup
-- ============================================================================
CREATE OR REPLACE FUNCTION GetUserByEmail(pLoginMail VARCHAR(550))
RETURNS TABLE (
    UserId BIGINT,
    FirstName VARCHAR(100),
    LastName VARCHAR(100),
    EmailId VARCHAR(255),
    LoginPass VARCHAR(255),
    UserRole VARCHAR(51),
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.UserRole, u.CreatedOn, u.UpdatedOn
    FROM BlogUser u
    WHERE u.EmailId = pLoginMail;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- BLOG IMAGE FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: BlogImageInsert
-- Purpose: Records a new uploaded image
--
-- Parameters:
--   pImageName - Original filename
--   pImagePath - Storage path
--   pSize - File size in bytes
--   pCreatedTime - Upload timestamp
--   pUserId - Uploader's user ID
--
-- Returns: The new BlogImageId
--
-- Called By: BlogImageRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION BlogImageInsert(
    pImageName VARCHAR(150),
    pImagePath VARCHAR(550),
    pSize INT,
    pCreatedTime TIMESTAMP,
    pUserId BIGINT
)
RETURNS BIGINT AS $$
DECLARE
    vImageId BIGINT;
BEGIN
    INSERT INTO BlogImage (ImageName, ImagePath, Size, CreatedTime, UserId)
    VALUES (pImageName, pImagePath, pSize, pCreatedTime, pUserId)
    RETURNING BlogImageId INTO vImageId;

    RETURN vImageId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPagedBlogImages
-- Purpose: Retrieves paginated list of uploaded images
--
-- Parameters:
--   pPageSize - Number of images per page
--   pOffset - Number of images to skip
--
-- Returns: Paginated image records ordered by most recent
--
-- Called By: BlogImageRepo.GetPagedData()
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPagedBlogImages(pPageSize INT, pOffset INT)
RETURNS TABLE (
    BlogImageId BIGINT,
    ImageName VARCHAR(150),
    ImagePath VARCHAR(550),
    Size INT,
    CreatedTime TIMESTAMP,
    UserId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT i.BlogImageId, i.ImageName, i.ImagePath, i.Size, i.CreatedTime, i.UserId
    FROM BlogImage i
    ORDER BY i.BlogImageId DESC
    LIMIT pPageSize OFFSET pOffset;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- POST FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: SelectAllPosts
-- Purpose: Retrieves all posts with author information
--
-- Returns: All posts with author's name
--
-- Called By: BlogSvc.GetAllPosts()
-- ============================================================================
CREATE OR REPLACE FUNCTION SelectAllPosts()
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    Published BOOLEAN,
    UserId BIGINT,
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550),
    BlogWriter VARCHAR(201)
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        p.PostId, p.Title, p.Abstract, p.PostContent,
        p.CreatedOn, p.UpdatedOn, p.Published, p.UserId,
        p.Tags, p.FeaturedImage,
        CONCAT(u.FirstName, ' ', u.LastName)::VARCHAR(201) AS BlogWriter
    FROM Post p
    INNER JOIN BlogUser u ON p.UserId = u.UserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: PostSelect
-- Purpose: Retrieves a single post by ID
--
-- Parameters:
--   pPostId - The post's unique identifier
--
-- Returns: Single post record
--
-- Called By: BlogPostRepo.GetSingle()
-- ============================================================================
CREATE OR REPLACE FUNCTION PostSelect(pPostId BIGINT)
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    UserId BIGINT,
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550),
    Published BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        p.PostId, p.Title, p.Abstract, p.PostContent,
        p.CreatedOn, p.UpdatedOn, p.UserId, p.Tags,
        p.FeaturedImage, p.Published
    FROM Post p
    WHERE p.PostId = pPostId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: PostsByUserId
-- Purpose: Retrieves all posts by a specific author
--
-- Parameters:
--   pUserId - The author's user ID
--
-- Returns: All posts by the specified user
--
-- Called By: BlogSvc.GetPostsByUser()
-- ============================================================================
CREATE OR REPLACE FUNCTION PostsByUserId(pUserId BIGINT)
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    UserId BIGINT,
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550),
    Published BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        p.PostId, p.Title, p.Abstract, p.PostContent,
        p.CreatedOn, p.UpdatedOn, p.UserId, p.Tags,
        p.FeaturedImage, p.Published
    FROM Post p
    WHERE p.UserId = pUserId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPagedBlogList
-- Purpose: Retrieves paginated list of published posts with comment counts
--
-- Parameters:
--   pPageSize - Number of posts per page
--   pOffset - Number of posts to skip
--
-- Returns: Paginated published posts ordered by most recent
--
-- Called By: BlogSvc.GetPagedPosts()
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPagedBlogList(pPageSize INT, pOffset INT)
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    CommentCount BIGINT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    Published BOOLEAN,
    UserId BIGINT,
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550)
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        p.PostId, p.Title, p.Abstract, p.PostContent,
        (SELECT COUNT(*) FROM BlogComment c WHERE c.PostId = p.PostId) AS CommentCount,
        p.CreatedOn, p.UpdatedOn, p.Published, p.UserId,
        p.Tags, p.FeaturedImage
    FROM Post p
    WHERE p.Published = TRUE
    ORDER BY p.PostId DESC
    LIMIT pPageSize OFFSET pOffset;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: PostInsert
-- Purpose: Creates a new blog post
--
-- Parameters:
--   pTitle - Post title
--   pAbstract - Post summary
--   pPostContent - Full post content
--   pUserId - Author's user ID
--   pTags - Comma-separated tags
--   pFeaturedImage - Hero image path
--   pCreatedOn - Creation timestamp
--   pPublished - Publication status
--
-- Returns: The new PostId
--
-- Called By: BlogPostRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION PostInsert(
    pTitle VARCHAR(550),
    pAbstract VARCHAR(550),
    pPostContent TEXT,
    pUserId BIGINT,
    pTags VARCHAR(550),
    pFeaturedImage VARCHAR(550),
    pCreatedOn TIMESTAMP,
    pPublished BOOLEAN
)
RETURNS BIGINT AS $$
DECLARE
    vPostId BIGINT;
BEGIN
    INSERT INTO Post (Title, Abstract, PostContent, UserId, Tags, FeaturedImage, CreatedOn, Published)
    VALUES (pTitle, pAbstract, pPostContent, pUserId, pTags, pFeaturedImage, pCreatedOn, pPublished)
    RETURNING PostId INTO vPostId;

    RETURN vPostId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: PostUpdate
-- Purpose: Updates an existing blog post
--
-- Parameters: All post fields including PostId
--
-- Called By: BlogPostRepo.Update()
-- ============================================================================
CREATE OR REPLACE FUNCTION PostUpdate(
    pPostId BIGINT,
    pTitle VARCHAR(550),
    pAbstract VARCHAR(550),
    pPostContent TEXT,
    pUserId BIGINT,
    pTags VARCHAR(550),
    pFeaturedImage VARCHAR(550),
    pUpdatedOn TIMESTAMP,
    pPublished BOOLEAN
)
RETURNS VOID AS $$
BEGIN
    UPDATE Post
    SET
        Title = pTitle,
        Abstract = pAbstract,
        PostContent = pPostContent,
        UserId = pUserId,
        Tags = pTags,
        FeaturedImage = pFeaturedImage,
        UpdatedOn = pUpdatedOn,
        Published = pPublished
    WHERE PostId = pPostId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- TAG FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: GetAllTags
-- Purpose: Retrieves all tags
--
-- Returns: All tag records
--
-- Called By: TagSvc.GetAll()
-- ============================================================================
CREATE OR REPLACE FUNCTION GetAllTags()
RETURNS TABLE (
    TagId BIGINT,
    TagName VARCHAR(150)
) AS $$
BEGIN
    RETURN QUERY
    SELECT t.TagId, t.TagName FROM Tag t;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: TagSelect
-- Purpose: Retrieves a single tag by ID
--
-- Parameters:
--   pTagId - The tag's unique identifier
--
-- Returns: Single tag record
--
-- Called By: TagRepo.GetSingle()
-- ============================================================================
CREATE OR REPLACE FUNCTION TagSelect(pTagId BIGINT)
RETURNS TABLE (
    TagId BIGINT,
    TagName VARCHAR(150)
) AS $$
BEGIN
    RETURN QUERY
    SELECT t.TagId, t.TagName FROM Tag t WHERE t.TagId = pTagId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: TagInsert
-- Purpose: Creates a new tag
--
-- Parameters:
--   pTagName - Tag display name
--
-- Returns: The new TagId
--
-- Called By: TagRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION TagInsert(pTagName VARCHAR(150))
RETURNS BIGINT AS $$
DECLARE
    vTagId BIGINT;
BEGIN
    INSERT INTO Tag (TagName) VALUES (pTagName)
    RETURNING TagId INTO vTagId;

    RETURN vTagId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: TagUpdate
-- Purpose: Updates an existing tag
--
-- Parameters:
--   pTagId - Tag to update
--   pTagName - New tag name
--
-- Called By: TagRepo.Update()
-- ============================================================================
CREATE OR REPLACE FUNCTION TagUpdate(pTagId BIGINT, pTagName VARCHAR(150))
RETURNS VOID AS $$
BEGIN
    UPDATE Tag SET TagName = pTagName WHERE TagId = pTagId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- BLOG COMMENT FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: BlogCommentSelect
-- Purpose: Retrieves a single comment by ID
--
-- Parameters:
--   pCommentId - The comment's unique identifier
--
-- Returns: Single comment record
--
-- Called By: BlogCommentRepo.GetSingle()
-- ============================================================================
CREATE OR REPLACE FUNCTION BlogCommentSelect(pCommentId BIGINT)
RETURNS TABLE (
    CommentId BIGINT,
    PostId BIGINT,
    GivenOn TIMESTAMP,
    GivenBy VARCHAR(350),
    Email VARCHAR(350),
    Comment VARCHAR(850),
    Published BOOLEAN,
    ParentCommentId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.CommentId, c.PostId, c.GivenOn, c.GivenBy, c.Email,
        c.Comment, c.Published, c.ParentCommentId
    FROM BlogComment c
    WHERE c.CommentId = pCommentId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: ApproveBlogComment
-- Purpose: Approves a comment for display
--
-- Parameters:
--   pCommentId - Comment to approve
--
-- Called By: BlogCommentRepo for moderation
-- ============================================================================
CREATE OR REPLACE FUNCTION ApproveBlogComment(pCommentId BIGINT)
RETURNS VOID AS $$
BEGIN
    UPDATE BlogComment SET Published = TRUE WHERE CommentId = pCommentId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPostParentComments
-- Purpose: Retrieves top-level (non-reply) comments for a post
--
-- Parameters:
--   pPostId - The post's unique identifier
--
-- Returns: Top-level published comments
--
-- Called By: BlogSvc for displaying comments
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPostParentComments(pPostId BIGINT)
RETURNS TABLE (
    CommentId BIGINT,
    PostId BIGINT,
    GivenOn TIMESTAMP,
    GivenBy VARCHAR(350),
    Email VARCHAR(350),
    Comment VARCHAR(850),
    Published BOOLEAN,
    ParentCommentId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.CommentId, c.PostId, c.GivenOn, c.GivenBy, c.Email,
        c.Comment, c.Published, c.ParentCommentId
    FROM BlogComment c
    WHERE c.Published = TRUE
      AND (c.ParentCommentId IS NULL OR c.ParentCommentId = 0)
      AND c.PostId = pPostId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPostChildComments
-- Purpose: Retrieves reply comments for a post
--
-- Parameters:
--   pPostId - The post's unique identifier
--
-- Returns: Reply comments (those with a parent)
--
-- Called By: BlogSvc for displaying threaded comments
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPostChildComments(pPostId BIGINT)
RETURNS TABLE (
    CommentId BIGINT,
    PostId BIGINT,
    GivenOn TIMESTAMP,
    GivenBy VARCHAR(350),
    Email VARCHAR(350),
    Comment VARCHAR(850),
    Published BOOLEAN,
    ParentCommentId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.CommentId, c.PostId, c.GivenOn, c.GivenBy, c.Email,
        c.Comment, c.Published, c.ParentCommentId
    FROM BlogComment c
    WHERE c.Published = TRUE
      AND c.ParentCommentId IS NOT NULL
      AND c.PostId = pPostId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPagedUnAppComments
-- Purpose: Retrieves paginated unapproved comments for moderation
--
-- Parameters:
--   pPageSize - Number of comments per page
--   pOffset - Number of comments to skip
--
-- Returns: Paginated unapproved comments
--
-- Called By: Admin moderation queue
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPagedUnAppComments(pPageSize INT, pOffset INT)
RETURNS TABLE (
    CommentId BIGINT,
    PostId BIGINT,
    GivenOn TIMESTAMP,
    GivenBy VARCHAR(350),
    Email VARCHAR(350),
    Comment VARCHAR(850),
    Published BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.CommentId, c.PostId, c.GivenOn, c.GivenBy, c.Email,
        c.Comment, c.Published
    FROM BlogComment c
    WHERE c.Published = FALSE
    ORDER BY c.CommentId DESC
    LIMIT pPageSize OFFSET pOffset;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetPagedComments
-- Purpose: Retrieves paginated list of all comments
--
-- Parameters:
--   pPageSize - Number of comments per page
--   pOffset - Number of comments to skip
--
-- Returns: Paginated comments ordered by most recent
--
-- Called By: Admin comment management
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPagedComments(pPageSize INT, pOffset INT)
RETURNS TABLE (
    CommentId BIGINT,
    PostId BIGINT,
    GivenOn TIMESTAMP,
    GivenBy VARCHAR(350),
    Email VARCHAR(350),
    Comment VARCHAR(850),
    Published BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        c.CommentId, c.PostId, c.GivenOn, c.GivenBy, c.Email,
        c.Comment, c.Published
    FROM BlogComment c
    ORDER BY c.CommentId DESC
    LIMIT pPageSize OFFSET pOffset;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: BlogCommentInsert
-- Purpose: Creates a new comment on a post
--
-- Parameters:
--   pPostId - The post being commented on
--   pGivenOn - Comment timestamp
--   pGivenBy - Commenter's display name
--   pEmail - Commenter's email
--   pComment - Comment content
--   pPublished - Initial publication status
--   pParentId - Parent comment ID for replies (null for top-level)
--
-- Returns: The new CommentId
--
-- Called By: BlogCommentRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION BlogCommentInsert(
    pPostId BIGINT,
    pGivenOn TIMESTAMP,
    pGivenBy VARCHAR(350),
    pEmail VARCHAR(350),
    pComment VARCHAR(850),
    pPublished BOOLEAN,
    pParentId BIGINT
)
RETURNS BIGINT AS $$
DECLARE
    vCommentId BIGINT;
BEGIN
    INSERT INTO BlogComment (PostId, GivenOn, GivenBy, Email, Comment, Published, ParentCommentId)
    VALUES (pPostId, pGivenOn, pGivenBy, pEmail, pComment, pPublished, pParentId)
    RETURNING CommentId INTO vCommentId;

    RETURN vCommentId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- USER EVENT FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: GetUserEvents
-- Purpose: Retrieves all events for a specific user
--
-- Parameters:
--   pUserId - The user's unique identifier
--
-- Returns: User's events ordered by date descending
--
-- Called By: UserEventRepo.GetAllById()
-- ============================================================================
CREATE OR REPLACE FUNCTION GetUserEvents(pUserId BIGINT)
RETURNS TABLE (
    EventId BIGINT,
    LogoIconPath VARCHAR(350),
    EventTitle VARCHAR(350),
    SessionTitle VARCHAR(350),
    EventUrl VARCHAR(350),
    EventDate TIMESTAMP,
    Type VARCHAR(50),
    UserId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        e.EventId, e.LogoIconPath, e.EventTitle, e.SessionTitle,
        e.EventUrl, e.EventDate, e.Type, e.UserId
    FROM UserEvents e
    WHERE e.UserId = pUserId
    ORDER BY e.EventDate DESC;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: UserEventInsert
-- Purpose: Creates a new user event
--
-- Parameters: All event fields
--
-- Returns: The new EventId
--
-- Called By: UserEventRepo.Insert()
-- ============================================================================
CREATE OR REPLACE FUNCTION UserEventInsert(
    pLogoIconPath VARCHAR(350),
    pEventTitle VARCHAR(350),
    pSessionTitle VARCHAR(350),
    pEventUrl VARCHAR(350),
    pEventDate TIMESTAMP,
    pType VARCHAR(50),
    pUserId BIGINT
)
RETURNS BIGINT AS $$
DECLARE
    vEventId BIGINT;
BEGIN
    INSERT INTO UserEvents (LogoIconPath, EventTitle, SessionTitle, EventUrl, EventDate, Type, UserId)
    VALUES (pLogoIconPath, pEventTitle, pSessionTitle, pEventUrl, pEventDate, pType, pUserId)
    RETURNING EventId INTO vEventId;

    RETURN vEventId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: UserEventSelect
-- Purpose: Retrieves a single event by ID
--
-- Parameters:
--   pEventId - The event's unique identifier
--
-- Returns: Single event record
--
-- Called By: UserEventRepo.GetSingle()
-- ============================================================================
CREATE OR REPLACE FUNCTION UserEventSelect(pEventId BIGINT)
RETURNS TABLE (
    EventId BIGINT,
    LogoIconPath VARCHAR(350),
    EventTitle VARCHAR(350),
    SessionTitle VARCHAR(350),
    EventUrl VARCHAR(350),
    EventDate TIMESTAMP,
    Type VARCHAR(50),
    UserId BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        e.EventId, e.LogoIconPath, e.EventTitle, e.SessionTitle,
        e.EventUrl, e.EventDate, e.Type, e.UserId
    FROM UserEvents e
    WHERE e.EventId = pEventId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: UserEventUpdate
-- Purpose: Updates an existing user event
--
-- Parameters: All event fields including EventId
--
-- Called By: UserEventRepo.Update()
-- ============================================================================
CREATE OR REPLACE FUNCTION UserEventUpdate(
    pEventId BIGINT,
    pLogoIconPath VARCHAR(350),
    pEventTitle VARCHAR(350),
    pSessionTitle VARCHAR(350),
    pEventUrl VARCHAR(350),
    pEventDate TIMESTAMP,
    pType VARCHAR(50),
    pUserId BIGINT
)
RETURNS VOID AS $$
BEGIN
    UPDATE UserEvents
    SET
        LogoIconPath = pLogoIconPath,
        EventTitle = pEventTitle,
        SessionTitle = pSessionTitle,
        EventUrl = pEventUrl,
        EventDate = pEventDate,
        Type = pType,
        UserId = pUserId
    WHERE EventId = pEventId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- ADMIN FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: GetTheCounts
-- Purpose: Retrieves blog and comment counts for dashboard
--
-- Returns: Latest post with blog and comment counts
--
-- Called By: AdminSvc for dashboard stats
-- ============================================================================
CREATE OR REPLACE FUNCTION GetTheCounts()
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    BlogCount BIGINT,
    CommentCount BIGINT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    Published BOOLEAN,
    UserId BIGINT,
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550)
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        p.PostId, p.Title, p.Abstract, p.PostContent,
        (SELECT COUNT(*) FROM Post WHERE Published = TRUE) AS BlogCount,
        (SELECT COUNT(*) FROM BlogComment) AS CommentCount,
        p.CreatedOn, p.UpdatedOn, p.Published, p.UserId,
        p.Tags, p.FeaturedImage
    FROM Post p
    WHERE p.Published = TRUE
    ORDER BY p.PostId DESC
    LIMIT 1;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: GetAdminCounts
-- Purpose: Retrieves all dashboard counts for admin overview
--
-- Returns: Counts for blogs, comments, tags, users, and unapproved comments
--
-- Called By: AdminSvc for admin dashboard
-- ============================================================================
CREATE OR REPLACE FUNCTION GetAdminCounts()
RETURNS TABLE (
    BlogCount BIGINT,
    CommentCount BIGINT,
    TagCount BIGINT,
    UserCount BIGINT,
    UnAppComments BIGINT
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        (SELECT COUNT(*) FROM Post WHERE Published = TRUE) AS BlogCount,
        (SELECT COUNT(*) FROM BlogComment) AS CommentCount,
        (SELECT COUNT(*) FROM Tag) AS TagCount,
        (SELECT COUNT(*) FROM BlogUser) AS UserCount,
        (SELECT COUNT(*) FROM BlogComment WHERE Published = FALSE) AS UnAppComments;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- USER LOGIN FUNCTIONS
-- ============================================================================

-- ============================================================================
-- FUNCTION: InsertUserLogin
-- Purpose: Records a new login session
--
-- Parameters:
--   pUserId - The logged-in user
--   pAccessToken - JWT access token
--   pRefreshToken - Refresh token
--   pExpiresOn - Token expiration time
--
-- Returns: The new LoginId
--
-- Called By: AuthSvc.AppLogin()
-- ============================================================================
CREATE OR REPLACE FUNCTION InsertUserLogin(
    pUserId BIGINT,
    pAccessToken TEXT,
    pRefreshToken TEXT,
    pExpiresOn TIMESTAMP
)
RETURNS BIGINT AS $$
DECLARE
    vLoginId BIGINT;
BEGIN
    INSERT INTO UserLogin (UserId, AccessToken, RefreshToken, LoginTime, ExpiresOn, IsActive)
    VALUES (pUserId, pAccessToken, pRefreshToken, NOW(), pExpiresOn, TRUE)
    RETURNING LoginId INTO vLoginId;

    RETURN vLoginId;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- FUNCTION: InsertLoginLog
-- Purpose: Records a login attempt for auditing
--
-- Parameters:
--   pUserId - User ID (null if failed login)
--   pAttemptedEmail - Email used in attempt
--   pSuccess - Whether login succeeded
--   pIpAddress - Client IP address
--   pUserAgent - Browser user agent
--
-- Returns: The new LogId
--
-- Called By: AuthSvc for audit logging
-- ============================================================================
CREATE OR REPLACE FUNCTION InsertLoginLog(
    pUserId BIGINT,
    pAttemptedEmail VARCHAR(255),
    pSuccess BOOLEAN,
    pIpAddress VARCHAR(100),
    pUserAgent VARCHAR(500)
)
RETURNS BIGINT AS $$
DECLARE
    vLogId BIGINT;
BEGIN
    INSERT INTO LoginLog (UserId, AttemptedEmail, Success, IpAddress, UserAgent, AttemptedOn)
    VALUES (pUserId, pAttemptedEmail, pSuccess, pIpAddress, pUserAgent, NOW())
    RETURNING LogId INTO vLogId;

    RETURN vLogId;
END;
$$ LANGUAGE plpgsql;
