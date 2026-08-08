-- ============================================================================
-- 022-FixBlogUserProjection.sql
--
-- Purpose:
--   Make SelectBlogUserById project EVERY column of BlogUser, so that a
--   read-modify-write round trip through the profile screen cannot silently
--   destroy data it never loaded.
--
-- Changes:
--   PART A - SelectBlogUserById returns all 26 BlogUser columns. The nine that
--            were missing are: Username, IsSiteOwner, Title, Tagline,
--            InstagramUrl, PhoneNumber, Location, CVFilePath, ResumeEnabled.
--
-- Business Rules / why this is a defect, not a tidy-up:
--   - ManageProfile.razor.cs loads the signed-in user with
--     BlogUserRepo.GetSingle -> SelectBlogUserById, binds the result into its
--     form model, and on Save writes the form model back through
--     UpdateResumeFields. Any column the function does not return arrives as
--     NULL/false, renders as an empty box, and is then PERSISTED as empty.
--   - Opening Manage Profile and pressing Save therefore erased the whole
--     resume - Title, Tagline, Location, PhoneNumber, CVFilePath,
--     InstagramUrl - and switched ResumeEnabled off. On the site owner's row
--     that is also the data source for the portfolio home page (REQ-UI-049)
--     and /resume (REQ-UI-036), so one Save blanked the public site.
--   - Username and IsSiteOwner were equally absent. Losing IsSiteOwner would
--     detach the portfolio from its owner; the partial unique index
--     IdxSingleSiteOwner means re-electing an owner is not a plain UPDATE.
--
--   - This is the FOURTH instance of one systemic pattern in this codebase: a
--     read projection that omits columns the write path persists.
--       1. BlogPostRepo.GetAll/GetAllById omitted BlogWriter, PublishedOn and
--          ScheduledPublishOn                                   (REQ-UI-017)
--       2. SelectBlogUserById omitted MustChangePassword         (script 021)
--       3. BlogPostRepo.SelectByIdSql/SelectBySlugSql omitted
--          PublishedOn and ScheduledPublishOn                    (REQ-NFR-008)
--       4. SelectBlogUserById omitted the nine columns above     (this script)
--     Script 021 fixed instance 2 by adding the ONE column it needed. This
--     script deliberately projects the whole row instead, so the next column
--     added to BlogUser cannot reopen the same defect a fifth time.
--
--   - The return type changes, so CREATE OR REPLACE is not enough: PostgreSQL
--     refuses to change a function's OUT parameters in place (42P13). The
--     function is dropped by exact signature first, which is safe and
--     idempotent. DbUp runs this once, but the script is re-runnable.
--
-- Dependencies:
--   002-CreateStoredFunctions.sql  (original SelectBlogUserById)
--   012-ResumeAndImageManagement.sql (resume columns)
--   013 / 017 (Username, IsSiteOwner)
--   021-LoginAuditAndForcedChange.sql (MustChangePassword projection)
--
-- Rollback:
--   DROP FUNCTION IF EXISTS SelectBlogUserById(BIGINT);
--   -- then re-run the SelectBlogUserById definition from script 021.
--   Rolling back re-introduces the data-loss defect; do not roll back without
--   also reverting the callers.
-- ============================================================================

-- ============================================================================
-- PART A: SelectBlogUserById projects every BlogUser column  [REQ-FN-053]
-- ============================================================================
DROP FUNCTION IF EXISTS SelectBlogUserById(BIGINT);

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
    SpeakDescription VARCHAR(1050),
    MustChangePassword BOOLEAN,
    Username VARCHAR(50),
    IsSiteOwner BOOLEAN,
    Title VARCHAR(150),
    Tagline VARCHAR(500),
    InstagramUrl VARCHAR(255),
    PhoneNumber VARCHAR(50),
    Location VARCHAR(150),
    CVFilePath VARCHAR(550),
    ResumeEnabled BOOLEAN
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        u.UserId, u.FirstName, u.LastName, u.EmailId, u.LoginPass,
        u.CreatedOn, u.UpdatedOn, u.UserRole, u.IsConfirmed,
        u.ProfileImagePath, u.ProfileDescription,
        u.TwitterUrl, u.LinkedInUrl, u.GitHubUrl,
        u.PodDescription, u.SpeakDescription,
        u.MustChangePassword,
        u.Username, u.IsSiteOwner,
        u.Title, u.Tagline, u.InstagramUrl,
        u.PhoneNumber, u.Location, u.CVFilePath, u.ResumeEnabled
    FROM BlogUser u
    WHERE u.UserId = pUserId;
END;
$$ LANGUAGE plpgsql;
