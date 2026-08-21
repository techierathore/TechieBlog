using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserSkill data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserSkill entities using Dapper. Skills are the
/// "Skills &amp; Expertise" block of <c>/resume</c>, grouped by category, and the rows maintained at
/// <c>/admin/skills</c>.</para>
///
/// <para><b>Code Flow:</b> A page injects <see cref="IUserSkillsRepo"/>, calls an <c>…Async</c>
/// member, and the member routes through the protected helpers on <c>GenericRepository</c>, which
/// open the connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL, <see cref="DbTimestamp"/>.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage. Both twins execute the
/// same SQL constant, so they cannot drift apart.</para>
///
/// <para><b>Timestamp binding (REQ-NFR-026, trap 1):</b> <c>UserSkills.CreatedOn</c> is declared
/// <c>TIMESTAMP</c> without time zone while callers supply <c>DateTime.UtcNow</c>, whose <c>Kind</c>
/// is <c>Utc</c>. <see cref="DbTimestamp.AsTimestamp(DateTime)"/> drops the Kind so Npgsql sends
/// <c>timestamp</c> and the instant is stored exactly as supplied.</para>
/// </remarks>
public class UserSkillsRepo : GenericRepository<UserSkill>, IUserSkillsRepo
{
    private const string SkillColumns = "SkillId, UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn";

    private const string SelectAllSql = @"
            SELECT " + SkillColumns + @"
            FROM userskills
            ORDER BY DisplayOrder ASC";

    private const string SelectByUserIdSql = @"
            SELECT " + SkillColumns + @"
            FROM userskills
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";

    private const string SelectByUserIdAndCategorySql = @"
            SELECT " + SkillColumns + @"
            FROM userskills
            WHERE UserId = @UserId AND Category = @Category
            ORDER BY DisplayOrder ASC";

    private const string SelectByIdSql = @"
            SELECT " + SkillColumns + @"
            FROM userskills
            WHERE SkillId = @SkillId";

    private const string SelectPagedSql = @"
            SELECT " + SkillColumns + @"
            FROM userskills
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";

    private const string SelectCategoriesSql = @"
            SELECT DISTINCT Category
            FROM userskills
            WHERE UserId = @UserId
            ORDER BY Category";

    private const string InsertSql = @"
            INSERT INTO userskills (UserId, Category, SkillName, IconPath, DisplayOrder, CreatedOn)
            VALUES (@UserId, @Category, @SkillName, @IconPath, @DisplayOrder, @CreatedOn)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING SkillId";

    private const string UpdateSql = @"
            UPDATE userskills SET
                UserId = @UserId,
                Category = @Category,
                SkillName = @SkillName,
                IconPath = @IconPath,
                DisplayOrder = @DisplayOrder
            WHERE SkillId = @SkillId";

    private const string DeleteSql = "DELETE FROM userskills WHERE SkillId = @SkillId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserSkillsRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every skill in the table, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Display order is the sequence the resume renders within a
    /// category, so it is applied in SQL rather than left to each caller.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All skills, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<UserSkill>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserSkill>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every skill belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic parent-key member and the named member are the same
    /// query for this entity — a skill's only parent is its user.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByUserIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's skills, or an empty sequence when they have none.</returns>
    public override Task<IEnumerable<UserSkill>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Gets every skill belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The resume groups the returned rows by category in memory rather
    /// than issuing one query per category, so this single read serves the whole section.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's skills, or an empty sequence when they have none.</returns>
    public async Task<IEnumerable<UserSkill>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserSkill>(
            SelectByUserIdSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a user's skills within one category, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category matching is exact, so a caller must pass the stored
    /// spelling — there is no lookup table normalising it.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user and
    /// category → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="category">The category name to filter on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching skills, or an empty sequence when none match.</returns>
    public async Task<IEnumerable<UserSkill>> GetByUserIdAndCategoryAsync(long userId, string category, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserSkill>(
            SelectByUserIdAndCategorySql,
            new { UserId = userId, Category = category },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single skill by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>,
    /// which is how the admin screen reports "this row has already been deleted".</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The skill, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserSkill?> GetSingleAsync(long skillId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(skillId, cancellationToken);
    }

    /// <summary>
    /// Gets a single skill by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The skill, or <c>null</c> when no row carries that key.</returns>
    public async Task<UserSkill?> GetByIdAsync(long skillId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserSkill>(
            SelectByIdSql, new { SkillId = skillId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single skill by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The skill, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserSkill?> GetIntSingleAsync(int skillId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(skillId, cancellationToken);
    }

    /// <summary>
    /// Gets a page of skills, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long skill list never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<UserSkill>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserSkill>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the distinct category names a user has skills in, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Categories are free text on the skill row, so the set of known
    /// categories has to be derived from the data; the admin form offers them as suggestions.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → DISTINCT query → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The distinct category names in alphabetical order.</returns>
    public async Task<IEnumerable<string>> GetCategoriesAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<string>(
            SelectCategoriesSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new skill, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserSkills</c>.</para>
    /// </remarks>
    /// <param name="skill">The skill to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(UserSkill skill, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(skill), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a skill and returns the generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> normalise the timestamp → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserSkills</c>.</para>
    /// </remarks>
    /// <param name="skill">The skill to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>SkillId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(UserSkill skill, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(skill), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new skill and returns its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The name the admin screen uses; the behaviour is exactly the
    /// RETURNING insert, so it forwards rather than duplicating the SQL.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserSkills</c>.</para>
    /// </remarks>
    /// <param name="skill">The skill to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>SkillId</c>.</returns>
    public Task<long> CreateAsync(UserSkill skill, CancellationToken cancellationToken = default)
    {
        return InsertToGetIdAsync(skill, cancellationToken);
    }

    /// <summary>
    /// Updates an existing skill, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is deliberately not written — an edit must not
    /// restamp when the skill was recorded.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>SkillId</c>.</para>
    /// </remarks>
    /// <param name="skill">The skill carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(UserSkill skill, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(skill), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a skill by identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown identifier affects no rows and is treated as a
    /// no-op rather than an error, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>UserSkills</c>.</para>
    /// </remarks>
    /// <param name="skillId">The skill identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long skillId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { SkillId = skillId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every skill in the table, in display order.
    /// </summary>
    /// <returns>All skills.</returns>
    public override IEnumerable<UserSkill> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserSkill>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every skill belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's skills.</returns>
    public override IEnumerable<UserSkill> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets every skill belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's skills.</returns>
    public IEnumerable<UserSkill> GetByUserId(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserSkill>(SelectByUserIdSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets a user's skills within one category, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="category">The category name to filter on.</param>
    /// <returns>The matching skills.</returns>
    public IEnumerable<UserSkill> GetByUserIdAndCategory(long userId, string category)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserSkill>(
            SelectByUserIdAndCategorySql, new { UserId = userId, Category = category }).ToList();
    }

    /// <summary>
    /// Gets a single skill by its identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns>The skill, or <c>null</c> when not found.</returns>
    public override UserSkill? GetSingle(long skillId)
    {
        return GetById(skillId);
    }

    /// <summary>
    /// Gets a single skill by its identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns>The skill, or <c>null</c> when not found.</returns>
    public UserSkill? GetById(long skillId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserSkill>(SelectByIdSql, new { SkillId = skillId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single skill by INT identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    /// <returns>The skill, or <c>null</c> when not found.</returns>
    public override UserSkill? GetIntSingle(int skillId)
    {
        return GetById(skillId);
    }

    /// <summary>
    /// Gets a page of skills.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<UserSkill> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserSkill>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the distinct category names a user has skills in.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The distinct category names in alphabetical order.</returns>
    public IEnumerable<string> GetCategories(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<string>(SelectCategoriesSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Inserts a new skill.
    /// </summary>
    /// <param name="skill">The skill to persist.</param>
    public override void Insert(UserSkill skill)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(skill));
    }

    /// <summary>
    /// Inserts a skill and returns the generated identifier.
    /// </summary>
    /// <param name="skill">The skill to persist.</param>
    /// <returns>The generated <c>SkillId</c>.</returns>
    public override long InsertToGetId(UserSkill skill)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(skill));
    }

    /// <summary>
    /// Creates a new skill and returns its identifier.
    /// </summary>
    /// <param name="skill">The skill to persist.</param>
    /// <returns>The generated <c>SkillId</c>.</returns>
    public long Create(UserSkill skill)
    {
        return InsertToGetId(skill);
    }

    /// <summary>
    /// Updates an existing skill.
    /// </summary>
    /// <param name="skill">The skill carrying the new values.</param>
    public override void Update(UserSkill skill)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(skill));
    }

    /// <summary>
    /// Deletes a skill by identifier.
    /// </summary>
    /// <param name="skillId">The skill identifier.</param>
    public void Delete(long skillId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { SkillId = skillId });
    }

    // =================================================================================================
    // Parameter binding shared by both twins.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter object both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is normalised through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> because the column is <c>TIMESTAMP</c> without
    /// time zone; a <c>Kind = Utc</c> value would otherwise be sent as <c>timestamptz</c> and shifted
    /// into the session time zone on the way in.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="skill">The skill being persisted.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildInsertParameters(UserSkill skill)
    {
        return new
        {
            skill.UserId,
            skill.Category,
            skill.SkillName,
            skill.IconPath,
            skill.DisplayOrder,
            CreatedOn = DbTimestamp.AsTimestamp(skill.CreatedOn)
        };
    }

    /// <summary>
    /// Builds the parameter object the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is absent by design — an edit must not restamp
    /// when the skill was first recorded.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="skill">The skill being updated.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildUpdateParameters(UserSkill skill)
    {
        return new
        {
            skill.SkillId,
            skill.UserId,
            skill.Category,
            skill.SkillName,
            skill.IconPath,
            skill.DisplayOrder
        };
    }
}
