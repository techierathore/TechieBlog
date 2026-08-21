# 6. Epic Details

### Epic 1: Foundation & UI Scaffolding

**Goal:** Establish the modernized technical foundation (.NET 10, PostgreSQL, Fluent UI) and convert all HTML mockups from `mockups/` folder into Blazor components. This Agile approach enables early stakeholder feedback on visual design while infrastructure is being validated.

**Mockup Reference:** The `mockups/` folder contains 28 HTML mockups with CSS that serve as the visual specification for all UI pages. Each UI scaffold story references specific mockup files to convert.

**Exit Criteria:** Solution builds and runs on .NET 10 with Fluent UI, PostgreSQL connected, all mockups converted to Blazor components with navigation working.

---

#### Story 1.1: Project Migration to .NET 10 LTS

**As a** developer,
**I want** the solution migrated to .NET 10 LTS,
**so that** I have long-term support and access to latest Blazor features.

**Acceptance Criteria:**
1. All project files updated to target .NET 10 (net10.0)
2. All NuGet packages updated to .NET 10 compatible versions
3. Solution builds without errors or warnings
4. Application starts and displays default page

---

#### Story 1.2: Remove BlogSvc API Project

**As a** developer,
**I want** the REST API layer removed from the solution,
**so that** the architecture is simplified for direct service calls.

**Acceptance Criteria:**
1. BlogSvc project removed from solution
2. All references to BlogSvc removed from other projects
3. Solution builds successfully without API project
4. DI configuration updated to wire UI directly to BlogEngine services

---

#### Story 1.3: PostgreSQL Database Migration

**As a** developer,
**I want** the database migrated from MySQL to PostgreSQL,
**so that** I have broader hosting compatibility and modern database features.

**Acceptance Criteria:**
1. PostgreSQL database scripts created in BlogDb/PostgresScripts
2. All 22 tables migrated with correct PostgreSQL data types
3. DbUp configured to run PostgreSQL migrations
4. Connection string configuration updated for PostgreSQL
5. Application connects to PostgreSQL and runs migrations successfully

---

#### Story 1.4: Fluent UI Blazor Integration

**As a** developer,
**I want** Microsoft Fluent UI Blazor integrated replacing Blazorise,
**so that** I have a modern, Microsoft-supported UI component library.

**Acceptance Criteria:**
1. Blazorise packages removed from solution
2. Microsoft.FluentUI.AspNetCore.Components packages installed
3. Fluent UI services registered in Program.cs
4. Base layout updated to use Fluent UI providers
5. Sample page renders with Fluent UI components successfully

---

#### Story 1.5: CSS Theme Infrastructure Setup

**As a** developer,
**I want** CSS variable-based theming infrastructure established,
**so that** themes can be customized without code changes.

**Mockup Reference:** Convert CSS from `mockups/styles.css`, `mockups/theme-developer.css`, `mockups/theme-minimal.css`

**Acceptance Criteria:**
1. CSS custom properties file created based on `mockups/styles.css` variables (colors, fonts, spacing)
2. "Fluent Modern" theme (default) derived from `mockups/styles.css` with light/dark variants
3. "Developer Dark" theme derived from `mockups/theme-developer.css`
4. "Minimal Clean" theme derived from `mockups/theme-minimal.css`
5. Theme variables applied to base layout and components
6. Theme switching mechanism implemented via configuration
7. Documentation comments explain each variable's purpose

---

#### Story 1.6: Main Layout & Navigation Shell

**As a** reader,
**I want** a consistent navigation shell across all pages,
**so that** I can easily navigate the blog.

**Mockup Reference:** Extract layout structure from `mockups/01-home.html` (header, footer, sidebar patterns are consistent across all mockups)

**Acceptance Criteria:**
1. Main layout created matching mockup `.layout`, `.header`, `.footer` structure
2. Header with site title/logo, navigation links, theme toggle, and user menu as shown in mockups
3. Responsive sidebar navigation matching mockup `.sidebar` pattern
4. Footer with copyright and links matching mockup `.footer` structure
5. Layout adapts correctly to mobile/tablet/desktop breakpoints per mockup responsive CSS
6. All navigation links point to corresponding Blazor pages
7. Theme toggle button functional (light/dark mode switching)

---

#### Story 1.7: Public Pages UI Scaffolds

**As a** reader,
**I want** all public-facing pages converted from HTML mockups to Blazor components,
**so that** I can see the visual structure of the blog.

**Mockup Reference:** Convert the following mockups to Blazor pages:
- `mockups/01-home.html` → Home page
- `mockups/02-blog-post.html` → Blog post page
- `mockups/03-category-archive.html` → Category archive page
- `mockups/04-tag-archive.html` → Tag archive page
- `mockups/05-series-view.html` → Series view page
- `mockups/06-search-results.html` → Search results page
- `mockups/07-author-profile.html` → Author profile page

**Acceptance Criteria:**
1. Home page converted from `01-home.html` with featured posts area, recent posts grid, sidebar
2. Blog post page converted from `02-blog-post.html` with article area, author info, comments section, ratings
3. Category archive page converted from `03-category-archive.html` with filtered post listing
4. Tag archive page converted from `04-tag-archive.html` with filtered post listing
5. Series view page converted from `05-series-view.html` with ordered post listing
6. Search results page converted from `06-search-results.html` with search box and results area
7. Author profile page converted from `07-author-profile.html` with bio and posts
8. All pages preserve mockup visual fidelity using Fluent UI components where appropriate
9. Placeholder content clearly indicates where dynamic content will appear
10. Theme variant mockups (`theme2-*.html`, `theme3-*.html`) verified compatible with theme switching

---

#### Story 1.8: Authentication Pages UI Scaffolds

**As a** user,
**I want** authentication pages converted from HTML mockups to Blazor components,
**so that** I can see the login and registration experience.

**Mockup Reference:** Convert the following mockups to Blazor pages:
- `mockups/08-login.html` → Login page
- `mockups/09-register.html` → Registration page
- `mockups/10-forgot-password.html` → Forgot password page
- `mockups/11-reset-password.html` → Reset password page

**Acceptance Criteria:**
1. Login page converted from `08-login.html` with email/password form, remember me, forgot password link
2. Registration page converted from `09-register.html` with email, password, confirm password, terms checkbox
3. Forgot password page converted from `10-forgot-password.html` with email input and instructions
4. Reset password page converted from `11-reset-password.html` with new password form
5. All forms use Fluent UI form components while preserving mockup layout
6. Validation styling placeholders in place per mockup design
7. Responsive design maintained from mockups for all authentication pages

---

#### Story 1.9: User Dashboard Pages UI Scaffolds

**As a** registered user,
**I want** my personal dashboard pages converted from HTML mockups to Blazor components,
**so that** I can see my profile and activity areas.

**Mockup Reference:** Convert the following mockups to Blazor pages:
- `mockups/12-user-profile.html` → User profile page
- `mockups/13-my-favorites.html` → My favorites page
- `mockups/14-my-comments.html` → My comments page
- `mockups/15-edit-profile.html` → Edit profile page
- `mockups/16-change-password.html` → Change password page

**Acceptance Criteria:**
1. User profile page converted from `12-user-profile.html` with avatar, bio, settings sections
2. My favorites page converted from `13-my-favorites.html` with bookmarked posts list
3. My comments page converted from `14-my-comments.html` with comment history
4. Profile edit page converted from `15-edit-profile.html` with form fields
5. Change password page converted from `16-change-password.html` with current/new password fields
6. Navigation between user pages working per mockup link structure

---

#### Story 1.10: Content Management Pages UI Scaffolds

**As an** author,
**I want** content management pages converted from HTML mockups to Blazor components,
**so that** I can see the post creation and management experience.

**Mockup Reference:** Convert the following mockups to Blazor pages:
- `mockups/17-post-editor.html` → Post editor page
- `mockups/18-my-posts.html` → My posts list page
- `mockups/19-media-library.html` → Media library page
- `mockups/20-draft-preview.html` → Draft preview page

**Acceptance Criteria:**
1. Post editor page converted from `17-post-editor.html` with title, Markdown editor area, preview pane, metadata sidebar
2. My posts list page converted from `18-my-posts.html` with post table, status filters, action buttons
3. Media library page converted from `19-media-library.html` with upload area, image grid, selection mechanism
4. Draft preview page converted from `20-draft-preview.html` showing full post preview
5. Category/tag selection components scaffolded per mockup design
6. Scheduling controls scaffolded per mockup design
7. Series selection component scaffolded per mockup design

---

#### Story 1.11: Admin Dashboard UI Scaffold

**As an** admin,
**I want** the admin dashboard converted from HTML mockup to Blazor component,
**so that** I can see the administrative overview layout.

**Mockup Reference:** Convert `mockups/21-admin-dashboard.html` → Admin dashboard page

**Acceptance Criteria:**
1. Admin dashboard page converted from `21-admin-dashboard.html` with statistics cards (posts, users, comments, views)
2. Quick actions section with common admin tasks per mockup design
3. Recent activity feed placeholder per mockup layout
4. Charts/graphs placeholders for analytics per mockup design
5. Admin-specific navigation sidebar matching mockup structure
6. Responsive layout maintained from mockup for admin pages

---

#### Story 1.12: Admin Management Pages UI Scaffolds

**As an** admin,
**I want** all admin management pages converted from HTML mockups to Blazor components,
**so that** I can see the full administrative interface.

**Mockup Reference:** Convert the following mockups to Blazor pages:
- `mockups/22-admin-posts.html` → All posts management page
- `mockups/23-admin-users.html` → User management page
- `mockups/24-admin-comments.html` → Comment moderation page
- `mockups/25-admin-categories.html` → Category management page
- `mockups/26-admin-tags.html` → Tag management page
- `mockups/27-admin-subscribers.html` → Subscriber management page
- `mockups/28-admin-settings.html` → Site settings page

**Acceptance Criteria:**
1. All posts management page converted from `22-admin-posts.html` with data grid, filters, bulk actions
2. User management page converted from `23-admin-users.html` with user list, role badges, action buttons
3. Comment moderation page converted from `24-admin-comments.html` with pending comments queue, approve/reject actions
4. Category management page converted from `25-admin-categories.html` with category list, add/edit forms
5. Tag management page converted from `26-admin-tags.html` with tag list, merge capability indicator
6. Subscriber management page converted from `27-admin-subscribers.html` with subscriber list, export button, newsletter composer
7. Site settings page converted from `28-admin-settings.html` with configuration sections, save button
8. All admin pages consistent in styling and navigation per mockup design patterns

---

#### Story 1.13: CI/CD Pipeline Setup

**As a** developer,
**I want** a CI/CD pipeline configured,
**so that** builds, tests, and deployments are automated and reliable.

**Acceptance Criteria:**
1. GitHub Actions workflow file created (.github/workflows/ci.yml)
2. Pipeline triggers on push to main/dev branches and pull requests
3. Build step compiles all projects successfully
4. Test step runs all unit tests with results reporting
5. Build artifacts produced for deployment
6. Pipeline status badge added to README
7. Branch protection rules documented (require passing CI)

---

#### Story 1.14: Testing Infrastructure Setup

**As a** developer,
**I want** testing frameworks configured,
**so that** I can write and run automated tests.

**Acceptance Criteria:**
1. xUnit test project created (TechieBlog.Tests)
2. bUnit package installed for Blazor component testing
3. Test project references BlogEngine and BlogUI projects
4. Sample unit test for a BlogEngine service method
5. Sample component test for a Fluent UI component
6. Test database configuration for integration tests (in-memory or test container)
7. Test runner configured in CI pipeline
8. Code coverage reporting enabled (optional threshold)

---

### Epic 2: Authentication & User Management

**Goal:** Implement complete authentication system with JWT tokens, 5-tier role-based access control, and user management functionality. Users can register, login, reset passwords, and access role-appropriate features.

**Exit Criteria:** Full authentication workflow functional, roles enforced, users can self-manage profiles.

---

#### Story 2.1: JWT Authentication Service Implementation

**As a** user,
**I want** to authenticate with email and password,
**so that** I can access protected features.

**Acceptance Criteria:**
1. AuthService implemented with login method accepting email/password
2. JWT token generated with appropriate claims (userId, email, roles)
3. Token expiration configured (e.g., 24 hours)
4. Refresh token mechanism implemented
5. Password verification using secure hashing (BCrypt or similar)
6. Failed login attempts tracked for rate limiting
7. Unit tests for authentication logic

---

#### Story 2.2: User Registration Flow

**As a** visitor,
**I want** to register for an account,
**so that** I can engage with blog content.

**Acceptance Criteria:**
1. Registration endpoint accepts email, password, display name
2. Email uniqueness validated
3. Password strength requirements enforced (min 8 chars, mixed case, number)
4. New users assigned Reader role by default
5. Registration form connected to backend service
6. Success message displayed with login redirect
7. Validation errors displayed inline on form

---

#### Story 2.3: Password Reset Flow

**As a** user,
**I want** to reset my forgotten password,
**so that** I can regain access to my account.

**Acceptance Criteria:**
1. Forgot password accepts email and generates reset token
2. Reset token stored with expiration (e.g., 1 hour)
3. Email sent with reset link (SMTP integration)
4. Reset password page validates token and accepts new password
5. Password updated and token invalidated on successful reset
6. User redirected to login with success message
7. Invalid/expired token shows appropriate error

---

#### Story 2.4: Role-Based Authorization Implementation

**As an** admin,
**I want** role-based access control enforced,
**so that** users only access appropriate features.

**Acceptance Criteria:**
1. Five roles defined: Admin, Editor, Author, Contributor, Reader
2. Role hierarchy implemented (Admin > Editor > Author > Contributor > Reader)
3. Authorization policies created for each permission level
4. [Authorize] attributes applied to protected pages/components
5. UI elements hidden based on user role
6. Unauthorized access returns 403 with appropriate message
7. Role permissions documented

---

#### Story 2.5: User Profile Management

**As a** registered user,
**I want** to manage my profile,
**so that** I can update my information and preferences.

**Acceptance Criteria:**
1. Profile page displays current user information
2. Edit profile allows updating display name, bio, avatar URL
3. Change password requires current password verification
4. Email change requires re-verification (or disabled for MVP)
5. Profile updates persisted to database
6. Success/error feedback displayed to user

---

#### Story 2.6: Admin User Management

**As an** admin,
**I want** to manage all users,
**so that** I can maintain the user base.

**Acceptance Criteria:**
1. User list displays all users with pagination
2. Search/filter users by name, email, role
3. View user details including registration date, last login
4. Change user role (with confirmation for privilege escalation)
5. Disable/enable user accounts
6. Delete user with confirmation (soft delete or hard delete with data handling)
7. Audit log entry created for admin actions

---

### Epic 3: Content Management Core

**Goal:** Deliver complete blog post lifecycle including Markdown editing, categories/tags, drafts, scheduling, and series. Authors can create, edit, preview, and publish content efficiently.

**Exit Criteria:** Full post CRUD working, Markdown editor functional, posts displayable on public pages.

---

#### Story 3.1: Blog Post CRUD Operations

**As an** author,
**I want** to create, read, update, and delete blog posts,
**so that** I can manage my content.

**Acceptance Criteria:**
1. Create post with title, content, excerpt, slug
2. Auto-generate slug from title (with manual override)
3. Read post by ID or slug
4. Update post with version tracking (updated date)
5. Delete post (soft delete with recovery option)
6. Post list shows author's posts with status indicators
7. Repository and service layer implemented with unit tests

---

#### Story 3.2: Markdown Editor Integration

**As an** author,
**I want** to write posts in Markdown with live preview,
**so that** I can focus on content with formatting flexibility.

**Acceptance Criteria:**
1. Markdown editor component integrated (select appropriate Blazor library)
2. Side-by-side or toggle preview mode
3. Common formatting toolbar (bold, italic, headers, links, images, code)
4. Syntax highlighting in editor
5. Preview renders Markdown to HTML accurately
6. Editor content syncs with post model
7. Auto-save draft while editing (every 30 seconds or on pause)

---

#### Story 3.3: Category Management

**As an** author,
**I want** to organize posts into categories,
**so that** readers can find related content.

**Acceptance Criteria:**
1. Category CRUD operations implemented
2. Categories have name, slug, description
3. Hierarchical categories supported (parent/child)
4. Post can belong to one primary category
5. Category selector in post editor
6. Category archive page displays posts in category
7. Admin can manage all categories

---

#### Story 3.4: Tag Management

**As an** author,
**I want** to add tags to posts,
**so that** content is discoverable by topic.

**Acceptance Criteria:**
1. Tag CRUD operations implemented
2. Tags have name and slug
3. Post can have multiple tags
4. Tag input with autocomplete in post editor
5. Create new tags inline while editing
6. Tag archive page displays posts with tag
7. Tag cloud or list component for sidebar

---

#### Story 3.5: Draft and Preview Workflow

**As an** author,
**I want** to save drafts and preview before publishing,
**so that** I can refine content before it goes live.

**Acceptance Criteria:**
1. Posts have status: Draft, Published, Archived
2. Save as draft doesn't make post public
3. Preview draft shows full post rendering (accessible only to author/editors)
4. Publish action changes status and sets publish date
5. Unpublish returns post to draft status
6. Draft indicator visible in post list
7. Preview link shareable for editorial review (with token)

---

#### Story 3.6: Post Scheduling

**As an** author,
**I want** to schedule posts for future publication,
**so that** I can plan my content calendar.

**Acceptance Criteria:**
1. Schedule date/time picker in post editor
2. Scheduled status distinct from Draft and Published
3. Background job publishes posts at scheduled time
4. Scheduled posts visible in author's post list with scheduled date
5. Edit scheduled post before publication
6. Cancel scheduling returns to draft
7. Time zone handling documented

---

#### Story 3.7: Series/Collections Feature

**As an** author,
**I want** to group posts into series,
**so that** readers can follow multi-part content.

**Acceptance Criteria:**
1. Series CRUD operations (name, description, slug)
2. Add posts to series with order number
3. Series selector in post editor
4. Post displays series navigation (previous/next in series)
5. Series landing page shows all posts in order
6. Series list page shows all available series
7. Author can reorder posts within series

---

#### Story 3.8: Public Blog Display

**As a** reader,
**I want** to view published blog posts,
**so that** I can read the content.

**Acceptance Criteria:**
1. Home page displays recent published posts
2. Featured posts section highlights selected content
3. Individual post page renders full Markdown content
4. Post displays author info, publish date, category, tags
5. Related posts shown based on category/tags
6. Reading time estimate displayed
7. Social sharing links (static URLs, no API integration)
8. SEO-friendly URLs using slugs

---

### Epic 4: Engagement & Social Features

**Goal:** Enable reader interaction through comments with moderation, star ratings, and favorites. Create community engagement while maintaining content quality through moderation tools.

**Exit Criteria:** Readers can comment, rate, and favorite posts; admins can moderate content.

---

#### Story 4.1: Comments System Implementation

**As a** logged-in reader,
**I want** to comment on posts,
**so that** I can engage with content and community.

**Acceptance Criteria:**
1. Comment form on post page (logged-in users only)
2. Comments stored with user reference, post reference, timestamp
3. Comments display below post in chronological order
4. Comment count shown on post cards/listings
5. User can edit their own comments (within time limit)
6. User can delete their own comments
7. Flat comment structure (no threading for MVP)

---

#### Story 4.2: Comment Moderation Workflow

**As an** editor/admin,
**I want** to moderate comments,
**so that** I can maintain discussion quality.

**Acceptance Criteria:**
1. Comments can require approval before display (configurable)
2. Moderation queue shows pending comments
3. Approve/reject actions with optional reason
4. Bulk moderation actions (approve all, reject all selected)
5. Reported comments flagged for review
6. Moderator can edit comments (with edit indicator)
7. Moderator can delete any comment
8. Notification to author when comment rejected (optional)

---

#### Story 4.3: Star Rating System

**As a** logged-in reader,
**I want** to rate posts with stars,
**so that** I can indicate content quality.

**Acceptance Criteria:**
1. 1-5 star rating widget on post page
2. Rating stored per user per post (one rating per user)
3. User can change their rating
4. Average rating displayed on post
5. Rating count displayed
6. Rating displayed on post cards in listings
7. Ratings queryable for "top rated" lists

---

#### Story 4.4: Favorites/Bookmarks Feature

**As a** logged-in reader,
**I want** to favorite posts,
**so that** I can easily find them later.

**Acceptance Criteria:**
1. Favorite toggle button on post page and cards
2. Favorites stored per user
3. My Favorites page lists all favorited posts
4. Favorite count optionally displayed on posts
5. Remove from favorites functionality
6. Favorites sortable by date added
7. Visual indicator on favorited posts

---

### Epic 5: Media, Subscribers & Analytics

**Goal:** Complete media upload and management, subscriber list functionality with newsletter sending, and analytics tracking. Enable content enrichment and audience growth features.

**Exit Criteria:** Images uploadable and manageable, subscribers collected and contactable, basic analytics visible.

---

#### Story 5.1: Image Upload & Storage

**As an** author,
**I want** to upload images for my posts,
**so that** I can include visual content.

**Acceptance Criteria:**
1. Image upload component in media library
2. Support common formats (JPG, PNG, GIF, WebP)
3. File size validation (configurable max size)
4. Images stored via configurable storage provider interface
5. Unique filename generation to prevent conflicts
6. Upload progress indicator
7. Error handling for failed uploads

---

#### Story 5.2: Media Library Management

**As an** author,
**I want** to manage my uploaded images,
**so that** I can reuse and organize media.

**Acceptance Criteria:**
1. Media library displays uploaded images in grid
2. Image details (filename, size, upload date, dimensions)
3. Search/filter images
4. Delete image with confirmation
5. Copy image URL for use in posts
6. Insert image directly into post editor
7. Pagination for large libraries

---

#### Story 5.3: Subscriber Sign-up Flow

**As a** visitor,
**I want** to subscribe to the blog,
**so that** I can receive updates about new content.

**Acceptance Criteria:**
1. Subscribe form component (email input)
2. Email validation
3. Duplicate subscription handling
4. Subscriber stored in database
5. Success confirmation message
6. Optional: double opt-in via email confirmation
7. Subscribe form placeable in sidebar, footer, or dedicated page

---

#### Story 5.4: Subscriber Management

**As an** admin,
**I want** to manage subscribers,
**so that** I can maintain my mailing list.

**Acceptance Criteria:**
1. Subscriber list with pagination
2. Search subscribers by email
3. View subscription date
4. Remove subscriber manually
5. Export subscribers to CSV
6. Import subscribers from CSV (optional)
7. Subscriber count displayed on dashboard

---

#### Story 5.5: Newsletter Sending

**As an** admin,
**I want** to send newsletters to subscribers,
**so that** I can share updates and drive engagement.

**Acceptance Criteria:**
1. Newsletter composer with subject and body
2. Rich text or Markdown editor for newsletter content
3. Preview newsletter before sending
4. Send to all subscribers or filtered segment
5. SMTP integration for email delivery
6. Sending progress/status indicator
7. Basic send history log
8. Unsubscribe link included in all emails

---

#### Story 5.6: Post View Analytics

**As an** admin/author,
**I want** to see post view statistics,
**so that** I can understand content performance.

**Acceptance Criteria:**
1. Track page view on each post visit
2. Distinguish total views vs unique views (by session/user)
3. View count displayed on post (optional, configurable)
4. Views visible in admin post list
5. View trends over time (optional: simple chart)
6. Don't count author's own views (optional)

---

#### Story 5.7: Analytics Dashboard

**As an** admin,
**I want** to see aggregate analytics,
**so that** I can understand overall blog performance.

**Acceptance Criteria:**
1. Dashboard shows total posts, users, comments, subscribers
2. Popular posts list (by views)
3. Recent comments activity
4. New subscribers count (recent period)
5. Engagement stats (avg comments, avg rating per post)
6. Simple charts for trends (posts over time, views over time)
7. Date range filter for analytics

---

### Epic 6: SEO, Theming & Production Polish

**Goal:** Finalize SEO features (RSS, sitemap), complete theme system with pre-built options, and ensure production readiness with documentation and polish.

**Exit Criteria:** RSS and sitemap functional, themes switchable, documentation complete, production-ready.

---

#### Story 6.1: RSS Feed Generation

**As a** reader,
**I want** an RSS feed,
**so that** I can subscribe using my feed reader.

**Acceptance Criteria:**
1. RSS 2.0 feed endpoint (/feed or /rss)
2. Feed includes recent published posts (configurable count)
3. Each item has title, link, description, pubDate, author
4. Feed auto-updates when posts published
5. RSS link in page header (auto-discovery)
6. RSS icon/link in UI
7. Valid RSS format (passes validation)

---

#### Story 6.2: Sitemap Generation

**As a** search engine,
**I want** a sitemap.xml,
**so that** I can index the blog content.

**Acceptance Criteria:**
1. Sitemap.xml endpoint at /sitemap.xml
2. Includes all published posts with lastmod date
3. Includes category and tag archive pages
4. Includes static pages (about, contact if exist)
5. Proper XML sitemap format
6. Auto-updates when content changes
7. Robots.txt references sitemap location

---

#### Story 6.3: Pre-built Theme Completion

**As a** developer,
**I want** 2-3 pre-built themes,
**so that** I can see theming possibilities and choose a starting point.

**Acceptance Criteria:**
1. Light theme fully styled and polished
2. Dark theme fully styled and polished
3. Accent theme (different color palette) completed
4. All themes use only CSS variables (no hardcoded values)
5. Theme switching works via configuration
6. Each theme visually distinct while maintaining usability
7. Theme preview screenshots in documentation

---

#### Story 6.4: Configuration & Settings Finalization

**As an** admin,
**I want** site settings configurable,
**so that** I can customize the blog for my needs.

**Acceptance Criteria:**
1. Site title and tagline configurable
2. Posts per page setting
3. Comment moderation toggle
4. Theme selection dropdown
5. SMTP settings configuration
6. Storage provider settings
7. Settings persisted to database or config file
8. Settings take effect without restart where possible

---

#### Story 6.5: Code Cleanup & Documentation

**As a** developer,
**I want** clean, documented code,
**so that** I can learn from and customize the project.

**Acceptance Criteria:**
1. Code follows consistent formatting and naming conventions
2. Complex logic has explanatory comments
3. All public APIs have XML documentation
4. README with project overview, features, architecture
5. Quick start guide (clone, configure, run)
6. Configuration documentation (all settings explained)
7. Customization guide (theming, extending)
8. No dead code, unused files, or commented-out blocks

---

#### Story 6.6: Sample Data & Development Setup

**As a** developer,
**I want** sample data and easy setup,
**so that** I can quickly see the blog in action.

**Acceptance Criteria:**
1. Database seed script with sample data
2. Sample posts demonstrating features (Markdown, images, series)
3. Sample users for each role
4. Sample categories and tags
5. Sample comments and ratings
6. One-command setup script (or clear manual steps)
7. Development vs production configuration documented

---

#### Story 6.7: Health Monitoring & Logging

**As a** developer/operator,
**I want** health monitoring and structured logging configured,
**so that** I can detect and diagnose production issues.

**Acceptance Criteria:**
1. Health check endpoint implemented (/health)
2. Health check verifies database connectivity
3. Health check verifies critical service availability
4. Serilog configured with structured logging
5. Log levels configurable per environment (Debug for dev, Warning+ for prod)
6. Logs include correlation IDs for request tracing
7. Console and file sinks configured (with rolling file option)
8. Exception logging captures full stack traces
9. Health endpoint documented for monitoring integration

---
