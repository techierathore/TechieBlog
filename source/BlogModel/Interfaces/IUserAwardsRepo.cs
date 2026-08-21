using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for the awards and recognitions section of a user's resume.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>UserAward</c>. The inherited generic CRUD
/// surface keys on the primary key alone, which is not how this table is read — the resume asks for
/// "every award this user holds, in the order they chose" — so the members declared here are the ones
/// callers actually use.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read — <c>ResumeAwards.razor</c> calls <see cref="GetByUserIdAsync"/> for the public
///         resume.</item>
///   <item>Write — <c>ManageAwards.razor</c> calls <see cref="GetByIdAsync"/> to load the edit form,
///         then <see cref="CreateAsync"/>, the inherited <c>UpdateAsync</c>, or
///         <see cref="DeleteAsync"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserAwardsRepo</c> over Dapper and
/// PostgreSQL; registered transient in <c>BlogSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> Every read is scoped to one user — there is no site-wide award listing. Ordering
/// is a contract: rows come back by <c>DisplayOrder</c> ascending, the sequence the owner arranged in
/// the admin editor and the sequence the resume renders. Callers must not re-sort. This contract throws
/// on a data-access failure; there is no <c>Result</c> surface between it and the pages, so a page that
/// cannot tolerate an exception must guard the call itself.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> Every <c>…Async</c> member below carries a default
/// implementation that calls its synchronous twin and wraps the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline, parks the calling thread for the whole round trip, and throws synchronously rather than
/// returning a faulted task. <c>UserAwardsRepo</c> overrides all four with genuine async Dapper and does
/// honour the token; any other implementer still inheriting the defaults is unconverted. The
/// member-level <c>Flow:</c> notes describe that override, not the default.</para>
/// </remarks>
public interface IUserAwardsRepo : IGenericRepository<UserAward>
{
    /// <summary>
    /// Gets all awards for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>The user's awards ordered by <c>DisplayOrder</c> ascending; an empty sequence — never
    /// <c>null</c> — when the user holds none or does not exist.</returns>
    IEnumerable<UserAward> GetByUserId(long userId);

    /// <summary>
    /// Gets an award by its ID.
    /// </summary>
    /// <param name="awardId">Award ID.</param>
    /// <returns>The award, or <c>null</c> when the identifier is unknown — a normal answer, never
    /// signalled by an exception.</returns>
    UserAward? GetById(long awardId);

    /// <summary>
    /// Creates a new award.
    /// </summary>
    /// <param name="award">Award to create; its <c>AwardId</c> is ignored and the database assigns one.</param>
    /// <returns>The generated identifier, always greater than zero.</returns>
    long Create(UserAward award);

    /// <summary>
    /// Deletes an award by ID.
    /// </summary>
    /// <param name="awardId">Award ID to delete.</param>
    void Delete(long awardId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // Each member carries a default implementation that runs its synchronous twin, for the same
    // reason IGenericRepository does: adding an abstract member here would break every implementer
    // at once, including hand-written test doubles that this conversion has no business touching.
    // A default is correct but is not the fix — UserAwardsRepo overrides all of them with genuine
    // async Dapper, and any implementer that still inherits these is unconverted.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets all awards for a specific user without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserId"/>, same rows and
    /// same display order.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user → buffered list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The user's awards ordered by <c>DisplayOrder</c> ascending; an empty sequence — never
    /// <c>null</c> — when the user holds none. Fully buffered, so it is safe to enumerate twice.</returns>
    Task<IEnumerable<UserAward>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserId(userId));

    /// <summary>
    /// Gets an award by its ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetById"/>; an unknown identifier
    /// is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="awardId">Award ID.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The award, or <c>null</c> when the identifier is unknown.</returns>
    Task<UserAward?> GetByIdAsync(long awardId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetById(awardId));

    /// <summary>
    /// Creates a new award without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Create"/>.</para>
    /// <para><b>Flow:</b> bind parameters → open the connection asynchronously → INSERT … RETURNING.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="award">Award to create; its <c>AwardId</c> is ignored and the database assigns one.</param>
    /// <param name="cancellationToken">Cancels the insert; ignored by the inherited default.</param>
    /// <returns>The generated identifier, always greater than zero. A constraint violation surfaces as a
    /// <c>Npgsql.NpgsqlException</c> — failures here are unexpected and are thrown, not returned.</returns>
    Task<long> CreateAsync(UserAward award, CancellationToken cancellationToken = default)
        => Task.FromResult(Create(award));

    /// <summary>
    /// Deletes an award by ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Delete"/>; deleting an unknown
    /// identifier affects no rows and is not an error.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row.</para>
    /// </remarks>
    /// <param name="awardId">Award ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement; ignored by the inherited default.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op on an unknown identifier.</returns>
    Task DeleteAsync(long awardId, CancellationToken cancellationToken = default)
    {
        Delete(awardId);
        return Task.CompletedTask;
    }
}
