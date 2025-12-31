# 9. Coding Standards and Conventions

### 9.1 Naming Conventions (MANDATORY)

**CRITICAL RULE: No underscores (`_`) in any identifier names.** Use PascalCase or camelCase exclusively.

#### 9.1.1 C# Code Naming

| Element | Convention | Correct | Incorrect |
|---------|------------|---------|-----------|
| **Classes** | PascalCase | `BlogPostService` | `Blog_Post_Service` |
| **Interfaces** | PascalCase with `I` prefix | `IBlogPostRepo` | `I_Blog_Post_Repo` |
| **Methods** | PascalCase | `GetAllPosts()` | `Get_All_Posts()` |
| **Properties** | PascalCase | `PostTitle` | `Post_Title` |
| **Local Variables** | camelCase | `blogPost` | `blog_post` |
| **Parameters** | camelCase | `postId` | `post_id` |
| **Private Fields** | camelCase (no underscore prefix) | `connectionString` | `_connectionString` |
| **Constants** | PascalCase | `MaxPageSize` | `MAX_PAGE_SIZE` |
| **Enums** | PascalCase | `TokenStatus.ValidToken` | `Token_Status.Valid_Token` |

#### 9.1.2 Database Object Naming

| Element | Convention | Correct | Incorrect |
|---------|------------|---------|-----------|
| **Tables** | PascalCase | `BlogPost`, `UserFavorite` | `blog_post`, `user_favorite` |
| **Columns** | PascalCase | `PostId`, `CreatedOn` | `post_id`, `created_on` |
| **Stored Procedures/Functions** | PascalCase | `GetPostById`, `InsertBlogPost` | `get_post_by_id`, `sp_InsertBlogPost` |
| **Indexes** | PascalCase with `Idx` prefix | `IdxPostSlug` | `idx_post_slug` |
| **Foreign Keys** | PascalCase with `Fk` prefix | `FkPostUserId` | `fk_post_user_id` |
| **Primary Keys** | PascalCase with `Pk` prefix | `PkBlogPost` | `pk_blog_post` |

#### 9.1.3 File and Folder Naming

| Element | Convention | Correct | Incorrect |
|---------|------------|---------|-----------|
| **C# Files** | PascalCase | `BlogPostRepo.cs` | `blog_post_repo.cs` |
| **Razor Files** | PascalCase | `PostEditor.razor` | `post_editor.razor` |
| **CSS Files** | kebab-case | `fluent-modern.css` | `fluent_modern.css` |
| **SQL Scripts** | Number prefix + PascalCase | `001-CreateTables.sql` | `001_create_tables.sql` |
| **Folders** | PascalCase | `BlogPages`, `DbAccess` | `Blog_Pages`, `Db_Access` |

### 9.2 Data Access Standards (Dapper ORM)

**MANDATORY: Continue using Dapper as the micro-ORM for all data access.**

#### 9.2.1 Repository Pattern with Dapper

```csharp
/// <summary>
/// Repository for managing blog post data access operations.
/// Implements the generic repository pattern using Dapper for PostgreSQL.
/// Used by BlogSvc to perform CRUD operations on the Post table.
/// </summary>
public class BlogPostRepo : GenericRepository<BlogPost>, IBlogPostRepo
{
    /// <summary>
    /// Initializes a new instance of BlogPostRepo with database connection.
    /// The connection string is injected via DI from appsettings.json.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from configuration.</param>
    public BlogPostRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Retrieves a single blog post by its unique identifier.
    /// Calls the GetPostById stored function in PostgreSQL.
    /// Returns null if no post exists with the given ID.
    /// </summary>
    /// <param name="postId">The unique identifier of the blog post.</param>
    /// <returns>BlogPost entity or null if not found.</returns>
    public override BlogPost GetSingle(long postId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("postId", postId);
        return connection.Query<BlogPost>(
            "SELECT * FROM GetPostById(@postId)",
            parameters
        ).FirstOrDefault();
    }
}
```

#### 9.2.2 Dapper Best Practices

| Practice | Implementation |
|----------|----------------|
| **Connection Management** | Use `using` statements for automatic disposal |
| **Parameters** | Always use `DynamicParameters` - never concatenate SQL |
| **Stored Functions** | Prefer PostgreSQL functions over inline SQL |
| **Async Operations** | Use `QueryAsync`, `ExecuteAsync` for all DB calls |
| **Result Mapping** | Leverage Dapper's automatic mapping to POCOs |

### 9.3 XML Documentation Standards (MANDATORY)

**CRITICAL: Every class and method MUST have XML documentation comments.**

#### 9.3.1 Class Documentation Template

```csharp
/// <summary>
/// [Brief one-line description of what this class does]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [Explain the role of this class in the solution]</para>
/// <para><b>Code Flow:</b> [Describe how this class fits into the overall flow]</para>
/// <para><b>Dependencies:</b> [List key dependencies and why they're needed]</para>
/// <para><b>Usage:</b> [Explain where/how this class is used]</para>
/// </remarks>
/// <example>
/// <code>
/// // Example usage of this class
/// var service = new ExampleService(dependency);
/// var result = service.DoSomething();
/// </code>
/// </example>
public class ExampleClass
{
    // ...
}
```

#### 9.3.2 Method Documentation Template

```csharp
/// <summary>
/// [Brief one-line description of what this method does]
/// </summary>
/// <remarks>
/// <para><b>Business Logic:</b> [Explain any business rules applied]</para>
/// <para><b>Flow:</b> [Step-by-step description for complex methods]</para>
/// <para><b>Side Effects:</b> [Describe any state changes or external calls]</para>
/// </remarks>
/// <param name="paramName">[Description of parameter and valid values]</param>
/// <returns>[Description of return value and possible states]</returns>
/// <exception cref="ExceptionType">[When this exception is thrown]</exception>
/// <example>
/// <code>
/// var result = MyMethod("input");
/// </code>
/// </example>
public ReturnType MethodName(ParamType paramName)
{
    // ...
}
```

#### 9.3.3 Documentation Examples for TechieBlog

**Service Class Example:**

```csharp
/// <summary>
/// Provides authentication and authorization services for the TechieBlog application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Handles user login, signup, JWT token generation, and token validation.
/// This is the central authentication service used by both the UI layer and any future API endpoints.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>UI calls AppLogin() with encrypted credentials</item>
///   <item>Credentials are decrypted using AppEncrypt utility</item>
///   <item>Password is hashed and validated against BlogUserRepo</item>
///   <item>On success, JWT token is generated with user claims</item>
///   <item>Token is stored in UserLogin table for tracking</item>
///   <item>Encrypted user data and token returned to UI</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>IBlogUserRepo - User data access</item>
///   <item>IUserLoginRepository - Token tracking</item>
///   <item>AppEncrypt - Credential encryption/decryption</item>
/// </list>
///
/// <para><b>Security Note:</b> All sensitive data is encrypted in transit using AppEncrypt.
/// Passwords are hashed using SHA256 before database comparison.</para>
/// </remarks>
public class AuthSvc
{
    /// <summary>
    /// Authenticates a user with email and password credentials.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Decrypt email and password from SvcData wrapper</item>
    ///   <item>Hash the password using AppEncrypt.CreateHash()</item>
    ///   <item>Query BlogUserRepo for matching credentials</item>
    ///   <item>Generate 15-day JWT token with user claims (ID, Name, Email, Role)</item>
    ///   <item>Store login record in UserLogin table</item>
    ///   <item>Return encrypted user data with tokens</item>
    /// </list>
    ///
    /// <para><b>Token Claims:</b> PrimarySid (UserId), Name, Email, Role</para>
    /// </remarks>
    /// <param name="loginData">Encrypted login credentials containing LoginEmail and LoginPass.</param>
    /// <returns>
    /// SvcData containing encrypted user profile and JWT token on success.
    /// Returns null if credentials are invalid or user not found.
    /// </returns>
    /// <exception cref="Exception">Logged and rethrown on database or encryption errors.</exception>
    public SvcData AppLogin(SvcData loginData)
    {
        // Implementation...
    }
}
```

**Repository Class Example:**

```csharp
/// <summary>
/// Data access repository for BlogPost entities using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for the Post table in PostgreSQL.
/// Extends GenericRepository to inherit common data access patterns.</para>
///
/// <para><b>Code Flow:</b> Called by BlogSvc service layer. Each method opens a new
/// database connection, executes a stored function, and returns mapped entities.</para>
///
/// <para><b>Database Objects Used:</b></para>
/// <list type="bullet">
///   <item>GetPostById - Retrieve single post</item>
///   <item>GetPagedBlogList - Paginated post listing</item>
///   <item>InsertPost - Create new post</item>
///   <item>UpdatePost - Modify existing post</item>
/// </list>
/// </remarks>
public class BlogPostRepo : GenericRepository<BlogPost>, IBlogPostRepo
{
    // ...
}
```

### 9.4 Database Script Documentation Standards (MANDATORY)

**CRITICAL: All SQL scripts must include detailed comments explaining purpose and logic.**

#### 9.4.1 Table Creation Script Template

```sql
-- ============================================================================
-- Script: 001-CreateTables.sql
-- Purpose: Creates all core tables for TechieBlog PostgreSQL database
-- Author: [Developer Name]
-- Created: [Date]
-- Modified: [Date] - [Description of changes]
-- ============================================================================

-- ============================================================================
-- TABLE: BlogPost
-- Purpose: Stores all blog post content and metadata
--
-- Relationships:
--   - BlogUser (UserId) - Author of the post
--   - Category (via PostCategory junction) - Post categorization
--   - Series (SeriesId) - Optional series grouping
--
-- Business Rules:
--   - Slug must be unique for SEO-friendly URLs
--   - Published = false indicates draft status
--   - ScheduledFor enables future publishing
--
-- Indexes:
--   - PkBlogPost: Primary key on PostId
--   - IdxPostSlug: Unique index for URL lookups
--   - IdxPostUserId: Foreign key index for author queries
-- ============================================================================
CREATE TABLE BlogPost (
    -- Primary identifier, auto-generated
    PostId BIGSERIAL PRIMARY KEY,

    -- Post title displayed in UI and used for SEO
    Title VARCHAR(550) NOT NULL,

    -- URL-friendly identifier, auto-generated from title
    -- Must be unique across all posts for clean URLs
    Slug VARCHAR(550) UNIQUE,

    -- Short summary shown in post listings and meta description
    Abstract VARCHAR(550),

    -- Full post content in Markdown format
    -- Rendered to HTML on display
    PostContent TEXT NOT NULL,

    -- Timestamp when post was first created (draft or published)
    CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    -- Timestamp of last modification
    UpdatedOn TIMESTAMP,

    -- Foreign key to BlogUser - the post author
    -- Required - every post must have an author
    UserId BIGINT NOT NULL REFERENCES BlogUser(UserId),

    -- Comma-separated tag names for quick display
    -- Normalized tags stored in PostTag junction table
    Tags VARCHAR(550),

    -- Path to featured/hero image for post
    FeaturedImage VARCHAR(550),

    -- Publication status: false = draft, true = published
    Published BOOLEAN NOT NULL DEFAULT FALSE,

    -- Future publish date for scheduled posts
    -- NULL means immediate publish when Published = true
    ScheduledFor TIMESTAMP,

    -- SEO: Custom title for search engines (overrides Title if set)
    SeoTitle VARCHAR(255),

    -- SEO: Meta description for search results
    SeoDescription VARCHAR(500),

    -- Optional series grouping for multi-part content
    SeriesId BIGINT REFERENCES Series(SeriesId),

    -- Order within series (1, 2, 3, etc.)
    SeriesOrder INT
);

-- Index for fast slug lookups (used in URL routing)
CREATE UNIQUE INDEX IdxPostSlug ON BlogPost(Slug);

-- Index for author's post queries
CREATE INDEX IdxPostUserId ON BlogPost(UserId);

-- Index for published posts sorted by date (common query pattern)
CREATE INDEX IdxPostPublished ON BlogPost(Published, CreatedOn DESC);
```

#### 9.4.2 Stored Function Documentation Template

```sql
-- ============================================================================
-- FUNCTION: GetPostById
-- Purpose: Retrieves a single blog post with author information
--
-- Parameters:
--   @postId (BIGINT) - The unique identifier of the post to retrieve
--
-- Returns: Single row with post data and author name, or empty if not found
--
-- Business Logic:
--   1. Joins BlogPost with BlogUser to get author's full name
--   2. Returns all post fields needed for display
--   3. Does NOT check Published status - caller must filter if needed
--
-- Called By:
--   - BlogPostRepo.GetSingle() - Single post retrieval
--   - BlogSvc.GetPostForEdit() - Admin post editing
--
-- Performance Notes:
--   - Uses primary key lookup - O(1) performance
--   - Consider caching results for frequently accessed posts
--
-- Example Usage:
--   SELECT * FROM GetPostById(123);
-- ============================================================================
CREATE OR REPLACE FUNCTION GetPostById(postId BIGINT)
RETURNS TABLE (
    PostId BIGINT,
    Title VARCHAR(550),
    Slug VARCHAR(550),
    Abstract VARCHAR(550),
    PostContent TEXT,
    CreatedOn TIMESTAMP,
    UpdatedOn TIMESTAMP,
    UserId BIGINT,
    BlogWriter VARCHAR(201),  -- FirstName + ' ' + LastName
    Tags VARCHAR(550),
    FeaturedImage VARCHAR(550),
    Published BOOLEAN,
    ScheduledFor TIMESTAMP
) AS $$
BEGIN
    -- Return post with joined author name
    -- BlogWriter is computed from user's first and last name
    RETURN QUERY
    SELECT
        p.PostId,
        p.Title,
        p.Slug,
        p.Abstract,
        p.PostContent,
        p.CreatedOn,
        p.UpdatedOn,
        p.UserId,
        CONCAT(u.FirstName, ' ', u.LastName)::VARCHAR(201) AS BlogWriter,
        p.Tags,
        p.FeaturedImage,
        p.Published,
        p.ScheduledFor
    FROM BlogPost p
    INNER JOIN BlogUser u ON p.UserId = u.UserId
    WHERE p.PostId = postId;
END;
$$ LANGUAGE plpgsql;
```

#### 9.4.3 Migration Script Header Template

```sql
-- ============================================================================
-- Migration: 005-AddRatingSystem.sql
-- Purpose: Adds star rating functionality for blog posts
--
-- Changes:
--   - Creates PostRating table for user ratings
--   - Adds GetPostAverageRating function
--   - Adds InsertOrUpdateRating function
--
-- Dependencies:
--   - Requires BlogPost table (001-CreateTables.sql)
--   - Requires BlogUser table (001-CreateTables.sql)
--
-- Rollback Script: 005-Rollback-AddRatingSystem.sql
--
-- Author: [Developer Name]
-- Date: [Date]
-- Ticket: [Story/Issue Reference]
-- ============================================================================
```

### 9.5 Additional Coding Standards

#### 9.5.1 General C# Standards

| Standard | Implementation |
|----------|----------------|
| **Nullable Reference Types** | Enable `<Nullable>enable</Nullable>` in all projects |
| **Async/Await** | All database and I/O operations must be async |
| **Dependency Injection** | Constructor injection only, no service locator pattern |
| **Fluent UI Components** | Use Fluent UI components exclusively, no mixing with Blazorise |
| **CSS Variables** | All colors, fonts, spacing via CSS custom properties only |

#### 9.5.2 Error Handling Standards

```csharp
/// <summary>
/// Standard error handling pattern for service methods.
/// </summary>
/// <remarks>
/// All exceptions are logged with full context before rethrowing.
/// Never swallow exceptions silently - always log or handle explicitly.
/// </remarks>
public async Task<Result<T>> ServiceMethodAsync()
{
    try
    {
        // Business logic here
        return Result<T>.Success(data);
    }
    catch (Exception ex)
    {
        // Log with structured data for debugging
        logger.LogError(ex,
            "Failed to execute {Method} for {EntityId}. Context: {Context}",
            nameof(ServiceMethodAsync), entityId, additionalContext);

        // Rethrow or return failure result
        return Result<T>.Failure(ex.Message);
    }
}
```

### 9.6 Critical Integration Rules

| Rule | Implementation |
|------|----------------|
| **Existing API Compatibility** | N/A - BlogSvc removed, service interfaces updated |
| **Database Integration** | All queries via Dapper, stored functions preferred |
| **Error Handling** | Use Result pattern or exceptions with logging |
| **Logging Consistency** | Serilog with structured logging, correlation IDs |

---
