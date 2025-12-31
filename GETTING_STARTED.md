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
