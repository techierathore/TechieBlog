# Feature Ideation: Image Management, Resume Page & Author Profiles

**Session Date:** 2026-01-02
**Mode:** YOLO - Focused Ideation
**Topics:** Image Management System + Resume/Portfolio Page + Multi-Author Profiles
**Reference:** https://www.nitinpandit.com/

---

## Executive Summary

This document defines the implementation strategy for three interconnected features:

1. **Image Management System** - Server-side upload, storage, and management of images
2. **Resume/Portfolio Page** - Site owner's public resume (exact replica of nitinpandit.com behavior)
3. **Multi-Author Profiles** - Author profile pages with full resume capabilities

**Key Insight:** These features are deeply interconnected. The Resume Page requires robust image management for profile photos, company logos, award badges, and CV files. The multi-author system extends resume capabilities to all authors while maintaining a hierarchical structure.

### User Hierarchy

```
┌─────────────────────────────────────────────────────────────────┐
│                        TECHIEBLOG                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   GUEST (Anonymous)          AUTHOR (AppUser)         ADMIN     │
│   ─────────────────          ───────────────          ─────     │
│   • Read articles            • Everything Guest has   • Everything│
│   • View author profiles     • Own profile management • Manage all│
│   • Browse /authors          • Write/edit own posts   • User mgmt │
│   • See author resumes       • Own resume/portfolio   • Site config│
│                              • Own image uploads      │          │
│                                                                 │
│   PUBLIC PAGES               ADMIN PAGES (filtered)   ADMIN PAGES│
│   /authors                   Same as Admin but        Full access │
│   /author/{username}         only own data visible    to all data │
│   /resume (site owner)                                           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Part 1: Resume Page Specification

### 1.1 Section Structure (Top to Bottom)

| # | Section | Description | Images Required |
|---|---------|-------------|-----------------|
| 1 | **Hero/Introduction** | Full-screen intro with name, title, tagline, social links, CTA buttons | Profile Photo, Background Image (optional) |
| 2 | **About Me** | Brief bio with experience statistics | None (text only) |
| 3 | **Professional Experience** | Reverse-chronological timeline | Company Logos |
| 4 | **Skills & Expertise** | Categorized skill groups | Skill Icons (optional) |
| 5 | **Awards & Recognition** | Achievement badges and descriptions | Award Badge Images |
| 6 | **Community Contributions** | Impact statistics and descriptions | None (text only) |
| 7 | **Contact** | Email, phone, location, social links | None |

**Removed:** Latest Articles section (per user request)

---

### 1.2 Data Model Design

#### 1.2.1 Existing Tables to Extend

**BlogUser Table - Add Columns:**

```sql
-- Multi-author and Resume fields to add to BlogUser
ALTER TABLE BlogUser ADD COLUMN Username VARCHAR(50) UNIQUE;  -- URL slug, chosen by user
ALTER TABLE BlogUser ADD COLUMN IsSiteOwner BOOLEAN DEFAULT FALSE; -- Identifies whose resume shows at /resume
ALTER TABLE BlogUser ADD COLUMN Title VARCHAR(150);           -- "Senior Solutions Consultant"
ALTER TABLE BlogUser ADD COLUMN Tagline VARCHAR(500);         -- "Passionate about technology..."
ALTER TABLE BlogUser ADD COLUMN InstagramUrl VARCHAR(255);    -- Missing social link
ALTER TABLE BlogUser ADD COLUMN PhoneNumber VARCHAR(50);      -- Contact phone
ALTER TABLE BlogUser ADD COLUMN Location VARCHAR(150);        -- "Noida NCR, India"
ALTER TABLE BlogUser ADD COLUMN CVFilePath VARCHAR(550);      -- Path to downloadable CV
ALTER TABLE BlogUser ADD COLUMN ResumeEnabled BOOLEAN DEFAULT FALSE; -- Toggle resume section visibility
```

**Username Notes:**
- Chosen by user during registration or profile setup
- Used in URL: `/author/{username}`
- Must be unique, URL-safe (lowercase, alphanumeric, hyphens)
- Examples: `john-doe`, `ravi-sharma`, `techie-jane`

**Reuse UserEvents Table for Experience:**

The existing `UserEvents` table can be repurposed with a Type discriminator:

| Field | Resume Usage |
|-------|--------------|
| LogoIconPath | Company logo image |
| EventTitle | Company name |
| SessionTitle | Job title/role |
| EventUrl | Company website |
| EventDate | End date of role |
| Type | "Experience" (discriminator) |

**Add columns to UserEvents:**

```sql
ALTER TABLE UserEvents ADD COLUMN StartDate TIMESTAMP;        -- Role start date
ALTER TABLE UserEvents ADD COLUMN Description TEXT;           -- Bullet points (markdown)
ALTER TABLE UserEvents ADD COLUMN DisplayOrder INT DEFAULT 0; -- Manual ordering
ALTER TABLE UserEvents ADD COLUMN IsCurrent BOOLEAN DEFAULT FALSE; -- Current role flag
```

#### 1.2.2 New Tables Required

**UserSkills Table:**

```sql
CREATE TABLE UserSkills (
    SkillId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    Category VARCHAR(100) NOT NULL,        -- "AI/Emerging Tech", "Cloud/SaaS", etc.
    SkillName VARCHAR(150) NOT NULL,       -- "Azure", "Docker", etc.
    IconPath VARCHAR(350),                 -- Optional skill icon
    DisplayOrder INT DEFAULT 0,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IdxUserSkillsUserId ON UserSkills(UserId);
CREATE INDEX IdxUserSkillsCategory ON UserSkills(Category);
```

**UserAwards Table:**

```sql
CREATE TABLE UserAwards (
    AwardId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    AwardTitle VARCHAR(255) NOT NULL,      -- "Microsoft MVP"
    AwardDescription TEXT,                  -- "10 consecutive years..."
    BadgeImagePath VARCHAR(550),           -- Award badge image
    AwardUrl VARCHAR(350),                 -- Link to award page
    AwardYear VARCHAR(50),                 -- "2015-2024" or "2023"
    DisplayOrder INT DEFAULT 0,
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IdxUserAwardsUserId ON UserAwards(UserId);
```

**UserStats Table:**

```sql
CREATE TABLE UserStats (
    StatId BIGSERIAL PRIMARY KEY,
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),
    StatLabel VARCHAR(100) NOT NULL,       -- "Years Experience"
    StatValue VARCHAR(50) NOT NULL,        -- "14+"
    StatCategory VARCHAR(50),              -- "about", "community"
    DisplayOrder INT DEFAULT 0
);
CREATE INDEX IdxUserStatsUserId ON UserStats(UserId);
```

---

### 1.3 Resume Page UI Components

#### Component Hierarchy:

```
Pages/
  ResumePage.razor                    -- Public resume view (/resume or /{username})

Pages/AdminPages/
  ManageResume.razor                  -- Main resume admin dashboard
  ManageExperience.razor              -- CRUD for experience entries
  ManageSkills.razor                  -- CRUD for skills
  ManageAwards.razor                  -- CRUD for awards
  ManageStats.razor                   -- CRUD for statistics

Components/Resume/
  ResumeHero.razor                    -- Hero section with intro
  ResumeAbout.razor                   -- About section
  ResumeExperience.razor              -- Experience timeline
  ResumeSkills.razor                  -- Skills grid
  ResumeAwards.razor                  -- Awards section
  ResumeCommunity.razor               -- Community stats
  ResumeContact.razor                 -- Contact section
```

#### Hero Section Behavior (nitinpandit.com replica):

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│                      [Profile Photo]                        │
│                         (circle)                            │
│                                                             │
│                    Hi, I'm {FirstName}                      │
│                     {Title}                                 │
│                                                             │
│                      {Tagline}                              │
│                                                             │
│            [Get In Touch]    [Download CV]                  │
│                                                             │
│              [LinkedIn] [GitHub] [X] [Instagram]            │
│                                                             │
│                         ↓ Scroll                            │
└─────────────────────────────────────────────────────────────┘
```

---

## Part 2: Image Management System

### 2.1 Image Categories & Storage

| Category | Storage Path | Max Size | Formats | Used By |
|----------|--------------|----------|---------|---------|
| Profile Photos | `/uploads/profiles/` | 2MB | jpg, png, webp | Resume Hero |
| Company Logos | `/uploads/logos/` | 500KB | jpg, png, svg, webp | Experience Timeline |
| Award Badges | `/uploads/awards/` | 500KB | jpg, png, svg, webp | Awards Section |
| Skill Icons | `/uploads/icons/` | 200KB | png, svg, webp | Skills Grid |
| Blog Images | `/uploads/blog/` | 5MB | jpg, png, gif, webp | Blog Posts |
| CV Files | `/uploads/cv/` | 10MB | pdf | Resume Download |
| General | `/uploads/general/` | 5MB | jpg, png, gif, webp | Miscellaneous |

### 2.2 BlogImage Table Enhancement

```sql
-- Add category and metadata to existing BlogImage table
ALTER TABLE BlogImage ADD COLUMN Category VARCHAR(50) DEFAULT 'general';
ALTER TABLE BlogImage ADD COLUMN AltText VARCHAR(255);
ALTER TABLE BlogImage ADD COLUMN MimeType VARCHAR(100);
ALTER TABLE BlogImage ADD COLUMN Width INT;
ALTER TABLE BlogImage ADD COLUMN Height INT;

CREATE INDEX IdxBlogImageCategory ON BlogImage(Category);
```

### 2.3 Image Upload Service

**IBlogImageService Interface:**

```csharp
public interface IBlogImageService
{
    Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId);
    Task<BlogImage> UploadImageFromUrlAsync(string url, string category, long userId);
    Task<bool> DeleteImageAsync(long imageId, long userId);
    Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long userId);
    Task<BlogImage> GetImageAsync(long imageId);
    string GetImageUrl(string imagePath);
    Task<bool> ValidateImageAsync(IBrowserFile file, string category);
}
```

**Implementation Notes:**

1. **File Naming:** `{category}_{userId}_{timestamp}_{randomSuffix}.{ext}`
2. **Storage Location:** `wwwroot/uploads/{category}/`
3. **Validation:** Size limits, format validation, image dimension checks
4. **Thumbnails:** Generate 150x150 thumbnails for gallery views
5. **Cleanup:** Orphan image detection and cleanup job

### 2.4 Admin Image Management UI

**ManageImages.razor - Gallery View:**

```
┌─────────────────────────────────────────────────────────────┐
│  Image Library                                    [Upload]  │
├─────────────────────────────────────────────────────────────┤
│  [All] [Profiles] [Logos] [Awards] [Icons] [Blog] [CV]     │
├─────────────────────────────────────────────────────────────┤
│  ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐  ┌─────┐     │
│  │ img │  │ img │  │ img │  │ img │  │ img │  │ img │     │
│  │     │  │     │  │     │  │     │  │     │  │     │     │
│  └─────┘  └─────┘  └─────┘  └─────┘  └─────┘  └─────┘     │
│  name.jpg name.png logo.svg ...                             │
│  [Copy URL] [Delete]                                        │
├─────────────────────────────────────────────────────────────┤
│  Showing 1-12 of 45 images          [< Prev] [Next >]      │
└─────────────────────────────────────────────────────────────┘
```

### 2.5 Image Picker Component

**Reusable ImagePicker.razor:**

```razor
@* Usage: <ImagePicker Category="profiles" @bind-SelectedImagePath="user.ProfileImagePath" /> *@

<div class="image-picker">
    <div class="current-image">
        @if (!string.IsNullOrEmpty(SelectedImagePath))
        {
            <img src="@SelectedImagePath" alt="Selected" />
            <button @onclick="ClearSelection">Remove</button>
        }
    </div>

    <button @onclick="OpenGallery">Choose from Library</button>
    <button @onclick="OpenUpload">Upload New</button>

    @* Gallery Modal *@
    @* Upload Modal *@
</div>
```

---

## Part 3: Implementation Roadmap

### Phase 1: Image Management Foundation

| Task | Priority | Complexity |
|------|----------|------------|
| 1.1 Extend BlogImage table with Category, AltText, MimeType, Width, Height | High | Low |
| 1.2 Implement BlogImageService with upload/delete/get | High | Medium |
| 1.3 Create ManageImages.razor admin page | High | Medium |
| 1.4 Create ImagePicker.razor reusable component | High | Medium |
| 1.5 Add image validation (size, format, dimensions) | Medium | Low |
| 1.6 Implement thumbnail generation | Low | Medium |

### Phase 2: Resume Data Model

| Task | Priority | Complexity |
|------|----------|------------|
| 2.1 Add resume columns to BlogUser table | High | Low |
| 2.2 Extend UserEvents table for Experience | High | Low |
| 2.3 Create UserSkills table | High | Low |
| 2.4 Create UserAwards table | High | Low |
| 2.5 Create UserStats table | Medium | Low |
| 2.6 Create repository interfaces and implementations | High | Medium |

### Phase 3: Resume Admin Pages

| Task | Priority | Complexity |
|------|----------|------------|
| 3.1 ManageResume.razor - Dashboard with toggles | High | Medium |
| 3.2 Resume profile section (name, title, tagline, photo, CV) | High | Medium |
| 3.3 ManageExperience.razor - CRUD for experience | High | Medium |
| 3.4 ManageSkills.razor - CRUD for skills by category | High | Medium |
| 3.5 ManageAwards.razor - CRUD for awards | High | Medium |
| 3.6 ManageStats.razor - CRUD for statistics | Medium | Low |

### Phase 4: Resume Public Page

| Task | Priority | Complexity |
|------|----------|------------|
| 4.1 ResumePage.razor - Main public view | High | High |
| 4.2 ResumeHero.razor - Hero section component | High | Medium |
| 4.3 ResumeAbout.razor - About section | Medium | Low |
| 4.4 ResumeExperience.razor - Timeline component | High | High |
| 4.5 ResumeSkills.razor - Skills grid | Medium | Medium |
| 4.6 ResumeAwards.razor - Awards display | Medium | Medium |
| 4.7 ResumeCommunity.razor - Stats display | Low | Low |
| 4.8 ResumeContact.razor - Contact section | Medium | Low |

### Phase 5: Styling & Polish

| Task | Priority | Complexity |
|------|----------|------------|
| 5.1 Resume page responsive CSS (match theme fonts) | High | Medium |
| 5.2 Smooth scroll behavior between sections | Medium | Low |
| 5.3 Anchor navigation highlighting | Low | Low |
| 5.4 Print-friendly resume view | Low | Medium |

---

## Part 4: Database Migration Script

```sql
-- Migration: Resume Page and Image Management Extensions
-- Version: 005-ResumeAndImageManagement.sql

-- ============================================================================
-- PART A: Extend BlogImage for categorization
-- ============================================================================
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Category VARCHAR(50) DEFAULT 'general';
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS AltText VARCHAR(255);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS MimeType VARCHAR(100);
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Width INT;
ALTER TABLE BlogImage ADD COLUMN IF NOT EXISTS Height INT;

CREATE INDEX IF NOT EXISTS IdxBlogImageCategory ON BlogImage(Category);

-- ============================================================================
-- PART B: Extend BlogUser for Multi-Author and Resume
-- ============================================================================
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Username VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS IsSiteOwner BOOLEAN DEFAULT FALSE;
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Title VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Tagline VARCHAR(500);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS InstagramUrl VARCHAR(255);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS PhoneNumber VARCHAR(50);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS Location VARCHAR(150);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS CVFilePath VARCHAR(550);
ALTER TABLE BlogUser ADD COLUMN IF NOT EXISTS ResumeEnabled BOOLEAN DEFAULT FALSE;

-- Unique constraint on Username (only for non-null values)
CREATE UNIQUE INDEX IF NOT EXISTS IdxBlogUserUsername ON BlogUser(Username) WHERE Username IS NOT NULL;

-- Ensure only one site owner (partial unique index)
CREATE UNIQUE INDEX IF NOT EXISTS IdxSingleSiteOwner
    ON BlogUser ((CASE WHEN IsSiteOwner = TRUE THEN 1 END))
    WHERE IsSiteOwner = TRUE;

-- ============================================================================
-- PART C: Extend UserEvents for Experience Timeline
-- ============================================================================
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS StartDate TIMESTAMP;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS Description TEXT;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS DisplayOrder INT DEFAULT 0;
ALTER TABLE UserEvents ADD COLUMN IF NOT EXISTS IsCurrent BOOLEAN DEFAULT FALSE;

-- ============================================================================
-- PART D: Create UserSkills Table
-- ============================================================================
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

-- ============================================================================
-- PART E: Create UserAwards Table
-- ============================================================================
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

-- ============================================================================
-- PART F: Create UserStats Table
-- ============================================================================
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

---

## Part 5: Key Technical Decisions

### 5.1 Image Storage Strategy

**Decision:** Local file system storage in `wwwroot/uploads/`

**Rationale:**
- Simple deployment (no external dependencies)
- Fast access (served directly by Kestrel/IIS)
- Easy backup (part of file system)
- Can migrate to cloud storage later if needed

**Current Implementation:**
```
wwwroot/uploads/
├── profiles/     # Profile photos
├── logos/        # Company logos
├── awards/       # Award badges
├── icons/        # Skill icons
├── blog/         # Blog post images
├── cv/           # CV/Resume PDFs
└── general/      # Miscellaneous
```

#### Future Scaling: Cloud Storage Alternatives

When the site grows and local storage becomes a bottleneck, consider these **cost-effective** alternatives (ordered by cost-effectiveness):

| Provider | Pricing | Egress Fees | Notes |
|----------|---------|-------------|-------|
| **Cloudflare R2** | $0.015/GB/month | **FREE** | S3-compatible, best value for read-heavy workloads |
| **Backblaze B2** | $0.006/GB/month | $0.01/GB | Very affordable, S3-compatible API |
| **Wasabi** | $0.0069/GB/month | **FREE** | No egress fees, flat rate |
| **DigitalOcean Spaces** | $5/month for 250GB | 1TB free, then $0.01/GB | Simple pricing, CDN included |
| **Cloudinary** | Free tier: 25GB | N/A | Image optimization & CDN included |
| **ImageKit** | Free tier: 20GB | N/A | Real-time transformations, CDN |
| **MinIO (Self-hosted)** | Free (hosting cost only) | N/A | S3-compatible, full control |
| Azure Blob Storage | $0.018/GB/month | $0.087/GB | More expensive, enterprise features |

**Recommended Migration Path:**
1. **Phase 1 (Now):** Local `wwwroot/uploads/` - simple, no cost
2. **Phase 2 (Growth):** Cloudflare R2 or Backblaze B2 - cheap, no/low egress
3. **Phase 3 (Scale):** CDN layer (Cloudflare) in front of storage

**Implementation Note for Future Migration:**
Design `IBlogImageService` with storage abstraction so switching providers requires only a new implementation:

```csharp
public interface IImageStorageProvider
{
    Task<string> UploadAsync(Stream file, string path, string contentType);
    Task<bool> DeleteAsync(string path);
    string GetPublicUrl(string path);
}

// Implementations:
// - LocalStorageProvider (current)
// - CloudflareR2Provider (future)
// - BackblazeB2Provider (future)
```

### 5.2 URL Strategy (Multi-Author)

**Decision:** Hierarchical URL structure

| URL | Purpose | Resolves To |
|-----|---------|-------------|
| `/resume` | Site owner's full resume | User where `IsSiteOwner = true` |
| `/authors` | List of all authors | Simple list view |
| `/author/{username}` | Author profile + resume | Specific author by username |

**Rationale:**
- `/resume` provides a prominent, memorable URL for site owner
- `/author/{username}` gives each author their own profile space
- `/authors` enables discovery of all contributors
- Clean separation between site-level and user-level content

### 5.3 Experience vs Events Table Reuse

**Decision:** Reuse `UserEvents` table with Type discriminator

**Rationale:**
- Existing table has similar structure
- Avoids table proliferation
- Type field already exists for discrimination
- Add new columns for Experience-specific needs

### 5.4 Theme Integration

**Decision:** Match existing blog theme fonts only, custom layout for resume

**Rationale:**
- Resume needs specific layout (full-width sections, scroll behavior)
- Fonts provide visual consistency
- Behavior replicates nitinpandit.com

---

## Part 6: Image Requirements Summary

### Images Needed for Resume Page

| Section | Image Type | Required | Storage Path |
|---------|-----------|----------|--------------|
| Hero | Profile Photo | Yes | `/uploads/profiles/` |
| Hero | Background Image | No | `/uploads/general/` |
| Experience | Company Logos | Yes (per entry) | `/uploads/logos/` |
| Skills | Skill Icons | No | `/uploads/icons/` |
| Awards | Badge Images | Yes (per entry) | `/uploads/awards/` |
| Download | CV PDF | Yes | `/uploads/cv/` |

### Image Management Features Required

1. **Upload** - Drag-drop or file picker upload
2. **Categorize** - Assign category during/after upload
3. **Browse** - Gallery view with category filters
4. **Select** - Pick image for use in forms (ImagePicker component)
5. **Delete** - Remove unused images
6. **Copy URL** - Quick copy image URL for markdown use

---

## Part 7: Multi-Author Architecture

### 7.1 Author Profile Page Structure

**URL:** `/author/{username}`

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│  ┌──────────┐                                                   │
│  │  Avatar  │   {FirstName} {LastName}                         │
│  │  (150px) │   @{username}                                    │
│  └──────────┘   {Title}                                        │
│                                                                 │
│  {Bio / ProfileDescription}                                     │
│                                                                 │
│  [LinkedIn] [GitHub] [Twitter] [Instagram]                     │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Articles by {FirstName}                          [View All →]  │
│  ────────────────────────                                       │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐               │
│  │  PostCard   │ │  PostCard   │ │  PostCard   │               │
│  │             │ │             │ │             │               │
│  └─────────────┘ └─────────────┘ └─────────────┘               │
│                                                                 │
├─────────────────────────────────────────────────────────────────┤
│  (If ResumeEnabled = true)                                      │
│                                                                 │
│  Experience                                                     │
│  ──────────                                                     │
│  [Logo] Current Role @ Company (2022 - Present)                │
│         • Bullet point 1                                        │
│         • Bullet point 2                                        │
│  [Logo] Previous Role @ Company (2019 - 2022)                  │
│         • Bullet point 1                                        │
│                                                                 │
│  Skills & Expertise                                             │
│  ─────────────────                                              │
│  Category 1: Skill, Skill, Skill                               │
│  Category 2: Skill, Skill, Skill                               │
│                                                                 │
│  Awards & Recognition                                           │
│  ───────────────────                                            │
│  [Badge] Award Title - Description                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 7.2 Authors Listing Page

**URL:** `/authors`

```
┌─────────────────────────────────────────────────────────────────┐
│  Our Authors                                                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  [Avatar] Ravi Sharma          Senior Developer    [View →]    │
│           @ravi-sharma         15 articles                      │
│                                                                 │
│  ─────────────────────────────────────────────────────────────  │
│                                                                 │
│  [Avatar] Jane Doe             Tech Writer         [View →]    │
│           @jane-doe            8 articles                       │
│                                                                 │
│  ─────────────────────────────────────────────────────────────  │
│                                                                 │
│  [Avatar] John Smith           DevOps Engineer     [View →]    │
│           @john-smith          12 articles                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 7.3 Blog Post → Author Link

On each blog post, the author name links directly to `/author/{username}`:

```
┌─────────────────────────────────────────────────────────────────┐
│  [Featured Image]                                               │
│                                                                 │
│  Post Title Here                                                │
│                                                                 │
│  [Avatar] By Ravi Sharma  •  Jan 2, 2026  •  5 min read       │
│           ^^^^^^^^^^^^                                          │
│           Clickable link to /author/ravi-sharma                │
│                                                                 │
│  Post content...                                                │
└─────────────────────────────────────────────────────────────────┘
```

### 7.4 Authorization Rules

| Action | Guest | Author | Admin |
|--------|-------|--------|-------|
| View `/authors` | Yes | Yes | Yes |
| View `/author/{username}` | Yes | Yes | Yes |
| View `/resume` | Yes | Yes | Yes |
| Edit own profile | No | Yes | Yes |
| Edit own resume data | No | Yes | Yes |
| Edit other's profile | No | No | Yes |
| Edit other's resume data | No | No | Yes |
| Access ManageImages (own) | No | Yes | Yes |
| Access ManageImages (all) | No | No | Yes |
| Set IsSiteOwner flag | No | No | Yes |

### 7.5 Admin Page Filtering Logic

Authors and Admins use the **same admin pages**, but with filtered data:

```csharp
// Example: ManageExperience.razor
@code {
    private long CurrentUserId => AuthService.GetCurrentUserId();
    private bool IsAdmin => AuthService.IsInRole("Admin");

    private async Task LoadExperience()
    {
        if (IsAdmin)
        {
            // Admin sees all users' experience (with user selector)
            experiences = await ExperienceRepo.GetAll();
        }
        else
        {
            // Author sees only their own
            experiences = await ExperienceRepo.GetByUserId(CurrentUserId);
        }
    }
}
```

**UI Implications:**
- Authors see simplified UI (no user selector dropdown)
- Admins see user selector to switch between users
- Same components, conditional rendering

### 7.6 Site Owner Identification

**Decision:** `IsSiteOwner` flag on BlogUser table

```sql
-- Only one user should have IsSiteOwner = true
-- Enforce at application level or with partial unique index:
CREATE UNIQUE INDEX IdxSingleSiteOwner
    ON BlogUser ((CASE WHEN IsSiteOwner THEN 1 END));
```

**Usage:**
```csharp
// ResumePage.razor route handler
var siteOwner = await UserRepo.GetSiteOwner(); // WHERE IsSiteOwner = true
if (siteOwner == null)
{
    // Redirect to setup or show "not configured"
}
```

### 7.7 Differences: /resume vs /author/{username}

| Aspect | `/resume` (Site Owner) | `/author/{username}` |
|--------|------------------------|----------------------|
| Layout | Full-page nitinpandit.com style | Profile + Resume sections |
| Navigation | Anchor scroll nav | Standard page layout |
| Hero | Full-screen with background | Compact header |
| Visibility | Always visible | Controlled by ResumeEnabled |
| Purpose | Site branding, featured profile | Author discovery |

### 7.8 Author Profile Admin UI

**ManageProfile.razor** - Self-service profile management

```
┌─────────────────────────────────────────────────────────────────┐
│  My Profile                                      [Save Changes] │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  BASIC INFORMATION                                              │
│  ─────────────────                                              │
│  Avatar:        [ImagePicker]                                   │
│  Display Name:  [___John Doe_______________]                   │
│  Username:      [___john-doe_______________] → /author/john-doe │
│  Title:         [___Software Engineer______]                   │
│  Bio:           [_________________________]                     │
│                 [_________________________]                     │
│                                                                 │
│  SOCIAL LINKS                                                   │
│  ────────────                                                   │
│  LinkedIn:      [___https://linkedin.com/in/johndoe___]        │
│  GitHub:        [___https://github.com/johndoe________]        │
│  Twitter:       [___https://twitter.com/johndoe_______]        │
│  Instagram:     [___________________________]                   │
│                                                                 │
│  RESUME SETTINGS                                                │
│  ───────────────                                                │
│  [x] Show resume section on my profile                         │
│  CV File:       [ImagePicker - CV category]   [Download]       │
│  Phone:         [___+91 9876543210___]                         │
│  Location:      [___Noida, India_____]                         │
│                                                                 │
│  RESUME DATA                                                    │
│  ───────────                                                    │
│  [Manage Experience →]  (4 entries)                            │
│  [Manage Skills →]      (12 skills in 3 categories)            │
│  [Manage Awards →]      (2 awards)                             │
│  [Manage Stats →]       (5 statistics)                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Appendix A: nitinpandit.com Feature Mapping

| nitinpandit.com Feature | TechieBlog Implementation |
|-------------------------|---------------------------|
| Navigation menu | Anchor links within resume page |
| Hero section | ResumeHero.razor component |
| Profile photo (circle) | ImagePicker + CSS styling |
| Name + Title | BlogUser.FirstName + Title |
| Tagline | BlogUser.Tagline |
| Get In Touch button | Scroll to Contact section |
| Download CV button | Link to BlogUser.CVFilePath |
| Social links | TwitterUrl, LinkedInUrl, GitHubUrl, InstagramUrl |
| About section | BlogUser.ProfileDescription |
| Experience stats | UserStats table (category: "about") |
| Experience timeline | UserEvents (Type: "Experience") |
| Company logos | UserEvents.LogoIconPath via ImagePicker |
| Role bullets | UserEvents.Description (markdown) |
| Skills matrix | UserSkills table with Category grouping |
| Awards section | UserAwards table |
| MVP badges | UserAwards.BadgeImagePath |
| Community stats | UserStats table (category: "community") |
| Contact section | ResumeContact.razor with BlogUser fields |

---

## Appendix B: File Structure

```
source/BlogUI/
├── Pages/
│   ├── ResumePage.razor              # Site owner's full resume (/resume)
│   ├── AuthorsPage.razor             # Authors listing (/authors)
│   ├── AuthorProfilePage.razor       # Individual author profile (/author/{username})
│   └── AdminPages/
│       ├── ManageProfile.razor       # Self-service profile management
│       ├── ManageResume.razor        # Resume dashboard (admin view)
│       ├── ManageExperience.razor    # Experience CRUD
│       ├── ManageSkills.razor        # Skills CRUD
│       ├── ManageAwards.razor        # Awards CRUD
│       ├── ManageStats.razor         # Stats CRUD
│       └── ManageImages.razor        # Image gallery
├── Components/
│   ├── Resume/
│   │   ├── ResumeHero.razor          # Full-page hero (for /resume)
│   │   ├── ResumeAbout.razor
│   │   ├── ResumeExperience.razor
│   │   ├── ResumeSkills.razor
│   │   ├── ResumeAwards.razor
│   │   ├── ResumeCommunity.razor
│   │   └── ResumeContact.razor
│   ├── Author/
│   │   ├── AuthorHeader.razor        # Compact header (for /author/{username})
│   │   ├── AuthorArticles.razor      # Author's articles list
│   │   └── AuthorListItem.razor      # Row in /authors list
│   └── ImagePicker.razor             # Reusable image selector
└── wwwroot/
    ├── uploads/
    │   ├── profiles/
    │   ├── logos/
    │   ├── awards/
    │   ├── icons/
    │   ├── blog/
    │   ├── cv/
    │   └── general/
    └── css/
        ├── resume.css                # Full resume page styles
        └── author.css                # Author profile/list styles

source/BlogEngine/
├── Services/
│   ├── IBlogImageService.cs
│   └── BlogImageService.cs
└── DbAccess/
    ├── UserSkillsRepo.cs
    ├── UserAwardsRepo.cs
    └── UserStatsRepo.cs

source/BlogModel/
└── Models/
    ├── UserSkill.cs
    ├── UserAward.cs
    └── UserStat.cs

source/BlogDb/
└── PostgresScripts/
    └── 005-ResumeAndImageManagement.sql
```

---

## Appendix C: Implementation Phases (Updated)

### Phase 1: Image Management Foundation
(No change from original)

### Phase 2: Resume Data Model
(Updated to include Username, IsSiteOwner)

### Phase 3: Resume Admin Pages
(No change)

### Phase 4: Resume Public Page (/resume)
(No change)

### Phase 5: Multi-Author Pages (NEW)

| Task | Priority | Complexity |
|------|----------|------------|
| 5.1 AuthorsPage.razor - List all authors | High | Low |
| 5.2 AuthorProfilePage.razor - Individual profile view | High | Medium |
| 5.3 AuthorHeader.razor - Compact profile header | High | Low |
| 5.4 AuthorArticles.razor - Author's posts list | High | Medium |
| 5.5 AuthorListItem.razor - Row component for list | Medium | Low |
| 5.6 ManageProfile.razor - Self-service profile editor | High | Medium |
| 5.7 Update PostCard to link author to profile | Medium | Low |
| 5.8 Add authorization filtering to admin pages | High | Medium |

### Phase 6: Styling & Polish
(Renamed from Phase 5, includes author styles)

---

## Session Summary

### Brainstorming Outcomes

**Topics Explored:**
1. Image Management System
2. Resume/Portfolio Page (nitinpandit.com replica)
3. Multi-Author Profiles

**Key Decisions Made:**

| Decision | Choice |
|----------|--------|
| Image Storage (Now) | Local `wwwroot/uploads/` |
| Image Storage (Future) | Cloudflare R2 or Backblaze B2 |
| Resume URL | `/resume` for site owner |
| Author URL | `/author/{username}` |
| Authors List | Simple list at `/authors` |
| Resume Features | Full access for all authors |
| Admin Pages | Same pages, filtered by user role |
| Site Owner ID | `IsSiteOwner` flag on BlogUser |
| Username | Chosen by user, unique, URL-safe |

**Database Changes:**
- 3 new tables: `UserSkills`, `UserAwards`, `UserStats`
- Extended: `BlogUser` (9 new columns), `UserEvents` (4 new columns), `BlogImage` (5 new columns)

**New Pages/Components:**
- 3 public pages: `/resume`, `/authors`, `/author/{username}`
- 7 admin pages for content management
- 10+ reusable components

**Implementation Phases:**
1. Image Management Foundation
2. Resume Data Model
3. Resume Admin Pages
4. Resume Public Page
5. Multi-Author Pages
6. Styling & Polish

---

**Document Status:** Complete
**Session Date:** 2026-01-02
**Next Action:** Begin Phase 1 implementation (Image Management Foundation)
