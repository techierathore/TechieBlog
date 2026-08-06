# TechieBlog Coding Standards

**Last Updated:** 2026-08-02
**Status:** Authoritative for all code under `source/` and `tests/`. Conformance enforced via repo-root `.editorconfig` + verifier grep checks in §"Enforcement".

> **Per-project decision (day-1, 2026-08-02): instance fields take NO prefix.** A scan of `source/`
> found ~250 bare (no-prefix) private instance fields against 32 `_underscore`-prefixed and 1
> `obj`-prefixed — bare wins at ~89%, so the codebase's own convention is adopted rather than
> imposed. Within the bare style the inherited convention (recorded in the archived
> `docs/architecture.md` §9.1.1) is **camelCase** for private instance fields, and that is what this
> document requires. The 32 underscore-prefixed fields are standards drift and are remediated
> incrementally under `REQ-NFR-021`, not in a big-bang rename.

## Database Naming Conventions

### Tables and Columns
- PascalCase: `CustomerOrder` NOT `customer_order`
- Singular: `CustomerOrder` NOT `CustomerOrders`
- **NEVER use underscores** in any DB object name
- FK columns: `{TableName}Id` (e.g., `CustomerId`)
- PK: `{TableName}Id` (e.g., `UserId`)

### Stored Procedures & Functions
- PascalCase verb prefix: `GetCustomerOrders`, `InsertOrder`, `CalculateTotal`
- Action prefixes: Get / Insert / Update / Delete / Calculate
- This project uses **PostgreSQL stored functions** (`CREATE OR REPLACE FUNCTION`), not procedures; call them as `SELECT * FROM GetPostById(@postId)`.

### Indexes & Constraints
- Index: `IX{Table}{Column}` · PK: `Pk{Table}` · FK: `Fk{Table}{Ref}` · Unique: `Uc{Table}{Column}`
- *Existing code uses the `Idx{Table}{Column}` form (e.g. `IdxPostSlug`, `IdxUserSkillsUserId`). Keep `Idx…` for consistency inside this codebase; do not mix both forms.*

### Migration scripts
- Numbered prefix + PascalCase: `012-ResumeAndImageManagement.sql`
- Numbers are sequential and never reused; every script is idempotent (`IF NOT EXISTS`) because DbUp runs at every startup.
- Each script carries the header comment block documenting purpose, changes, dependencies and rollback.

## C# Conventions

### Classes & Interfaces
- PascalCase for classes; `I` prefix for interfaces; descriptive names.
- Async methods end with `Async`.

### Fields, Parameters, Locals

**NEVER use underscores** anywhere in any identifier.

| Kind | Convention | Example |
|------|-----------|---------|
| **Instance fields** | camelCase, **no prefix** (no underscores) | `private readonly ILogger<X> logger;`<br>`private readonly IBlogPostRepo blogPostRepo;`<br>`private string cachedPublicKey;` |
| **Static / `const` fields** | PascalCase, no prefix | `private const string CachePrefix = "…";` |
| **Method parameters** | camelCase, no prefix | `LoginAsync(string email, string password)` |
| **Local variables** | camelCase, no prefix | `var response = await …` |
| **Booleans** | same casing + `Is`/`Has`/`Can` | `IsAuthenticated`, `isValid`, `hasAccess` |
| **Properties** | PascalCase, no prefix | `public string ConnectionString { get; set; }` |
| **Constants** | PascalCase, no underscores | `MaxRetryCount` NOT `MAX_RETRY_COUNT` |
| **Test methods** | Short PascalCase, no underscores — full scenario in XML `<summary>` | `LoginRejectsBadPassword` not `Login_BadPassword_ReturnsUnauthorized` |

**Rejected forms:** `_underscore` field prefixes, `obj`/`a`/`v` Hungarian-style prefixes, snake_case anywhere, type prefixes (`strName`), underscores in test method names.

### Controller-action parameters
This solution has no controllers (the REST layer was removed). If an API head is ever added, parameter names flow through to OpenAPI: keep them camelCase and meaningful. Body DTO **property** names stay PascalCase.

### Environment Variables
**PascalCase, no separators.** `TechieBlogBaseUrl` NOT `TECHIEBLOG_BASE_URL` and NOT `TechieBlog__BaseUrl`. Use a custom configuration provider mapping PascalCase env vars → `:`-nested config paths. Read via `IConfiguration["Section:Key"]` only — never `Environment.GetEnvironmentVariable(...)`. *(Existing keys: `AppDbConString`, `SiteSettings:BaseUrl`.)*

### Project & solution naming — the primary head carries the PRODUCT name
- The product's **primary executable head** project is named exactly `TechieBlog` — `source/TechieBlog/TechieBlog.csproj`. ✅ Already correct.
- **`TechieBlog.App` is BANNED** (owner rule 2026-07-10): "App" says nothing — the product name already names the app. Never scaffold it.
- Secondary heads of a multi-head product take a **descriptive** dotted suffix: `TechieBlog.Api`, `TechieBlog.Desktop`, `TechieBlog.Cli`. Satellite projects keep their conventional names — in this repo the satellites are `BlogModel` (assembly `BlogModels`), `BlogEngine`, `BlogUI` (RCL) and `BlogDb`; a future test project is `TechieBlog.Tests`.

### File Structure
```csharp
using System;

namespace BlogEngine.Services;

public class DatabaseService
{
    private readonly ILogger<DatabaseService> logger;
    private readonly IConfiguration configuration;

    public DatabaseService(ILogger<DatabaseService> logger, IConfiguration configuration)
    {
        this.logger = logger;
        this.configuration = configuration;
    }

    public string ConnectionString { get; set; }

    public async Task<DataTable> GetDataAsync(string queryName)
    {
        var connString = configuration.GetConnectionString("Default");
        var result = await ExecuteQueryAsync(connString, queryName);
        return result;
    }
}
```

*(With no-prefix camelCase fields, `this.` disambiguation in constructors is expected and correct — it is the cost of the convention the codebase already uses.)*

### Best Practices
- One class per file. File name matches class.
- File-scoped namespaces. Nullable reference types enabled (**currently `disable` — see `REQ-NFR-022`**).
- Methods small (<20 lines). Single responsibility.
- Max 3 nesting levels. Early returns for validation.
- ConfigureAwait(false) in libraries.
- StringBuilder for loop concatenation. Dispose IDisposable. Cache expensive ops.
- Constructor injection only — no service locator.
- Services return `Result` / `Result<T>` (`BlogModels.Common`) for expected failures; exceptions are logged with context before rethrowing.

### Data access (Dapper)
- **Dapper is the ORM for all data access.** No EF Core.
- Always `DynamicParameters` — never string-concatenate SQL.
- Prefer PostgreSQL stored functions over inline SQL.
- `using` statements for connections; async (`QueryAsync`, `ExecuteAsync`) for all DB calls.
- Repositories extend `GenericRepository<T>` in `BlogEngine/DaCore/`.

### XML Documentation (MANDATORY on public members)
`<summary>`, `<remarks>`, `<param>`, `<returns>`, `<exception>` — all required. For classes, the `<remarks>` block documents **Purpose**, **Code Flow**, **Dependencies** and **Usage**; for methods, **Business Logic**, **Flow** and **Side Effects**. This codebase is an educational reference — the documentation is part of the deliverable, not an afterthought.

### Testing
- Short PascalCase test name, no underscores. Full scenario in XML `<summary>`.
- Arrange-Act-Assert. One assertion per test where practical.
- xUnit for services, bUnit for Blazor components; integration tests run against a PostgreSQL test container.

### Security
- Never hardcode credentials. Parameterized queries. Validate inputs. Log security events.
- No secret ever lands in `appsettings.json` that ships in the template — connection strings and SMTP credentials come from user secrets or environment configuration.

### Logging — Serilog file sink (MANDATORY, every .NET app type)
- **Every executable head gets Serilog with a rolling FILE sink** — web, API, MAUI, desktop, console/CLI, background service. No exceptions.
- Wire at startup, before anything else can fail: `Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().WriteTo.File("logs/techieblog-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14).CreateLogger();` then `builder.Host.UseSerilog()`. Read overrides from the `Serilog` section of `appsettings.json`.
- Log unhandled exceptions at the head boundary: `try/catch` + `Log.Fatal` around startup, `AppDomain.CurrentDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` handlers, and `Log.CloseAndFlush()` on exit.
- **Class libraries never reference Serilog** — `BlogEngine`, `BlogUI`, `BlogModels` and `BlogDb` log through `ILogger<T>` / `Microsoft.Extensions.Logging.Abstractions` only. ✅ Already correct.
- App code logs through injected `ILogger<T>` with structured message templates (`logger.LogInformation("Imported {Count} rows", n)`), not static `Log.*`, outside the startup boundary.
- The `logs/` output folder is gitignored (the owner adds it — agents never run git).
- **Current state:** compliant except for the two unhandled-exception handlers (`REQ-NFR-013`).

### Blazor UI testability — stable element ids
- Every interactive or data-bound element the verifier must reach (buttons, inputs, grids, key value labels) carries a stable, unique **`data-testid`** or element `id` so headless Playwright selectors do not drift.
- Name them by intent, not layout: `data-testid="login-submit"`, `data-testid="posts-grid"`, `data-testid="total-rating-value"` — never positional (`button2`).
- Put the id on the element whose data the gate asserts (the grid itself, the value label), so "rows present AND non-empty" maps to one addressable element.
- (The MAUI analogue is `AutomationId`; not applicable to this project today.)

### CSS and theming
- **No hardcoded colours, fonts, spacing or radii in components** — every value is a CSS custom property defined in `source/BlogUI/wwwroot/Themes/_variables.css` and overridden per theme.
- CSS file names are kebab-case: `fluent-modern.css`, `developer-dark.css`.
- A new component must render correctly in all three site themes × light and dark before it is considered done.

## Enforcement

### .editorconfig (machine-checkable)
- File-scoped namespaces (`warning`)
- Async-method `Async` suffix (`warning`)
- `var` for locals (`warning`)
- Nullable reference types enabled
- No `_` prefix on private fields (`warning` via custom naming rule)

### Verifier grep checks
```bash
# Forbidden underscore-prefix fields
grep -rE "private(\s+readonly)?\s+\w+\s+_[a-z]" source/ 2>/dev/null

# Forbidden test-method underscores
grep -rE "public\s+(async\s+)?Task\s+\w+_\w+\s*\(" tests/ 2>/dev/null

# Forbidden Hungarian/obj/a/v prefixes (this project is no-prefix)
grep -rE "private(\s+readonly)?\s+\w+\s+obj[A-Z]" source/ 2>/dev/null

# Hardcoded colours in Razor/CSS outside the theme files
grep -rnE "#[0-9a-fA-F]{3,6}\b" source/BlogUI --include="*.razor" 2>/dev/null
```

### Severity
- **Error**: file-scoped namespace, underscore field prefix
- **Warning**: nullable, async suffix
- **Info**: consider fixing
