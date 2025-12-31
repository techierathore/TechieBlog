using BlogModels.Interfaces;
using System.Data;

namespace BlogEngine.DaCore;

/// <summary>
/// Base repository class implementing generic data access patterns with Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a base implementation of IGenericRepository for all
/// entity repositories. Uses Dapper for PostgreSQL data access operations.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Derived repositories inherit from this class</item>
///   <item>Connection string injected via constructor</item>
///   <item>GetOpenConnection() provides database connections via factory</item>
///   <item>Abstract methods implemented by derived classes for entity-specific operations</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>Dapper - Micro-ORM for data access</item>
///   <item>DbConnectionFactory - Connection creation</item>
///   <item>Npgsql - PostgreSQL driver (via factory)</item>
/// </list>
///
/// <para><b>Usage:</b> Inherit from this class and implement abstract methods
/// for each entity type (e.g., BlogPostRepo, BlogUserRepo).</para>
/// </remarks>
/// <typeparam name="TEntity">The entity type this repository manages.</typeparam>
/// <example>
/// <code>
/// public class BlogPostRepo : GenericRepository&lt;BlogPost&gt;, IBlogPostRepo
/// {
///     public BlogPostRepo(string connectionString) : base(connectionString) { }
///
///     public override BlogPost GetSingle(long postId)
///     {
///         using var connection = GetOpenConnection();
///         return connection.QueryFirstOrDefault&lt;BlogPost&gt;(
///             "SELECT * FROM PostSelect(@pPostId)",
///             new { pPostId = postId });
///     }
/// }
/// </code>
/// </example>
public abstract class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class
{
    private readonly string connectionString;
    private readonly EDbConnectionTypes dbType;

    /// <summary>
    /// Initializes a new instance of GenericRepository with PostgreSQL connection.
    /// </summary>
    /// <remarks>
    /// The database type is set to PostgreSQL by default. Connection string is stored
    /// for creating connections on demand.
    /// </remarks>
    /// <param name="connectionString">PostgreSQL connection string from configuration.</param>
    public GenericRepository(string connectionString)
    {
        dbType = EDbConnectionTypes.PostgreSql;
        this.connectionString = connectionString;
    }

    /// <summary>
    /// Creates and returns an open database connection.
    /// </summary>
    /// <remarks>
    /// <para><b>Important:</b> Callers are responsible for disposing the connection.
    /// Always use with a using statement or dispose manually.</para>
    /// </remarks>
    /// <returns>An open IDbConnection to the PostgreSQL database.</returns>
    /// <example>
    /// <code>
    /// using var connection = GetOpenConnection();
    /// var results = connection.Query&lt;Entity&gt;("SELECT * FROM Entity");
    /// </code>
    /// </example>
    public IDbConnection GetOpenConnection()
    {
        return DbConnectionFactory.GetDbConnection(dbType, connectionString);
    }

    /// <summary>
    /// Retrieves all entities of type TEntity.
    /// </summary>
    /// <returns>Collection of all entities.</returns>
    public abstract IEnumerable<TEntity> GetAll();

    /// <summary>
    /// Retrieves all entities associated with the specified ID.
    /// </summary>
    /// <param name="aSingleId">The parent/foreign key ID to filter by.</param>
    /// <returns>Collection of matching entities.</returns>
    public abstract IEnumerable<TEntity> GetAllById(long aSingleId);

    /// <summary>
    /// Retrieves a paginated subset of entities.
    /// </summary>
    /// <param name="pageSize">Number of entities per page.</param>
    /// <param name="offSet">Number of entities to skip.</param>
    /// <returns>Paginated collection of entities.</returns>
    public abstract IEnumerable<TEntity> GetPagedData(int pageSize, int offSet);

    /// <summary>
    /// Retrieves a single entity by its primary key (BIGINT).
    /// </summary>
    /// <param name="aSingleId">The entity's unique identifier.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    public abstract TEntity GetSingle(long aSingleId);

    /// <summary>
    /// Retrieves a single entity by its primary key (INT).
    /// </summary>
    /// <param name="aSingleId">The entity's unique identifier.</param>
    /// <returns>The entity if found, null otherwise.</returns>
    public abstract TEntity GetIntSingle(int aSingleId);

    /// <summary>
    /// Inserts a new entity into the database.
    /// </summary>
    /// <param name="aEntity">The entity to insert.</param>
    public abstract void Insert(TEntity aEntity);

    /// <summary>
    /// Inserts a new entity and returns its generated ID.
    /// </summary>
    /// <param name="entity">The entity to insert.</param>
    /// <returns>The generated primary key ID.</returns>
    public abstract long InsertToGetId(TEntity entity);

    /// <summary>
    /// Updates an existing entity in the database.
    /// </summary>
    /// <param name="aEntityToUpdate">The entity with updated values.</param>
    public abstract void Update(TEntity aEntityToUpdate);
}
