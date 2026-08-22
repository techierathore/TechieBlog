using BlogModels;
using BlogModels.Models;

namespace TechieBlog.Tests.Resume;

/// <summary>
/// Unit tests for the speaking-engagement rules behind the Speaker Profile page.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers UAT-006. The Speaker Profile page splits its two tables on
/// <see cref="UserEvent.IsUpcoming"/>, which is DERIVED from the date rather than stored — migration
/// 031 records why. A derived rule with no test is a rule that silently changes meaning the next
/// time somebody edits the expression, and the failure mode is a page advertising a talk that
/// already happened, which nobody would notice from a green build.</para>
/// <para><b>Dependencies:</b> None — these are pure model rules; no database and no substitutes.</para>
/// <para><b>Note on the boundary:</b> the comparison is against <c>DateTime.Today</c>, so these
/// tests are written relative to today rather than to fixed literals. Hard-coded dates here would
/// start failing on their own the day they drifted into the past.</para>
/// </remarks>
public class SpeakingEngagementTests
{
    /// <summary>
    /// A session dated later than today belongs in Future Sessions.
    /// </summary>
    [Fact]
    public void SessionDatedInTheFutureIsUpcoming()
    {
        var session = BuildSession(DateTime.Today.AddDays(30));

        Assert.True(session.IsUpcoming);
    }

    /// <summary>
    /// A session dated before today belongs in Past Sessions.
    /// </summary>
    [Fact]
    public void SessionDatedInThePastIsNotUpcoming()
    {
        var session = BuildSession(DateTime.Today.AddDays(-1));

        Assert.False(session.IsUpcoming);
    }

    /// <summary>
    /// A session running TODAY still counts as upcoming, for the whole of the day. This is the
    /// boundary the rule exists to get right: comparing against DateTime.Now instead of Today would
    /// move a talk starting at 09:00 into the past table while it was still being delivered.
    /// </summary>
    [Fact]
    public void SessionRunningTodayIsStillUpcoming()
    {
        var session = BuildSession(DateTime.Today);

        Assert.True(session.IsUpcoming);
    }

    /// <summary>
    /// A time-of-day earlier than now on today's date does not push the session into the past,
    /// because only the date component is compared.
    /// </summary>
    [Fact]
    public void EarlierTimeTodayDoesNotMakeSessionPast()
    {
        var session = BuildSession(DateTime.Today.AddHours(1));

        Assert.True(session.IsUpcoming);
    }

    /// <summary>
    /// A row with no date at all is treated as PAST. Defaulting the other way would file every
    /// legacy row with a missing date under Future Sessions, where a visitor reads them as
    /// announcements of talks that are not happening.
    /// </summary>
    [Fact]
    public void SessionWithNoDateIsTreatedAsPast()
    {
        var session = BuildSession(default);

        Assert.False(session.IsUpcoming);
    }

    /// <summary>
    /// The discriminator the Speaker Profile page filters on is the shared constant, not a literal
    /// retyped at each call site. A drifted spelling makes rows invisible rather than failing, which
    /// is the whole reason EventTypes exists.
    /// </summary>
    [Fact]
    public void EventTypeConstantsMatchTheStoredValues()
    {
        Assert.Equal("Speaking", EventTypes.Speaking);
        Assert.Equal("Experience", EventTypes.Experience);
        Assert.NotEqual(EventTypes.Speaking, EventTypes.Experience);
    }

    /// <summary>
    /// Builds a speaking row carrying only what these rules depend on.
    /// </summary>
    /// <param name="eventDate">The date the session runs on.</param>
    /// <returns>The session under test.</returns>
    private static UserEvent BuildSession(DateTime eventDate) => new()
    {
        EventType = EventTypes.Speaking,
        EventTitle = "A Conference",
        SessionTitle = "A Talk",
        EventDate = eventDate,
        UserID = 1
    };
}
