using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for the dated entries on a resume — employment, speaking, education.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>UserEvent</c>, one table serving several
/// resume sections that differ only by a free-text <c>EventType</c> discriminator. The inherited generic
/// CRUD surface keys on the primary key alone; the members declared here add the two accesses the resume
/// actually needs — filter by type, and re-order a section by drag and drop.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read — <c>ResumeExperience.razor</c> and its siblings call
///         <see cref="GetByUserAndTypeAsync"/> once per section.</item>
///   <item>Write — <c>ManageExperience.razor</c> calls the inherited <c>InsertToGetIdAsync</c> or
///         <c>UpdateAsync</c> for one entry, <see cref="UpdateDisplayOrdersAsync"/> after a drag, and
///         <see cref="DeleteAsync"/> to remove one.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserEventRepo</c> over Dapper and
/// PostgreSQL; registered transient in <c>BlogSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> <c>EventType</c> is free text with no lookup table and no normalisation behind
/// it, so a caller must spell it exactly as stored — a typo silently yields an empty section rather than
/// an error. Callers pin the value in a constant (for example
/// <c>ManageExperience.ExperienceEventType</c>) instead of writing the literal at each site. Ordering is
/// a contract: rows come back by <c>DisplayOrder</c> ascending, then by <c>EventDate</c> descending, so
/// entries the owner has not explicitly ranked still read newest-first. Callers must not re-sort.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> Every <c>…Async</c> member below carries a default
/// implementation that calls its synchronous twin and wraps the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline, parks the calling thread for the whole round trip, and throws synchronously rather than
/// returning a faulted task. <c>UserEventRepo</c> overrides all three with genuine async Dapper and does
/// honour the token; any other implementer still inheriting the defaults is unconverted. The
/// member-level <c>Flow:</c> notes describe that override, not the default.</para>
/// </remarks>
public interface IUserEventRepo : IGenericRepository<UserEvent>
{
    /// <summary>
    /// Gets all events for a user filtered by event type.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="eventType">The event type to filter by (e.g., "Experience", "Speaking"); matched
    /// exactly, so an unrecognised value yields an empty section rather than an error.</param>
    /// <returns>The matching events ordered by <c>DisplayOrder</c> ascending, then <c>EventDate</c>
    /// descending; an empty sequence — never <c>null</c> — when nothing matches.</returns>
    IEnumerable<UserEvent> GetByUserAndType(long userId, string eventType);

    /// <summary>
    /// Deletes an event by ID.
    /// </summary>
    /// <param name="eventId">Event ID to delete.</param>
    void Delete(long eventId);

    /// <summary>
    /// Updates the display order for multiple events.
    /// </summary>
    /// <param name="eventOrders">Map of EventId to its new DisplayOrder. A <c>null</c> or empty map is accepted and is a no-op. The
    /// update is <i>not</i> atomic across entries — the statements share one connection but no
    /// transaction, so a mid-run failure can leave the section partially re-ordered.</param>
    void UpdateDisplayOrders(Dictionary<long, int> eventOrders);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // Each member carries a default implementation that runs its synchronous twin, for the same
    // reason IGenericRepository does: adding an abstract member here would break every implementer
    // at once, including hand-written test doubles that this conversion has no business touching.
    // A default is correct but is not the fix — UserEventRepo overrides all of them with genuine
    // async Dapper, and any implementer that still inherits these is unconverted.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets all events for a user filtered by event type, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetByUserAndType"/>. The type is
    /// free text with no lookup table behind it, so the match is exact and the caller must spell it
    /// as stored.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query filtered on user and type →
    /// buffered list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <param name="eventType">The event type to filter by (e.g., "Experience", "Speaking"); matched
    /// exactly.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The matching events ordered by <c>DisplayOrder</c> ascending, then <c>EventDate</c>
    /// descending; an empty sequence — never <c>null</c> — when nothing matches. Fully buffered, so it
    /// is safe to enumerate twice.</returns>
    Task<IEnumerable<UserEvent>> GetByUserAndTypeAsync(long userId, string eventType, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserAndType(userId, eventType));

    /// <summary>
    /// Deletes an event by ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Delete"/>; deleting an unknown
    /// identifier affects no rows and is not an error.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row.</para>
    /// </remarks>
    /// <param name="eventId">Event ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement; ignored by the inherited default.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op on an unknown identifier.</returns>
    Task DeleteAsync(long eventId, CancellationToken cancellationToken = default)
    {
        Delete(eventId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the display order for multiple events, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="UpdateDisplayOrders"/>. An empty
    /// map is a no-op, so a drag that ended where it started never reaches the database.</para>
    /// <para><b>Flow:</b> open one connection asynchronously → Dapper multi-execute, one statement
    /// per entry.</para>
    /// <para><b>Side Effects:</b> Updates one row per supplied identifier.</para>
    /// </remarks>
    /// <param name="eventOrders">Map of EventId to its new DisplayOrder. A <c>null</c> or empty map is accepted and is a no-op.</param>
    /// <param name="cancellationToken">Cancels the statements; ignored by the inherited default. Because
    /// the statements are not wrapped in a transaction, cancelling part-way leaves the entries already
    /// written at their new order.</param>
    /// <returns>A task that completes when every row has been written. Unknown identifiers are silently
    /// skipped — no row count is reported.</returns>
    Task UpdateDisplayOrdersAsync(Dictionary<long, int> eventOrders, CancellationToken cancellationToken = default)
    {
        UpdateDisplayOrders(eventOrders);
        return Task.CompletedTask;
    }
}
