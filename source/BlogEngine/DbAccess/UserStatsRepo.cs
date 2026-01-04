using Dapper;
using BlogModels;
using BlogModels.Models;
using BlogModels.Interfaces;

/// <summary>
/// Repository for managing UserStat data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserStat entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class UserStatsRepo : GenericRepository<UserStat>, IUserStatsRepo
{
    public UserStatsRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all user stats ordered by display order.
    /// </summary>
    public override IEnumerable<UserStat> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder
            FROM userstats
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserStat>(sql).ToList();
    }

    /// <summary>
    /// Gets all stats for a specific user ordered by display order.
    /// </summary>
    public override IEnumerable<UserStat> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets all stats for a specific user.
    /// </summary>
    public IEnumerable<UserStat> GetByUserId(long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder
            FROM userstats
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserStat>(sql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets stats for a user filtered by category.
    /// </summary>
    public IEnumerable<UserStat> GetByUserIdAndCategory(long userId, string category)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder
            FROM userstats
            WHERE UserId = @UserId AND StatCategory = @Category
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserStat>(sql, new { UserId = userId, Category = category }).ToList();
    }

    /// <summary>
    /// Gets a single stat by ID.
    /// </summary>
    public override UserStat GetSingle(long statId)
    {
        return GetById(statId);
    }

    /// <summary>
    /// Gets a stat by its ID.
    /// </summary>
    public UserStat GetById(long statId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder
            FROM userstats
            WHERE StatId = @StatId";
        return vConn.Query<UserStat>(sql, new { StatId = statId }).FirstOrDefault();
    }

    public override UserStat GetIntSingle(int statId)
    {
        return GetSingle(statId);
    }

    /// <summary>
    /// Gets paginated stats.
    /// </summary>
    public override IEnumerable<UserStat> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder
            FROM userstats
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<UserStat>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new stat.
    /// </summary>
    public override void Insert(UserStat stat)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userstats (UserId, StatLabel, StatValue, StatCategory, DisplayOrder)
            VALUES (@UserId, @StatLabel, @StatValue, @StatCategory, @DisplayOrder)";
        vConn.Execute(sql, new
        {
            stat.UserId,
            stat.StatLabel,
            stat.StatValue,
            stat.StatCategory,
            stat.DisplayOrder
        });
    }

    /// <summary>
    /// Inserts a stat and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(UserStat stat)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userstats (UserId, StatLabel, StatValue, StatCategory, DisplayOrder)
            VALUES (@UserId, @StatLabel, @StatValue, @StatCategory, @DisplayOrder)
            RETURNING StatId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            stat.UserId,
            stat.StatLabel,
            stat.StatValue,
            stat.StatCategory,
            stat.DisplayOrder
        });
    }

    /// <summary>
    /// Creates a new stat and returns its ID.
    /// </summary>
    public long Create(UserStat stat)
    {
        return InsertToGetId(stat);
    }

    /// <summary>
    /// Updates an existing stat.
    /// </summary>
    public override void Update(UserStat stat)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE userstats SET
                UserId = @UserId,
                StatLabel = @StatLabel,
                StatValue = @StatValue,
                StatCategory = @StatCategory,
                DisplayOrder = @DisplayOrder
            WHERE StatId = @StatId";
        vConn.Execute(sql, new
        {
            stat.StatId,
            stat.UserId,
            stat.StatLabel,
            stat.StatValue,
            stat.StatCategory,
            stat.DisplayOrder
        });
    }

    /// <summary>
    /// Deletes a stat by ID.
    /// </summary>
    public void Delete(long statId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM userstats WHERE StatId = @StatId";
        vConn.Execute(sql, new { StatId = statId });
    }
}
