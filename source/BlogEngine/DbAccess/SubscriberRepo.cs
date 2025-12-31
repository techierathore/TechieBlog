using Dapper;
using BlogModels;

/// <summary>
/// Repository for managing subscriber data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for Subscriber entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class SubscriberRepo : GenericRepository<Subscriber>, ISubscriberRepo
{
    public SubscriberRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all subscribers ordered by subscription date.
    /// </summary>
    public override IEnumerable<Subscriber> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   COALESCE(IsConfirmed, TRUE) as IsActive
            FROM Subscriber
            ORDER BY SubscribedOn DESC";
        return vConn.Query<Subscriber>(sql).ToList();
    }

    /// <summary>
    /// Gets all subscribers by parent ID (not applicable).
    /// </summary>
    public override IEnumerable<Subscriber> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single subscriber by ID.
    /// </summary>
    public override Subscriber GetSingle(long subscriberId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   COALESCE(IsConfirmed, TRUE) as IsActive
            FROM Subscriber
            WHERE SubscriberId = @SubscriberId";
        return vConn.Query<Subscriber>(sql, new { SubscriberId = subscriberId }).FirstOrDefault();
    }

    public override Subscriber GetIntSingle(int subscriberId)
    {
        return GetSingle(subscriberId);
    }

    /// <summary>
    /// Gets a subscriber by email address.
    /// </summary>
    public Subscriber GetByEmail(string email)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   COALESCE(IsConfirmed, TRUE) as IsActive
            FROM Subscriber
            WHERE LOWER(Email) = LOWER(@Email)";
        return vConn.Query<Subscriber>(sql, new { Email = email }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if an email already exists.
    /// </summary>
    public bool EmailExists(string email)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT COUNT(1) FROM Subscriber
            WHERE LOWER(Email) = LOWER(@Email)";
        return vConn.ExecuteScalar<int>(sql, new { Email = email }) > 0;
    }

    /// <summary>
    /// Gets all active (confirmed) subscribers.
    /// </summary>
    public IEnumerable<Subscriber> GetActiveSubscribers()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   TRUE as IsActive
            FROM Subscriber
            WHERE IsConfirmed = TRUE
            ORDER BY SubscribedOn DESC";
        return vConn.Query<Subscriber>(sql).ToList();
    }

    /// <summary>
    /// Gets subscribers by active status.
    /// </summary>
    public IEnumerable<Subscriber> GetByStatus(bool isActive)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            WHERE IsConfirmed = @IsActive
            ORDER BY SubscribedOn DESC";
        return vConn.Query<Subscriber>(sql, new { IsActive = isActive }).ToList();
    }

    /// <summary>
    /// Searches subscribers by email.
    /// </summary>
    public IEnumerable<Subscriber> SearchByEmail(string query)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            WHERE Email ILIKE @Query
            ORDER BY SubscribedOn DESC
            LIMIT 50";
        return vConn.Query<Subscriber>(sql, new { Query = $"%{query}%" }).ToList();
    }

    /// <summary>
    /// Gets paginated subscribers.
    /// </summary>
    public override IEnumerable<Subscriber> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            ORDER BY SubscribedOn DESC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<Subscriber>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new subscriber.
    /// </summary>
    public override void Insert(Subscriber subscriber)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Subscriber (Email, Name, SubscribedOn, IsConfirmed, Preferences)
            VALUES (@Email, @Name, @SubscribedOn, @IsConfirmed, @Preferences)";
        vConn.Execute(sql, new
        {
            subscriber.Email,
            subscriber.Name,
            subscriber.SubscribedOn,
            subscriber.IsConfirmed,
            subscriber.Preferences
        });
    }

    /// <summary>
    /// Inserts a subscriber and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(Subscriber subscriber)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO Subscriber (Email, Name, SubscribedOn, IsConfirmed, Preferences)
            VALUES (@Email, @Name, @SubscribedOn, @IsConfirmed, @Preferences)
            RETURNING SubscriberId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            subscriber.Email,
            subscriber.Name,
            subscriber.SubscribedOn,
            subscriber.IsConfirmed,
            subscriber.Preferences
        });
    }

    /// <summary>
    /// Updates an existing subscriber.
    /// </summary>
    public override void Update(Subscriber subscriber)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE Subscriber SET
                Email = @Email,
                Name = @Name,
                IsConfirmed = @IsConfirmed,
                Preferences = @Preferences
            WHERE SubscriberId = @SubscriberId";
        vConn.Execute(sql, new
        {
            subscriber.SubscriberId,
            subscriber.Email,
            subscriber.Name,
            subscriber.IsConfirmed,
            subscriber.Preferences
        });
    }

    /// <summary>
    /// Updates subscriber active status.
    /// </summary>
    public void UpdateStatus(long subscriberId, bool isActive)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE Subscriber SET IsConfirmed = @IsActive
            WHERE SubscriberId = @SubscriberId";
        vConn.Execute(sql, new { SubscriberId = subscriberId, IsActive = isActive });
    }

    /// <summary>
    /// Gets total subscriber count.
    /// </summary>
    public int GetTotalCount()
    {
        using var vConn = GetOpenConnection();
        return vConn.ExecuteScalar<int>("SELECT COUNT(*) FROM Subscriber");
    }

    /// <summary>
    /// Gets active subscriber count.
    /// </summary>
    public int GetActiveCount()
    {
        using var vConn = GetOpenConnection();
        return vConn.ExecuteScalar<int>("SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed = TRUE");
    }
}
