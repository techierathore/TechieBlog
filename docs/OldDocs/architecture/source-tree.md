# 7. Source Tree Integration

### 7.1 Existing Project Structure (Relevant Parts)

```plaintext
source/
├── BlogDb/
│   ├── MySqlScripts/           # Existing MySQL scripts
│   │   ├── 00-DBCreationScript.sql
│   │   ├── 01-BlogImageSps.sql
│   │   ├── 02-BlogUserSps.sql
│   │   ├── 03-PostSps.sql
│   │   └── ...
│   └── BlogDbSvc.cs            # DbUp runner
├── BlogModel/
│   ├── Models/                 # AppUser, BlogPost, BlogComment, etc.
│   ├── Interfaces/             # IGenericRepository, service interfaces
│   └── Common/                 # AppConstants, AppEncrypt
├── BlogEngine/
│   ├── Services/               # AuthSvc, BlogSvc, TagSvc
│   ├── DbAccess/               # Repository implementations
│   └── DaCore/                 # GenericRepository, DbConnectionFactory
├── BlogSvc/                    # TO BE REMOVED
├── BlogUI/
│   ├── Pages/
│   │   ├── AdminPages/         # Dashboard, Lists, Management
│   │   ├── BlogPages/          # BlogHome, BlogPage
│   │   └── UiElements/         # Blazorise samples (remove)
│   ├── Components/             # AlertIcon, ContentPanel
│   ├── Layouts/                # MainLayout, AdminLayout, AuthLayout
│   └── Common/                 # CustomAuthStateProvider
└── TechieBlog/
    ├── Program.cs
    ├── Services/               # AuthService, ManageService
    └── Pages/                  # Error page only
```

### 7.2 New File Organization

```plaintext
source/
├── BlogDb/
│   ├── MySqlScripts/           # Existing (deprecated, keep for reference)
│   ├── PostgresScripts/        # NEW: PostgreSQL migrations
│   │   ├── 001-CreateTables.sql
│   │   ├── 002-CreateStoredFunctions.sql
│   │   ├── 003-SeedData.sql
│   │   └── ...
│   └── BlogDbSvc.cs            # Updated for PostgreSQL
├── BlogModel/
│   ├── Models/
│   │   ├── PostRating.cs       # NEW
│   │   ├── UserFavorite.cs     # NEW
│   │   ├── Series.cs           # NEW
│   │   └── SiteSettings.cs     # NEW
│   └── Interfaces/
│       ├── IRatingSvc.cs       # NEW
│       ├── IFavoriteSvc.cs     # NEW
│       └── ISeriesSvc.cs       # NEW
├── BlogEngine/
│   ├── Services/
│   │   ├── RatingSvc.cs        # NEW
│   │   ├── FavoriteSvc.cs      # NEW
│   │   ├── SeriesSvc.cs        # NEW
│   │   ├── SubscriberSvc.cs    # NEW
│   │   └── AnalyticsSvc.cs     # NEW
│   └── DbAccess/
│       ├── PostRatingRepo.cs   # NEW
│       ├── UserFavoriteRepo.cs # NEW
│       ├── SeriesRepo.cs       # NEW
│       └── SettingsRepo.cs     # NEW
├── BlogUI/
│   ├── Pages/
│   │   ├── BlogPages/          # Existing + new public pages
│   │   │   ├── Home.razor      # From mockup 01
│   │   │   ├── PostView.razor  # From mockup 02
│   │   │   ├── CategoryArchive.razor  # From mockup 03
│   │   │   ├── TagArchive.razor       # From mockup 04
│   │   │   ├── SeriesView.razor       # From mockup 05
│   │   │   ├── SearchResults.razor    # From mockup 06
│   │   │   └── AuthorProfile.razor    # From mockup 07
│   │   ├── AuthPages/          # NEW: Authentication pages
│   │   │   ├── Login.razor     # From mockup 08
│   │   │   ├── Register.razor  # From mockup 09
│   │   │   ├── ForgotPassword.razor   # From mockup 10
│   │   │   └── ResetPassword.razor    # From mockup 11
│   │   ├── UserPages/          # NEW: User dashboard
│   │   │   ├── Profile.razor   # From mockup 12
│   │   │   ├── MyFavorites.razor      # From mockup 13
│   │   │   ├── MyComments.razor       # From mockup 14
│   │   │   ├── EditProfile.razor      # From mockup 15
│   │   │   └── ChangePassword.razor   # From mockup 16
│   │   ├── AuthorPages/        # NEW: Content management
│   │   │   ├── PostEditor.razor       # From mockup 17
│   │   │   ├── MyPosts.razor   # From mockup 18
│   │   │   ├── MediaLibrary.razor     # From mockup 19
│   │   │   └── DraftPreview.razor     # From mockup 20
│   │   └── AdminPages/         # Existing + enhanced
│   │       ├── Dashboard.razor        # From mockup 21
│   │       ├── AllPosts.razor  # From mockup 22
│   │       ├── Users.razor     # From mockup 23
│   │       ├── Comments.razor  # From mockup 24
│   │       ├── Categories.razor       # From mockup 25
│   │       ├── Tags.razor      # From mockup 26
│   │       ├── Subscribers.razor      # From mockup 27
│   │       └── Settings.razor  # From mockup 28
│   ├── Components/             # NEW: Fluent UI components
│   │   ├── RatingStars.razor
│   │   ├── FavoriteButton.razor
│   │   ├── MarkdownEditor.razor
│   │   ├── PostCard.razor
│   │   ├── CommentThread.razor
│   │   ├── TagCloud.razor
│   │   ├── SeriesNav.razor
│   │   └── ThemeToggle.razor
│   ├── Themes/                 # NEW: CSS theme files
│   │   ├── _variables.css      # Base CSS variables
│   │   ├── fluent-modern.css   # Theme 1
│   │   ├── developer-dark.css  # Theme 2
│   │   └── minimal-clean.css   # Theme 3
│   └── Layouts/
│       ├── MainLayout.razor    # Updated for Fluent UI
│       ├── AdminLayout.razor   # Updated for Fluent UI
│       └── AuthLayout.razor    # Updated for Fluent UI
└── TechieBlog/
    ├── Program.cs              # Updated DI configuration
    └── wwwroot/
        └── css/                # Theme CSS files deployed here
```

### 7.3 Integration Guidelines

| Guideline | Standard |
|-----------|----------|
| **File Naming** | PascalCase for .cs and .razor files, kebab-case for .css files |
| **Folder Organization** | Group by feature domain (BlogPages, AdminPages, etc.) |
| **Import/Export Patterns** | Use `_Imports.razor` for common namespaces, explicit using for services |
| **Namespace Convention** | `BlogUI.Pages.BlogPages`, `BlogEngine.Services`, etc. |

---
