# TechieBlog - Blazor Blog Engine Template

> A production-ready, Blazor-native blogging engine built on .NET 10 LTS.
> Clone, customize, deploy in under a week.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS-purple)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-blueviolet)](https://blazor.net/)

---

## Why TechieBlog?

There's no simple, Blazor-native blogging solution that developers can clone, understand, and customize. TechieBlog fills that gap:

- **Educational Reference** - Clean architecture designed for readability over cleverness
- **Production Ready** - Not a demo; a real blogging engine you can deploy
- **Fully Customizable** - CSS variable theming, no code changes needed for visual customization
- **Modern Stack** - .NET 10 LTS, Blazor Server, PostgreSQL, Fluent UI

---

## Quick Start

### Option 1: Use as Template (Recommended)

1. Click the green **"Use this template"** button above
2. Create your new repository
3. Clone your new repo locally
4. Continue with setup below

### Option 2: Clone Directly

```bash
git clone https://github.com/user/repo.git MyBlog
cd MyBlog
```

### Rename to Your Project (Optional)

After cloning, you can rename the project from "TechieBlog" to your custom name:

**Windows (PowerShell):**
```powershell
.\scripts\Rename-Project.ps1 -NewName "MyBlog"

# Preview changes first (recommended):
.\scripts\Rename-Project.ps1 -NewName "MyBlog" -DryRun
```

**Linux/macOS (Bash):**
```bash
chmod +x scripts/rename-project.sh
./scripts/rename-project.sh MyBlog

# Preview changes first (recommended):
./scripts/rename-project.sh MyBlog --dry-run
```

This renames:
- Solution file: `TechieBlog.slnx` → `MyBlog.slnx`
- Host project: `source/TechieBlog/` → `source/MyBlog/`
- All related namespaces and references

> **Note:** The component libraries (BlogUI, BlogEngine, BlogModels, BlogDb) keep their generic names for clarity.

### Setup

1. **Prerequisites**
   - [.NET 10 SDK](https://dotnet.microsoft.com/download)
   - [PostgreSQL 15+](https://www.postgresql.org/download/)

2. **Configure Database**
   ```bash
   # Create PostgreSQL database
   createdb techieblog
   ```

3. **Update Connection String**

   Edit `source/TechieBlog/appsettings.Development.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Host=localhost;Database=techieblog;Username=postgres;Password=YOUR_PASSWORD"
     }
   }
   ```

4. **Run the Application**
   ```bash
   dotnet run --project source/TechieBlog
   ```

5. **Open in Browser**

   Navigate to `https://localhost:5001`

---

## Features

### Content Management
- Full CRUD operations for blog posts
- Markdown editor with live preview
- Categories and tags for organization
- Draft, publish, and scheduling workflows
- Series/collections for multi-part content

### User Engagement
- Comment system with moderation
- Star ratings (1-5) for posts
- Favorites/bookmarks for readers
- Multi-author support

### Resume & Author Profiles
- **Resume Page** - Professional resume display for site owner
  - Experience timeline with current/past positions
  - Skills organized by category (Languages, Frameworks, Tools)
  - Awards, certifications, and achievements
  - Downloadable CV/resume file support
  - Contact information and social links
- **Authors Page** (`/authors`) - Browse all content authors
  - Author cards with profile images and article counts
  - Links to individual author profile pages
- **Author Profile Pages** (`/author/{username}`) - Individual author profiles
  - Author bio and social media links
  - Complete list of articles by author
  - Professional profile presentation

### Image Management
- **Categorized Image Library** - Organize uploads by type
  - Profiles, Logos, Awards, Icons, Blog, CV, General categories
  - Category-specific file size limits and format validation
  - Admin interface for browsing and managing all images
- **Image Picker Component** - Reusable image selection for forms
  - Browse existing images or upload new
  - Category filtering and search
  - Copy image URL to clipboard
- **Validation & Security**
  - File type and size validation per category
  - Secure file storage with user ownership tracking

### User Management
- JWT authentication (email/password)
- 5-tier role system: Admin, Editor, Author, Contributor, Reader
- Self-service registration
- Password reset via email

### Subscribers & Newsletter
- Subscriber management
- Newsletter sending capability
- CSV export for subscriber lists

### Analytics & SEO
- Post view tracking (total and unique)
- Engagement statistics
- Auto-generated RSS feeds
- Auto-generated sitemap.xml

### Theming
- CSS variable-based theming (no code changes needed)
- Light/dark mode toggle
- 3 pre-built themes included
- Full visual customization via SCSS variables

---

## Architecture

TechieBlog uses a clean 5-project monolith architecture:

```
TechieBlog.slnx
├── BlogDb/           # Database scripts, migrations (DbUp)
├── BlogModel/        # Domain models, interfaces, DTOs
├── BlogEngine/       # Business logic, repositories, services
├── BlogUI/           # Razor Class Library (UI components)
└── TechieBlog/       # Blazor Server host application
```

**Design Principles:**
- No REST API layer - UI calls services directly for simplicity
- Clear separation of concerns
- Understandable in under 1 hour of code review
- Designed for readability, not cleverness

---

## Customization

### Theming (No Code Changes)

Modify CSS variables in `source/BlogUI/Styles/_variables.scss`:

```scss
// Colors
$primary-color: #0078d4;
$background-color: #ffffff;
$text-color: #323130;

// Typography
$font-family: 'Segoe UI', sans-serif;
$font-size-base: 16px;

// Spacing
$spacing-unit: 8px;
```

### Configuration

All settings in `appsettings.json`:

```json
{
  "SiteSettings": {
    "SiteName": "My Blog",
    "SiteDescription": "Welcome to my blog",
    "PostsPerPage": 10,
    "AllowComments": true,
    "RequireCommentApproval": false
  }
}
```

### Adding Features

The codebase is intentionally simple to extend:
1. Add models to `BlogModel/`
2. Add business logic to `BlogEngine/`
3. Add UI components to `BlogUI/`
4. Wire up in `TechieBlog/Program.cs`

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Framework | .NET 10 LTS |
| UI | Blazor Server + Fluent UI |
| Database | PostgreSQL |
| ORM | Dapper (micro-ORM) |
| Migrations | DbUp |
| Logging | Serilog |
| Auth | JWT (built-in) |

---

## Documentation

- [Getting Started Guide](GETTING_STARTED.md) - Detailed setup instructions
- [Architecture Overview](docs/architecture.md) - Technical deep-dive
- [Customization Guide](docs/customization.md) - Theming and extending
- [Deployment Guide](docs/deployment.md) - Production deployment

---

## Project Structure

```
TechieBlog/
├── docs/                    # Documentation
│   ├── architecture/        # Architecture docs
│   └── prd.md              # Product requirements (for reference)
├── mockups/                 # HTML/CSS mockups (design reference)
├── scripts/                 # Utility scripts
│   ├── Rename-Project.ps1  # Windows rename script
│   └── rename-project.sh   # Linux/macOS rename script
├── source/                  # Source code
│   ├── BlogDb/             # Database project
│   ├── BlogEngine/         # Business logic
│   ├── BlogModel/          # Domain models
│   ├── BlogUI/             # UI components
│   └── TechieBlog/         # Host application
├── .gitignore
├── GETTING_STARTED.md      # Setup guide
├── LICENSE.txt             # MIT License
├── README.md               # This file
└── TechieBlog.slnx         # Solution file
```

---

## Contributing

This is a template project designed for you to make your own. Feel free to:
- Fork and customize for your needs
- Submit issues for bugs you find
- Share your customizations with the community

---

## License

This project is licensed under the MIT License - see [LICENSE.txt](LICENSE.txt) for details.

You are free to use this for personal or commercial projects.

---

## Acknowledgments

- Built with [Microsoft Fluent UI Blazor](https://www.fluentui-blazor.net/)
- Inspired by the need for a simple, Blazor-native blogging solution
- Designed as both a practical tool and educational resource

---

**Happy Blogging!**
