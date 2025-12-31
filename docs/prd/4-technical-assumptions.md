# 4. Technical Assumptions

### 4.1 Repository Structure

**Monorepo** — Single repository containing all projects:

```
TechieBlog.sln
├── BlogDb/              # PostgreSQL scripts, DbUp migrations
├── BlogModel/           # Models, interfaces, DTOs, constants
├── BlogEngine/          # Business logic, repositories, services
├── BlogUI/              # Razor Class Library (Fluent UI components)
└── TechieBlog/          # Blazor Server host application
```

### 4.2 Service Architecture

**Monolith with Clean Architecture** — Single deployable unit with clear internal boundaries:

- **BlogModel:** Domain models, interfaces, DTOs — no dependencies
- **BlogEngine:** Business logic, repository implementations — depends on BlogModel
- **BlogUI:** Razor Class Library with all UI components — depends on BlogModel, BlogEngine
- **TechieBlog:** Host application, DI configuration, entry point — depends on all projects
- **BlogDb:** Database scripts only — no runtime dependencies

**Key Decision:** No REST API layer (BlogSvc removed). UI calls BlogEngine services directly for simplicity and performance in a template project.

### 4.3 Testing Requirements

**Unit + Integration Testing:**

- Unit tests for BlogEngine services and business logic
- Integration tests for repository layer with test database
- Component tests for critical Blazor UI components
- Manual testing convenience methods for development workflow
- No E2E browser automation for MVP (complexity vs. value for template project)

### 4.4 Additional Technical Assumptions

- **.NET 10 LTS:** Target framework for long-term support stability
- **Blazor Server:** Server-side rendering model (not WebAssembly)
- **Microsoft Fluent UI Blazor:** UI component library replacing Blazorise
- **PostgreSQL:** Primary database (migrating from MySQL)
- **Dapper:** Micro-ORM for data access (retained from current stack)
- **DbUp:** Database migration tool (retained from current stack)
- **Serilog:** Structured logging framework (retained from current stack)
- **JWT Authentication:** Built-in authentication without external identity providers
- **SMTP:** Email delivery for password reset and newsletters (configurable provider)
- **Configurable Storage:** Abstract file storage interface supporting network/cloud backends

---
