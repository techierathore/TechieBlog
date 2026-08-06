# Project Brief: TechieBlog 2.0

**Version:** 1.0
**Date:** December 16, 2025
**Status:** Draft for Review
**Generated From:** Brainstorming Session with Mary (Business Analyst)

---

## Executive Summary

**TechieBlog** is a no-fuss, embeddable blogging engine designed as a template/starter project for .NET developers. It serves dual purposes: (1) a learning resource to understand Blazor development, and (2) a ready-to-customize blogging solution that can be embedded in various applications.

**Primary Problem:** .NET developers who want to add blogging capabilities to their applications face a choice between heavyweight CMS platforms (WordPress, Ghost) that require separate infrastructure, or building from scratch. There's no simple, clean, Blazor-native solution they can clone, understand, and customize.

**Target Users:** .NET developers who want to learn Blazor while having a practical, production-ready blogging engine they can customize and deploy.

**Key Value Proposition:** A clean, readable, well-architected Blazor blogging engine that developers can clone, understand in hours, and customize to match any design — without the overhead of traditional CMS platforms.

---

## Problem Statement

### Current Pain Points

1. **No Blazor-Native Blogging Solution:** Developers building .NET applications must either integrate external platforms (WordPress, Ghost) or build blog functionality from scratch.

2. **Learning Gap:** Blazor developers lack real-world, production-quality reference implementations that demonstrate best practices for content management applications.

3. **Customization Friction:** Existing blogging platforms have rigid themes and architectures that make deep customization difficult without extensive learning curves.

4. **Over-Engineering:** Most CMS platforms come with features 90% of developers don't need (e-commerce, complex workflows, plugin ecosystems), adding unnecessary complexity.

### Why Existing Solutions Fall Short

| Solution | Gap |
|----------|-----|
| WordPress | PHP-based, requires separate hosting, heavy for simple needs |
| Ghost | Node.js-based, not .NET ecosystem, complex theming |
| Custom Build | Time-consuming, no reference architecture |
| Orchard Core | Full CMS, steep learning curve, over-engineered for blogs |

### Urgency

With .NET 10 LTS releasing and Blazor maturing as a production-ready framework, there's an opportunity to create the definitive Blazor blogging starter that developers will reference and use for years.

---

## Proposed Solution

### Core Concept

TechieBlog 2.0 is a **template/starter project** — not a NuGet package, not a SaaS product. Developers clone the repository, customize the themes via CSS variables, and deploy their own instance. The codebase is intentionally readable and well-documented to serve as a learning resource.

### Key Differentiators

1. **Clone & Customize:** Not a black-box package — full source code that developers own and modify
2. **Theme Independence:** CSS variable-based theming ensures no two TechieBlog sites need to look alike
3. **Clean Architecture:** 5-project structure that's easy to understand and extend
4. **Modern Stack:** .NET 10 LTS, Blazor Server, Fluent UI, PostgreSQL, Dapper
5. **Reusable UI Layer:** BlogUI as a Razor Class Library enables future scenarios (desktop app, MAUI Hybrid)

### High-Level Vision

A Blazor blogging engine that a competent .NET developer can:
- Clone and run in under 5 minutes
- Understand the architecture in under an hour
- Customize the theme in under a day
- Deploy to production in under a week

---

## Target Users

### Primary User Segment: .NET Developer (Template User)

**Profile:**
- Mid-level to senior .NET developer
- Familiar with C#, ASP.NET Core basics
- Learning or experienced with Blazor
- Needs blog functionality for personal or client projects

**Current Behaviors:**
- Evaluates CMS options, often settles for WordPress despite preferring .NET
- Spends time building custom solutions that lack polish
- Reads documentation and source code to learn

**Pain Points:**
- No Blazor reference implementation for content management
- Time wasted integrating non-.NET blog platforms
- Difficulty customizing rigid CMS themes

**Goals:**
- Add blog to existing .NET application quickly
- Learn Blazor patterns from production-quality code
- Have full control over appearance and functionality

### Secondary User Segment: Blog Reader/Contributor

**Profile:**
- End users of TechieBlog-powered sites
- May be readers, registered users, or content contributors

**Needs:**
- Clean reading experience
- Easy registration and engagement (comments, ratings)
- For contributors: intuitive content creation with Markdown support

---

## Goals & Success Metrics

### Project Objectives

- **O1:** Create a production-ready Blazor blogging engine on .NET 10 LTS
- **O2:** Migrate from MySQL to PostgreSQL for broader hosting compatibility
- **O3:** Replace Blazorise with Microsoft Fluent UI for modern, consistent styling
- **O4:** Structure codebase as an educational reference for Blazor developers
- **O5:** Enable easy theming without code changes

### User Success Metrics

- Developer can clone, build, and run locally in < 5 minutes
- Developer understands project structure in < 1 hour of code review
- Theme customization (colors, fonts) achievable in < 4 hours
- Full deployment to production in < 1 week

### Key Performance Indicators (KPIs)

| KPI | Target |
|-----|--------|
| Time to first successful local run | < 5 minutes |
| Code readability (subjective) | Clean enough to learn from |
| Theme change effort | CSS variables only, no Razor changes |
| GitHub stars (if open-sourced) | Community validation metric |

---

## MVP Scope

### Core Features (Must Have)

#### Authentication & Users
- **Email/Password Authentication:** Built-in JWT-based auth system
- **5 User Roles:** Admin, Editor, Author, Contributor, Reader
- **User Registration:** Self-service signup for readers
- **Password Reset:** Email-based password recovery

#### Content Management
- **Blog Posts CRUD:** Create, read, update, delete posts
- **Markdown Editor:** Write content in Markdown with preview
- **Categories & Tags:** Organize content with categories and tags
- **Draft/Preview:** Save drafts, preview before publishing
- **Scheduling:** Set future publish date/time
- **Series/Collections:** Group related posts together

#### Engagement
- **Comments:** Logged-in users can comment on posts (with moderation)
- **Star Ratings:** 1-5 star ratings per post (logged-in users, changeable)
- **Favorites:** Readers can bookmark/favorite posts

#### Media
- **Image Management:** Upload and manage images
- **Configurable Storage:** Network/cloud storage (NAS, Cloudflare R2, etc.)

#### Subscribers & Newsletter
- **Subscribe Form:** Capture email addresses
- **Subscriber List:** Store and manage subscribers
- **Send Newsletter:** Send emails directly from app
- **Manual Export:** Export subscriber list as needed

#### Analytics
- **Post Views:** Track total and unique views
- **Popular Posts:** Identify most-viewed content
- **Engagement Stats:** Comments and ratings per post

#### SEO
- **RSS Feed:** Auto-generated RSS for syndication (Dev.to, C# Corner)
- **Sitemap:** Auto-generated sitemap.xml

#### Theming
- **CSS Variables:** All colors, fonts, spacing via CSS variables
- **Light/Dark Mode:** User-controlled toggle in header, stored in localStorage/user profile
- **Site Themes:** 3 pre-built visual themes for public pages:
  1. **Fluent Modern** (Default) - Clean, professional Microsoft Fluent-inspired design
  2. **Developer Dark** - Code editor-inspired with syntax highlighting colors
  3. **Minimal Clean** - Typography-focused, generous whitespace, serif headings
- **Theme Variants:** Each site theme includes light and dark mode variants (6 total combinations)
- **Admin Theme Selection:** Site theme configurable via Admin Settings

#### Admin Dashboard
- **Statistics Overview:** Post counts, user counts, engagement metrics
- **Content Management:** Manage posts, comments, users
- **Settings:** Site configuration

### Out of Scope for MVP

- Email sequences (drip campaigns)
- Lead magnets (downloadable resources)
- Social login (Google, GitHub, etc.)
- Magic link authentication
- Advanced SEO (Open Graph, meta tag editor)
- Admin UI for theme creation
- Desktop/offline application wrapper
- Mobile applications
- Multi-tenancy
- Localization/internationalization
- Advanced analytics (referrer tracking, reading time)

### MVP Success Criteria

1. All MVP features functional and tested
2. Clean, documented codebase suitable for learning
3. Theme can be changed via CSS variables without code changes
4. Deploys successfully to standard .NET hosting
5. Owner can use it for personal blog and story site

---

## Post-MVP Vision

### Phase 2 Features

- **Admin Theme UI:** Visual interface for switching/customizing themes
- **Theme Creator:** Ability to create new themes from admin
- **Email Sequences:** Automated drip campaigns for subscribers
- **Lead Magnets:** Downloadable resources for email capture
- **Enhanced SEO:** Meta tag editor, Open Graph previews

### Long-term Vision

- **Desktop Writer:** Offline blog writing app using MAUI Blazor Hybrid wrapping BlogUI
- **Social Login:** Google, GitHub authentication options
- **Magic Links:** Passwordless authentication
- **Advanced Analytics:** Full analytics dashboard with trends
- **Community Themes:** Repository of community-contributed themes

### Expansion Opportunities

- **Template Marketplace:** Curated themes for TechieBlog
- **Documentation Site:** Comprehensive docs for developers
- **Video Tutorials:** YouTube series on customizing TechieBlog
- **Enterprise Features:** If demand emerges (multi-tenancy, SSO)

---

## Technical Considerations

### Platform Requirements

| Requirement | Specification |
|-------------|---------------|
| **Target Platform** | Web (Blazor Server) |
| **Runtime** | .NET 10 LTS |
| **Database** | PostgreSQL |
| **Supported Browsers** | Modern browsers (Chrome, Firefox, Edge, Safari) |
| **Hosting** | Any .NET-capable host (Azure, AWS, VPS, shared hosting) |

### Technology Stack

| Layer | Technology | Notes |
|-------|------------|-------|
| **Framework** | .NET 10 LTS | Long-term support version |
| **Frontend** | Blazor Server | Server-side rendering |
| **UI Library** | Microsoft Fluent UI Blazor | Replacing Blazorise |
| **Database** | PostgreSQL | Migrating from MySQL |
| **ORM** | Dapper | Micro-ORM, kept from current stack |
| **Authentication** | JWT | Built-in, no external providers for MVP |
| **DB Migrations** | DbUp | Kept from current stack |
| **Logging** | Serilog | Kept from current stack |

### Architecture Decisions

#### Project Structure (5 Projects)

```
TechieBlog.sln
├── BlogDb/              # PostgreSQL scripts, DbUp migrations
│   └── PostgresScripts/ # Migration scripts
├── BlogModel/           # Models, interfaces, DTOs, constants
├── BlogEngine/          # Business logic, repositories, services
│   ├── Services/        # AuthSvc, BlogSvc, etc.
│   └── DbAccess/        # Repository implementations
├── BlogUI/              # Razor Class Library (Fluent UI)
│   ├── Pages/           # All Blazor pages
│   ├── Components/      # Reusable components
│   ├── Layouts/         # Page layouts
│   └── Themes/          # CSS theme files
└── TechieBlog/          # Blazor Server host
    └── Program.cs       # Entry point, DI configuration
```

#### Key Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Remove BlogSvc | Yes | REST API layer unnecessary for template project |
| Keep BlogUI as RCL | Yes | Enables future desktop app scenario |
| Direct service calls | Yes | UI calls BlogEngine services directly, no API layer |
| CSS-based theming | Yes | Simplest customization path for developers |
| PostgreSQL only | Yes | Better hosting compatibility than MySQL |

#### Integration Requirements

- **Email:** SMTP for password reset and newsletters (configurable)
- **Storage:** Configurable network/cloud storage for images
- **No external APIs** for MVP (no social login, no third-party analytics)

#### Security Considerations

- JWT token security (proper signing key management)
- Password hashing with salt
- Input validation on all forms
- SQL injection prevention (parameterized queries via Dapper)
- HTTPS enforcement in production
- Rate limiting on authentication endpoints

---

## Constraints & Assumptions

### Constraints

| Constraint | Details |
|------------|---------|
| **Budget** | No constraints — personal/community project |
| **Timeline** | No hard deadline — AI-assisted development |
| **Resources** | Single developer with AI assistance |
| **Technical** | Must use .NET 10, Fluent UI, PostgreSQL (defined stack) |

### Key Assumptions

- Developers cloning this project have basic .NET/C# knowledge
- PostgreSQL is available on target hosting environments
- SMTP access available for email functionality
- Network storage accessible for image hosting
- Target users comfortable with command-line operations (clone, build, run)

---

## Risks & Open Questions

### Key Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Fluent UI learning curve** | Development slowdown | Reference Fluent UI docs, start with simple components |
| **PostgreSQL migration complexity** | Data loss, extended timeline | Careful migration scripting, backup existing data |
| **Scope creep** | Never-ending MVP | Strict adherence to defined MVP scope |
| **Theming flexibility vs simplicity** | Complex theming system | Start with CSS variables only, validate approach |

### Open Questions

1. **Image Storage Provider:** Which specific provider to support first? (Cloudflare R2, NAS, other?)
2. **Email Provider:** Built-in SMTP or abstract for different providers?
3. **Markdown Editor:** Which Blazor Markdown component to use?
4. **Rating Display:** Stars only or also show average/count?
5. **Comment Threading:** Flat comments or nested replies?

### Areas Needing Further Research

- Fluent UI Blazor component library capabilities and limitations
- PostgreSQL-specific features to leverage (JSONB for settings?)
- Best practices for Blazor Server theming with CSS variables
- MAUI Blazor Hybrid feasibility for future desktop app

---

## Implementation Phases

### Phase 1: Foundation & UI Scaffolding

**Focus:** Infrastructure migration + UI scaffolds from mockups

1. Migrate solution to .NET 10 LTS
2. Replace Blazorise with Microsoft Fluent UI Blazor
3. Migrate database from MySQL to PostgreSQL (scripts + data)
4. Remove BlogSvc project from solution
5. Restructure DI and service layer
6. Set up CSS variable-based theming infrastructure
7. **Convert HTML mockups to Blazor UI scaffolds** (Agile approach)

**Exit Criteria:** Solution builds, runs on .NET 10 with Fluent UI, all pages have UI scaffolds

### Phase 2: Core Functionality

**Focus:** Complete all content management features

1. Complete authentication (registration, password reset, email verification)
2. Implement 5-tier role system with permissions
3. Blog posts: full CRUD with Markdown editor
4. Categories and Tags management
5. Draft/Preview/Scheduling workflow
6. Series/Collections feature

**Exit Criteria:** Full content management workflow functional

### Phase 3: Engagement, Media, Subscribers & Analytics

**Focus:** User engagement and data features

1. Comments system with moderation workflow
2. Star ratings (1-5, per user, changeable)
3. Favorites/bookmarks for readers
4. Configurable image storage
5. Image upload and management UI
6. Subscriber management (subscribe, store, export)
7. Newsletter sending from app
8. Analytics: post views, popular posts, engagement stats
9. Admin dashboard with real metrics

**Exit Criteria:** All engagement and analytics features functional

### Phase 4: SEO & Polish

**Focus:** Production readiness and documentation

1. RSS feed generation
2. Sitemap generation
3. Finalize 2-3 pre-built themes
4. Code cleanup and documentation
5. Developer documentation (README, setup guide)
6. Sample data and quick-start guide

**Exit Criteria:** Production-ready, well-documented template

---

## Appendices

### A. Existing Codebase Summary

The current TechieBlog codebase is approximately 30-40% complete:

**Working:**
- User login with JWT
- Database schema (22 tables)
- Repository layer (partial)
- REST API (to be removed)

**Incomplete:**
- UI pages (stubs only)
- Service layer connections
- Public blog pages
- Most features

**To Be Migrated:**
- MySQL → PostgreSQL
- Blazorise → Fluent UI
- .NET 9.0 → .NET 10

### B. Brainstorming Session Summary

**Session Date:** December 16, 2025
**Participants:** User + Mary (Business Analyst)
**Approach:** Progressive Flow (Divergent → Convergent → Synthesis)

**Key Insights:**
- Project is NOT a commercial product — it's a community/learning resource
- Theming is critical — no two sites should look the same
- Multi-author with 5 roles needed for story site use case
- Agile approach preferred — UI scaffolds in Phase 1

### C. References

- [Microsoft Fluent UI Blazor](https://www.fluentui-blazor.net/)
- [.NET 10 Release Information](https://dotnet.microsoft.com/)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [DbUp Documentation](https://dbup.readthedocs.io/)
- [Dapper Documentation](https://github.com/DapperLib/Dapper)

---

## Next Steps

### Immediate Actions

1. Review and approve this Project Brief
2. Generate PRD based on this brief
3. Create Architecture Document
4. Create UI/UX Specifications
5. Create HTML mockups for all pages
6. Begin Phase 1 implementation

### PM Handoff

This Project Brief provides the full context for **TechieBlog 2.0**. Please start in 'PRD Generation Mode', review the brief thoroughly to work with the user to create the PRD section by section as the template indicates, asking for any necessary clarification or suggesting improvements.

---

*Generated by Mary (Business Analyst) — BMAD Framework*
*Brainstorming Session: December 16, 2025*
