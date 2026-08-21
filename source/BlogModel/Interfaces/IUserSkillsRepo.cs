using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for the skill rows that make up the resume's capability section.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>UserSkill</c>. The generic CRUD surface
/// inherited from <see cref="IGenericRepository{TEntity}"/> keys on the primary key alone, which is not
/// how this table is ever read — the resume asks for "every skill this user owns, in the order they
/// chose", so the members declared here are the ones callers actually use.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read — <c>ResumeSkills.razor</c> calls <see cref="GetByUserIdAsync"/> and groups the rows by
///         <c>Category</c> in the component; <see cref="GetCategoriesAsync"/> serves the admin editor's
///         category picker.</item>
///   <item>Write — <c>ManageSkills.razor</c> calls <see cref="CreateAsync"/>, the inherited
///         <c>UpdateAsync</c>, or <see cref="DeleteAsync"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserSkillsRepo</c> over Dapper and
/// PostgreSQL; registered transient in <c>BlogSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> Every read is scoped to one user — there is no "all skills" query, because a
/// skill has no meaning detached from the person who claims it. Ordering is a contract, not an
/// accident: rows come back by <c>DisplayOrder</c> ascending, which is the sequence the owner arranged
/// in the admin editor and the sequence the resume renders. Callers must not re-sort.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> Every <c>…Async</c> member below carries a default
/// implementation that calls its synchronous twin and wraps the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline, parks the calling thread for the whole round trip, and throws synchronously rather than
/// returning a faulted task. <c>UserSkillsRepo</c> overrides all six with genuine async Dapper and does
/// honour the token; any other implementer that still inherits these defaults is unconverted. The
/// member-level <c>Flow:</c> notes below describe that override, not the default.</para>
/// </remarks>
public interface IUserSkillsRepo : IGenericRepository<UserSkill>
{
    /// <summary>
    /// Gets all skills for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>The user's skills ordered by <c>DisplayOrder</c> ascending; an empty sequence — never
    /// <c>null</c> — when the user has no skills or does not exist.</returns>
    IEnumerable<UserSkill> GetByUserId(long userId);

    /// <summary>
    /// Gets skills for a user filtered by category.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter; matched exactly, nothing normalises it.</param>
    /// <returns>The matching skills ordered by <c>DisplayOrder</c> ascending; an empty sequence when
    /// nothing matches.</returns>
    IEnumerable<UserSkill> GetByUserIdAndCategory(long userId, string category);

    /// <summary>
    /// Gets a skill by its ID.
    /// </summary>
    /// <param name="skillId">Skill ID.</param>
    /// <returns>UserSkill if found, null otherwise.</returns>
    UserSkill? GetById(long skillId);

    /// <summary>
    /// Creates a new skill.
    /// </summary>
    /// <param name="skill">Skill to create.</param>
    /// <returns>The ID of the created skill.</returns>
    long Create(UserSkill skill);

    /// <summary>
    /// Deletes a skill by ID.
    /// </summary>
    /// <param name="skillId">Skill ID to delete.</param>
    void Delete(long skillId);

    /// <summary>
    /// Gets distinct categories for a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>The distinct category names in use, alphabetically; an empty sequence when the user has
    /// no skills. Uncategorised skills contribute no entry.</returns>
    IEnumerable<string> GetCategories(long userId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // Each member carries a default implementation that runs its synchronous twin, for the same
    // reason IGenericRepository does: adding an abstract member here would break every implementer
    // at once, including hand-written test doubles that this conversion has no business touching.
    // A default is correct but is not the fix — UserSkillsRepo overrides all of them with genuine
    // async Dapper, and any implementer that still inherits these is unconverted.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets all skills for a specific user without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserId"/>, same rows and
    /// same display order.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user → buffered list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The user's skills ordered by <c>DisplayOrder</c> ascending; an empty sequence — never
    /// <c>null</c> — when the user has no skills. Fully buffered, so it is safe to enumerate twice.</returns>
    Task<IEnumerable<UserSkill>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserId(userId));

    /// <summary>
    /// Gets skills for a user filtered by category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserIdAndCategory"/>; the
    /// category match is exact, because nothing normalises the free-text value.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user and category.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The matching skills ordered by <c>DisplayOrder</c> ascending; an empty sequence when
    /// nothing matches.</returns>
    Task<IEnumerable<UserSkill>> GetByUserIdAndCategoryAsync(long userId, string category, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserIdAndCategory(userId, category));

    /// <summary>
    /// Gets a skill by its ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetById"/>; an unknown identifier
    /// is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="skillId">Skill ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The skill, or <c>null</c> when the identifier is unknown. "Not found" is a normal
    /// answer here and is never signalled by an exception.</returns>
    Task<UserSkill?> GetByIdAsync(long skillId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetById(skillId));

    /// <summary>
    /// Creates a new skill without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Create"/>.</para>
    /// <para><b>Flow:</b> bind parameters → open the connection asynchronously → INSERT … RETURNING.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="skill">Skill to create; its <c>SkillId</c> is ignored and the database assigns one.</param>
    /// <param name="cancellationToken">Cancels the insert; ignored by the inherited default.</param>
    /// <returns>The generated identifier, always greater than zero. A constraint violation surfaces as
    /// a <c>Npgsql.NpgsqlException</c> — failures here are unexpected and are thrown, not returned.</returns>
    Task<long> CreateAsync(UserSkill skill, CancellationToken cancellationToken = default)
        => Task.FromResult(Create(skill));

    /// <summary>
    /// Deletes a skill by ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Delete"/>; deleting an unknown
    /// identifier affects no rows and is not an error.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row.</para>
    /// </remarks>
    /// <param name="skillId">Skill ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    Task DeleteAsync(long skillId, CancellationToken cancellationToken = default)
    {
        Delete(skillId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets distinct categories for a user without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetCategories"/>; categories are
    /// derived from the skill rows because there is no category table.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → DISTINCT query → buffered list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The distinct category names in use, alphabetically; an empty sequence when the user has
    /// no skills.</returns>
    Task<IEnumerable<string>> GetCategoriesAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetCategories(userId));
}
