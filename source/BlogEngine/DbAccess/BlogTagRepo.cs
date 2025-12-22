using Dapper;
using BlogModels;

/// <summary>
/// Repository for managing tag data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogTag entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class BlogTagRepo : GenericRepository<BlogTag>, IBlogTagRepo
{
    public BlogTagRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all tags ordered by name.
    /// </summary>
    public override IEnumerable<BlogTag> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            ORDER BY TagName";
        return vConn.Query<BlogTag>(sql).ToList();
    }

    /// <summary>
    /// Gets all tags with their post counts.
    /// </summary>
    public IEnumerable<BlogTag> GetAllWithCounts()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT t.TagId, t.TagName, t.Slug,
                   COUNT(pt.PostId) as PostCount
            FROM Tag t
            LEFT JOIN PostTag pt ON t.TagId = pt.TagId
            LEFT JOIN BlogPost p ON pt.PostId = p.PostId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY t.TagId, t.TagName, t.Slug
            ORDER BY t.TagName";
        return vConn.Query<BlogTag>(sql).ToList();
    }

    /// <summary>
    /// Gets all tags by parent ID (not used for flat tags).
    /// </summary>
    public override IEnumerable<BlogTag> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single tag by ID.
    /// </summary>
    public override BlogTag GetSingle(long tagId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE TagId = @TagId";
        return vConn.Query<BlogTag>(sql, new { TagId = tagId }).FirstOrDefault();
    }

    public override BlogTag GetIntSingle(int tagId)
    {
        return GetSingle(tagId);
    }

    /// <summary>
    /// Gets a tag by its URL slug.
    /// </summary>
    public BlogTag GetBySlug(string slug)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE Slug = @Slug";
        return vConn.Query<BlogTag>(sql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if a slug already exists.
    /// </summary>
    public bool SlugExists(string slug, long excludeTagId = 0)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(1) FROM Tag
            WHERE Slug = @Slug AND TagId != @ExcludeTagId";
        return vConn.ExecuteScalar<int>(sql, new { Slug = slug, ExcludeTagId = excludeTagId }) > 0;
    }

    /// <summary>
    /// Searches tags by name for autocomplete.
    /// </summary>
    public IEnumerable<BlogTag> SearchTags(string query)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE TagName ILIKE @Query
            ORDER BY TagName
            LIMIT 10";
        return vConn.Query<BlogTag>(sql, new { Query = $"%{query}%" }).ToList();
    }

    /// <summary>
    /// Gets paginated tags.
    /// </summary>
    public override IEnumerable<BlogTag> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            ORDER BY TagName
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogTag>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new tag.
    /// </summary>
    public override void Insert(BlogTag tag)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Tag (TagName, Slug)
            VALUES (@TagName, @Slug)";
        vConn.Execute(sql, new
        {
            tag.TagName,
            tag.Slug
        });
    }

    /// <summary>
    /// Inserts a tag and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(BlogTag tag)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Tag (TagName, Slug)
            VALUES (@TagName, @Slug)
            RETURNING TagId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            tag.TagName,
            tag.Slug
        });
    }

    /// <summary>
    /// Updates an existing tag.
    /// </summary>
    public override void Update(BlogTag tag)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE Tag SET
                TagName = @TagName,
                Slug = @Slug
            WHERE TagId = @TagId";
        vConn.Execute(sql, new
        {
            tag.TagId,
            tag.TagName,
            tag.Slug
        });
    }

    /// <summary>
    /// Deletes a tag by ID.
    /// </summary>
    public void Delete(long tagId)
    {
        using var vConn = GetOpenConnection();
        // First delete junction table entries
        vConn.Execute("DELETE FROM PostTag WHERE TagId = @TagId", new { TagId = tagId });
        // Then delete the tag
        vConn.Execute("DELETE FROM Tag WHERE TagId = @TagId", new { TagId = tagId });
    }

    /// <summary>
    /// Gets tags for a specific post.
    /// </summary>
    public IEnumerable<BlogTag> GetTagsForPost(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT t.TagId, t.TagName, t.Slug
            FROM Tag t
            INNER JOIN PostTag pt ON t.TagId = pt.TagId
            WHERE pt.PostId = @PostId
            ORDER BY t.TagName";
        return vConn.Query<BlogTag>(sql, new { PostId = postId }).ToList();
    }

    /// <summary>
    /// Sets tags for a post (replaces existing).
    /// </summary>
    public void SetTagsForPost(long postId, IEnumerable<long> tagIds)
    {
        using var vConn = GetOpenConnection();
        using var transaction = vConn.BeginTransaction();
        try
        {
            // Remove existing tags
            vConn.Execute("DELETE FROM PostTag WHERE PostId = @PostId",
                new { PostId = postId }, transaction);

            // Insert new tags
            if (tagIds != null && tagIds.Any())
            {
                const string insertSql = "INSERT INTO PostTag (PostId, TagId) VALUES (@PostId, @TagId)";
                foreach (var tagId in tagIds)
                {
                    vConn.Execute(insertSql, new { PostId = postId, TagId = tagId }, transaction);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Gets posts by tag ID.
    /// </summary>
    public IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            INNER JOIN PostTag pt ON p.PostID = pt.PostId
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE pt.TagId = @TagId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogPost>(sql, new { TagId = tagId, PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets count of posts with a specific tag.
    /// </summary>
    public int GetPostCountByTag(long tagId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM BlogPost p
            INNER JOIN PostTag pt ON p.PostID = pt.PostId
            WHERE pt.TagId = @TagId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)";
        return vConn.ExecuteScalar<int>(sql, new { TagId = tagId });
    }
}
