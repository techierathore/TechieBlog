-- ============================================================================
-- Script: 001-CreateTables.sql
-- Purpose: Creates all core tables for TechieBlog PostgreSQL database
-- Author: James (Dev Agent)
-- Created: 2025-12-17
-- Modified: 2025-12-17 - Initial PostgreSQL migration from MySQL
-- ============================================================================

-- ============================================================================
-- TABLE: UserRole
-- Purpose: Defines user roles for authorization (Admin, Blogger, Subscriber)
--
-- Business Rules:
--   - RoleName must be unique
--   - Every BlogUser must have a role assigned
-- ============================================================================
CREATE TABLE UserRole (
    -- Primary identifier, auto-generated
    RoleId SERIAL PRIMARY KEY,

    -- Role name displayed in UI and used for authorization checks
    RoleName VARCHAR(50) NOT NULL UNIQUE,

    -- Optional description of the role's permissions
    RoleDesc VARCHAR(255)
);

-- ============================================================================
-- TABLE: BlogUser
-- Purpose: Stores user accounts for authors, admins, and subscribers
--
-- Relationships:
--   - UserRole (UserRoleId) - User's authorization role
--   - Post (via UserId) - Posts authored by user
--   - BlogImage (via UserId) - Images uploaded by user
--
-- Business Rules:
--   - EmailId must be unique for login identification
--   - IsConfirmed indicates email verification status
--   - UserRole determines access permissions
-- ============================================================================
CREATE TABLE BlogUser (
    -- Primary identifier, auto-generated
    UserId BIGSERIAL PRIMARY KEY,

    -- User's first name for display
    FirstName VARCHAR(100) NOT NULL,

    -- User's last name for display
    LastName VARCHAR(100) NOT NULL,

    -- Email used for login - must be unique
    EmailId VARCHAR(255) NOT NULL UNIQUE,

    -- Hashed password for authentication
    LoginPass VARCHAR(255) NOT NULL,

    -- Timestamp when user account was created
    CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Timestamp of last profile update
    UpdatedOn TIMESTAMP,

    -- User role name for authorization (Admin, Blogger, Subscriber)
    UserRole VARCHAR(51) NOT NULL,

    -- Whether email has been confirmed
    IsConfirmed BOOLEAN DEFAULT FALSE,

    -- Path to user's profile image
    ProfileImagePath VARCHAR(255),

    -- User's bio/description shown on profile
    ProfileDescription TEXT,

    -- Twitter profile URL
    TwitterUrl VARCHAR(255),

    -- LinkedIn profile URL
    LinkedInUrl VARCHAR(255),

    -- GitHub profile URL
    GitHubUrl VARCHAR(255),

    -- Podcast description for speakers
    PodDescription VARCHAR(1050),

    -- Speaking engagement description
    SpeakDescription VARCHAR(1050)
);

-- Index for email lookups during login
CREATE UNIQUE INDEX IdxBlogUserEmail ON BlogUser(EmailId);

-- ============================================================================
-- TABLE: UserLogin
-- Purpose: Tracks active login sessions and JWT tokens
--
-- Relationships:
--   - BlogUser (UserId) - The logged-in user
--
-- Business Rules:
--   - Each login creates a new session record
--   - Tokens have expiration dates for security
--   - Used to track and invalidate sessions
-- ============================================================================
CREATE TABLE UserLogin (
    -- Primary identifier, auto-generated
    LoginId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- JWT access token for API authentication
    AccessToken TEXT,

    -- Refresh token for obtaining new access tokens
    RefreshToken TEXT,

    -- When the login session was created
    LoginTime TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- When the tokens expire
    ExpiresOn TIMESTAMP,

    -- Whether this session is still active
    IsActive BOOLEAN DEFAULT TRUE
);

-- Index for user session lookups
CREATE INDEX IdxUserLoginUserId ON UserLogin(UserId);

-- ============================================================================
-- TABLE: LoginLog
-- Purpose: Audit log for all login attempts (successful and failed)
--
-- Relationships:
--   - BlogUser (UserId) - The user attempting to login (nullable for failed)
--
-- Business Rules:
--   - Records all login attempts for security auditing
--   - Stores IP address and user agent for tracking
--   - Success field indicates whether login was successful
-- ============================================================================
CREATE TABLE LoginLog (
    -- Primary identifier, auto-generated
    LogId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser (nullable for failed login attempts)
    UserId BIGINT REFERENCES BlogUser(UserId),

    -- Email address used in login attempt
    AttemptedEmail VARCHAR(255),

    -- Whether the login was successful
    Success BOOLEAN NOT NULL,

    -- IP address of the login attempt
    IpAddress VARCHAR(100),

    -- Browser/client user agent string
    UserAgent VARCHAR(500),

    -- Timestamp of the login attempt
    AttemptedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for user login history lookups
CREATE INDEX IdxLoginLogUserId ON LoginLog(UserId);

-- Index for login attempt time queries
CREATE INDEX IdxLoginLogAttemptedOn ON LoginLog(AttemptedOn DESC);

-- ============================================================================
-- TABLE: Category
-- Purpose: Categorizes blog posts into topics
--
-- Relationships:
--   - PostCategory (junction) - Links to posts
--
-- Business Rules:
--   - Category names should be unique (enforced at application level)
-- ============================================================================
CREATE TABLE Category (
    -- Primary identifier, auto-generated
    CategoryId BIGSERIAL PRIMARY KEY,

    -- Category display name
    CategoryName VARCHAR(150) NOT NULL
);

-- ============================================================================
-- TABLE: Post
-- Purpose: Stores all blog post content and metadata
--
-- Relationships:
--   - BlogUser (UserId) - Author of the post
--   - PostCategory (junction) - Post categorization
--   - BlogComment - Comments on the post
--
-- Business Rules:
--   - Published = false indicates draft status
--   - ScheduledFor enables future publishing
--   - Tags stored as comma-separated string for quick display
-- ============================================================================
CREATE TABLE Post (
    -- Primary identifier, auto-generated
    PostId BIGSERIAL PRIMARY KEY,

    -- Post title displayed in UI and used for SEO
    Title VARCHAR(550) NOT NULL,

    -- Short summary shown in post listings and meta description
    Abstract VARCHAR(550),

    -- Full post content in Markdown format
    PostContent TEXT NOT NULL,

    -- Timestamp when post was first created
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    -- Timestamp of last modification
    UpdatedOn TIMESTAMP,

    -- Foreign key to BlogUser - the post author
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- Comma-separated tag names for quick display
    Tags VARCHAR(550),

    -- Path to featured/hero image for post
    FeaturedImage VARCHAR(550),

    -- Publication status: false = draft, true = published
    Published BOOLEAN NOT NULL DEFAULT FALSE,

    -- Future publish date for scheduled posts
    ScheduledFor TIMESTAMP,

    -- SEO: Custom title for search engines
    SeoTitle VARCHAR(255),

    -- SEO: Meta description for search results
    SeoDescription VARCHAR(500)
);

-- Index for author's post queries
CREATE INDEX IdxPostUserId ON Post(UserId);

-- Index for published posts sorted by date (common query pattern)
CREATE INDEX IdxPostPublished ON Post(Published, CreatedOn DESC);

-- ============================================================================
-- TABLE: PostCategory
-- Purpose: Junction table linking posts to categories (many-to-many)
--
-- Relationships:
--   - Post (PostId) - The blog post
--   - Category (CategoryId) - The category
--
-- Business Rules:
--   - Composite primary key prevents duplicate assignments
-- ============================================================================
CREATE TABLE PostCategory (
    -- Foreign key to Post
    PostId BIGINT NOT NULL REFERENCES Post(PostId),

    -- Foreign key to Category
    CategoryId BIGINT NOT NULL REFERENCES Category(CategoryId),

    -- Composite primary key
    PRIMARY KEY (PostId, CategoryId)
);

-- ============================================================================
-- TABLE: Tag
-- Purpose: Stores unique tags for post classification
--
-- Business Rules:
--   - Tag names should be unique
--   - Tags are linked to posts via the Tags field in Post table
-- ============================================================================
CREATE TABLE Tag (
    -- Primary identifier, auto-generated
    TagId BIGSERIAL PRIMARY KEY,

    -- Tag display name
    TagName VARCHAR(150) NOT NULL
);

-- ============================================================================
-- TABLE: BlogComment
-- Purpose: Stores user comments on blog posts
--
-- Relationships:
--   - Post (PostId) - The post being commented on
--   - BlogComment (ParentCommentId) - Self-reference for reply threads
--
-- Business Rules:
--   - Published = false indicates pending moderation
--   - ParentCommentId enables threaded comments
-- ============================================================================
CREATE TABLE BlogComment (
    -- Primary identifier, auto-generated
    CommentId BIGSERIAL PRIMARY KEY,

    -- Foreign key to Post
    PostId BIGINT NOT NULL REFERENCES Post(PostId),

    -- When the comment was submitted
    GivenOn TIMESTAMP NOT NULL,

    -- Display name of commenter
    GivenBy VARCHAR(350) NOT NULL,

    -- Commenter's email address
    Email VARCHAR(350) NOT NULL,

    -- Comment content
    Comment VARCHAR(850) NOT NULL,

    -- Whether comment is approved for display
    Published BOOLEAN NOT NULL DEFAULT FALSE,

    -- Parent comment for threaded replies (null for top-level)
    ParentCommentId BIGINT REFERENCES BlogComment(CommentId)
);

-- Index for post comment lookups
CREATE INDEX IdxBlogCommentPostId ON BlogComment(PostId);

-- Index for moderation queue (unpublished comments)
CREATE INDEX IdxBlogCommentPublished ON BlogComment(Published);

-- ============================================================================
-- TABLE: BlogImage
-- Purpose: Stores metadata for uploaded images
--
-- Relationships:
--   - BlogUser (UserId) - User who uploaded the image
--
-- Business Rules:
--   - ImagePath stores the relative file path
--   - Size is in bytes
-- ============================================================================
CREATE TABLE BlogImage (
    -- Primary identifier, auto-generated
    BlogImageId BIGSERIAL PRIMARY KEY,

    -- Original filename
    ImageName VARCHAR(150),

    -- Storage path for the image file
    ImagePath VARCHAR(550) NOT NULL,

    -- File size in bytes
    Size INT,

    -- When the image was uploaded
    CreatedTime TIMESTAMP NOT NULL,

    -- Foreign key to BlogUser - uploader
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId)
);

-- Index for user's images
CREATE INDEX IdxBlogImageUserId ON BlogImage(UserId);

-- ============================================================================
-- TABLE: Subscriber
-- Purpose: Stores email newsletter subscribers
--
-- Business Rules:
--   - Email must be unique
--   - IsConfirmed indicates double opt-in verification
-- ============================================================================
CREATE TABLE Subscriber (
    -- Primary identifier, auto-generated
    SubscriberId BIGSERIAL PRIMARY KEY,

    -- Subscriber's email address - must be unique
    Email VARCHAR(255) NOT NULL UNIQUE,

    -- Subscriber's display name
    Name VARCHAR(255) NOT NULL,

    -- When they subscribed
    SubscribedOn TIMESTAMP NOT NULL,

    -- Whether email has been confirmed (double opt-in)
    IsConfirmed BOOLEAN DEFAULT FALSE,

    -- JSON preferences for email topics
    Preferences TEXT
);

-- ============================================================================
-- TABLE: LeadMagnet
-- Purpose: Stores downloadable content offers
--
-- Business Rules:
--   - MagnetFilePath points to the downloadable file
-- ============================================================================
CREATE TABLE LeadMagnet (
    -- Primary identifier, auto-generated
    LeadMagnetId BIGSERIAL PRIMARY KEY,

    -- Display name of the lead magnet
    MagnetName VARCHAR(255) NOT NULL,

    -- Path to the downloadable file
    MagnetFilePath VARCHAR(550) NOT NULL,

    -- Description shown to users
    Description TEXT
);

-- ============================================================================
-- TABLE: LeadMagnetDownload
-- Purpose: Tracks lead magnet downloads by subscribers
--
-- Relationships:
--   - Subscriber (SubscriberId) - Who downloaded
--   - LeadMagnet (LeadMagnetId) - What was downloaded
-- ============================================================================
CREATE TABLE LeadMagnetDownload (
    -- Primary identifier, auto-generated
    DownloadId BIGSERIAL PRIMARY KEY,

    -- Foreign key to Subscriber
    SubscriberId BIGINT NOT NULL REFERENCES Subscriber(SubscriberId),

    -- Foreign key to LeadMagnet
    LeadMagnetId BIGINT NOT NULL REFERENCES LeadMagnet(LeadMagnetId),

    -- When the download occurred
    DownloadedOn TIMESTAMP NOT NULL
);

-- Index for subscriber download history
CREATE INDEX IdxLeadMagnetDownloadSubscriberId ON LeadMagnetDownload(SubscriberId);

-- ============================================================================
-- TABLE: UserEvents
-- Purpose: Stores speaking engagements and events for users
--
-- Relationships:
--   - BlogUser (UserId) - The speaker/presenter
-- ============================================================================
CREATE TABLE UserEvents (
    -- Primary identifier, auto-generated
    EventId BIGSERIAL PRIMARY KEY,

    -- Path to event/conference logo
    LogoIconPath VARCHAR(350),

    -- Name of the event/conference
    EventTitle VARCHAR(350),

    -- Title of the user's session
    SessionTitle VARCHAR(350),

    -- URL to the event page
    EventUrl VARCHAR(350),

    -- Date of the event
    EventDate TIMESTAMP,

    -- Type of event (Conference, Meetup, Webinar, etc.)
    Type VARCHAR(50),

    -- Foreign key to BlogUser
    UserId BIGINT REFERENCES BlogUser(UserId)
);

-- Index for user's events
CREATE INDEX IdxUserEventsUserId ON UserEvents(UserId);

-- ============================================================================
-- TABLE: UserSettings
-- Purpose: Stores user-specific UI and display settings
--
-- Relationships:
--   - BlogUser (UserId) - Owner of the settings
-- ============================================================================
CREATE TABLE UserSettings (
    -- Primary identifier, auto-generated
    SettingsId SERIAL PRIMARY KEY,

    -- Home page hero image path
    HomeImage VARCHAR(350),

    -- Text overlay on home image
    HomeImageText VARCHAR(250),

    -- Number of recent posts to show
    NumberOfLastPost SMALLINT,

    -- Number of categories to display
    NumberOfCategory SMALLINT,

    -- Posts per page for pagination
    PostNumberInPage SMALLINT,

    -- Number of top/featured posts
    NumberOfTopPost SMALLINT,

    -- When settings were last updated
    UpdatedTime TIMESTAMP,

    -- Foreign key to BlogUser
    UserId BIGINT REFERENCES BlogUser(UserId)
);

-- ============================================================================
-- TABLE: Widgets
-- Purpose: Stores configurable UI widgets (may be deprecated)
--
-- Relationships:
--   - BlogUser (UserId) - Widget owner
-- ============================================================================
CREATE TABLE Widgets (
    -- Primary identifier, auto-generated
    WidgetId SERIAL PRIMARY KEY,

    -- Widget display name
    WidgetName VARCHAR(150) NOT NULL,

    -- Widget content/configuration
    WidgetContent VARCHAR(550) NOT NULL,

    -- When widget was last updated
    UpdatedTime TIMESTAMP,

    -- Foreign key to BlogUser
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId)
);

-- ============================================================================
-- TABLE: PostViews
-- Purpose: Tracks view analytics for blog posts
--
-- Relationships:
--   - Post (PostId) - The viewed post
--
-- Business Rules:
--   - Each view creates a new record for detailed analytics
--   - ViewerIp can be used to filter unique views
-- ============================================================================
CREATE TABLE PostViews (
    -- Primary identifier, auto-generated
    ViewId BIGSERIAL PRIMARY KEY,

    -- Foreign key to Post
    PostId BIGINT NOT NULL REFERENCES Post(PostId),

    -- When the view occurred
    ViewedOn TIMESTAMP NOT NULL,

    -- IP address of the viewer
    ViewerIp VARCHAR(100)
);

-- Index for post view counts
CREATE INDEX IdxPostViewsPostId ON PostViews(PostId);

-- Index for time-based analytics
CREATE INDEX IdxPostViewsViewedOn ON PostViews(ViewedOn DESC);

-- ============================================================================
-- TABLE: UserActions
-- Purpose: Tracks user activity for analytics and auditing
--
-- Relationships:
--   - BlogUser (UserId) - The user performing the action
-- ============================================================================
CREATE TABLE UserActions (
    -- Primary identifier, auto-generated
    ActionId BIGSERIAL PRIMARY KEY,

    -- Foreign key to BlogUser (nullable for anonymous actions)
    UserId BIGINT REFERENCES BlogUser(UserId),

    -- Type of action performed
    ActionType VARCHAR(100) NOT NULL,

    -- When the action occurred
    ActionTimestamp TIMESTAMP NOT NULL,

    -- Additional details as JSON or text
    Details TEXT
);

-- Index for user action history
CREATE INDEX IdxUserActionsUserId ON UserActions(UserId);

-- ============================================================================
-- TABLE: Newsletter
-- Purpose: Stores newsletter content for email campaigns
--
-- Business Rules:
--   - Status: 'draft', 'scheduled', 'sent'
--   - ScheduledFor enables future sending
-- ============================================================================
CREATE TABLE Newsletter (
    -- Primary identifier, auto-generated
    NewsletterId BIGSERIAL PRIMARY KEY,

    -- Newsletter subject line
    Title VARCHAR(255) NOT NULL,

    -- Newsletter body content (HTML or Markdown)
    Content TEXT NOT NULL,

    -- When the newsletter was created
    CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- When to send (for scheduled newsletters)
    ScheduledFor TIMESTAMP,

    -- Current status: draft, scheduled, sent
    Status VARCHAR(50) DEFAULT 'draft' CHECK (Status IN ('draft', 'scheduled', 'sent'))
);

-- ============================================================================
-- TABLE: SubscriberNewsletter
-- Purpose: Tracks newsletter delivery and engagement per subscriber
--
-- Relationships:
--   - Subscriber (SubscriberId) - The recipient
--   - Newsletter (NewsletterId) - The newsletter sent
-- ============================================================================
CREATE TABLE SubscriberNewsletter (
    -- Primary identifier, auto-generated
    Id BIGSERIAL PRIMARY KEY,

    -- Foreign key to Subscriber
    SubscriberId BIGINT NOT NULL REFERENCES Subscriber(SubscriberId),

    -- Foreign key to Newsletter
    NewsletterId BIGINT NOT NULL REFERENCES Newsletter(NewsletterId),

    -- When the email was sent
    SentOn TIMESTAMP,

    -- When the email was opened (tracking pixel)
    OpenedOn TIMESTAMP,

    -- When a link was clicked
    ClickedOn TIMESTAMP
);

-- Index for subscriber's newsletter history
CREATE INDEX IdxSubscriberNewsletterSubscriberId ON SubscriberNewsletter(SubscriberId);

-- ============================================================================
-- TABLE: EmailSequence
-- Purpose: Defines automated email sequences (drip campaigns)
--
-- Business Rules:
--   - IsActive controls whether new subscribers enter the sequence
-- ============================================================================
CREATE TABLE EmailSequence (
    -- Primary identifier, auto-generated
    SequenceId BIGSERIAL PRIMARY KEY,

    -- Sequence display name
    Name VARCHAR(255) NOT NULL,

    -- Description of the sequence purpose
    Description TEXT,

    -- When the sequence was created
    CreatedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Whether the sequence is active for new subscribers
    IsActive BOOLEAN DEFAULT TRUE
);

-- ============================================================================
-- TABLE: EmailSequenceStep
-- Purpose: Individual emails within an email sequence
--
-- Relationships:
--   - EmailSequence (SequenceId) - Parent sequence
--
-- Business Rules:
--   - StepOrder determines email order
--   - DelayDays is days after previous step (or sequence start)
-- ============================================================================
CREATE TABLE EmailSequenceStep (
    -- Primary identifier, auto-generated
    StepId BIGSERIAL PRIMARY KEY,

    -- Foreign key to EmailSequence
    SequenceId BIGINT NOT NULL REFERENCES EmailSequence(SequenceId),

    -- Order within the sequence (1, 2, 3, etc.)
    StepOrder INT NOT NULL,

    -- Email subject line
    EmailSubject VARCHAR(255) NOT NULL,

    -- Email body content
    EmailContent TEXT NOT NULL,

    -- Days to wait after previous step
    DelayDays INT NOT NULL
);

-- Index for sequence step ordering
CREATE INDEX IdxEmailSequenceStepSequenceId ON EmailSequenceStep(SequenceId);

-- ============================================================================
-- TABLE: SubscriberSequence
-- Purpose: Tracks subscriber progress through email sequences
--
-- Relationships:
--   - Subscriber (SubscriberId) - The subscriber
--   - EmailSequence (SequenceId) - The sequence
--   - EmailSequenceStep (CurrentStepId) - Current position
-- ============================================================================
CREATE TABLE SubscriberSequence (
    -- Primary identifier, auto-generated
    Id BIGSERIAL PRIMARY KEY,

    -- Foreign key to Subscriber
    SubscriberId BIGINT NOT NULL REFERENCES Subscriber(SubscriberId),

    -- Foreign key to EmailSequence
    SequenceId BIGINT NOT NULL REFERENCES EmailSequence(SequenceId),

    -- When the subscriber entered the sequence
    StartedOn TIMESTAMP NOT NULL,

    -- Current step in the sequence
    CurrentStepId BIGINT REFERENCES EmailSequenceStep(StepId)
);

-- Index for subscriber's active sequences
CREATE INDEX IdxSubscriberSequenceSubscriberId ON SubscriberSequence(SubscriberId);
