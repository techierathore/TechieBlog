using Dapper;
using BlogModels;
using BlogEngine.DaCore;

/// <summary>
/// Repository for managing post rating data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for PostRating entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// <para><b>Story:</b> FIX-013 - Star Ratings Implementation (Epic 4, FR15-16)</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class PostRatingRepo : GenericRepository<PostRating>, IPostRatingRepo
{
    public PostRatingRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all ratings ordered by creation date.
    /// </summary>
    public override IEnumerable<PostRating> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT RatingId, PostId, UserId, Rating, CreatedOn, UpdatedOn
            FROM PostRating
            ORDER BY CreatedOn DESC";
        return vConn.Query<PostRating>(sql).ToList();
    }

    /// <summary>
    /// Gets all ratings for a specific post.
    /// </summary>
    public override IEnumerable<PostRating> GetAllById(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT RatingId, PostId, UserId, Rating, CreatedOn, UpdatedOn
            FROM PostRating
            WHERE PostId = @PostId
            ORDER BY CreatedOn DESC";
        return vConn.Query<PostRating>(sql, new { PostId = postId }).ToList();
    }

    /// <summary>
    /// Gets a single rating by ID.
    /// </summary>
    public override PostRating GetSingle(long ratingId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT RatingId, PostId, UserId, Rating, CreatedOn, UpdatedOn
            FROM PostRating
            WHERE RatingId = @RatingId";
        return vConn.Query<PostRating>(sql, new { RatingId = ratingId }).FirstOrDefault();
    }

    public override PostRating GetIntSingle(int ratingId)
    {
        return GetSingle(ratingId);
    }

    /// <summary>
    /// Gets a user's rating for a specific post.
    /// </summary>
    public PostRating GetByPostAndUser(long postId, long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT RatingId, PostId, UserId, Rating, CreatedOn, UpdatedOn
            FROM PostRating
            WHERE PostId = @PostId AND UserId = @UserId";
        return vConn.Query<PostRating>(sql, new { PostId = postId, UserId = userId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets the average rating for a post.
    /// </summary>
    public double GetAverageByPost(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COALESCE(AVG(Rating::DECIMAL), 0)
            FROM PostRating
            WHERE PostId = @PostId";
        return vConn.ExecuteScalar<double>(sql, new { PostId = postId });
    }

    /// <summary>
    /// Gets the total number of ratings for a post.
    /// </summary>
    public int GetCountByPost(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(*)
            FROM PostRating
            WHERE PostId = @PostId";
        return vConn.ExecuteScalar<int>(sql, new { PostId = postId });
    }

    /// <summary>
    /// Gets rating statistics for a post.
    /// </summary>
    public PostRatingStats GetStatsByPost(long postId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT 
                COALESCE(AVG(Rating::DECIMAL), 0) AS AverageRating,
                COUNT(*) AS RatingCount
            FROM PostRating
            WHERE PostId = @PostId";
        return vConn.Query<PostRatingStats>(sql, new { PostId = postId }).FirstOrDefault() 
            ?? new PostRatingStats { AverageRating = 0, RatingCount = 0 };
    }

    /// <summary>
    /// Gets top-rated posts for popular content lists.
    /// </summary>
    public IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT PostId
            FROM PostRating
            GROUP BY PostId
            HAVING COUNT(*) >= @MinRatings
            ORDER BY AVG(Rating) DESC, COUNT(*) DESC
            LIMIT @Count";
        return vConn.Query<long>(sql, new { Count = count, MinRatings = minRatings }).ToList();
    }

    /// <summary>
    /// Gets paginated ratings.
    /// </summary>
    public override IEnumerable<PostRating> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT RatingId, PostId, UserId, Rating, CreatedOn, UpdatedOn
            FROM PostRating
            ORDER BY CreatedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<PostRating>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new rating.
    /// </summary>
    public override void Insert(PostRating rating)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn)
            VALUES (@PostId, @UserId, @Rating, @CreatedOn)";
        vConn.Execute(sql, new
        {
            rating.PostId,
            rating.UserId,
            rating.Rating,
            rating.CreatedOn
        });
    }

    /// <summary>
    /// Inserts a rating and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(PostRating rating)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO PostRating (PostId, UserId, Rating, CreatedOn)
            VALUES (@PostId, @UserId, @Rating, @CreatedOn)
            RETURNING RatingId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            rating.PostId,
            rating.UserId,
            rating.Rating,
            rating.CreatedOn
        });
    }

    /// <summary>
    /// Updates an existing rating.
    /// </summary>
    public override void Update(PostRating rating)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE PostRating SET
                Rating = @Rating,
                UpdatedOn = @UpdatedOn
            WHERE RatingId = @RatingId";
        vConn.Execute(sql, new
        {
            rating.RatingId,
            rating.Rating,
            rating.UpdatedOn
        });
    }

    /// <summary>
    /// Deletes a rating by ID.
    /// </summary>
    public void Delete(long ratingId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM PostRating WHERE RatingId = @RatingId";
        vConn.Execute(sql, new { RatingId = ratingId });
    }
}
