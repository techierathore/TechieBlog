# TechieBlog - Comprehensive Application Documentation

## Table of Contents
1. [Overview](#1-overview)
2. [Project Structure](#2-project-structure)
3. [Each Project in Detail](#3-each-project-in-detail)
4. [Completed Functionality](#4-completed-functionality)
5. [Gaps and Issues to Fix](#5-gaps-and-issues-to-fix)
6. [Recommendations](#6-recommendations)

---

## 1. Overview

### What is TechieBlog?

**TechieBlog** is an **open-source WordPress alternative** built using modern Microsoft technologies. It's a full-stack blogging platform designed to provide content creators with a professional blogging experience without the overhead of traditional CMS platforms.

### Technology Stack

| Component | Technology |
|-----------|------------|
| **Runtime** | .NET 9.0 |
| **Frontend** | Blazor Server (Server-side Rendering) |
| **UI Library** | Blazorise v1.7.0 with Bootstrap |
| **Database** | MySQL |
| **ORM** | Dapper (Micro-ORM) |
| **Authentication** | JWT (JSON Web Tokens) |
| **Logging** | Serilog |
| **API Docs** | Swagger/OpenAPI |
| **DB Migrations** | DbUp |

### Architecture Pattern

The application follows a **Layered Architecture**:

```
┌─────────────────────────────────────────────────────────┐
│                  TechieBlog (UI Layer)                  │
│              Blazor Server Application                  │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│                BlogUI (Component Library)               │
│         Pages, Components, Layouts, Styles              │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              BlogSvc (REST API - Optional)              │
│                 API Controllers                         │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              BlogEngine (Business Logic)                │
│            Services & Repositories                      │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              BlogModel (Data Contracts)                 │
│        Models, Interfaces, Constants                    │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│           BlogDb (Database Management)                  │
│          MySQL Scripts, Migrations                      │
└─────────────────────────────────────────────────────────┘
```

---

## 2. Project Structure

### Solution Organization

```
TechieBlog.sln
├── source/
│   ├── TechieBlog/          # Main Blazor Server Web App
│   ├── BlogUI/              # Razor Class Library (UI Components)
│   ├── BlogSvc/             # REST API Project
│   ├── BlogEngine/          # Business Logic & Data Access
│   ├── BlogModel/           # Domain Models & Interfaces
│   └── BlogDb/              # Database Scripts & Migrations
├── docs/                    # Documentation folder
├── README.md
├── LICENSE.txt
└── .gitignore
```

### Project Dependencies Graph

```
TechieBlog ─────► BlogUI ─────► BlogModel
    │                              ▲
    ├───────► BlogEngine ──────────┤
    │              │               │
    │              ▼               │
    │          BlogDb              │
    │                              │
    └───────► BlogSvc ─────────────┘
```

---

## 3. Each Project in Detail

### 3.1 TechieBlog (Main Web Application)

**Type:** Blazor Server Application (.NET 9.0)

**Purpose:** The main entry point - hosts the web application with Blazor Server for real-time interactivity.

**Key Files:**
- `Program.cs` - Application bootstrap, DI configuration, middleware pipeline
- `Services/AuthService.cs` - Frontend authentication service
- `Services/ManageService.cs` - Generic CRUD operations service (stub)

**Configuration:**
- Blazorise UI with Bootstrap providers
- Blazored.LocalStorage for token persistence
- Serilog logging with file output
- CustomAuthStateProvider for authentication state

**Connection String:** `host=localhost;port=49166;user id=root;database=TechieBlog;`

---

### 3.2 BlogUI (Reusable UI Library)

**Type:** Razor Class Library (.NET 9.0)

**Purpose:** Contains all UI components, pages, layouts, and styles shared across the application.

**Structure:**
```
BlogUI/
├── Pages/
│   ├── AdminPages/          # Admin dashboard pages
│   │   ├── LoginPage.razor
│   │   ├── AdminDashboard.razor
│   │   ├── BlogsList.razor
│   │   ├── ManagePost.razor
│   │   ├── TagsList.razor
│   │   ├── ManageTag.razor
│   │   ├── CommentsList.razor
│   │   ├── ManageComments.razor
│   │   └── 404Page.razor
│   ├── BlogPages/           # Public blog pages
│   │   ├── BlogHome.razor
│   │   └── BlogPage.razor
│   └── UiElements/          # UI component showcase
│       ├── AlertsPage.razor
│       ├── ButtonsPage.razor
│       ├── CardsPage.razor
│       ├── CarouselPage.razor
│       ├── GridPage.razor
│       ├── ModalsPage.razor
│       ├── TabsPage.razor
│       └── TypographyPage.razor
├── Components/              # Reusable components
│   ├── AlertIcon.razor
│   └── ConentPanel.razor    # (Note: typo - should be ContentPanel)
├── Layouts/                 # Page layouts
│   ├── MainLayout.razor
│   ├── AdminLayout.razor
│   └── AuthLayout.razor
├── Common/
│   └── CustomAuthStateProvider.cs
├── Styles/                  # SCSS stylesheets
└── wwwroot/                 # Static assets
```

**Key Dependencies:**
- Blazorise.Bootstrap v1.7.0
- Blazorise.DataGrid v1.7.0
- Blazorise.Charts v1.7.0
- Microsoft.AspNetCore.Components.Authorization v9.0.0

---

### 3.3 BlogSvc (REST API Service)

**Type:** ASP.NET Core REST API (.NET 9.0)

**Purpose:** Provides RESTful API endpoints for blog operations, useful for external integrations or mobile apps.

**Controllers:**

| Controller | Base Route | Endpoints |
|------------|------------|-----------|
| `AuthSvc` | `/authsvc` | AppSignUp, AppLogin, GetUserByToken |
| `BlogSvc` | `/blogsvc` | GetAllPosts, GetSinglePost, SavePost, UpdatePost |
| `TagSvc` | `/tagsvc` | GetAllTags, GetSingleTag, SaveTag, UpdateTag |

**Features:**
- Swagger/OpenAPI documentation (development mode)
- JWT authentication support
- Serilog logging

**Launch URLs:**
- HTTPS: `https://localhost:7241`
- HTTP: `http://localhost:5241`
- Swagger UI: `/swagger/ui`

---

### 3.4 BlogEngine (Business Logic Layer)

**Type:** Class Library (.NET 9.0)

**Purpose:** Contains all business logic, services, and data access implementations.

**Structure:**
```
BlogEngine/
├── Services/                # Business logic
│   ├── AuthSvc.cs          # Authentication service
│   ├── BlogSvc.cs          # Blog post operations
│   └── TagSvc.cs           # Tag management
├── DbAccess/               # Repository implementations
│   ├── BlogPostRepo.cs
│   ├── BlogCommentRepo.cs
│   ├── BlogImageRepo.cs
│   ├── BlogTagRepo.cs
│   ├── BlogUserRepo.cs
│   ├── UserLoginRepo.cs
│   ├── UserEventRepo.cs
│   ├── LoginLogRepo.cs
│   └── SvcTokenRepo.cs
├── DaCore/                 # Data access core
│   ├── GenericRepository.cs
│   └── DbConnectionFactory.cs
└── BlogSvcInitializer.cs   # DI registration
```

**Key Dependencies:**
- Dapper v2.1.35
- MySql.Data v9.1.0
- System.IdentityModel.Tokens.Jwt v8.2.1

---

### 3.5 BlogModel (Data Contracts)

**Type:** Class Library (.NET 9.0)

**Purpose:** Defines domain models, interfaces, and shared constants.

**Models:**
| Model | Purpose |
|-------|---------|
| `AppUser` | User account with profile info |
| `BlogPost` | Blog article content |
| `BlogComment` | Post comments with threading |
| `BlogTag` | Content tagging |
| `BlogImage` | Image metadata |
| `UserLogin` | Login session tracking |
| `UserRole` | Role definitions |
| `UserEvent` | User activities/events |
| `LoginLog` | Login audit trail |
| `SvcToken` | API tokens |
| `AdminCounts` | Dashboard statistics |

**Interfaces:**
- `IAuthService` - Authentication contract
- `IManageService<T>` - Generic CRUD operations
- `IGenericRepository<T>` - Data access pattern
- Entity-specific interfaces (IBlogPostRepo, IBlogUserRepo, etc.)

**Constants (AppConstants.cs):**
| Constant | Value | Purpose |
|----------|-------|---------|
| `JWTTokenGenKey` | "Xp@ns@JwTokenBieSR@viKum@r" | JWT signing key |
| `AppSalt` | "Xp@ns@r" | Password hashing salt |
| `BlogListPageSize` | 4 | Blog list pagination |
| `ListPageSize` | 5 | Default pagination |

---

### 3.6 BlogDb (Database Management)

**Type:** Class Library (.NET 9.0)

**Purpose:** Database versioning, migrations, and schema management using DbUp.

**Migration Scripts:**
```
MySqlScripts/
├── 00-DBCreationScript.sql      # Creates 19 tables
├── 01-BlogImageSps.sql          # Image stored procedures
├── 02-BlogUserSps.sql           # User stored procedures
├── 03-PostSps.sql               # Post stored procedures
├── 04-UserEventSps.sql          # Event stored procedures
├── 05-TagSps.sql                # Tag stored procedures
├── 06-BlogCommentSps.sql        # Comment stored procedures
├── 07-AdminSPs.sql              # Admin statistics
└── 16-MasterDataScript.sql      # Initial seed data
```

**Database Tables (19 total):**

| Category | Tables |
|----------|--------|
| **Core Blog** | BlogUser, Post, BlogComment, Category, PostCategory, Tag, BlogImage |
| **Analytics** | PostViews, UserActions, UserEvents, UserSettings, Widgets |
| **Subscribers** | Subscriber, LeadMagnet, LeadMagnetDownload |
| **Email Marketing** | Newsletter, SubscriberNewsletter, EmailSequence, EmailSequenceStep, SubscriberSequence |

---

## 4. Completed Functionality

### 4.1 Fully Working Features

#### Authentication System (85% Complete)
- [x] User login with JWT token generation
- [x] Password encryption with salt
- [x] Token storage in browser localStorage
- [x] Custom authentication state provider
- [x] Claims-based identity
- [x] Login tracking and audit

#### UI Component Library (100% Complete)
- [x] AlertsPage - Alert component showcase
- [x] ButtonsPage - Button variants
- [x] CardsPage - Card components
- [x] CarouselPage - Image carousel
- [x] GridPage - Bootstrap grid system
- [x] ModalsPage - Modal dialogs
- [x] TabsPage - Tab navigation
- [x] TypographyPage - Typography styles

#### Layouts (80% Complete)
- [x] AdminLayout - Full admin navigation sidebar
- [x] AuthLayout - Authentication page layout
- [x] 404Page - Error page
- [ ] MainLayout - Blog public layout (stub)

#### API Endpoints (90% Complete)
| Endpoint | Status |
|----------|--------|
| POST /authsvc/AppSignUp | Working |
| POST /authsvc/AppLogin | Working |
| POST /authsvc/GetUserByToken | Working |
| GET /blogsvc/GetAllPosts/{userId}/{isAdmin} | Working |
| GET /blogsvc/GetSinglePost/{id} | Working |
| POST /blogsvc/SavePost | Working |
| PUT /blogsvc/UpdatePost | Working |
| GET /tagsvc/GetAllTags | Working |
| GET /tagsvc/GetSingleTag/{id} | Working |
| POST /tagsvc/SaveTag | Working |
| PUT /tagsvc/UpdateTag | Working |

#### Database Layer (55% Complete)
- [x] Complete database schema (19 tables)
- [x] 21 stored procedures
- [x] DbUp migration system
- [x] GenericRepository base class
- [x] Database connection factory

#### Service Layer (50% Complete)
- [x] AuthSvc - Full authentication logic
- [x] BlogSvc - Basic post CRUD
- [x] TagSvc - Basic tag CRUD
- [x] JWT token generation/validation

---

### 4.2 Partially Working Features

#### Repository Implementations (54% Average Completion)

| Repository | GetAll | GetAllById | GetSingle | Insert | InsertToGetId | Update | GetPaged | Custom | Total |
|------------|--------|------------|-----------|--------|---------------|--------|----------|--------|-------|
| BlogUserRepo | Yes | No | Yes | Yes | Yes | Yes | No | 3 methods | 62% |
| BlogPostRepo | Yes | Yes | Yes | Yes | No | Yes | Yes | 1 method | 62% |
| BlogTagRepo | Yes | No | Yes | Yes | No | Yes | No | - | 38% |
| BlogCommentRepo | No | Yes | Yes | Yes | No | No | Yes | 4 methods | 50% |
| BlogImageRepo | No | No | No | Yes | No | Yes | Yes | - | 25% |
| UserLoginRepo | Yes | No | No | Yes | No | Yes | No | 1 method | 37% |
| LoginLogRepo | Yes | No | Yes | Yes | No | No | No | 2 methods | 37% |
| SvcTokenRepo | Yes | No | Yes | Yes | No | Yes | No | 1 method | 50% |
| UserEventRepo | No | Yes | Yes | Yes | No | Yes | No | - | 37% |

#### Admin Pages Status

| Page | Data Loading | Form Submission | Navigation | Overall |
|------|--------------|-----------------|------------|---------|
| LoginPage | Yes | Yes | Yes | 90% |
| AdminDashboard | No | N/A | Yes | 10% |
| BlogsList | No (commented out) | N/A | Broken | 15% |
| ManagePost | No | No (empty) | Yes | 10% |
| TagsList | No (commented out) | N/A | Broken | 15% |
| ManageTag | Partial | Partial | Wrong URLs | 40% |
| CommentsList | No | N/A | Broken | 5% |
| ManageComments | No | No | N/A | 0% |

---

## 5. Gaps and Issues to Fix

### 5.1 Critical Issues (Must Fix)

#### 1. **Blog List Pages Don't Load Data**
**Files:** `BlogsList.razor.cs`, `TagsList.razor.cs`, `CommentsList.razor.cs`

**Problem:** API calls are commented out, causing pages to show "Loading..." indefinitely.

```csharp
// Current (broken):
// ObjectList = await ManageSvc.GetAllSubsAsync(SvcUrl);

// Should be:
ObjectList = await ManageSvc.GetAllSubsAsync(SvcUrl);
```

**Impact:** Admin cannot view or manage any content.

---

#### 2. **ManagePost SaveData() is Empty**
**File:** `ManagePost.razor.cs:45`

**Problem:** The save button does nothing - method body is empty.

```csharp
private async Task SaveData()
{
    // Completely empty - no implementation
}
```

**Impact:** Cannot create or edit blog posts.

---

#### 3. **CommentsList Uses Wrong Model**
**File:** `CommentsList.razor.cs`

**Problem:** Uses `BlogPost` model instead of `BlogComment`.

```csharp
// Current (wrong):
public List<BlogPost> ObjectList { get; set; }

// Should be:
public List<BlogComment> ObjectList { get; set; }
```

**Impact:** Comments page is fundamentally broken.

---

#### 4. **Missing DELETE Endpoints**
**Files:** `BlogSvc.cs`, `TagSvc.cs` (in BlogSvc project)

**Problem:** No delete functionality for posts or tags.

**Impact:** Cannot delete content from admin interface.

---

#### 5. **DbConnectionFactory Null Handling**
**File:** `DbConnectionFactory.cs:28`

**Problem:** Calls `Open()` on potentially null connection.

```csharp
// Current (risky):
connection.Open();  // Throws if connection is null

// Should add null check:
if (connection != null)
    connection.Open();
else
    throw new ArgumentException("Unsupported database type");
```

---

#### 6. **InsertToGetId Not Implemented**
**Files:** Most repository files

**Problem:** 7 of 9 repositories throw `NotImplementedException` for `InsertToGetId()`.

**Impact:** Cannot get IDs of newly created records (needed for many workflows).

---

### 5.2 High Priority Issues

#### 7. **Public Blog Pages are Stubs**
**Files:** `BlogHome.razor`, `BlogPage.razor`

**Problem:** Only contain `<h3>` headers with no content.

**Impact:** The actual blog (public-facing) doesn't work at all.

---

#### 8. **MainLayout Not Implemented**
**File:** `MainLayout.razor`

**Problem:** Empty layout file - no public blog layout.

**Impact:** No proper layout for blog visitors.

---

#### 9. **ManageService Not Implemented**
**File:** `TechieBlog/Services/ManageService.cs`

**Problem:** All methods throw `NotImplementedException`.

**Impact:** Generic CRUD operations don't work.

---

#### 10. **Wrong Navigation Links**
**Files:** Multiple admin pages

**Problem:** Links point to wrong routes:
- "Add New" buttons link to `/ManageAccount` (doesn't exist)
- Edit links use wrong URL patterns

---

#### 11. **No Authorization on API Endpoints**
**Files:** `BlogSvc.cs`, `TagSvc.cs` controllers

**Problem:** All endpoints are public - no `[Authorize]` attributes.

**Impact:** Anyone can modify content without authentication.

---

### 5.3 Medium Priority Issues

#### 12. **AccessToken = RefreshToken**
**File:** `AuthSvc.cs` (BlogEngine)

**Problem:** Both tokens are set to the same JWT value.

```csharp
AccessToken = sJWToken,
RefreshToken = sJWToken,  // Should be different
```

---

#### 13. **Token Expiry Mismatch**
**File:** `AuthSvc.cs`

**Problem:** JWT expires in 15 days but UserLogin token expires in 2 days.

---

#### 14. **Hardcoded Database Type**
**File:** `GenericRepository.cs:25`

**Problem:** Database type hardcoded to MySQL, defeating DbConnectionFactory's purpose.

---

#### 15. **Missing Error Handling in Services**
**Files:** `BlogSvc.cs`, `TagSvc.cs` (BlogEngine)

**Problem:** No try-catch or logging - errors propagate uncaught.

---

#### 16. **Schema Mismatches in Seed Data**
**File:** `16-MasterDataScript.sql`

**Problem:** References columns/tables that don't match creation script.

---

### 5.4 Low Priority Issues

#### 17. **Typo in Component Name**
**File:** `ConentPanel.razor` should be `ContentPanel.razor`

---

#### 18. **Async/Await Issues in ManageTag**
**File:** `ManageTag.razor.cs`

**Problem:** Navigation called without proper await.

---

#### 19. **GetIntSingle Never Used**
**Files:** All repositories

**Problem:** Method exists in interface but never implemented or needed.

---

#### 20. **Missing IManageService Registration**
**File:** `BlogSvcInitializer.cs`

**Problem:** Only 2 repositories registered, rest are missing from DI.

---

## 6. Recommendations

### Immediate Actions (Priority 1)

1. **Enable data loading in list pages** - Uncomment API calls in BlogsList, TagsList, CommentsList

2. **Implement ManagePost.SaveData()** - Add create/update logic

3. **Fix CommentsList model** - Change from BlogPost to BlogComment

4. **Add DELETE endpoints** - Implement delete for posts and tags

5. **Implement InsertToGetId** - Essential for workflow completion

### Short-Term Actions (Priority 2)

6. **Build public blog pages** - Implement BlogHome and BlogPage with real content

7. **Complete MainLayout** - Design public blog layout

8. **Implement ManageService** - Enable generic CRUD operations

9. **Add API authorization** - Protect endpoints with [Authorize]

10. **Fix navigation links** - Correct all broken routes

### Medium-Term Actions (Priority 3)

11. **Separate access/refresh tokens** - Implement proper token refresh flow

12. **Implement remaining auth methods** - Registration, password reset, email verification

13. **Add comprehensive error handling** - Try-catch with logging in all services

14. **Complete all repository methods** - Eliminate NotImplementedException

15. **Add input validation** - Validate at service layer boundaries

### Long-Term Actions (Priority 4)

16. **Implement subscriber features** - Newsletter, lead magnets, email sequences

17. **Add analytics** - Post views, user actions tracking

18. **Implement widgets** - Dashboard customization

19. **Add comment moderation workflow** - Full approval system

20. **Deploy to cloud** - Docker containerization, CI/CD pipeline

---

## Appendix A: File Reference

| Component | File Path |
|-----------|-----------|
| Main App Entry | `source/TechieBlog/Program.cs` |
| Auth Service | `source/TechieBlog/Services/AuthService.cs` |
| Backend Auth | `source/BlogEngine/Services/AuthSvc.cs` |
| Login Page | `source/BlogUI/Pages/AdminPages/LoginPage.razor` |
| Auth Provider | `source/BlogUI/Common/CustomAuthStateProvider.cs` |
| Blog API | `source/BlogSvc/Controllers/BlogSvc.cs` |
| Post Repository | `source/BlogEngine/DbAccess/BlogPostRepo.cs` |
| DB Schema | `source/BlogDb/MySqlScripts/00-DBCreationScript.sql` |
| App Constants | `source/BlogModel/Common/AppConstants.cs` |
| DI Setup | `source/BlogEngine/BlogSvcInitializer.cs` |

---

## Appendix B: Quick Start Guide

### Prerequisites
- .NET 9.0 SDK
- MySQL Server (running on port 49166)
- Visual Studio 2022 or VS Code

### Setup Steps

1. **Clone Repository**
   ```bash
   git clone <repository-url>
   cd TechieBlog
   ```

2. **Setup Database**
   ```bash
   # Create MySQL database named 'TechieBlog'
   # Run migration scripts in order from source/BlogDb/MySqlScripts/
   ```

3. **Update Connection String**
   Edit `source/TechieBlog/appsettings.json`:
   ```json
   "AppDbConString": "host=localhost;port=49166;user id=root;password=yourpassword;database=TechieBlog;"
   ```

4. **Run Application**
   ```bash
   dotnet run --project source/TechieBlog
   ```

5. **Access Application**
   - Main App: `https://localhost:5001`
   - API Swagger: `https://localhost:7241/swagger`

### Default Login
- Email: Ravi@techieblog.com
- Password: admin_password (Note: Change immediately in production)

---

*Documentation generated on: December 2024*
*Application Version: Development*
