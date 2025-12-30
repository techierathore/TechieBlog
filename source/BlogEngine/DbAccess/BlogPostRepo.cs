/// <summary>
/// Repository for managing blog post data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogPost entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL stored procedures.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class BlogPostRepo : GenericRepository<BlogPost>, IBlogPostRepo
{
    public BlogPostRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all blog posts (admin view).
    /// </summary>
    public override IEnumerable<BlogPost> GetAll()
    {
        using var vConn = GetOpenConnection();
        // Use inline SQL to filter out deleted posts
        const string sql = @"
            SELECT PostID, Title, Slug, Abstract, PostContent, CreatedOn, UpdatedOn,
                   UserID, Tags, CategoryId, FeaturedImage, Published, IsDeleted, DeletedOn,
                   SeriesId, SeriesPartNumber
            FROM BlogPost
            WHERE IsDeleted = FALSE OR IsDeleted IS NULL
            ORDER BY CreatedOn DESC";
        return vConn.Query<BlogPost>(sql).ToList();
    }

    /// <summary>
    /// Gets all posts by a specific user (author view).
    /// </summary>
    public override IEnumerable<BlogPost> GetAllById(long aUserID)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT PostID, Title, Slug, Abstract, PostContent, CreatedOn, UpdatedOn,
                   UserID, Tags, CategoryId, FeaturedImage, Published, IsDeleted, DeletedOn,
                   SeriesId, SeriesPartNumber
            FROM BlogPost
            WHERE UserID = @UserID AND (IsDeleted = FALSE OR IsDeleted IS NULL)
            ORDER BY CreatedOn DESC";
        return vConn.Query<BlogPost>(sql, new { UserID = aUserID }).ToList();
    }

    public override BlogPost GetIntSingle(int aSingleId)
    {
        return GetSingle((long)aSingleId);
    }

    /// <summary>
    /// Gets paginated list of all posts.
    /// </summary>
    public override IEnumerable<BlogPost> GetPagedData(int PageSize, int OffSet)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT PostID, Title, Slug, Abstract, PostContent, CreatedOn, UpdatedOn,
                   UserID, Tags, CategoryId, FeaturedImage, Published, IsDeleted
            FROM BlogPost
            WHERE IsDeleted = FALSE OR IsDeleted IS NULL
            ORDER BY CreatedOn DESC
            LIMIT @PageSize OFFSET @OffSet";
        return vConn.Query<BlogPost>(sql, new { PageSize, OffSet }).ToList();
    }

    /// <summary>
    /// Gets dashboard count statistics.
    /// </summary>
    public BlogPost GetTheCounts()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) as BlogCount
            FROM BlogPost
            WHERE IsDeleted = FALSE OR IsDeleted IS NULL";
        return vConn.Query<BlogPost>(sql).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single post by ID.
    /// </summary>
    public override BlogPost GetSingle(long aSingleId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter,
                   s.Name as SeriesName, s.Slug as SeriesSlug
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            LEFT JOIN BlogSeries s ON p.SeriesId = s.SeriesId
            WHERE p.PostID = @PostID";
        return vConn.Query<BlogPost>(sql, new { PostID = aSingleId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a post by its URL slug.
    /// </summary>
    public BlogPost GetBySlug(string slug)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter,
                   s.Name as SeriesName, s.Slug as SeriesSlug
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            LEFT JOIN BlogSeries s ON p.SeriesId = s.SeriesId
            WHERE p.Slug = @Slug AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)";
        return vConn.Query<BlogPost>(sql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Gets published posts for public display.
    /// </summary>
    public IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogPost>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Checks if a slug already exists.
    /// </summary>
    public bool SlugExists(string slug, long excludePostId = 0)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(1) FROM BlogPost
            WHERE Slug = @Slug AND PostID != @ExcludePostId";
        return vConn.ExecuteScalar<int>(sql, new { Slug = slug, ExcludePostId = excludePostId }) > 0;
    }

    /// <summary>
    /// Inserts a new blog post.
    /// </summary>
    public override void Insert(BlogPost aPost)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO BlogPost (Title, Slug, Abstract, PostContent, UserID, Tags, FeaturedImage, CreatedOn, Published, PublishedOn, ScheduledPublishOn, IsDeleted, SeriesId, SeriesPartNumber)
            VALUES (@Title, @Slug, @Abstract, @PostContent, @UserID, @Tags, @FeaturedImage, @CreatedOn, @Published, @PublishedOn, @ScheduledPublishOn, FALSE, @SeriesId, @SeriesPartNumber)";
        vConn.Execute(sql, new
        {
            aPost.Title,
            aPost.Slug,
            aPost.Abstract,
            aPost.PostContent,
            aPost.UserID,
            aPost.Tags,
            aPost.FeaturedImage,
            aPost.CreatedOn,
            aPost.Published,
            aPost.PublishedOn,
            aPost.ScheduledPublishOn,
            aPost.SeriesId,
            aPost.SeriesPartNumber
        });
    }

    /// <summary>
    /// Inserts a post and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(BlogPost aPost)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO BlogPost (Title, Slug, Abstract, PostContent, UserID, Tags, FeaturedImage, CreatedOn, Published, PublishedOn, ScheduledPublishOn, IsDeleted, SeriesId, SeriesPartNumber)
            VALUES (@Title, @Slug, @Abstract, @PostContent, @UserID, @Tags, @FeaturedImage, @CreatedOn, @Published, @PublishedOn, @ScheduledPublishOn, FALSE, @SeriesId, @SeriesPartNumber)
            RETURNING PostID";
        return vConn.ExecuteScalar<long>(sql, new
        {
            aPost.Title,
            aPost.Slug,
            aPost.Abstract,
            aPost.PostContent,
            aPost.UserID,
            aPost.Tags,
            aPost.FeaturedImage,
            aPost.CreatedOn,
            aPost.Published,
            aPost.PublishedOn,
            aPost.ScheduledPublishOn,
            aPost.SeriesId,
            aPost.SeriesPartNumber
        });
    }

    /// <summary>
    /// Updates an existing blog post.
    /// </summary>
    public override void Update(BlogPost aPost)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE BlogPost SET
                Title = @Title,
                Slug = @Slug,
                Abstract = @Abstract,
                PostContent = @PostContent,
                Tags = @Tags,
                FeaturedImage = @FeaturedImage,
                UpdatedOn = @UpdatedOn,
                Published = @Published,
                PublishedOn = @PublishedOn,
                ScheduledPublishOn = @ScheduledPublishOn,
                SeriesId = @SeriesId,
                SeriesPartNumber = @SeriesPartNumber
            WHERE PostID = @PostID";
        vConn.Execute(sql, new
        {
            aPost.PostID,
            aPost.Title,
            aPost.Slug,
            aPost.Abstract,
            aPost.PostContent,
            aPost.Tags,
            aPost.FeaturedImage,
            aPost.UpdatedOn,
            aPost.Published,
            aPost.PublishedOn,
            aPost.ScheduledPublishOn,
            aPost.SeriesId,
            aPost.SeriesPartNumber
        });
    }

    /// <summary>
    /// Soft deletes a post by setting IsDeleted flag.
    /// </summary>
    public void SoftDelete(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE BlogPost SET
                IsDeleted = TRUE,
                DeletedOn = @DeletedOn
            WHERE PostID = @PostID";
        vConn.Execute(sql, new { PostID = postId, DeletedOn = DateTime.UtcNow });
    }

    /// <summary>
    /// Gets the most recent published post (featured post).
    /// </summary>
    public BlogPost GetFeaturedPost()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.CreatedOn DESC
            LIMIT 1";
        return vConn.Query<BlogPost>(sql).FirstOrDefault();
    }

    /// <summary>
    /// Gets the total count of published posts.
    /// </summary>
    public int GetPublishedPostCount()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL)";
        return vConn.ExecuteScalar<int>(sql);
    }

    /// <summary>
    /// Gets published posts filtered by category ID.
    /// </summary>
    public IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.CategoryId = @CategoryId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogPost>(sql, new { CategoryId = categoryId, PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the count of published posts in a category.
    /// </summary>
    public int GetPostCountByCategory(long categoryId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE CategoryId = @CategoryId
                AND Published = TRUE
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";
        return vConn.ExecuteScalar<int>(sql, new { CategoryId = categoryId });
    }

    /// <summary>
    /// Gets all scheduled posts for admin view.
    /// </summary>
    public IEnumerable<BlogPost> GetScheduledPosts()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn, p.ScheduledPublishOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = FALSE
                AND p.ScheduledPublishOn IS NOT NULL
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.ScheduledPublishOn ASC";
        return vConn.Query<BlogPost>(sql).ToList();
    }

    /// <summary>
    /// Gets posts that are due for publishing (scheduled time has passed).
    /// </summary>
    public IEnumerable<BlogPost> GetDueScheduledPosts(DateTime now)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn, p.ScheduledPublishOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = FALSE
                AND p.ScheduledPublishOn IS NOT NULL
                AND p.ScheduledPublishOn <= @Now
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.ScheduledPublishOn ASC";
        return vConn.Query<BlogPost>(sql, new { Now = now }).ToList();
    }

    /// <summary>
    /// Gets posts belonging to a series, ordered by part number.
    /// </summary>
    public IEnumerable<BlogPost> GetPostsBySeries(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.SeriesId = @SeriesId
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.SeriesPartNumber ASC";
        return vConn.Query<BlogPost>(sql, new { SeriesId = seriesId }).ToList();
    }

    /// <summary>
    /// Gets count of posts in a series.
    /// </summary>
    public int GetPostCountBySeries(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE SeriesId = @SeriesId
                AND Published = TRUE
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";
        return vConn.ExecuteScalar<int>(sql, new { SeriesId = seriesId });
    }

    /// <summary>
    /// Gets the highest part number in a series.
    /// </summary>
    public int GetMaxPartNumberInSeries(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COALESCE(MAX(SeriesPartNumber), 0) FROM BlogPost
            WHERE SeriesId = @SeriesId
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";
        return vConn.ExecuteScalar<int>(sql, new { SeriesId = seriesId });
    }

    /// <summary>
    /// Clears series association from all posts in a series.
    /// </summary>
    public void ClearSeriesFromPosts(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE BlogPost SET
                SeriesId = NULL,
                SeriesPartNumber = NULL,
                UpdatedOn = @UpdatedOn
            WHERE SeriesId = @SeriesId";
        vConn.Execute(sql, new { SeriesId = seriesId, UpdatedOn = DateTime.UtcNow });
    }

    /// <summary>
    /// Searches posts by title, abstract, and content using PostgreSQL ILIKE.
    /// </summary>
    public IEnumerable<BlogPost> SearchPosts(string query, int pageSize = 10, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<BlogPost>();
        
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE 
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
                AND (p.Title ILIKE @Query 
                    OR p.Abstract ILIKE @Query 
                    OR p.PostContent ILIKE @Query
                    OR p.Tags ILIKE @Query)
            ORDER BY p.CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogPost>(sql, new { Query = $"%{query}%", PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the count of search results.
    /// </summary>
    public int GetSearchResultCount(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;
        
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE Published = TRUE 
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)
                AND (Title ILIKE @Query 
                    OR Abstract ILIKE @Query 
                    OR PostContent ILIKE @Query
                    OR Tags ILIKE @Query)";
        return vConn.ExecuteScalar<int>(sql, new { Query = $"%{query}%" });
    }
}
