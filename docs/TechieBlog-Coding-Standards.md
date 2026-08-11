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
- Wire at startup, before anything else can fail, then `builder.Host.UseSerilog()`. Read overrides from the `Serilog` section of `appsettings.json`.
- **Anchor the path; never pass a bare relative one** (`REQ-NFR-037`). A relative sink path resolves against the process WORKING DIRECTORY, which differs between `dotnet run` (the project folder), the built exe (wherever it was launched from) and a container (`WORKDIR`). That produced **two** log folders for one application on one day — 6.2 MB in the repo root and 305 MB under `source/TechieBlog/`. Resolve against `AppContext.BaseDirectory`, which is identical however the head is launched. Not `ContentRootPath` — it defaults to `Directory.GetCurrentDirectory()` and re-creates the bug under another name.
- **Bound the VOLUME, not just the file count** (`REQ-NFR-036`). `retainedFileCountLimit` alone is NOT a bound: Serilog defaults `fileSizeLimitBytes` to 1 GB and, with `rollOnFileSizeLimit` left `false`, silently STOPS WRITING at that ceiling — deaf on the loudest day, which is the day the log was needed. Always set all three, and treat `fileSizeLimitBytes × retainedFileCountLimit` as the number an operator budgets disk against:

  ```csharp
  .WriteTo.File(
      path: Path.Combine(logDirectory, "techieblog-.log"),   // absolute, anchored on AppContext.BaseDirectory
      rollingInterval: RollingInterval.Day,
      retainedFileCountLimit: 10,
      fileSizeLimitBytes: 10L * 1024 * 1024,
      rollOnFileSizeLimit: true)
  ```

  Current contract: **web host 10 MB × 10 = 100 MB**, configurable via the `LogFile` section (`TechieBlog.Configuration.LogFileSettings`, which exposes `WorstCaseTotalBytes`); **BlogApp 10 MB × 14 = 140 MB**, hardcoded in `MauiProgram` because logging is wired before configuration is loaded. Log the resolved bound once at startup so it appears in the log it describes. Set `LogFileEnabled=false` in a container — the file lands in an ephemeral layer and Docker already captures stdout.
- Development is loud **on purpose** and that is not the thing to fix: Blazor render-tree and SignalR at `Debug` cost ~61 KB per request against ~124 bytes in Production, and that detail is what makes a circuit defect diagnosable. Keep it; the bound above is what stops it filling a drive.
- Log unhandled exceptions at the head boundary: `try/catch` + `Log.Fatal` around startup, `AppDomain.CurrentDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` handlers, and `Log.CloseAndFlush()` on exit.
- **Class libraries never reference Serilog** — `BlogEngine`, `BlogUI`, `BlogModels` and `BlogDb` log through `ILogger<T>` / `Microsoft.Extensions.Logging.Abstractions` only. ✅ Already correct.
- App code logs through injected `ILogger<T>` with structured message templates (`logger.LogInformation("Imported {Count} rows", n)`), not static `Log.*`, outside the startup boundary.
- The `logs/` output folder is gitignored, as is `.smokeout/` (per-agent `-p:OutDir` build output) — `REQ-NFR-036`. Agents never run git, so the owner untracks anything already committed.
- **Current state:** compliant. Both heads have the unhandled-exception handlers (`REQ-NFR-013`), an anchored path (`REQ-NFR-037`) and a total-volume bound (`REQ-NFR-036`).

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
> **Corrected 2026-08-07 — the previous patterns had a blind spot.** They used `\w+` for the field
> type, which cannot match a generic (`<`, `>`, `,`), an array (`[]`), a nullable (`?`) or a qualified
> (`.`) type name. So `private readonly ILogger<X> _logger;` was **never reported** — 7 of the 14
> underscore fields found during `REQ-NFR-021` were exactly that shape and had been passing the gate
> for the life of the project. The patterns below accept the full type grammar and also cover
> `static`. Verified: the old pattern matches `IRepo _repo` but not `ILogger<Foo> _logger`; the new
> one matches both.

```bash
# Forbidden underscore-prefix fields (generic/array/nullable/qualified-type aware)
grep -rEn "private(\s+static)?(\s+readonly)?\s+[\w.<>,\[\]?]+\s+_[a-zA-Z]" source/ \
  --include=*.cs --include=*.razor 2>/dev/null | grep -v "/obj/\|/bin/"

# Forbidden test-method underscores
grep -rE "public\s+(async\s+)?(Task|void)\s+\w+_\w+\s*\(" tests/ 2>/dev/null

# Forbidden Hungarian/obj/a/v prefixes (this project is no-prefix)
grep -rEn "private(\s+static)?(\s+readonly)?\s+[\w.<>,\[\]?]+\s+(obj|str|int|bln)[A-Z]" source/ \
  --include=*.cs --include=*.razor 2>/dev/null | grep -v "/obj/\|/bin/"

# Forbidden a-/v- prefixed parameters and locals (e.g. aLoggedUser, vIdentity)
grep -rEn "\b(a|v)[A-Z][a-zA-Z]*\s*[,)=;]" source/ --include=*.cs 2>/dev/null | grep -v "/obj/\|/bin/"

# Hardcoded colours in Razor/CSS outside the theme files
grep -rnE "#[0-9a-fA-F]{3,6}\b" source/BlogUI --include="*.razor" 2>/dev/null
```

#### Exception-text disclosure — the scope is `source/`, not a list of services (REQ-NFR-033)

> **Widened 2026-08-11 (REQ-NFR-033).** The REQ-NFR-031 gate named four services — `BlogSvc`,
> `TagSvc`, `CategorySvc`, `SeriesSvc` — and went green the moment those four were curated. **46
> disclosures were still live elsewhere.** Counted, not estimated:
>
> | Where | Count | Reachable by |
> |---|---|---|
> | `UserStatsSvc` | 8 | admin only |
> | `CommentSvc` | 6 | **anonymous** — the public article comment form |
> | `SiteSettingsService` | 2 | admin only, but the rows hold the SMTP password |
> | `SmtpEmailService` | 2 | **anonymous** — the newsletter-subscribe path |
> | `DatabaseHealthProbe` | 1 | **anonymous** — `/healthz` is `AllowAnonymous` |
> | `BlogApp/ConnectionProbe` + `ConnectionSetup` | 4 | pre-authentication first-run screen |
> | Ten admin pages under `BlogUI` | 23 | admin only |
>
> Three of those groups were reachable **without authenticating**, which is precisely the case the
> four-service gate was never able to see. Note also that 23 of the 46 lived in `.razor` and
> `.razor.cs` files that the earlier `--include=*.cs`-only sweep never looked at.
>
> A gate that names files certifies the files, not the rule. These patterns take `source/` as their
> path so a new service, page or head is covered the day it is written.

```bash
# 1. Exception text inside a Result the caller renders
grep -rEn "Result(<[^>]*>)?\.Failure\s*\(.*\bex\.Message" source/ \
  --include=*.cs --include=*.razor 2>/dev/null | grep -v "/obj/\|/bin/"

# 2. Exception text assigned to a message a page binds into its markup
grep -rEn "\b(StatusMessage|UploadError|ErrorMessage|errorMessage|statusMessage|Message)\s*=\s*[^;]*\bex\.Message" source/ \
  --include=*.cs --include=*.razor 2>/dev/null | grep -v "/obj/\|/bin/"
```

**Both must return zero.** The rule: log the exception through `ILogger<T>` with context, then
return or assign a curated `private const string`. The correlation id `CorrelationIdMiddleware`
stamps on every event (REQ-NFR-015) is what ties a user's report back to the stack trace, so
nothing is lost by withholding the text.

The patterns target the **sinks** — a `Result` the caller renders, an assignment a page binds —
rather than the `ex.Message` token itself. That is deliberate: three uses in `source/` are correct
and must not be driven out by a blunter pattern.

| Correct use | Where | Why it stays |
|---|---|---|
| `Console.WriteLine($"FATAL ERROR: {ex.Message}")` | `BlogDb/MigrationRunner.cs` | Process boundary of a CLI. The audience is an operator reading a terminal; there is no user surface. |
| `Console.Error.WriteLine($"FATAL: …{ex.Message}")` | `TechieBlog/Program.cs` | Startup boundary. The host is already failing to start; nothing is being served. |
| `throw new InvalidOperationException($"…: {ex.Message}", ex)` | `TechieBlog/Middleware/ForwardedHeadersSetup.cs` | Configuration validation that **fails the host at startup**, before a request exists. |

One further deliberate exemption, marked by naming the caught variable `curated` rather than `ex`:
`ImagePicker` and `ManageImages` surface `curated.Message` from an `InvalidOperationException`
raised by `BlogImageService`. That message is always one of the service's own constants — a
category validation rule, or the REQ-NFR-040 storage-failure sentence — and carries no exception
text or server path. **`ex` means "an exception whose text is untrusted"; a differently-named
variable is a claim that the message was authored, and that claim must be true.**

Both patterns are also enforced as a test — `tests/unit/Ops/ExceptionDisclosureTests.cs` scans the
same tree on every build, so the rule fails CI rather than waiting for someone to run a grep.

### Severity
- **Error**: file-scoped namespace, underscore field prefix
- **Warning**: nullable, async suffix
- **Info**: consider fixing
