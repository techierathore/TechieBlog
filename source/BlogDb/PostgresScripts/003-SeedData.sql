-- ============================================================================
-- Script: 003-SeedData.sql
-- Purpose: Seeds initial data for TechieBlog PostgreSQL database
-- Author: James (Dev Agent)
-- Created: 2025-12-17
-- Modified: 2025-12-17 - Initial seed data for PostgreSQL migration
-- ============================================================================

-- ============================================================================
-- USER ROLES
-- Purpose: Define the core authorization roles for the application
--
-- Roles:
--   1. Admin - Full system access including user management
--   2. Blogger - Can create and manage own posts
--   3. Subscriber - Can read posts and leave comments
-- ============================================================================
INSERT INTO UserRole (RoleName, RoleDesc)
VALUES ('Admin', 'Administrator role with full access');

INSERT INTO UserRole (RoleName, RoleDesc)
VALUES ('Blogger', 'Blogger role with access to create and manage blog posts');

INSERT INTO UserRole (RoleName, RoleDesc)
VALUES ('Subscriber', 'Subscriber role with access to read and comment on blog posts');

-- ============================================================================
-- DEFAULT ADMIN USER
-- Purpose: Create initial admin user for system access
--
-- Credentials:
--   Email: Ravi@techieblog.com
--   Password: admin_password (should be changed immediately after first login)
--
-- Note: In production, the password should be properly hashed using
-- the application's AppEncrypt.CreateHash() method before deployment.
-- ============================================================================
INSERT INTO BlogUser (
    FirstName,
    LastName,
    EmailId,
    LoginPass,
    CreatedOn,
    UpdatedOn,
    UserRole,
    IsConfirmed,
    ProfileImagePath,
    ProfileDescription,
    TwitterUrl,
    LinkedInUrl,
    GitHubUrl,
    PodDescription,
    SpeakDescription
)
VALUES (
    'S Ravi',
    'Kumar',
    'Ravi@techieblog.com',
    'admin_password',  -- TODO: Replace with hashed password in production
    NOW(),
    NOW(),
    'Admin',
    TRUE,  -- Admin account is pre-confirmed
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL
);

-- ============================================================================
-- DEFAULT CATEGORIES
-- Purpose: Seed initial blog categories for content organization
-- ============================================================================
INSERT INTO Category (CategoryName) VALUES ('Technology');
INSERT INTO Category (CategoryName) VALUES ('Programming');
INSERT INTO Category (CategoryName) VALUES ('Web Development');
INSERT INTO Category (CategoryName) VALUES ('DevOps');
INSERT INTO Category (CategoryName) VALUES ('Career');

-- ============================================================================
-- DEFAULT USER SETTINGS
-- Purpose: Create default display settings for the admin user
--
-- Settings define:
--   - Number of recent posts on home page (5)
--   - Number of categories to display (5)
--   - Posts per page for pagination (10)
--   - Number of featured/top posts (3)
-- ============================================================================
INSERT INTO UserSettings (
    HomeImage,
    HomeImageText,
    NumberOfLastPost,
    NumberOfCategory,
    PostNumberInPage,
    NumberOfTopPost,
    UpdatedTime,
    UserId
)
VALUES (
    NULL,
    'Welcome to TechieBlog',
    5,
    5,
    10,
    3,
    NOW(),
    1  -- Admin user ID
);
