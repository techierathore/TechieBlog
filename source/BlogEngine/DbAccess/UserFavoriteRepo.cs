using Dapper;
using BlogModels;
using BlogEngine.DaCore;

/// <summary>
/// Repository for managing user favorite/bookmark data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserFavorite entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// <para><b>Story:</b> FIX-014 - Favorites/Bookmarks Implementation (Epic 4, FR17)</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class UserFavoriteRepo : GenericRepository<UserFavorite>, IUserFavoriteRepo
{
    public UserFavoriteRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all favorites (not typically used - use GetByUser instead).
    /// </summary>
    public override IEnumerable<UserFavorite> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT FavoriteId, PostId, UserId, CreatedOn
            FROM UserFavorite
            ORDER BY CreatedOn DESC";
        return vConn.Query<UserFavorite>(sql).ToList();
    }

    /// <summary>
    /// Gets all favorites by user ID.
    /// </summary>
    public override IEnumerable<UserFavorite> GetAllById(long userId)
    {
        return GetByUser(userId);
    }

    /// <summary>
    /// Gets a single favorite by ID.
    /// </summary>
    public override UserFavorite GetSingle(long favoriteId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT FavoriteId, PostId, UserId, CreatedOn
            FROM UserFavorite
            WHERE FavoriteId = @FavoriteId";
        return vConn.Query<UserFavorite>(sql, new { FavoriteId = favoriteId }).FirstOrDefault();
    }

    public override UserFavorite GetIntSingle(int favoriteId)
    {
        return GetSingle(favoriteId);
    }

    /// <summary>
    /// Gets a favorite by post and user IDs.
    /// </summary>
    public UserFavorite GetByPostAndUser(long postId, long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT FavoriteId, PostId, UserId, CreatedOn
            FROM UserFavorite
            WHERE PostId = @PostId AND UserId = @UserId";
        return vConn.Query<UserFavorite>(sql, new { PostId = postId, UserId = userId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets all favorites for a user with post details.
    /// </summary>
    public IEnumerable<UserFavorite> GetByUser(long userId, int limit = 50, int offset = 0)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT 
                uf.FavoriteId, uf.PostId, uf.UserId, uf.CreatedOn,
                p.PostId, p.Title, p.Slug, p.Abstract, p.Tags, p.FeaturedImage,
                p.CreatedOn, p.Published, p.PublishedOn,
                u.FirstName || ' ' || u.LastName AS BlogWriter
            FROM UserFavorite uf
            INNER JOIN BlogPost p ON uf.PostId = p.PostId
            LEFT JOIN BlogUser u ON p.UserId = u.UserId
            WHERE uf.UserId = @UserId
              AND p.Published = TRUE
              AND COALESCE(p.IsDeleted, FALSE) = FALSE
            ORDER BY uf.CreatedOn DESC
            LIMIT @Limit OFFSET @Offset";
        
        return vConn.Query<UserFavorite, BlogPost, UserFavorite>(
            sql,
            (favorite, post) =>
            {
                favorite.Post = post;
                return favorite;
            },
            new { UserId = userId, Limit = limit, Offset = offset },
            splitOn: "PostId"
        ).ToList();
    }

    /// <summary>
    /// Gets the count of favorites for a post.
    /// </summary>
    public int GetCountByPost(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM UserFavorite
            WHERE PostId = @PostId";
        return vConn.ExecuteScalar<int>(sql, new { PostId = postId });
    }

    /// <summary>
    /// Gets the count of favorites for a user.
    /// </summary>
    public int GetCountByUser(long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*) FROM UserFavorite uf
            INNER JOIN BlogPost p ON uf.PostId = p.PostId
            WHERE uf.UserId = @UserId
              AND p.Published = TRUE
              AND COALESCE(p.IsDeleted, FALSE) = FALSE";
        return vConn.ExecuteScalar<int>(sql, new { UserId = userId });
    }

    /// <summary>
    /// Gets paginated favorites.
    /// </summary>
    public override IEnumerable<UserFavorite> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT FavoriteId, PostId, UserId, CreatedOn
            FROM UserFavorite
            ORDER BY CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<UserFavorite>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new favorite.
    /// </summary>
    public override void Insert(UserFavorite favorite)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO UserFavorite (PostId, UserId, CreatedOn)
            VALUES (@PostId, @UserId, @CreatedOn)
            ON CONFLICT (PostId, UserId) DO NOTHING";
        vConn.Execute(sql, new
        {
            favorite.PostId,
            favorite.UserId,
            favorite.CreatedOn
        });
    }

    /// <summary>
    /// Inserts a favorite and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(UserFavorite favorite)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO UserFavorite (PostId, UserId, CreatedOn)
            VALUES (@PostId, @UserId, @CreatedOn)
            ON CONFLICT (PostId, UserId) DO UPDATE SET CreatedOn = @CreatedOn
            RETURNING FavoriteId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            favorite.PostId,
            favorite.UserId,
            favorite.CreatedOn
        });
    }

    /// <summary>
    /// Updates an existing favorite (not typically used).
    /// </summary>
    public override void Update(UserFavorite favorite)
    {
        // Favorites don't need updates - they're either created or deleted
    }

    /// <summary>
    /// Deletes a favorite by post and user IDs.
    /// </summary>
    public void Delete(long postId, long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            DELETE FROM UserFavorite
            WHERE PostId = @PostId AND UserId = @UserId";
        vConn.Execute(sql, new { PostId = postId, UserId = userId });
    }
}
