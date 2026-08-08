using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for the headline figures shown on the portfolio home page and resume.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>UserStat</c> — the "12 years experience",
/// "40 talks delivered" tiles. Each row is an owner-authored label/value pair rather than something the
/// application computes, which is why this is a plain repository and not an analytics query.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read — <c>Home.razor</c> injects this contract directly for the statistic tiles;
///         <c>UserStatsSvc</c> wraps <see cref="GetByUserIdAsync"/> for callers that want the failures
///         folded into a <c>Result</c> instead of thrown.</item>
///   <item>Write — the admin editor calls <see cref="CreateAsync"/>, the inherited <c>UpdateAsync</c>,
///         or <see cref="DeleteAsync"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserStatsRepo</c> over Dapper and
/// PostgreSQL; registered transient in <c>BlogSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> Reads are scoped to one user and ordered by <c>DisplayOrder</c> ascending, which
/// is the sequence the tiles render in — callers must not re-sort. This contract throws on a data-access
/// failure; a caller that would rather not handle exceptions on a render path should go through
/// <c>UserStatsSvc</c>, which converts them to a failed <c>Result</c>.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> Every <c>…Async</c> member below carries a default
/// implementation that calls its synchronous twin and wraps the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline and parks the calling thread for the whole round trip. <c>UserStatsRepo</c> overrides all five
/// with genuine async Dapper and does honour the token; any other implementer still inheriting the
/// defaults is unconverted. The member-level <c>Flow:</c> notes describe that override, not the
/// default.</para>
/// </remarks>
public interface IUserStatsRepo : IGenericRepository<UserStat>
{
    /// <summary>
    /// Gets all stats for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>The user's statistics ordered by <c>DisplayOrder</c> ascending; an empty sequence —
    /// never <c>null</c> — when the user has none or does not exist.</returns>
    IEnumerable<UserStat> GetByUserId(long userId);

    /// <summary>
    /// Gets stats for a user filtered by category.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter; matched exactly, nothing normalises it.</param>
    /// <returns>The matching statistics ordered by <c>DisplayOrder</c> ascending; an empty sequence when
    /// nothing matches. Rows with a null category never match.</returns>
    IEnumerable<UserStat> GetByUserIdAndCategory(long userId, string category);

    /// <summary>
    /// Gets a stat by its ID.
    /// </summary>
    /// <param name="statId">Stat ID.</param>
    /// <returns>The statistic, or <c>null</c> when the identifier is unknown — a normal answer, never
    /// signalled by an exception.</returns>
    UserStat? GetById(long statId);

    /// <summary>
    /// Creates a new stat.
    /// </summary>
    /// <param name="stat">Stat to create; its <c>StatId</c> is ignored and the database assigns one.</param>
    /// <returns>The generated identifier, always greater than zero.</returns>
    long Create(UserStat stat);

    /// <summary>
    /// Deletes a stat by ID.
    /// </summary>
    /// <param name="statId">Stat ID to delete.</param>
    void Delete(long statId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // Each member carries a default implementation that runs its synchronous twin, for the same
    // reason IGenericRepository does: adding an abstract member here would break every implementer
    // at once, including hand-written test doubles that this conversion has no business touching.
    // A default is correct but is not the fix — UserStatsRepo overrides all of them with genuine
    // async Dapper, and any implementer that still inherits these is unconverted.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets all stats for a specific user without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserId"/>, same rows and
    /// same display order. This is the read behind the resume's About and Community blocks and the
    /// portfolio home page's statistic tiles.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user → buffered list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The user's statistics ordered by <c>DisplayOrder</c> ascending; an empty sequence —
    /// never <c>null</c> — when the user has none. Fully buffered, so it is safe to enumerate twice.</returns>
    Task<IEnumerable<UserStat>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserId(userId));

    /// <summary>
    /// Gets stats for a user filtered by category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserIdAndCategory"/>; the
    /// category column is nullable, so an uncategorised statistic never matches.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user and category.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter; matched exactly.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The matching statistics ordered by <c>DisplayOrder</c> ascending; an empty sequence when
    /// nothing matches.</returns>
    Task<IEnumerable<UserStat>> GetByUserIdAndCategoryAsync(long userId, string category, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserIdAndCategory(userId, category));

    /// <summary>
    /// Gets a stat by its ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetById"/>; an unknown identifier
    /// is a normal answer and yields <c>null</c>, which is how the service reports a row that has
    /// already been deleted.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="statId">Stat ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The statistic, or <c>null</c> when the identifier is unknown.</returns>
    Task<UserStat?> GetByIdAsync(long statId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetById(statId));

    /// <summary>
    /// Creates a new stat without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Create"/>.</para>
    /// <para><b>Flow:</b> bind parameters → open the connection asynchronously → INSERT … RETURNING.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="stat">Stat to create; its <c>StatId</c> is ignored and the database assigns one.</param>
    /// <param name="cancellationToken">Cancels the insert; ignored by the inherited default.</param>
    /// <returns>The generated identifier, always greater than zero. A constraint violation surfaces as a
    /// <c>Npgsql.NpgsqlException</c> — failures here are unexpected and are thrown, not returned.</returns>
    Task<long> CreateAsync(UserStat stat, CancellationToken cancellationToken = default)
        => Task.FromResult(Create(stat));

    /// <summary>
    /// Deletes a stat by ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Delete"/>; deleting an unknown
    /// identifier affects no rows and is not an error.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row.</para>
    /// </remarks>
    /// <param name="statId">Stat ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement; ignored by the inherited default.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op on an unknown identifier.</returns>
    Task DeleteAsync(long statId, CancellationToken cancellationToken = default)
    {
        Delete(statId);
        return Task.CompletedTask;
    }
}
