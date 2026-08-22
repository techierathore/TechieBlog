# TechieBlog — Blazor Blog Engine Template

> A production-ready, Blazor-native blogging engine and personal-site platform built on .NET 10 LTS.
> Clone, customize, deploy.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![.NET](https://img.shields.io/badge/.NET-10.0%20LTS-purple)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-blueviolet)](https://blazor.net/)
[![UI](https://img.shields.io/badge/UI-TrBlazeUI-0ea5e9)](https://github.com/techierathore/TrBlazeUI)

---

## Why TechieBlog?

There's no simple, Blazor-native blogging solution that developers can clone, understand and
customize. TechieBlog fills that gap:

- **Educational reference** — clean architecture written for readability over cleverness
- **Production ready** — not a demo; a real engine that runs a live site
- **Two heads, one codebase** — the same Razor Class Library serves a Blazor Server website *and* a
  MAUI Blazor Hybrid desktop admin app
- **Fully themeable** — CSS custom properties, no code changes needed to restyle
- **Modern stack** — .NET 10 LTS, Blazor Server, PostgreSQL, Dapper, TrBlazeUI

---

## Quick Start

### 1. Get the code

**Use as a template (recommended):** click **"Use this template"**, create your repository, clone it.

**Or clone directly:**

```bash
git clone https://github.com/user/repo.git MyBlog
cd MyBlog
```

### 2. Rename to your project (optional)

```powershell
# Windows
.\scripts\Rename-Project.ps1 -NewName "MyBlog" -DryRun   # preview first
.\scripts\Rename-Project.ps1 -NewName "MyBlog"
```

```bash
# Linux / macOS
chmod +x scripts/rename-project.sh
./scripts/rename-project.sh MyBlog --dry-run             # preview first
./scripts/rename-project.sh MyBlog
```

Renames the solution, the host project folder and the related namespaces. The libraries
(`BlogUI`, `BlogEngine`, `BlogModels`, `BlogDb`) keep their generic names on purpose.

### 3. Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL 15+](https://www.postgresql.org/download/)
- A GitHub PAT with `read:packages` — see the next step
- *(Only to build the desktop head)* the **MAUI** workload: `dotnet workload install maui`

### 4. Authenticate to the TrBlazeUI feed — restore fails without it

The UI depends on `TrBlazeUI.Components` and `TrBlazeUI.Icons.Lucide`, which live on a **private**
GitHub Packages feed. `nuget.config` declares the feed but deliberately carries **no credentials**;
store yours in your user-level NuGet config, outside the repo:

```bash
dotnet nuget add source https://nuget.pkg.github.com/techierathore/index.json \
  --name TrBlazeUI \
  --username <your-github-username> \
  --password <PAT with read:packages> \
  --store-password-in-clear-text
```

Skipping this produces `error NU1301 … 401/403` on restore. For CI, the workflow reads the
`TrBlazeUiPackagesToken` secret — see
[GETTING_STARTED.md](GETTING_STARTED.md#nuget-feed-access-required--the-build-will-not-restore-without-it).

### 5. Create the database

```bash
createdb techieblog
```

You do **not** need to run any SQL by hand. DbUp applies every script in
`source/BlogDb/PostgresScripts/` automatically at host startup, in filename order, and journals what
it has already run.

### 6. Configure the connection string

Edit `source/TechieBlog/appsettings.Development.json`. The key is a **top-level `AppDbConString`** —
*not* `ConnectionStrings:DefaultConnection`. `Program.cs` reads
`builder.Configuration["AppDbConString"]` and throws at startup if it is missing.

```json
{
  "AppDbConString": "Host=localhost;Port=5432;Database=techieblog;Username=postgres;Password=YOUR_PASSWORD"
}
```

### 7. Set the two required secrets

Sign-in and at-rest encryption need these. They are **not** in `appsettings.json` and there are no
defaults — set them once per machine:

```bash
cd source/TechieBlog
dotnet user-secrets set "JwtSigningKey"    "<a long random string, 32+ chars>"
dotnet user-secrets set "AppEncryptionKey" "<a different long random string>"
```

> **If you also run the desktop app:** it reads the same two values from its own connection-setup
> screen, and they must match the website's **byte for byte**. A mismatch fails silently — sessions
> and encrypted values simply will not round-trip between the two heads.

### 8. Run

```bash
dotnet run --project source/TechieBlog
```

Then open **<http://localhost:5373>** (or `https://localhost:7373`).

The seeded administrator is `Ravi@techieblog.com`. Its password, and the other seeded accounts, are
listed in [`docs/TechieBlog-UsageGuide.md`](docs/TechieBlog-UsageGuide.md) — every seeded account is
flagged `MustChangePassword`, so the first sign-in lands on the change-password screen by design.

> **A fresh database starts EMPTY of content.** The seed creates the staff accounts and the taxonomy
> only — no sample posts. Every page has a designed empty state, so the site is presentable from the
> first run and you are never deleting someone else's demo data.

---

## Features

### Content
- Full CRUD for posts, with a Markdown editor and live preview
- Categories, tags and multi-part **series**
- Draft / publish workflow, with the publication date distinct from the creation date
- Reading-time estimates and per-post view counts

### Public site
- Portfolio-style landing page driven by the site owner's profile
- Post, category, tag, series and search pages
- **Speaker Profile** (`/speaker-profile`) — past and upcoming speaking engagements, with an admin
  screen to manage them
- Resume page with experience, skills, awards and a downloadable CV
- About page, RSS feed (`/rss`) and a generated sitemap

### Engagement — no reader accounts required
- Comments and 1–5 star ratings from **anonymous** visitors, keyed by email
- **Double opt-in email verification** before a comment or rating counts
- **Self-hosted captcha** on every public write surface — generated and validated in-process, no
  third-party service
- Comment moderation queue for staff

### Newsletter
- Subscriber management with CSV export
- Newsletter composer, public archive (`/newsletters`) and per-issue pages
- Per-issue unsubscribe tokens

### Administration
- JWT authentication with a role hierarchy: **Admin › Editor › Author › Contributor › Reader**
- User administration: create, edit, activate/deactivate and **soft-delete** accounts, with guards
  against deleting yourself, the site owner, or the last active administrator
- Media library with per-category validation (profiles, logos, awards, icons, blog, CV, general)
- Site settings, analytics dashboard, and a maintenance action to clear cached content

> Public self-service registration is **deliberately absent**. Accounts are created by an
> administrator; engagement is anonymous and email-verified instead.

### Desktop admin app (`source/BlogApp`)
A MAUI Blazor Hybrid head that reuses the same `BlogUI` library, so the admin surface is written
once. It connects to the same PostgreSQL database, stores its connection settings in the OS
credential store, and can upload media to the server over **SFTP**.

> **The website owns the schema.** BlogApp never runs DbUp. Deploy the website *first* so migrations
> land, then distribute a matching desktop build — a desktop binary newer than the database will fail
> on the missing column.

### Theming
- CSS custom properties (OKLCH), no code changes needed
- Four theme sets: `trblaze-modern` (default), `developer`, `minimal`, `fluent-modern`
- Light and dark via a `dark` class on `<html>`, with a toggle in every shell
- **Dark is the shipped default**, changeable in *Settings → Theme*

---

## Architecture

A clean monolith — six projects plus a test project, no REST API layer:

```
TechieBlog.slnx
├── source/
│   ├── BlogDb/       # DbUp migration scripts (PostgresScripts/*.sql)
│   ├── BlogModel/    # Domain models, interfaces, DTOs  (assembly: BlogModels)
│   ├── BlogEngine/   # Business logic, repositories, services
│   ├── BlogUI/       # Razor Class Library — every page and component
│   ├── TechieBlog/   # Blazor Server host (the website)
│   └── BlogApp/      # MAUI Blazor Hybrid desktop admin head
└── tests/
    └── TechieBlog.Tests/
```

**Design principles**

- **No REST API layer** — Blazor Server components call services directly. Fewer moving parts, and
  nothing to keep in sync.
- **The UI lives in a library, not the host.** Both heads reference `BlogUI`, so a screen is built
  once and appears in both.
- **Dapper over stored functions and inline SQL** — the data layer is readable SQL, not generated
  queries.
- Understandable in about an hour of code reading.

---

## Customization

### Theming

Edit the token layer in **`source/BlogUI/wwwroot/css/theme.css`**:

```css
:root, :root[data-site-theme="trblaze-modern"] {
    --primary: oklch(0.546 0.215 262.881);
    --background: oklch(1 0 0);
    --foreground: oklch(0.145 0 0);
    --radius: 0.625rem;
}
```

Dark values live under the `.dark` selector in the same file. Add a theme by adding a
`:root[data-site-theme="your-name"]` block.

> ⚠ **Two things that will waste your time if you don't know them.**
>
> 1. `source/BlogUI/Styles/*.scss` is **dead legacy** — nothing references it and nothing compiles
>    it. Editing `_variables.scss` changes nothing. The live token layer is `wwwroot/css/theme.css`.
> 2. The build consumes TrBlazeUI's **prebuilt** stylesheet and runs **no Tailwind JIT pass**, so
>    arbitrary-value utilities such as `text-[clamp(30px,5vw,46px)]` or `max-w-[1100px]` are never
>    generated. They render as unknown classes and silently do nothing. Use utilities that already
>    exist in the shipped CSS, or write a real rule in `wwwroot/css/`.

### Site settings

Most configuration is **stored in the database** and edited at `/settings` — site title, tagline,
posts per page, comment moderation, SEO and social fields, SMTP, storage provider and theme. Only
infrastructure concerns (`AppDbConString`, `JwtSigningKey`, `AppEncryptionKey`, logging) live in
configuration files and secrets.

### Adding a feature

1. Model → `source/BlogModel/`
2. Repository + service → `source/BlogEngine/`
3. Page or component → `source/BlogUI/` (both heads pick it up)
4. Registration → `source/BlogEngine/BlogSvcInitializer.cs`
5. Schema change → a new numbered script in `source/BlogDb/PostgresScripts/`

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Framework | .NET 10 LTS |
| Web UI | Blazor Server + **TrBlazeUI** (shadcn-compatible, Lucide icons) |
| Desktop UI | .NET MAUI Blazor Hybrid (Windows) |
| Database | PostgreSQL |
| Data access | Dapper (micro-ORM) + PostgreSQL stored functions |
| Migrations | DbUp |
| Logging | Serilog |
| Auth | JWT, PBKDF2-HMAC-SHA256 password hashing |
| Tests | xUnit + NSubstitute; Playwright for UI smokes |

---

## Testing

```bash
dotnet test tests/TechieBlog.Tests/TechieBlog.Tests.csproj
```

Coding conventions are enforced at **build time**, not by review: `tests/unit/Ops/` scans `source/`
and `tests/` for underscore-prefixed fields, underscored test names, Hungarian prefixes and
exception-message disclosure — and each scanner carries a self-test proving its pattern can actually
match, so a dead check fails the build instead of reading as a pass.

---

## Documentation

| Document | What it covers |
|---|---|
| [GETTING_STARTED.md](GETTING_STARTED.md) | Detailed setup |
| [docs/TechieBlog-Architecture.md](docs/TechieBlog-Architecture.md) | Technical deep-dive |
| [docs/TechieBlog-UsageGuide.md](docs/TechieBlog-UsageGuide.md) | Test accounts, test plan, setup |
| [docs/TechieBlog-Coding-Standards.md](docs/TechieBlog-Coding-Standards.md) | Conventions the build enforces |
| [docs/TechieBlog-UIDesign.md](docs/TechieBlog-UIDesign.md) | UI spec — the visual contract |
| [docs/mockups/](docs/mockups/) | Rendered screen mockups (the **only** mockup set) |
| [docs/devguides/](docs/devguides/) | Screen-by-screen developer guide |
| [docs/customization.md](docs/customization.md) | Theming and extending |
| [docs/deployment.md](docs/deployment.md) · [docs/Prod-Deploy-Checklist.md](docs/Prod-Deploy-Checklist.md) | Production deployment |
| [PROJECT-STATUS.md](PROJECT-STATUS.md) | Current build state and open items |

---

## Project Structure

```
TechieBlog/
├── docs/                       # Documentation
│   ├── mockups/                # Screen mockups — the visual contract
│   ├── devguides/              # Screen-by-screen developer guide
│   ├── data/                   # One-off data-load scripts (disposable)
│   └── OldDocs/                # Archived, read-only history
├── scripts/                    # Rename-Project.ps1 · rename-project.sh
├── source/                     # Source code (see Architecture above)
├── tests/                      # xUnit tests + Playwright smoke harnesses
├── GETTING_STARTED.md
├── PROJECT-STATUS.md
├── LICENSE.txt
└── TechieBlog.slnx
```

---

## Contributing

This is a template designed for you to make your own. Fork it, customize it, open issues for bugs,
and share what you build.

---

## License

MIT — see [LICENSE.txt](LICENSE.txt). Free for personal and commercial use.

---

## Acknowledgments

- UI built with [TrBlazeUI](https://github.com/techierathore/TrBlazeUI) and
  [Lucide](https://lucide.dev/) icons
- Inspired by the need for a simple, Blazor-native blogging solution
- Designed as both a practical tool and an educational reference

---

**Happy Blogging!**
