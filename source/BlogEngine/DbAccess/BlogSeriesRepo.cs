using Dapper;
using BlogModels;

/// <summary>
/// Repository for managing blog series data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogSeries entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class BlogSeriesRepo : GenericRepository<BlogSeries>, IBlogSeriesRepo
{
    public BlogSeriesRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all series ordered by name.
    /// </summary>
    public override IEnumerable<BlogSeries> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            ORDER BY s.Name";
        return vConn.Query<BlogSeries>(sql).ToList();
    }

    /// <summary>
    /// Gets all series with their post counts.
    /// </summary>
    public IEnumerable<BlogSeries> GetAllWithCounts()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName,
                   COUNT(p.PostID) as PostCount
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            LEFT JOIN BlogPost p ON s.SeriesId = p.SeriesId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                     s.AuthorId, s.CreatedOn, s.UpdatedOn, u.FirstName, u.LastName
            ORDER BY s.Name";
        return vConn.Query<BlogSeries>(sql).ToList();
    }

    /// <summary>
    /// Gets all series by author ID.
    /// </summary>
    public override IEnumerable<BlogSeries> GetAllById(long authorId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            WHERE s.AuthorId = @AuthorId
            ORDER BY s.Name";
        return vConn.Query<BlogSeries>(sql, new { AuthorId = authorId }).ToList();
    }

    /// <summary>
    /// Gets a single series by ID.
    /// </summary>
    public override BlogSeries GetSingle(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            WHERE s.SeriesId = @SeriesId";
        return vConn.Query<BlogSeries>(sql, new { SeriesId = seriesId }).FirstOrDefault();
    }

    public override BlogSeries GetIntSingle(int seriesId)
    {
        return GetSingle(seriesId);
    }

    /// <summary>
    /// Gets a series by its URL slug.
    /// </summary>
    public BlogSeries GetBySlug(string slug)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            WHERE s.Slug = @Slug";
        return vConn.Query<BlogSeries>(sql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if a slug already exists.
    /// </summary>
    public bool SlugExists(string slug, long excludeSeriesId = 0)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(1) FROM BlogSeries
            WHERE Slug = @Slug AND SeriesId != @ExcludeSeriesId";
        return vConn.ExecuteScalar<int>(sql, new { Slug = slug, ExcludeSeriesId = excludeSeriesId }) > 0;
    }

    /// <summary>
    /// Gets paginated series.
    /// </summary>
    public override IEnumerable<BlogSeries> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            ORDER BY s.Name
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<BlogSeries>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new series.
    /// </summary>
    public override void Insert(BlogSeries series)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO BlogSeries (Name, Slug, Description, Status, AuthorId, CreatedOn, UpdatedOn)
            VALUES (@Name, @Slug, @Description, @Status, @AuthorId, @CreatedOn, @UpdatedOn)";
        vConn.Execute(sql, new
        {
            series.Name,
            series.Slug,
            series.Description,
            series.Status,
            series.AuthorId,
            series.CreatedOn,
            series.UpdatedOn
        });
    }

    /// <summary>
    /// Inserts a series and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(BlogSeries series)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO BlogSeries (Name, Slug, Description, Status, AuthorId, CreatedOn, UpdatedOn)
            VALUES (@Name, @Slug, @Description, @Status, @AuthorId, @CreatedOn, @UpdatedOn)
            RETURNING SeriesId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            series.Name,
            series.Slug,
            series.Description,
            series.Status,
            series.AuthorId,
            series.CreatedOn,
            series.UpdatedOn
        });
    }

    /// <summary>
    /// Updates an existing series.
    /// </summary>
    public override void Update(BlogSeries series)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE BlogSeries SET
                Name = @Name,
                Slug = @Slug,
                Description = @Description,
                Status = @Status,
                UpdatedOn = @UpdatedOn
            WHERE SeriesId = @SeriesId";
        vConn.Execute(sql, new
        {
            series.SeriesId,
            series.Name,
            series.Slug,
            series.Description,
            series.Status,
            series.UpdatedOn
        });
    }

    /// <summary>
    /// Deletes a series by ID.
    /// </summary>
    public void Delete(long seriesId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM BlogSeries WHERE SeriesId = @SeriesId";
        vConn.Execute(sql, new { SeriesId = seriesId });
    }
}
