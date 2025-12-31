# 4. Data Models and Schema Changes

### 4.1 Existing Data Models (Preserved)

The following models exist in `BlogModel/Models/` and will be preserved with minor updates:

| Model | Purpose | Migration Notes |
|-------|---------|-----------------|
| `AppUser` | User accounts with roles, profile | Add nullable annotations |
| `BlogPost` | Blog post content | Add SEO fields, slug |
| `BlogComment` | Post comments | No changes |
| `BlogTag` | Tag definitions | No changes |
| `BlogImage` | Media library items | No changes |
| `UserLogin` | JWT token tracking | No changes |
| `LoginLog` | Authentication audit | No changes |
| `UserEvent` | User activity events | No changes |
| `SvcData` | Service data wrapper | Evaluate for removal |
| `SvcToken` | Service tokens | No changes |
| `Widget` | Custom widgets | Evaluate for removal per PRD |

### 4.2 New Data Models Required

#### PostRating Model

```csharp
public class PostRating
{
    public long RatingId { get; set; }
    public long PostId { get; set; }
    public long UserId { get; set; }
    public int Rating { get; set; }  // 1-5 stars
    public DateTime RatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}
```

**Purpose:** Track user ratings on posts (1-5 stars, one per user per post)
**Integration:** Links Post and BlogUser tables

#### UserFavorite Model

```csharp
public class UserFavorite
{
    public long FavoriteId { get; set; }
    public long UserId { get; set; }
    public long PostId { get; set; }
    public DateTime FavoritedOn { get; set; }
}
```

**Purpose:** Track user bookmarked/favorite posts
**Integration:** Links BlogUser and Post tables

#### Series Model

```csharp
public class Series
{
    public long SeriesId { get; set; }
    public string Name { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
```

**Purpose:** Group related posts into ordered series
**Integration:** New table with PostSeries junction table

#### SiteSettings Model

```csharp
public class SiteSettings
{
    public int SettingsId { get; set; }
    public string SiteTitle { get; set; }
    public string Tagline { get; set; }
    public string ActiveTheme { get; set; }
    public int PostsPerPage { get; set; }
    public bool RequireCommentModeration { get; set; }
    public string SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public string SmtpUser { get; set; }
    public string SmtpPasswordEncrypted { get; set; }
    public DateTime UpdatedOn { get; set; }
}
```

**Purpose:** Centralized site configuration
**Integration:** Replaces UserSettings (which was user-specific)

### 4.3 Schema Integration Strategy

**Database Changes Required:**

| Change Type | Details |
|-------------|---------|
| **New Tables** | PostRating, UserFavorite, Series, PostSeries, SiteSettings |
| **Modified Tables** | Post (add Slug, ScheduledFor, SeriesId), BlogUser (add PasswordResetToken, ResetTokenExpiry) |
| **New Indexes** | Post.Slug (unique), PostRating (PostId, UserId unique), UserFavorite (UserId, PostId unique) |
| **Migration Strategy** | DbUp scripts in BlogDb/PostgresScripts/, numbered sequentially |

**Backward Compatibility:**

- All existing table structures preserved with PostgreSQL type mappings
- MySQL `BIGINT` → PostgreSQL `BIGINT`
- MySQL `TINYINT(1)` → PostgreSQL `BOOLEAN`
- MySQL `LONGTEXT` → PostgreSQL `TEXT`
- MySQL `ENUM` → PostgreSQL `VARCHAR` with CHECK constraint
- MySQL `BIT(1)` → PostgreSQL `BOOLEAN`

### 4.4 PostgreSQL Type Mappings

| MySQL Type | PostgreSQL Type | Notes |
|------------|-----------------|-------|
| `BIGINT AUTO_INCREMENT` | `BIGSERIAL` | Primary keys |
| `INT AUTO_INCREMENT` | `SERIAL` | Secondary keys |
| `VARCHAR(n)` | `VARCHAR(n)` | Direct mapping |
| `LONGTEXT` | `TEXT` | Unlimited length |
| `DATETIME` | `TIMESTAMP` | Timezone-aware option available |
| `TINYINT(1)` | `BOOLEAN` | True boolean type |
| `BIT(1)` | `BOOLEAN` | True boolean type |
| `ENUM(...)` | `VARCHAR` + CHECK | Or custom ENUM type |

---
