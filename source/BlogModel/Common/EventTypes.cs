namespace BlogModels;

/// <summary>
/// The discriminator values stored in <c>UserEvents.Type</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>UserEvents</c> is one table holding several kinds of timeline entry,
/// told apart only by a free-text <c>Type</c> column — there is no lookup table and no check
/// constraint, so a misspelling does not fail, it simply makes the row invisible to every screen
/// that reads the correct spelling. Holding the literals here turns that silent disappearance into
/// a compile error.</para>
///
/// <para><b>Code Flow:</b> purely declarative. A writer stamps <see cref="UserEvent.EventType"/>
/// with one of these before saving; a reader passes the same constant to
/// <c>IUserEventRepo.GetByUserAndTypeAsync</c>.</para>
///
/// <para><b>Dependencies:</b> None — bottom of the graph, like <see cref="AppRoles"/>.</para>
///
/// <para><b>Matching is case-insensitive by convention, not by the database.</b> The stored value is
/// plain text and the repository's <c>type = @EventType</c> predicate is case-SENSITIVE. Existing
/// rows were written as <c>Experience</c>, so anything comparing in C# uses
/// <see cref="System.StringComparison.OrdinalIgnoreCase"/> defensively while every WRITE goes
/// through these constants to keep the stored casing uniform. Do not introduce a differently-cased
/// literal on a write path.</para>
///
/// <para><b>Usage:</b> <c>await eventRepo.GetByUserAndTypeAsync(userId, EventTypes.Speaking)</c>.</para>
/// </remarks>
public static class EventTypes
{
    /// <summary>
    /// A position in the owner's work history, rendered by the resume timeline.
    /// </summary>
    /// <remarks>
    /// Rows of this type are the only ones that use <c>StartDate</c> and <c>IsCurrent</c>: the
    /// position's span is <c>StartDate</c> → <c>EventDate</c>, and <c>IsCurrent</c> renders the end
    /// as "Present".
    /// </remarks>
    public const string Experience = "Experience";

    /// <summary>
    /// A conference, meetup or workshop session, rendered by the Speaker Profile page.
    /// </summary>
    /// <remarks>
    /// Rows of this type use <c>EventDate</c> as the single date the session ran on — <c>StartDate</c>
    /// is null and <c>IsCurrent</c> is meaningless — plus <c>EventUrl</c> for the event page and
    /// <c>RegistrationUrl</c> for an upcoming session's sign-up link (migration 031).
    /// </remarks>
    public const string Speaking = "Speaking";
}
