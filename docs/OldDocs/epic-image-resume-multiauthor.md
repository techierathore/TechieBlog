# Epic: Image Management, Resume Page & Multi-Author Profiles

**Epic ID:** EPIC-IRM-001
**Created:** 2026-01-02
**Source:** `docs/feature-ideation-images-resume.md`
**Status:** Ready for Implementation

---

## Orchestrator Execution Plan

This plan enables **maximum parallelization** across 6 work streams. The orchestrator should spawn multiple agents simultaneously where dependencies allow.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        PARALLEL EXECUTION TIMELINE                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  WAVE 1 (No Dependencies - Start Immediately in Parallel)                   │
│  ════════════════════════════════════════════════════════                   │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │ STREAM A    │ │ STREAM B    │ │ STREAM C    │ │ STREAM D    │           │
│  │ Database    │ │ Models      │ │ Upload      │ │ CSS/Themes  │           │
│  │ Migration   │ │ (C# POCO)   │ │ Directories │ │ Foundation  │           │
│  │ [DEV]       │ │ [DEV]       │ │ [DEV]       │ │ [DEV]       │           │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘ └─────────────┘           │
│         │               │               │                                   │
│  ═══════╪═══════════════╪═══════════════╪═══════════════════════════════   │
│                                                                             │
│  WAVE 2 (After Models Complete)                                             │
│  ══════════════════════════════                                             │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐                           │
│  │ STREAM E    │ │ STREAM F    │ │ STREAM G    │                           │
│  │ Repositories│ │ Image Svc   │ │ User Repo   │                           │
│  │ (Skills,    │ │ Interface + │ │ Extensions  │                           │
│  │  Awards,    │ │ Implement   │ │             │                           │
│  │  Stats)     │ │ [DEV]       │ │ [DEV]       │                           │
│  │ [DEV]       │ │             │ │             │                           │
│  └──────┬──────┘ └──────┬──────┘ └──────┬──────┘                           │
│         │               │               │                                   │
│  ═══════╪═══════════════╪═══════════════╪═══════════════════════════════   │
│                                                                             │
│  WAVE 3 (After Services Complete - Parallel UI Streams)                     │
│  ══════════════════════════════════════════════════════                     │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐           │
│  │ STREAM H    │ │ STREAM I    │ │ STREAM J    │ │ STREAM K    │           │
│  │ ImagePicker │ │ Admin CRUD  │ │ Public      │ │ Author      │           │
│  │ Component   │ │ Pages       │ │ Resume Page │ │ Pages       │           │
│  │ [DEV]       │ │ [DEV x4]    │ │ Components  │ │ [DEV x2]    │           │
│  │             │ │             │ │ [DEV x4]    │ │             │           │
│  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘           │
│                                                                             │
│  WAVE 4 (Integration & QA)                                                  │
│  ═════════════════════════                                                  │
│  ┌─────────────────────────────────────────────────────────────┐           │
│  │ STREAM L: Integration Testing & QA Gate                      │           │
│  │ [QA]                                                         │           │
│  └─────────────────────────────────────────────────────────────┘           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## WAVE 1: Foundation Layer (No Dependencies)

### Stream A: Database Migration Script
**Agent:** `/dev`
**Priority:** CRITICAL
**Parallel:** Yes (independent)

**Task:** Create migration script `005-ResumeAndImageManagement.sql`

**File:** `source/BlogDb/PostgresScripts/005-ResumeAndImageManagement.sql`

**Specification:**
```sql
-- PART A: Extend BlogImage table
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Category VARCHAR(50) DEFAULT 'general';
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS AltText VARCHAR(255);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS MimeType VARCHAR(100);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Width INT;
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Height INT;
CREATE INDEX IF NOT EXISTS IdxBlogImageCategory ON BlogImage(Category);

-- PART B: Extend BlogUser (AppUser) for Multi-Author and Resume
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Username VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS IsSiteOwner BOOLEAN DEFAULT FALSE;
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Title VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Tagline VARCHAR(500);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS InstagramUrl VARCHAR(255);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS PhoneNumber VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Location VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS CVFilePath VARCHAR(550);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS ResumeEnabled BOOLEAN DEFAULT FALSE;
CREATE UNIQUE INDEX IF NOT EXISTS IdxBlogUserUsername ON BlogUser(Username) WHERE Username IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IdxSingleSiteOwner ON BlogUser ((CASE WHEN IsSiteOwner = TRUE THEN 1 END)) WHERE IsSiteOwner = TRUE;

-- PART C: Extend UserEvents for Experience Timeline
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS StartDate TIMESTAMP;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS Description TEXT;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS DisplayOrder INT DEFAULT 0;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS IsCurrent BOOLEAN DEFAULT FALSE;

-- PART D: Create UserSkills Table
CREATE TABLE IF NOT EXISTS UserSkills (
    SkillId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    Category VARCHAR(100) NOT NULL,
    SkillName VARCHAR(150) NOT NULL,
    IconPath VARCHAR(350),
    DisplayOrder INT DEFAULT 0,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IdxUserSkillsUserId ON UserSkills(UserId);
CREATE INDEX IF NOT EXISTS IdxUserSkillsCategory ON UserSkills(Category);

-- PART E: Create UserAwards Table
CREATE TABLE IF NOT EXISTS UserAwards (
    AwardId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    AwardTitle VARCHAR(255) NOT NULL,
    AwardDescription TEXT,
    BadgeImagePath VARCHAR(550),
    AwardUrl VARCHAR(350),
    AwardYear VARCHAR(50),
    DisplayOrder INT DEFAULT 0,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS IdxUserAwardsUserId ON UserAwards(UserId);

-- PART F: Create UserStats Table
CREATE TABLE IF NOT EXISTS UserStats (
    StatId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    StatLabel VARCHAR(100) NOT NULL,
    StatValue VARCHAR(50) NOT NULL,
    StatCategory VARCHAR(50),
    DisplayOrder INT DEFAULT 0
);
CREATE INDEX IF NOT EXISTS IdxUserStatsUserId ON UserStats(UserId);
```

**Acceptance Criteria:**
- [ ] Migration runs without errors via DbUp
- [ ] All indexes created correctly
- [ ] Unique constraints enforced (Username, single SiteOwner)
- [ ] Existing data preserved

---

### Stream B: C# Model Classes
**Agent:** `/dev`
**Priority:** CRITICAL
**Parallel:** Yes (independent)

**Task:** Create 3 new model classes + extend 3 existing models

**Files to Create:**

#### B.1: `source/BlogModel/Models/UserSkill.cs`
```csharp
namespace BlogModels.Models;

public class UserSkill
{
    public long SkillId { get; set; }
    public long UserId { get; set; }
    public string Category { get; set; }
    public string SkillName { get; set; }
    public string? IconPath { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOn { get; set; }
}
```

#### B.2: `source/BlogModel/Models/UserAward.cs`
```csharp
namespace BlogModels.Models;

public class UserAward
{
    public long AwardId { get; set; }
    public long UserId { get; set; }
    public string AwardTitle { get; set; }
    public string? AwardDescription { get; set; }
    public string? BadgeImagePath { get; set; }
    public string? AwardUrl { get; set; }
    public string? AwardYear { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedOn { get; set; }
}
```

#### B.3: `source/BlogModel/Models/UserStat.cs`
```csharp
namespace BlogModels.Models;

public class UserStat
{
    public long StatId { get; set; }
    public long UserId { get; set; }
    public string StatLabel { get; set; }
    public string StatValue { get; set; }
    public string? StatCategory { get; set; }
    public int DisplayOrder { get; set; }
}
```

**Files to Extend:**

#### B.4: Extend `source/BlogModel/Models/AppUser.cs`
Add these properties:
```csharp
public string? Username { get; set; }
public bool IsSiteOwner { get; set; }
public string? Title { get; set; }
public string? Tagline { get; set; }
public string? InstagramUrl { get; set; }
public string? PhoneNumber { get; set; }
public string? Location { get; set; }
public string? CVFilePath { get; set; }
public bool ResumeEnabled { get; set; }
```

#### B.5: Extend `source/BlogModel/Models/BlogImage.cs`
Add these properties:
```csharp
public string Category { get; set; } = "general";
public string? AltText { get; set; }
public string? MimeType { get; set; }
public int? Width { get; set; }
public int? Height { get; set; }
```

#### B.6: Extend `source/BlogModel/Models/UserEvent.cs`
Add these properties:
```csharp
public DateTime? StartDate { get; set; }
public string? Description { get; set; }
public int DisplayOrder { get; set; }
public bool IsCurrent { get; set; }
```

**Acceptance Criteria:**
- [ ] All models follow existing namespace conventions
- [ ] Nullable reference types used appropriately
- [ ] Properties match database column names exactly

---

### Stream C: Upload Directory Structure
**Agent:** `/dev`
**Priority:** HIGH
**Parallel:** Yes (independent)

**Task:** Create upload directory structure and ensure it's git-tracked

**Directories to Create:**
```
source/BlogUI/wwwroot/uploads/
├── profiles/     # Profile photos
├── logos/        # Company logos
├── awards/       # Award badges
├── icons/        # Skill icons
├── blog/         # Blog post images
├── cv/           # CV/Resume PDFs
└── general/      # Miscellaneous
```

**Implementation:**
1. Create all directories
2. Add `.gitkeep` file in each empty directory
3. Add `web.config` or similar to prevent direct listing

**Acceptance Criteria:**
- [ ] All 7 directories exist under `wwwroot/uploads/`
- [ ] Directories are committed to git
- [ ] Static file serving works for these paths

---

### Stream D: CSS Foundation for Resume/Author Pages
**Agent:** `/dev`
**Priority:** MEDIUM
**Parallel:** Yes (independent)

**Task:** Create CSS files for resume and author pages

**Files to Create:**

#### D.1: `source/BlogUI/wwwroot/css/resume.css`
Styles for the full-page resume view (`/resume`):
- Full-screen hero section
- Anchor navigation
- Experience timeline
- Skills grid layout
- Awards display
- Smooth scroll behavior

#### D.2: `source/BlogUI/wwwroot/css/author.css`
Styles for author profile pages:
- Compact author header
- Author list view
- Author articles grid

**Reference:** Match existing theme fonts from `source/BlogUI/wwwroot/Themes/`

**Acceptance Criteria:**
- [ ] Responsive design (mobile-first)
- [ ] Uses existing theme CSS variables
- [ ] No visual regressions in existing pages

---

## WAVE 2: Service Layer (Depends on Wave 1 Models)

### Stream E: New Repository Implementations
**Agent:** `/dev`
**Priority:** HIGH
**Depends On:** Stream B (Models)

**Task:** Create repositories for new tables

**Files to Create:**

#### E.1: `source/BlogEngine/DbAccess/UserSkillsRepo.cs`
```csharp
public interface IUserSkillsRepo
{
    Task<IEnumerable<UserSkill>> GetByUserIdAsync(long userId);
    Task<IEnumerable<UserSkill>> GetByUserIdAndCategoryAsync(long userId, string category);
    Task<UserSkill> GetByIdAsync(long skillId);
    Task<long> CreateAsync(UserSkill skill);
    Task<bool> UpdateAsync(UserSkill skill);
    Task<bool> DeleteAsync(long skillId);
    Task<IEnumerable<string>> GetCategoriesAsync(long userId);
}
```

#### E.2: `source/BlogEngine/DbAccess/UserAwardsRepo.cs`
```csharp
public interface IUserAwardsRepo
{
    Task<IEnumerable<UserAward>> GetByUserIdAsync(long userId);
    Task<UserAward> GetByIdAsync(long awardId);
    Task<long> CreateAsync(UserAward award);
    Task<bool> UpdateAsync(UserAward award);
    Task<bool> DeleteAsync(long awardId);
}
```

#### E.3: `source/BlogEngine/DbAccess/UserStatsRepo.cs`
```csharp
public interface IUserStatsRepo
{
    Task<IEnumerable<UserStat>> GetByUserIdAsync(long userId);
    Task<IEnumerable<UserStat>> GetByUserIdAndCategoryAsync(long userId, string category);
    Task<UserStat> GetByIdAsync(long statId);
    Task<long> CreateAsync(UserStat stat);
    Task<bool> UpdateAsync(UserStat stat);
    Task<bool> DeleteAsync(long statId);
}
```

**Pattern:** Follow existing `GenericRepository` patterns in `source/BlogEngine/DaCore/`

**Acceptance Criteria:**
- [ ] All CRUD operations implemented
- [ ] Uses existing `DbConnectionFactory`
- [ ] SQL queries use parameterized inputs
- [ ] Registered in DI container

---

### Stream F: Blog Image Service
**Agent:** `/dev`
**Priority:** HIGH
**Depends On:** Stream B (Models), Stream C (Directories)

**Task:** Create comprehensive image upload and management service

**Files to Create:**

#### F.1: `source/BlogEngine/Services/IBlogImageService.cs`
```csharp
public interface IBlogImageService
{
    Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId);
    Task<BlogImage> UploadImageFromUrlAsync(string url, string category, long userId);
    Task<bool> DeleteImageAsync(long imageId, long userId);
    Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null);
    Task<IEnumerable<BlogImage>> GetImagesByUserAsync(long userId);
    Task<BlogImage?> GetImageAsync(long imageId);
    string GetImageUrl(string imagePath);
    Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category);
}
```

#### F.2: `source/BlogEngine/Services/BlogImageService.cs`
Implementation with:
- File validation (size, format per category)
- File naming: `{category}_{userId}_{timestamp}_{guid}.{ext}`
- Storage to `wwwroot/uploads/{category}/`
- Image dimension detection (for Width/Height fields)
- MIME type detection

**Category Constraints:**
| Category | Max Size | Allowed Formats |
|----------|----------|-----------------|
| profiles | 2MB | jpg, png, webp |
| logos | 500KB | jpg, png, svg, webp |
| awards | 500KB | jpg, png, svg, webp |
| icons | 200KB | png, svg, webp |
| blog | 5MB | jpg, png, gif, webp |
| cv | 10MB | pdf |
| general | 5MB | jpg, png, gif, webp |

**Acceptance Criteria:**
- [ ] File validation enforces category constraints
- [ ] Uploaded files stored in correct directories
- [ ] Orphan image cleanup method exists
- [ ] Service registered in DI

---

### Stream G: Extended User Repository
**Agent:** `/dev`
**Priority:** HIGH
**Depends On:** Stream B (Models)

**Task:** Extend existing user repository for multi-author/resume features

**File to Modify:** Find and extend existing user repository

**Methods to Add:**
```csharp
Task<AppUser?> GetByUsernameAsync(string username);
Task<AppUser?> GetSiteOwnerAsync();
Task<IEnumerable<AppUser>> GetAllAuthorsAsync(); // Users with posts
Task<bool> UpdateUsernameAsync(long userId, string username);
Task<bool> SetSiteOwnerAsync(long userId);
Task<bool> UpdateResumeFieldsAsync(long userId, AppUser resumeData);
Task<bool> IsUsernameAvailableAsync(string username);
```

**Acceptance Criteria:**
- [ ] `GetByUsernameAsync` returns null for non-existent usernames
- [ ] `GetSiteOwnerAsync` returns single user or null
- [ ] `IsUsernameAvailableAsync` handles case-insensitive check
- [ ] `SetSiteOwnerAsync` removes flag from previous owner

---

## WAVE 3: UI Layer (Depends on Wave 2 Services)

### Stream H: ImagePicker Component
**Agent:** `/dev`
**Priority:** HIGH
**Depends On:** Stream F (BlogImageService)

**Task:** Create reusable image picker component

**File:** `source/BlogUI/Components/ImagePicker.razor`

**Features:**
- Display current selected image
- "Choose from Library" button → opens gallery modal
- "Upload New" button → opens upload modal
- Category filtering
- Returns selected image path via `@bind-SelectedImagePath`

**Usage:**
```razor
<ImagePicker Category="profiles"
             @bind-SelectedImagePath="user.ProfileImagePath"
             UserId="@currentUserId" />
```

**Acceptance Criteria:**
- [ ] Gallery modal shows images filtered by category
- [ ] Upload validates file per category constraints
- [ ] Supports clearing selection
- [ ] Works with all 7 image categories

---

### Stream I: Admin CRUD Pages (4 Parallel Sub-Tasks)
**Agent:** `/dev` (spawn 4 instances)
**Priority:** HIGH
**Depends On:** Streams E, F, G, H

**Task:** Create admin management pages for resume data

#### I.1: `source/BlogUI/Pages/AdminPages/ManageImages.razor`
- Gallery view with category tabs
- Upload new images
- Delete images
- Copy URL functionality
- Pagination

#### I.2: `source/BlogUI/Pages/AdminPages/ManageExperience.razor`
- List user's experience entries (from UserEvents where Type='Experience')
- Add/Edit/Delete experience
- Drag-reorder (DisplayOrder)
- Uses ImagePicker for company logo
- Admin sees user selector dropdown

#### I.3: `source/BlogUI/Pages/AdminPages/ManageSkills.razor`
- Group skills by category
- Add/Edit/Delete skills
- Add new categories
- Reorder within category

#### I.4: `source/BlogUI/Pages/AdminPages/ManageAwards.razor`
- List awards
- Add/Edit/Delete awards
- Uses ImagePicker for badge image
- Reorder awards

**Authorization Pattern:**
```csharp
@code {
    private bool IsAdmin => AuthService.IsInRole("Admin");
    private long CurrentUserId => IsAdmin && selectedUserId.HasValue
        ? selectedUserId.Value
        : AuthService.GetCurrentUserId();

    // Admin sees user selector, Author sees only their data
}
```

**Acceptance Criteria:**
- [ ] Authors see only their own data
- [ ] Admins can switch between users
- [ ] All CRUD operations work
- [ ] Form validation present

---

### Stream J: Public Resume Page Components (4 Parallel Sub-Tasks)
**Agent:** `/dev` (spawn 4 instances)
**Priority:** HIGH
**Depends On:** Streams E, F, G

**Task:** Create public-facing resume page and components

#### J.1: `source/BlogUI/Pages/ResumePage.razor` + `ResumeHero.razor`
**Route:** `/resume`

Hero section layout:
```
┌─────────────────────────────────────────────────────────────┐
│                      [Profile Photo]                        │
│                         (circle)                            │
│                    Hi, I'm {FirstName}                      │
│                     {Title}                                 │
│                      {Tagline}                              │
│            [Get In Touch]    [Download CV]                  │
│              [LinkedIn] [GitHub] [X] [Instagram]            │
│                         ↓ Scroll                            │
└─────────────────────────────────────────────────────────────┘
```

- Full-viewport height
- Smooth scroll to sections
- Loads user where `IsSiteOwner = true`

#### J.2: `source/BlogUI/Components/Resume/ResumeExperience.razor`
Timeline layout:
```
┌─────────────────────────────────────────────────────────────┐
│  Experience                                                 │
│  ──────────                                                 │
│  [Logo] Current Role @ Company (2022 - Present)            │
│         • Bullet point 1                                    │
│         • Bullet point 2                                    │
│  [Logo] Previous Role @ Company (2019 - 2022)              │
│         • Bullet point 1                                    │
└─────────────────────────────────────────────────────────────┘
```

- Reverse chronological order
- Description renders as markdown (bullet points)
- "Present" for IsCurrent entries

#### J.3: `source/BlogUI/Components/Resume/ResumeSkills.razor`
Grid layout by category:
```
┌─────────────────────────────────────────────────────────────┐
│  Skills & Expertise                                         │
│  ─────────────────                                          │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐        │
│  │ AI/Emerging  │ │ Cloud/SaaS   │ │ Development  │        │
│  │ ──────────── │ │ ──────────── │ │ ──────────── │        │
│  │ • ChatGPT    │ │ • Azure      │ │ • C#         │        │
│  │ • ML/AI      │ │ • AWS        │ │ • Python     │        │
│  │ • Copilot    │ │ • Docker     │ │ • JavaScript │        │
│  └──────────────┘ └──────────────┘ └──────────────┘        │
└─────────────────────────────────────────────────────────────┘
```

#### J.4: `source/BlogUI/Components/Resume/ResumeAwards.razor` + `ResumeContact.razor`

**ResumeAwards:**
```
┌─────────────────────────────────────────────────────────────┐
│  Awards & Recognition                                       │
│  ───────────────────                                        │
│  [Badge] Microsoft MVP (2015-2024)                         │
│          10 consecutive years of recognition...             │
│  [Badge] Google Cloud Champion                              │
│          Cloud architecture excellence...                   │
└─────────────────────────────────────────────────────────────┘
```

**ResumeContact:**
```
┌─────────────────────────────────────────────────────────────┐
│  Get In Touch                                               │
│  ────────────                                               │
│  📧 email@example.com                                       │
│  📱 +91 9876543210                                          │
│  📍 Noida NCR, India                                        │
│  [LinkedIn] [GitHub] [Twitter] [Instagram]                  │
└─────────────────────────────────────────────────────────────┘
```

**Acceptance Criteria:**
- [ ] Full-page resume matches nitinpandit.com layout
- [ ] Anchor navigation works
- [ ] Responsive on mobile
- [ ] Downloads CV correctly

---

### Stream K: Author Pages (2 Parallel Sub-Tasks)
**Agent:** `/dev` (spawn 2 instances)
**Priority:** MEDIUM
**Depends On:** Streams E, F, G

#### K.1: Author Listing & Profile Pages
**Files:**
- `source/BlogUI/Pages/BlogPages/AuthorsPage.razor` (route: `/authors`)
- `source/BlogUI/Pages/BlogPages/AuthorProfilePage.razor` (route: `/author/{username}`)
- `source/BlogUI/Components/Author/AuthorListItem.razor`
- `source/BlogUI/Components/Author/AuthorHeader.razor`

**AuthorsPage Layout:**
```
┌─────────────────────────────────────────────────────────────┐
│  Our Authors                                                │
├─────────────────────────────────────────────────────────────┤
│  [Avatar] Ravi Sharma          Senior Developer    [View →] │
│           @ravi-sharma         15 articles                  │
│  ─────────────────────────────────────────────────────────  │
│  [Avatar] Jane Doe             Tech Writer         [View →] │
│           @jane-doe            8 articles                   │
└─────────────────────────────────────────────────────────────┘
```

**AuthorProfilePage Layout:**
- Compact header (not full-page hero)
- Author's articles list
- Resume sections (if `ResumeEnabled = true`)

#### K.2: Self-Service Profile Management
**File:** `source/BlogUI/Pages/AdminPages/ManageProfile.razor`

Layout:
```
┌─────────────────────────────────────────────────────────────┐
│  My Profile                                      [Save]     │
├─────────────────────────────────────────────────────────────┤
│  BASIC INFO          │  SOCIAL LINKS                       │
│  Avatar: [Picker]    │  LinkedIn: [___]                    │
│  Display Name: [___] │  GitHub: [___]                      │
│  Username: [___]     │  Twitter: [___]                     │
│  Title: [___]        │  Instagram: [___]                   │
│  Bio: [___]          │                                      │
├─────────────────────────────────────────────────────────────┤
│  RESUME SETTINGS                                            │
│  [x] Show resume on profile    CV: [Picker]                │
│  Phone: [___]                  Location: [___]             │
├─────────────────────────────────────────────────────────────┤
│  [Manage Experience →]  [Manage Skills →]                  │
│  [Manage Awards →]      [Manage Stats →]                   │
└─────────────────────────────────────────────────────────────┘
```

**Acceptance Criteria:**
- [ ] `/authors` lists all users with published posts
- [ ] `/author/{username}` shows 404 for invalid usernames
- [ ] Username validation (URL-safe, unique)
- [ ] Profile changes save correctly

---

## WAVE 4: Integration & QA

### Stream L: Integration Testing & QA Gate
**Agent:** `/qa`
**Priority:** CRITICAL
**Depends On:** All Wave 3 streams

**Task:** Execute QA checklist and integration testing

**Checklist:**

#### Database & Data Layer
- [ ] Migration `005-ResumeAndImageManagement.sql` runs successfully
- [ ] All new tables created with correct schema
- [ ] Existing data not affected
- [ ] Indexes created correctly

#### Image Management
- [ ] Upload works for all 7 categories
- [ ] File validation enforces size/format limits
- [ ] Delete removes file from disk
- [ ] Gallery displays correctly
- [ ] ImagePicker integrates with forms

#### Resume Page (`/resume`)
- [ ] Loads site owner's data
- [ ] All 6 sections render correctly
- [ ] Social links work
- [ ] CV download works
- [ ] Responsive on mobile
- [ ] Smooth scroll between sections

#### Author Pages
- [ ] `/authors` lists authors with article counts
- [ ] `/author/{username}` shows correct profile
- [ ] 404 for invalid usernames
- [ ] Resume sections show when `ResumeEnabled = true`
- [ ] Author name in posts links to profile

#### Admin Pages
- [ ] Authors see only their own data
- [ ] Admins see user selector
- [ ] All CRUD operations work
- [ ] Form validation present
- [ ] Changes persist correctly

#### Security
- [ ] Authorization enforced on admin pages
- [ ] File uploads validated server-side
- [ ] No path traversal vulnerabilities
- [ ] Username input sanitized

---

## Agent Assignment Summary

| Stream | Agent | Tasks | Can Run With |
|--------|-------|-------|--------------|
| A | `/dev` | Database migration | B, C, D |
| B | `/dev` | Model classes | A, C, D |
| C | `/dev` | Directory setup | A, B, D |
| D | `/dev` | CSS files | A, B, C |
| E | `/dev` | Repositories (Skills, Awards, Stats) | F, G (after B) |
| F | `/dev` | BlogImageService | E, G (after B, C) |
| G | `/dev` | User repo extensions | E, F (after B) |
| H | `/dev` | ImagePicker component | I, J, K (after F) |
| I.1 | `/dev` | ManageImages | I.2, I.3, I.4, J.*, K.* |
| I.2 | `/dev` | ManageExperience | I.1, I.3, I.4, J.*, K.* |
| I.3 | `/dev` | ManageSkills | I.1, I.2, I.4, J.*, K.* |
| I.4 | `/dev` | ManageAwards | I.1, I.2, I.3, J.*, K.* |
| J.1 | `/dev` | ResumePage + Hero | J.2, J.3, J.4, I.*, K.* |
| J.2 | `/dev` | ResumeExperience | J.1, J.3, J.4, I.*, K.* |
| J.3 | `/dev` | ResumeSkills | J.1, J.2, J.4, I.*, K.* |
| J.4 | `/dev` | ResumeAwards + Contact | J.1, J.2, J.3, I.*, K.* |
| K.1 | `/dev` | Author listing/profile | K.2, I.*, J.* |
| K.2 | `/dev` | ManageProfile | K.1, I.*, J.* |
| L | `/qa` | Integration & QA | (after all) |

---

## Orchestrator Commands

```
# WAVE 1 - Start all 4 immediately
/dev "Stream A: Create DB migration 005-ResumeAndImageManagement.sql per spec"
/dev "Stream B: Create model classes UserSkill, UserAward, UserStat + extend AppUser, BlogImage, UserEvent"
/dev "Stream C: Create wwwroot/uploads directory structure with 7 category folders"
/dev "Stream D: Create resume.css and author.css with responsive layouts"

# WAVE 2 - After Wave 1 completes (or after Stream B specifically)
/dev "Stream E: Create UserSkillsRepo, UserAwardsRepo, UserStatsRepo following GenericRepository pattern"
/dev "Stream F: Create IBlogImageService + BlogImageService with category-based validation"
/dev "Stream G: Extend user repository with GetByUsername, GetSiteOwner, GetAllAuthors methods"

# WAVE 3 - After Wave 2 completes
/dev "Stream H: Create ImagePicker.razor reusable component"
/dev "Stream I.1: Create ManageImages.razor admin gallery page"
/dev "Stream I.2: Create ManageExperience.razor admin CRUD page"
/dev "Stream I.3: Create ManageSkills.razor admin CRUD page"
/dev "Stream I.4: Create ManageAwards.razor admin CRUD page"
/dev "Stream J.1: Create ResumePage.razor + ResumeHero.razor for /resume route"
/dev "Stream J.2: Create ResumeExperience.razor timeline component"
/dev "Stream J.3: Create ResumeSkills.razor grid component"
/dev "Stream J.4: Create ResumeAwards.razor + ResumeContact.razor components"
/dev "Stream K.1: Create AuthorsPage.razor, AuthorProfilePage.razor, AuthorListItem.razor, AuthorHeader.razor"
/dev "Stream K.2: Create ManageProfile.razor self-service page"

# WAVE 4 - After all Wave 3 completes
/qa "Stream L: Execute integration testing and QA gate checklist"
```

---

## Dependencies Graph

```
Wave 1 (Parallel)
    A ─┐
    B ─┼─→ Wave 2 (Parallel after B)
    C ─┤       E ─┐
    D ─┘       F ─┼─→ Wave 3 (Parallel after E,F,G,H)
               G ─┤       H ─→ I.1, I.2, I.3, I.4
                  │            J.1, J.2, J.3, J.4
                  │            K.1, K.2
                  │
                  └─────────────────────────→ Wave 4
                                                  L (QA)
```

---

## Estimated Parallel Execution

**With 4 concurrent agents:**
- Wave 1: 4 tasks in parallel
- Wave 2: 3 tasks in parallel
- Wave 3: 10 tasks in parallel (may need more agents or batching)
- Wave 4: 1 task (QA gate)

**Total unique task count:** 18 development tasks + 1 QA task = **19 tasks**

---

## Reference Files

| Reference | Location |
|-----------|----------|
| Feature Ideation | `docs/feature-ideation-images-resume.md` |
| Tech Stack | `docs/architecture/tech-stack.md` |
| Coding Standards | `docs/architecture/coding-standards.md` |
| Source Tree | `docs/architecture/source-tree.md` |
| Existing DB Scripts | `source/BlogDb/PostgresScripts/` |
| Existing Models | `source/BlogModel/Models/` |
| Existing Repos | `source/BlogEngine/DbAccess/` |
| Existing Components | `source/BlogUI/Components/` |

---

**Document Status:** Ready for Orchestrator Execution
**Created By:** Sarah (PO Agent)
**Date:** 2026-01-02
