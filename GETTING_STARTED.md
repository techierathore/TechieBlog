# Getting Started with TechieBlog

This guide walks you through setting up TechieBlog from scratch. By the end, you'll have a fully functional blog running locally.

**Estimated Time:** 15-30 minutes

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Get the Code](#get-the-code)
3. [Database Setup](#database-setup)
4. [Configuration](#configuration)
5. [Build and Run](#build-and-run)
6. [First Login](#first-login)
7. [Next Steps](#next-steps)
8. [Troubleshooting](#troubleshooting)

---

## Prerequisites

Before starting, ensure you have:

### Required

| Tool | Version | Download |
|------|---------|----------|
| .NET SDK | 10.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| PostgreSQL | 15+ | [Download](https://www.postgresql.org/download/) |
| Git | Any recent | [Download](https://git-scm.com/downloads) |

### Recommended

| Tool | Purpose |
|------|---------|
| Visual Studio 2022 | Full IDE experience |
| VS Code | Lightweight editing |
| pgAdmin | PostgreSQL management |
| Azure Data Studio | Database management |

### Verify Installation

```bash
# Check .NET
dotnet --version
# Should output: 10.0.x

# Check PostgreSQL
psql --version
# Should output: psql (PostgreSQL) 15.x or higher

# Check Git
git --version
```

### NuGet feed access (required — the build will not restore without it)

TechieBlog's UI layer depends on two packages that live on a **private** GitHub
Packages feed, not on nuget.org:

- `TrBlazeUI.Components`
- `TrBlazeUI.Icons.Lucide`

`nuget.config` at the repo root declares the feed but **contains no credentials
by design** — anything committed there would be published to every clone and
fork. Store your own token in your **user-level** NuGet config instead, which
lives outside the repository:

```bash
dotnet nuget add source https://nuget.pkg.github.com/techierathore/index.json \
  --name TrBlazeUI \
  --username <your-github-username> \
  --password <a PAT with the read:packages scope> \
  --store-password-in-clear-text
```

That writes to `~/.nuget/NuGet/NuGet.Config` (Linux/macOS) or
`%APPDATA%\NuGet\NuGet.Config` (Windows) and merges with the repo config.

Without it, `dotnet restore` fails with:

```
error NU1301: Failed to retrieve information about 'TrBlazeUI.Icons.Lucide'
  from remote source 'https://nuget.pkg.github.com/techierathore/download/...'
  Response status code does not indicate success: 403 (Forbidden).
```

#### CI setup — adding the `TrBlazeUiPackagesToken` secret

GitHub Actions does **not** read `nuget.config`. The workflow builds its own
`nuget.ci.config` at run time from a repository secret. Without that secret,
restore fails with the same `NU1301 … 403` shown above.

**Option A — dedicated PAT (works in every case).**

1. Go to **https://github.com/settings/tokens** → *Generate new token* →
   **classic**. Use a *classic* token, not fine-grained: GitHub Packages'
   NuGet registry authenticates against `read:packages`, which fine-grained
   tokens do not reliably grant.
2. Tick exactly one scope: **`read:packages`**. Nothing else is needed — this
   token only downloads packages. Set whatever expiry your policy requires and
   note the renewal date, because CI breaks the day it lapses.
3. *Generate token* and copy the value. GitHub shows it once.
4. In **this repository** go to **Settings → Secrets and variables → Actions →
   New repository secret**.
5. Name it **exactly** `TrBlazeUiPackagesToken` (the workflow reads that literal
   string; a typo silently falls back to `GITHUB_TOKEN` and fails).
6. Paste the token as the value and save.

**Option B — grant the repository access to the packages (no secret needed).**

Use this when the packages and this repository share an owner. On each package
page under **https://github.com/users/techierathore/packages** — for both
`TrBlazeUI.Components` and `TrBlazeUI.Icons.Lucide` — open
*Package settings → Manage Actions access → Add repository*, pick this
repository, and give it **Read**. The built-in `GITHUB_TOKEN` then suffices.

**Verify it worked.** Re-run the failed workflow. The `Preflight — TrBlazeUI
feed authentication` step runs before restore and prints:

```
TrBlazeUI feed reachable and authenticated (HTTP 200).
```

If the credential is still wrong it fails there with the remedy, instead of
burying the cause in ~60 lines of NuGet retry noise further down.

---

## Get the Code

### Option A: Use as GitHub Template (Recommended)

1. Go to the [TechieBlog repository](https://github.com/user/techieblog)
2. Click the green **"Use this template"** button
3. Choose **"Create a new repository"**
4. Name your repository (e.g., `my-blog`)
5. Clone your new repository:

```bash
git clone https://github.com/YOUR_USERNAME/my-blog.git
cd my-blog
```

### Option B: Clone Directly

```bash
git clone https://github.com/user/techieblog.git MyBlog
cd MyBlog
```

---

## Database Setup

### Step 1: Create the Database

**Using Command Line:**
```bash
# Connect to PostgreSQL
psql -U postgres

# Create database
CREATE DATABASE techieblog;

# Exit
\q
```

**Using pgAdmin:**
1. Open pgAdmin
2. Right-click "Databases" → "Create" → "Database"
3. Name: `techieblog`
4. Click "Save"

### Step 2: Run Migrations

Migrations run automatically on first application start, OR you can run them manually:

```bash
cd source/BlogDb
dotnet run
```

This creates all required tables, stored procedures, and seed data.

---

## Configuration

### Step 1: Create Local Configuration

Copy the example configuration:

```bash
# From the root directory
copy source\TechieBlog\appsettings.Development.json source\TechieBlog\appsettings.Local.json
```

Or on Mac/Linux:
```bash
cp source/TechieBlog/appsettings.Development.json source/TechieBlog/appsettings.Local.json
```

### Step 2: Update Connection String

Edit `source/TechieBlog/appsettings.Local.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=techieblog;Username=postgres;Password=YOUR_PASSWORD_HERE"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

**Replace `YOUR_PASSWORD_HERE` with your PostgreSQL password.**

### Step 3: Configure Site Settings (Optional)

In the same file, customize your site:

```json
{
  "SiteSettings": {
    "SiteName": "My Awesome Blog",
    "SiteDescription": "A blog about awesome things",
    "SiteUrl": "https://localhost:5001",
    "PostsPerPage": 10,
    "AllowRegistration": true,
    "AllowComments": true,
    "RequireCommentApproval": false
  }
}
```

---

## Build and Run

### Using Command Line

```bash
# From the root directory
dotnet restore
dotnet build

# Run the application
dotnet run --project source/TechieBlog
```

### Using Visual Studio

1. Open `TechieBlog.slnx`
2. Set `TechieBlog` as the startup project
3. Press `F5` or click "Start Debugging"

### Using VS Code

1. Open the root folder in VS Code
2. Press `Ctrl+Shift+P` → "Tasks: Run Task" → "build"
3. Press `F5` to start debugging

### Verify It's Running

Open your browser to: **https://localhost:5001**

You should see the TechieBlog home page!

---

## First Login

### Default Admin Account

On first run, a default admin account is created:

| Field | Value |
|-------|-------|
| Email | `admin@techieblog.local` |
| Password | `Admin123!` |

**IMPORTANT: Change this password immediately after first login!**

### Create Your Own Account

1. Go to `/register`
2. Create a new account
3. Log in as admin
4. Go to Admin → Users
5. Promote your new account to Admin
6. Delete the default admin account

---

## Next Steps

Now that you have TechieBlog running:

### Customize Your Blog

1. **Change Site Settings**
   - Admin → Settings → Update site name, description

2. **Customize the Theme**
   - Edit `source/BlogUI/Styles/_variables.scss`
   - Change colors, fonts, spacing
   - Rebuild to see changes

3. **Add Your First Post**
   - Log in as Author or Admin
   - Go to Admin → Posts → New Post
   - Write in Markdown, add tags, publish!

### Learn the Codebase

| Area | Location | Purpose |
|------|----------|---------|
| UI Components | `source/BlogUI/Components/` | Reusable Blazor components |
| Pages | `source/BlogUI/Pages/` | Page-level components |
| Business Logic | `source/BlogEngine/Services/` | Core functionality |
| Data Access | `source/BlogEngine/Repositories/` | Database operations |
| Models | `source/BlogModel/` | Domain entities and DTOs |

### Deploy to Production

See [Deployment Guide](docs/deployment.md) for:
- Docker deployment
- Azure App Service
- Linux server deployment
- SSL/HTTPS setup

---

## Troubleshooting

### NuGet Restore Fails on TrBlazeUI (403 Forbidden)

**Error:** `error NU1301: Failed to retrieve information about 'TrBlazeUI.Icons.Lucide'
from remote source … Response status code does not indicate success: 403 (Forbidden).`
Usually preceded by *"Your request could not be authenticated by the GitHub Packages
service."*

This is the single most common first-build failure: `TrBlazeUI.Components` and
`TrBlazeUI.Icons.Lucide` live on a **private** feed, and `nuget.config` carries no
credentials by design.

**Solutions:**
1. **Locally** — register the feed with your own PAT (`read:packages`) in your
   *user-level* NuGet config: see [NuGet feed access](#nuget-feed-access-required--the-build-will-not-restore-without-it).
   Never put the token in the repo's `nuget.config`.
2. **In CI** — add the `TrBlazeUiPackagesToken` repository secret, or grant the repo
   Read access on each package: see
   [CI setup](#ci-setup--adding-the-trblazeuipackagestoken-secret).
3. Confirm the token really carries `read:packages` — a token without it returns
   **401/403 on the package**, while still returning 200 on the feed's service index,
   which makes it look valid at a glance. Check with:
   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -u "<user>:<PAT>" \
     https://nuget.pkg.github.com/techierathore/download/trblazeui.components/index.json
   # 200 = good, 401/403 = the token lacks read:packages or the package denies access
   ```

### Database Connection Failed

**Error:** `Npgsql.NpgsqlException: Failed to connect`

**Solutions:**
1. Verify PostgreSQL is running
2. Check connection string in `appsettings.Local.json`
3. Ensure database exists: `psql -U postgres -c "\l"`
4. Check firewall allows port 5432

### Port Already in Use

**Error:** `System.IO.IOException: Failed to bind to address`

**Solutions:**
1. Change port in `Properties/launchSettings.json`
2. Kill process using the port:
   ```bash
   # Windows
   netstat -ano | findstr :5001
   taskkill /PID <PID> /F

   # Mac/Linux
   lsof -i :5001
   kill -9 <PID>
   ```

### Build Errors

**Error:** `The SDK 'Microsoft.NET.Sdk.Web' was not found`

**Solutions:**
1. Verify .NET 10 SDK is installed: `dotnet --list-sdks`
2. Restart your terminal/IDE after installing SDK
3. Run `dotnet restore` from root directory

### CSS Not Loading

**Error:** Page looks unstyled

**Solutions:**
1. Clear browser cache (`Ctrl+Shift+R`)
2. Rebuild the solution: `dotnet build`
3. Check browser console for 404 errors on CSS files

### Migration Errors

**Error:** `DbUp migration failed`

**Solutions:**
1. Ensure database exists and is empty
2. Check PostgreSQL user has CREATE permissions
3. Review logs in `source/TechieBlog/logs/`

---

## Getting Help

- **Issues:** Report bugs on GitHub Issues
- **Documentation:** See the `/docs` folder
- **Architecture:** Read `docs/architecture.md` for technical details

---

## Quick Reference

| Task | Command |
|------|---------|
| Build | `dotnet build` |
| Run | `dotnet run --project source/TechieBlog` |
| Test | `dotnet test` |
| Clean | `dotnet clean` |
| Restore | `dotnet restore` |

| URL | Purpose |
|-----|---------|
| https://localhost:5001 | Home page |
| https://localhost:5001/admin | Admin dashboard |
| https://localhost:5001/login | Login page |
| https://localhost:5001/register | Registration |

---

**You're all set! Happy blogging!**
