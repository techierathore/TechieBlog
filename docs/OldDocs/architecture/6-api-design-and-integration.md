# 6. API Design and Integration

### 6.1 API Integration Strategy

**Strategy:** Remove REST API layer entirely. BlogUI components call BlogEngine services directly via dependency injection.

| Aspect | Approach |
|--------|----------|
| **Authentication** | JWT tokens validated via Blazor AuthenticationStateProvider |
| **Authorization** | [Authorize] attributes on pages, role-based policies |
| **Versioning** | N/A - internal service calls, no external API |

### 6.2 Service Interface Patterns

All services follow this pattern for direct UI integration:

```csharp
public interface IBlogService
{
    // Async methods for UI responsiveness
    Task<BlogPost> GetPostByIdAsync(long postId);
    Task<BlogPost> GetPostBySlugAsync(string slug);
    Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int page, int pageSize);
    Task<long> CreatePostAsync(BlogPost post);
    Task UpdatePostAsync(BlogPost post);
    Task DeletePostAsync(long postId);

    // Sync methods where async provides no benefit
    int GetTotalPostCount();
}
```

### 6.3 Removed API Endpoints

The following BlogSvc controllers are being removed:

| Controller | Endpoints | Replacement |
|------------|-----------|-------------|
| `AuthSvc` | POST /auth/login, /auth/signup | Direct `AuthSvc` service calls |
| `BlogSvc` | CRUD /posts/* | Direct `BlogSvc` service calls |
| `TagSvc` | CRUD /tags/* | Direct `TagSvc` service calls |

---
