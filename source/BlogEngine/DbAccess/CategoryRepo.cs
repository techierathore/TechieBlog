using Dapper;
using BlogModels;

/// <summary>
/// Repository for managing category data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for Category entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class CategoryRepo : GenericRepository<Category>, ICategoryRepo
{
    public CategoryRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all categories ordered by name.
    /// </summary>
    public override IEnumerable<Category> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            ORDER BY CategoryName";
        return vConn.Query<Category>(sql).ToList();
    }

    /// <summary>
    /// Gets all categories with their post counts.
    /// </summary>
    public IEnumerable<Category> GetAllWithCounts()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT c.CategoryId, c.CategoryName, c.Slug, c.Description,
                   COUNT(p.PostID) as PostCount
            FROM Category c
            LEFT JOIN BlogPost p ON c.CategoryId = p.CategoryId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY c.CategoryId, c.CategoryName, c.Slug, c.Description
            ORDER BY c.CategoryName";
        return vConn.Query<Category>(sql).ToList();
    }

    /// <summary>
    /// Gets all categories by parent ID (not used for flat categories).
    /// </summary>
    public override IEnumerable<Category> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single category by ID.
    /// </summary>
    public override Category GetSingle(long categoryId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            WHERE CategoryId = @CategoryId";
        return vConn.Query<Category>(sql, new { CategoryId = categoryId }).FirstOrDefault();
    }

    public override Category GetIntSingle(int categoryId)
    {
        return GetSingle(categoryId);
    }

    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    public Category GetBySlug(string slug)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            WHERE Slug = @Slug";
        return vConn.Query<Category>(sql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if a slug already exists.
    /// </summary>
    public bool SlugExists(string slug, long excludeCategoryId = 0)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(1) FROM Category
            WHERE Slug = @Slug AND CategoryId != @ExcludeCategoryId";
        return vConn.ExecuteScalar<int>(sql, new { Slug = slug, ExcludeCategoryId = excludeCategoryId }) > 0;
    }

    /// <summary>
    /// Gets paginated categories.
    /// </summary>
    public override IEnumerable<Category> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            ORDER BY CategoryName
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<Category>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new category.
    /// </summary>
    public override void Insert(Category category)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Category (CategoryName, Slug, Description)
            VALUES (@CategoryName, @Slug, @Description)";
        vConn.Execute(sql, new
        {
            category.CategoryName,
            category.Slug,
            category.Description
        });
    }

    /// <summary>
    /// Inserts a category and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(Category category)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Category (CategoryName, Slug, Description)
            VALUES (@CategoryName, @Slug, @Description)
            RETURNING CategoryId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            category.CategoryName,
            category.Slug,
            category.Description
        });
    }

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    public override void Update(Category category)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE Category SET
                CategoryName = @CategoryName,
                Slug = @Slug,
                Description = @Description
            WHERE CategoryId = @CategoryId";
        vConn.Execute(sql, new
        {
            category.CategoryId,
            category.CategoryName,
            category.Slug,
            category.Description
        });
    }

    /// <summary>
    /// Deletes a category by ID.
    /// </summary>
    public void Delete(long categoryId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM Category WHERE CategoryId = @CategoryId";
        vConn.Execute(sql, new { CategoryId = categoryId });
    }
}
