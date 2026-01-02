using Dapper;
using BlogModels;
using BlogModels.Models;
using BlogModels.Interfaces;

/// <summary>
/// Repository for managing UserSkill data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserSkill entities using Dapper.</para>
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
/// </remarks>
namespace BlogEngine.DbAccess;

public class UserSkillsRepo : GenericRepository<UserSkill>, IUserSkillsRepo
{
    public UserSkillsRepo(string connectionString) : base(connectionString) { }

    /// <summary>
    /// Gets all user skills ordered by display order.
    /// </summary>
    public override IEnumerable<UserSkill> GetAll()
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn
            FROM userskills
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserSkill>(sql).ToList();
    }

    /// <summary>
    /// Gets all skills for a specific user ordered by display order.
    /// </summary>
    public override IEnumerable<UserSkill> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets all skills for a specific user.
    /// </summary>
    public IEnumerable<UserSkill> GetByUserId(long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn
            FROM userskills
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserSkill>(sql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets skills for a user filtered by category.
    /// </summary>
    public IEnumerable<UserSkill> GetByUserIdAndCategory(long userId, string category)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn
            FROM userskills
            WHERE UserId = @UserId AND Category = @Category
            ORDER BY DisplayOrder ASC";
        return vConn.Query<UserSkill>(sql, new { UserId = userId, Category = category }).ToList();
    }

    /// <summary>
    /// Gets a single skill by ID.
    /// </summary>
    public override UserSkill GetSingle(long skillId)
    {
        return GetById(skillId);
    }

    /// <summary>
    /// Gets a skill by its ID.
    /// </summary>
    public UserSkill GetById(long skillId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn
            FROM userskills
            WHERE SkillId = @SkillId";
        return vConn.Query<UserSkill>(sql, new { SkillId = skillId }).FirstOrDefault();
    }

    public override UserSkill GetIntSingle(int skillId)
    {
        return GetSingle(skillId);
    }

    /// <summary>
    /// Gets paginated skills.
    /// </summary>
    public override IEnumerable<UserSkill> GetPagedData(int pageSize, int offset)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn
            FROM userskills
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";
        return vConn.Query<UserSkill>(sql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new skill.
    /// </summary>
    public override void Insert(UserSkill skill)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userskills (UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn)
            VALUES (@UserId, @Category, @SkillName, @IconPath, @DisplayOrder, @CreatedOn)";
        vConn.Execute(sql, new
        {
            skill.UserId,
            skill.Category,
            skill.SkillName,
            skill.IconPath,
            skill.DisplayOrder,
            skill.CreatedOn
        });
    }

    /// <summary>
    /// Inserts a skill and returns the generated ID.
    /// </summary>
    public override long InsertToGetId(UserSkill skill)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            INSERT INTO userskills (UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn)
            VALUES (@UserId, @Category, @SkillName, @IconPath, @DisplayOrder, @CreatedOn)
            RETURNING SkillId";
        return vConn.ExecuteScalar<long>(sql, new
        {
            skill.UserId,
            skill.Category,
            skill.SkillName,
            skill.IconPath,
            skill.DisplayOrder,
            skill.CreatedOn
        });
    }

    /// <summary>
    /// Creates a new skill and returns its ID.
    /// </summary>
    public long Create(UserSkill skill)
    {
        return InsertToGetId(skill);
    }

    /// <summary>
    /// Updates an existing skill.
    /// </summary>
    public override void Update(UserSkill skill)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            UPDATE userskills SET
                UserId = @UserId,
                Category = @Category,
                SkillName = @SkillName,
                IconPath = @IconPath,
                DisplayOrder = @DisplayOrder
            WHERE SkillId = @SkillId";
        vConn.Execute(sql, new
        {
            skill.SkillId,
            skill.UserId,
            skill.Category,
            skill.SkillName,
            skill.IconPath,
            skill.DisplayOrder
        });
    }

    /// <summary>
    /// Deletes a skill by ID.
    /// </summary>
    public void Delete(long skillId)
    {
        using var vConn = GetOpenConnection();
        const string sql = "DELETE FROM userskills WHERE SkillId = @SkillId";
        vConn.Execute(sql, new { SkillId = skillId });
    }

    /// <summary>
    /// Gets distinct categories for a user.
    /// </summary>
    public IEnumerable<string> GetCategories(long userId)
    {
        using var vConn = GetOpenConnection();
        const string sql = @"
            SELECT DISTINCT Category
            FROM userskills
            WHERE UserId = @UserId
            ORDER BY Category";
        return vConn.Query<string>(sql, new { UserId = userId }).ToList();
    }
}
