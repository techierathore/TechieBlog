using Dapper;
using BlogModels;
using BlogModels.Models;
using BlogModels.Interfaces;

/// <summary>
/// Repository for managing UserAward data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserAward entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class UserAwardsRepo : GenericRepository<UserAward>, IUserAwardsRepo
{
    public UserAwardsRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all user awards ordered by display order.
    /// </summary>
    public override IEnumerable<UserAward> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT AwardId, UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn
            FROM userawards
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserAward>(sql).ToList();
    }

    /// <summary>
    /// Gets all awards for a specific user ordered by display order.
    /// </summary>
    public override IEnumerable<UserAward> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets all awards for a specific user.
    /// </summary>
    public IEnumerable<UserAward> GetByUserId(long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT AwardId, UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn
            FROM userawards
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserAward>(sql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets a single award by ID.
    /// </summary>
    public override UserAward GetSingle(long awardId)
    {
        return GetById(awardId);
    }

    /// <summary>
    /// Gets an award by its ID.
    /// </summary>
    public UserAward GetById(long awardId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT AwardId, UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn
            FROM userawards
            WHERE AwardId = @AwardId";
        return vConn.Query<UserAward>(sql, new { AwardId = awardId }).FirstOrDefault();
    }

    public override UserAward GetIntSingle(int awardId)
    {
        return GetSingle(awardId);
    }

    /// <summary>
    /// Gets paginated awards.
    /// </summary>
    public override IEnumerable<UserAward> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT AwardId, UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn
            FROM userawards
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<UserAward>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new award.
    /// </summary>
    public override void Insert(UserAward award)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userawards (UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn)
            VALUES (@UserId, @AwardTitle, @AwardDescription, @BadgeImagePath, @AwardUrl, @AwardYear, @DisplayOrder, @CreatedOn)";
        vConn.Execute(sql, new
        {
            award.UserId,
            award.AwardTitle,
            award.AwardDescription,
            award.BadgeImagePath,
            award.AwardUrl,
            award.AwardYear,
            award.DisplayOrder,
            award.CreatedOn
        });
    }

    /// <summary>
    /// Inserts an award and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(UserAward award)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userawards (UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn)
            VALUES (@UserId, @AwardTitle, @AwardDescription, @BadgeImagePath, @AwardUrl, @AwardYear, @DisplayOrder, @CreatedOn)
            RETURNING AwardId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            award.UserId,
            award.AwardTitle,
            award.AwardDescription,
            award.BadgeImagePath,
            award.AwardUrl,
            award.AwardYear,
            award.DisplayOrder,
            award.CreatedOn
        });
    }

    /// <summary>
    /// Creates a new award and returns its ID.
    /// </summary>
    public long Create(UserAward award)
    {
        return InsertToGetId(award);
    }

    /// <summary>
    /// Updates an existing award.
    /// </summary>
    public override void Update(UserAward award)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE userawards SET
                UserId = @UserId,
                AwardTitle = @AwardTitle,
                AwardDescription = @AwardDescription,
                BadgeImagePath = @BadgeImagePath,
                AwardUrl = @AwardUrl,
                AwardYear = @AwardYear,
                DisplayOrder = @DisplayOrder
            WHERE AwardId = @AwardId";
        vConn.Execute(sql, new
        {
            award.AwardId,
            award.UserId,
            award.AwardTitle,
            award.AwardDescription,
            award.BadgeImagePath,
            award.AwardUrl,
            award.AwardYear,
            award.DisplayOrder
        });
    }

    /// <summary>
    /// Deletes an award by ID.
    /// </summary>
    public void Delete(long awardId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM userawards WHERE AwardId = @AwardId";
        vConn.Execute(sql, new { AwardId = awardId });
    }
}
