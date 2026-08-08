-- ============================================================================
-- Script: 003-SeedData.sql
-- Purpose: Seeds initial data for TechieBlog PostgreSQL database
-- Author: James (Dev Agent)
-- Created: 2025-12-17
-- Modified: 2025-12-17 - Initial seed data for PostgreSQL migration
-- Modified: 2026-08-06 - [REQ-NFR-023] Bootstrap admin credential is now stored as a
--                        PBKDF2-HMAC-SHA256 hash instead of plain text, and every INSERT
--                        was made idempotent so re-running the script is safe.
--
-- Dependencies: 001-CreateTables.sql (UserRole, BlogUser, Category, UserSettings)
-- Follow-up:    017-SecurityAndTokenPersistence.sql adds BlogUser.MustChangePassword,
--               flags this account and repairs databases seeded before this change.
-- Rollback:     DELETE FROM UserSettings WHERE UserId = 1;
--               DELETE FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com';
--               DELETE FROM Category; DELETE FROM UserRole;
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
SELECT 'Admin', 'Administrator role with full access'
WHERE NOT EXISTS (SELECT 1 FROM UserRole WHERE RoleName = 'Admin');

INSERT INTO UserRole (RoleName, RoleDesc)
SELECT 'Blogger', 'Blogger role with access to create and manage blog posts'
WHERE NOT EXISTS (SELECT 1 FROM UserRole WHERE RoleName = 'Blogger');

INSERT INTO UserRole (RoleName, RoleDesc)
SELECT 'Subscriber', 'Subscriber role with access to read and comment on blog posts'
WHERE NOT EXISTS (SELECT 1 FROM UserRole WHERE RoleName = 'Subscriber');

-- ============================================================================
-- DEFAULT ADMIN USER  [REQ-NFR-023]
-- Purpose: Create the bootstrap administrator used to sign in to a fresh install
--
-- Credentials:
--   Email:    Ravi@techieblog.com
--   Password: admin_password
--
-- SECURITY: the password is NOT stored in plain text. LoginPass below holds a
-- PBKDF2-HMAC-SHA256 hash produced by BlogModels.PasswordHasher:
--
--   format     PBKDF2-SHA256$<iterations>$<base64 salt>$<base64 subkey>
--   algorithm  PBKDF2 with HMAC-SHA256 (Rfc2898DeriveBytes.Pbkdf2, BCL)
--   iterations 210000   (OWASP recommendation for PBKDF2-HMAC-SHA256)
--   salt       fixed 16-byte seed salt ("TechieBlogSeed01") so this migration is
--              deterministic and idempotent; every account created at runtime gets a
--              fresh random 128-bit salt instead
--
-- The account is flagged MustChangePassword by 017-SecurityAndTokenPersistence.sql,
-- so the very first sign-in has to replace this well-known bootstrap password. To
-- regenerate the literal below:
--   BlogModels.PasswordHasher.HashPasswordWithSalt(
--       "<new password>", Encoding.UTF8.GetBytes("TechieBlogSeed01"))
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
SELECT
    'S Ravi',
    'Kumar',
    'Ravi@techieblog.com',
    'PBKDF2-SHA256$210000$VGVjaGllQmxvZ1NlZWQwMQ==$m3BUDC+/QWc38+4jGaLfRF6VDV/ksim4+JCoOJJZjw4=',
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
WHERE NOT EXISTS (
    SELECT 1 FROM BlogUser WHERE LOWER(EmailId) = 'ravi@techieblog.com'
);

-- ============================================================================
-- DEFAULT CATEGORIES
-- Purpose: Seed initial blog categories for content organization
-- ============================================================================
INSERT INTO Category (CategoryName)
SELECT c.Name
FROM (VALUES ('Technology'), ('Programming'), ('Web Development'), ('DevOps'), ('Career')) AS c(Name)
WHERE NOT EXISTS (SELECT 1 FROM Category ex WHERE ex.CategoryName = c.Name);

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
SELECT
    NULL,
    'Welcome to TechieBlog',
    5,
    5,
    10,
    3,
    NOW(),
    u.UserId
FROM BlogUser u
WHERE LOWER(u.EmailId) = 'ravi@techieblog.com'
  AND NOT EXISTS (SELECT 1 FROM UserSettings s WHERE s.UserId = u.UserId);
