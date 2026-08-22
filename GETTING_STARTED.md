# Getting Started with TechieBlog

This guide takes you from a clone to a running site. By the end you'll have the website up locally,
signed in as an administrator.

**Estimated time:** 15–30 minutes (most of it waiting for the first restore)

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [NuGet feed access](#nuget-feed-access-required--the-build-will-not-restore-without-it)
3. [Get the code](#get-the-code)
4. [Database setup](#database-setup)
5. [Configuration](#configuration)
6. [Build and run](#build-and-run)
7. [First login](#first-login)
8. [Next steps](#next-steps)
9. [Troubleshooting](#troubleshooting)
10. [Quick reference](#quick-reference)

---

## Prerequisites

### Required

| Tool | Version | Download |
|------|---------|----------|
| .NET SDK | 10.0+ | [Download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| PostgreSQL | 15+ | [Download](https://www.postgresql.org/download/) |
| Git | Any recent | [Download](https://git-scm.com/downloads) |

### Optional

| Tool | Purpose |
|------|---------|
| .NET **MAUI** workload | Only needed to build `source/BlogApp`, the desktop admin head — `dotnet workload install maui` |
| Visual Studio 2022 / VS Code | IDE |
| pgAdmin | PostgreSQL management |

### Verify

```bash
dotnet --version     # 10.0.x
psql --version       # 15.x or higher
git --version
```

> **Note on the desktop head.** `source/BlogApp` targets
> `net10.0-windows10.0.19041.0`. Without the MAUI workload it will not build — but the *website*
> builds and runs fine on its own. To skip it, build the host project rather than the whole solution:
> `dotnet build source/TechieBlog`.

---

## NuGet feed access (required — the build will not restore without it)

TechieBlog's UI layer depends on two packages that live on a **private** GitHub Packages feed, not on
nuget.org:

- `TrBlazeUI.Components`
- `TrBlazeUI.Icons.Lucide`

`nuget.config` at the repo root declares the feed but **contains no credentials by design** —
anything committed there would be published to every clone and fork. Store your own token in your
**user-level** NuGet config instead, which lives outside the repository:

```bash
dotnet nuget add source https://nuget.pkg.github.com/techierathore/index.json \
  --name TrBlazeUI \
  --username <your-github-username> \
  --password <a PAT with the read:packages scope> \
  --store-password-in-clear-text
```

That writes to `~/.nuget/NuGet/NuGet.Config` (Linux/macOS) or `%APPDATA%\NuGet\NuGet.Config`
(Windows) and merges with the repo config.

Without it, `dotnet restore` fails with:

```
error NU1301: Failed to retrieve information about 'TrBlazeUI.Icons.Lucide'
  from remote source 'https://nuget.pkg.github.com/techierathore/download/...'
  Response status code does not indicate success: 403 (Forbidden).
```

### CI setup — adding the `TrBlazeUiPackagesToken` secret

GitHub Actions does **not** read `nuget.config`. The workflow builds its own `nuget.ci.config` at run
time from a repository secret. Without that secret, restore fails with the same `NU1301 … 403`.

**Option A — dedicated PAT (works in every case).**

1. Go to **https://github.com/settings/tokens** → *Generate new token* → **classic**. Use a *classic*
   token, not fine-grained: GitHub Packages' NuGet registry authenticates against `read:packages`,
   which fine-grained tokens do not reliably grant.
2. Tick exactly one scope: **`read:packages`**. Note the expiry — CI breaks the day it lapses.
3. *Generate token* and copy the value. GitHub shows it once.
4. In **this repository**: **Settings → Secrets and variables → Actions → New repository secret**.
5. Name it **exactly** `TrBlazeUiPackagesToken` (the workflow reads that literal string; a typo
   silently falls back to `GITHUB_TOKEN` and fails).
6. Paste the token and save.

**Option B — grant the repository access to the packages (no secret needed).**

Use this when the packages and this repository share an owner. On each package page under
**https://github.com/users/techierathore/packages** — for both `TrBlazeUI.Components` and
`TrBlazeUI.Icons.Lucide` — open *Package settings → Manage Actions access → Add repository*, pick
this repository, and give it **Read**. The built-in `GITHUB_TOKEN` then suffices.

**Verify.** Re-run the failed workflow. The `Preflight — TrBlazeUI feed authentication` step runs
before restore and prints:

```
TrBlazeUI feed reachable and authenticated (HTTP 200).
```

If the credential is still wrong it fails there with the remedy, instead of burying the cause in
~60 lines of NuGet retry noise.

---

## Get the code

### Option A: use as a GitHub template (recommended)

1. Click **"Use this template" → "Create a new repository"**
2. Name it (e.g. `my-blog`) and clone:

```bash
git clone https://github.com/YOUR_USERNAME/my-blog.git
cd my-blog
```

### Option B: clone directly

```bash
git clone https://github.com/user/techieblog.git MyBlog
cd MyBlog
```

---

## Database setup

### Step 1: create the database

```bash
psql -U postgres -c "CREATE DATABASE techieblog;"
```

Or in pgAdmin: right-click **Databases → Create → Database**, name it `techieblog`.

Create the **empty database only**. Do not create any tables.

### Step 2: migrations

**You don't need to do anything here.** DbUp runs automatically at host startup: it applies every
script in `source/BlogDb/PostgresScripts/` in filename order and journals what it has already run, so
restarting is safe and never re-applies a script.

To run them manually instead — useful when the app's database user lacks DDL rights, so migrations
run as an admin and the app connects as someone less privileged:

```bash
dotnet run --project source/BlogDb -- --help
```

`BlogDb` is a console application; pass the target with `--postgres` (or `--connection`).

> **Which database gets migrated?** The **website** owns the schema. The desktop app
> (`source/BlogApp`) deliberately never runs DbUp — it expects an already-migrated database. Deploy
> or run the website first, then the desktop head.

---

## Configuration

### Step 1: the connection string

Edit **`source/TechieBlog/appsettings.Development.json`**.

> ⚠ **Two things that will cost you an hour if you get them wrong.**
>
> 1. The key is a **top-level `AppDbConString`** — *not* `ConnectionStrings:DefaultConnection`.
>    `Program.cs` reads `builder.Configuration["AppDbConString"]` and throws at startup if it is
>    missing.
> 2. There is **no `appsettings.Local.json`**. The host loads `appsettings.json` and
>    `appsettings.{Environment}.json` only — a file called `appsettings.Local.json` is read by
>    nothing, so settings placed there are silently ignored.

```json
{
  "AppDbConString": "Host=localhost;Port=5432;Database=techieblog;Username=postgres;Password=YOUR_PASSWORD"
}
```

### Step 2: the two required secrets

Sign-in and at-rest encryption need these. They have **no defaults**, and they belong in user secrets
rather than a config file so they never reach the repository:

```bash
cd source/TechieBlog
dotnet user-secrets set "JwtSigningKey"    "<a long random string, 32+ chars>"
dotnet user-secrets set "AppEncryptionKey" "<a different long random string>"
cd ../..
```

These are stored under the `techieblog-host-secrets` user-secrets id, outside the repo.

> **If you also run the desktop app**, it reads the same two values from its own connection-setup
> screen, and they must match the website's **byte for byte**. A mismatch fails *silently* — sessions
> and encrypted values simply will not round-trip between the two heads.

Generate a decent value:

```bash
# Linux / macOS
openssl rand -base64 48

# Windows PowerShell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 }))
```

### Step 3: site settings

Almost everything else — site title, tagline, posts per page, comment moderation, SEO and social
fields, SMTP, storage provider, theme — is **stored in the database** and edited at `/settings` once
you are signed in. There is no `SiteSettings` block to fill in by hand for local development.

The one exception is **`SiteSettings:BaseUrl`**, used to build absolute links in outgoing email. A
non-Development host **refuses to start** without it; `appsettings.Development.json` already carries a
loopback value for local work, and the host logs one loud warning about it.

---

## Build and run

```bash
dotnet restore
dotnet build
dotnet run --project source/TechieBlog
```

> Building the **whole solution** includes `source/BlogApp` and therefore needs the MAUI workload. If
> you only want the website, build the host project: `dotnet build source/TechieBlog`.

**Visual Studio:** open `TechieBlog.slnx`, set `TechieBlog` as the startup project, press `F5`.

**VS Code:** open the root folder, `Ctrl+Shift+P` → *Tasks: Run Task* → `build`, then `F5`.

### Verify it's running

Open **<http://localhost:5373>** (or `https://localhost:7373`). Ports are defined in
`source/TechieBlog/Properties/launchSettings.json`.

You should see the home page. **A fresh database has no posts**, so the article sections show their
empty states — that is expected, not a broken install.

---

## First login

### The seeded administrator

| Field | Value |
|-------|-------|
| Email | `Ravi@techieblog.com` |
| Password | `admin_password` |

Seeded by `source/BlogDb/PostgresScripts/003-SeedData.sql`, stored as a PBKDF2-HMAC-SHA256 hash — the
password is never in the database in plain text.

**Every seeded account is flagged `MustChangePassword`**, so your first sign-in lands on
`/change-password` and no other page will open until you set a new one. That is the requirement
working, not a bug.

Two more accounts (Editor and Author) are seeded for testing. All of them, with passwords, are listed
in **[docs/TechieBlog-UsageGuide.md](docs/TechieBlog-UsageGuide.md)** — the single registry every test
and smoke run draws from.

> **There is no public registration.** Self-service sign-up was retired by design: accounts are
> created by an administrator at **Admin → Users → Add New User**, and public engagement (comments,
> ratings) is anonymous and email-verified instead. `/register` does not exist.

### Make it your own site

1. Sign in as the seeded admin and change the password when prompted.
2. **Admin → Users → Add New User** — create your own Admin account.
3. Sign in as yourself, then deactivate or delete the seeded accounts you don't need.
4. **Admin → Settings** — set your site title, tagline and admin email.

> Deleting a user is a **soft delete**: the account disappears from every list and can no longer
> sign in, but the row survives so its posts and comments stay attributed. The site owner and the
> last active administrator cannot be deleted — the UI explains why on the disabled button.

---

## Next steps

### Customize

1. **Site settings** — Admin → Settings.
2. **Theme** — edit `source/BlogUI/wwwroot/css/theme.css`. Four theme sets ship
   (`trblaze-modern` default, `developer`, `minimal`, `fluent-modern`); light/dark is a `dark` class
   on `<html>`, and **dark is the shipped default**, changeable in *Settings → Theme*.
3. **First post** — Admin → Posts → New Post. Markdown, with live preview.

> ⚠ **Two theming traps.**
> `source/BlogUI/Styles/*.scss` is **dead legacy** — nothing references or compiles it, so editing
> `_variables.scss` changes nothing. And the build uses TrBlazeUI's **prebuilt** stylesheet with no
> Tailwind JIT pass, so arbitrary-value utilities (`text-[clamp(...)]`, `max-w-[1100px]`) are never
> generated and silently do nothing — use existing utilities, or write a real rule in `wwwroot/css/`.

### Learn the codebase

| Area | Location |
|------|----------|
| Pages | `source/BlogUI/Pages/` |
| Reusable components | `source/BlogUI/Components/` |
| Business logic | `source/BlogEngine/Services/` |
| Data access | `source/BlogEngine/DbAccess/` |
| Domain models | `source/BlogModel/` |
| Migrations | `source/BlogDb/PostgresScripts/` |
| Desktop head | `source/BlogApp/` |

### Test

```bash
dotnet test tests/TechieBlog.Tests/TechieBlog.Tests.csproj
```

Coding conventions are enforced at **build time** by `tests/unit/Ops/` — and each scanner carries a
self-test proving its pattern can actually match, so a dead check fails the build instead of reading
as a pass.

### Deploy

See [docs/deployment.md](docs/deployment.md) and
[docs/Prod-Deploy-Checklist.md](docs/Prod-Deploy-Checklist.md).

---

## Troubleshooting

### NuGet restore fails on TrBlazeUI (401 / 403)

**Error:** `error NU1301: Failed to retrieve information about 'TrBlazeUI.Icons.Lucide' … 403
(Forbidden)`, usually preceded by *"Your request could not be authenticated by the GitHub Packages
service."*

The single most common first-build failure. Both packages live on a **private** feed and
`nuget.config` carries no credentials by design.

1. **Locally** — register the feed with your own PAT: see
   [NuGet feed access](#nuget-feed-access-required--the-build-will-not-restore-without-it). Never put
   the token in the repo's `nuget.config`.
2. **In CI** — add the `TrBlazeUiPackagesToken` secret, or grant the repo Read on each package.
3. Confirm the token really carries `read:packages`. A token without it returns **401/403 on the
   package** while still returning 200 on the feed's service index — so it looks valid at a glance:
   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' -u "<user>:<PAT>" \
     https://nuget.pkg.github.com/techierathore/download/trblazeui.components/index.json
   # 200 = good · 401/403 = missing read:packages, or the package denies access
   ```

### "AppDbConString is missing" at startup

The connection string key is **top-level `AppDbConString`**, not `ConnectionStrings:DefaultConnection`.
And check you edited `appsettings.Development.json` — **`appsettings.Local.json` is loaded by
nothing**.

### Sign-in fails, or sessions don't persist

`JwtSigningKey` and `AppEncryptionKey` are probably unset. See
[Step 2: the two required secrets](#step-2-the-two-required-secrets). Confirm with:

```bash
dotnet user-secrets list --project source/TechieBlog
```

If you also run the desktop app and it can read data but not stay signed in, the two heads' keys
don't match — a mismatch fails silently.

### Sign-in bounces to `/change-password`

Working as designed: every seeded account is flagged `MustChangePassword` and is held there until the
password is replaced.

### Database connection failed

`Npgsql.NpgsqlException: Failed to connect`

1. Is PostgreSQL running?
2. Is the host/port/password in `AppDbConString` right?
3. Does the database exist? `psql -U postgres -c "\l"`
4. Firewall allowing 5432?

### Port already in use

`System.IO.IOException: Failed to bind to address`

```bash
# Windows
netstat -ano | findstr :5373
taskkill /PID <PID> /F

# Mac/Linux
lsof -i :5373
kill -9 <PID>
```

Or change the ports in `source/TechieBlog/Properties/launchSettings.json`.

### Build fails on `source/BlogApp`

That project targets Windows and needs the MAUI workload: `dotnet workload install maui`. To build
only the website, target the host project: `dotnet build source/TechieBlog`.

### The page looks unstyled

1. Hard-refresh (`Ctrl+Shift+R`).
2. `dotnet build` again — static web assets are republished on build.
3. Check the browser console for 404s on `_content/BlogUI/...` or
   `_content/TrBlazeUI.Components/trblazeui.css`.

### Migration errors

`DbUp migration failed`

1. Does the database exist?
2. Does the PostgreSQL user hold CREATE/DDL rights? If not, run migrations separately —
   see [Database setup](#step-2-migrations).
3. `/healthz` reports whether DbUp's journal matches the scripts on disk, so a partially-applied
   schema is visible rather than silent.

---

## Getting help

- **Issues:** GitHub Issues
- **Architecture:** [docs/TechieBlog-Architecture.md](docs/TechieBlog-Architecture.md)
- **Test accounts and test plan:** [docs/TechieBlog-UsageGuide.md](docs/TechieBlog-UsageGuide.md)
- **Screen-by-screen developer guide:** [docs/devguides/](docs/devguides/)
- **Current build state:** [PROJECT-STATUS.md](PROJECT-STATUS.md)

---

## Quick reference

| Task | Command |
|------|---------|
| Restore | `dotnet restore` |
| Build (all, needs MAUI) | `dotnet build` |
| Build (website only) | `dotnet build source/TechieBlog` |
| Run | `dotnet run --project source/TechieBlog` |
| Test | `dotnet test tests/TechieBlog.Tests/TechieBlog.Tests.csproj` |
| Migrations by hand | `dotnet run --project source/BlogDb -- --help` |
| List secrets | `dotnet user-secrets list --project source/TechieBlog` |

| URL | Purpose |
|-----|---------|
| <http://localhost:5373> | Home page |
| <http://localhost:5373/login> | Sign in |
| <http://localhost:5373/admin> | Admin dashboard |
| <http://localhost:5373/settings> | Site settings |
| <http://localhost:5373/healthz> | Health, including schema state |

---

**You're all set. Happy blogging!**
