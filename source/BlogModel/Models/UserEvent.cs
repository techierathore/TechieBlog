namespace BlogModels;

/// <summary>
/// A speaking engagement or career position on the resume timeline.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One row of <c>UserEvents</c>. The table serves two overlapping resume
/// sections: conference/meetup talks (the original columns — <see cref="EventTitle"/>,
/// <see cref="SessionTitle"/>, <see cref="EventDate"/>) and the experience timeline added by
/// migration <c>012-ResumeAndImageManagement.sql</c> (<see cref="StartDate"/>,
/// <see cref="Description"/>, <see cref="IsCurrent"/>). Which set applies is inferred from
/// <see cref="EventType"/> — there is no discriminator column.</para>
///
/// <para><b>Code Flow:</b> Read and written by <c>BlogEngine.DbAccess.UserEventRepo</c>; rendered by
/// the resume and speaking pages.</para>
///
/// <para><b>Dependencies:</b> The <c>UserEvents</c> table and its foreign key to <c>BlogUser</c>.
/// Note <see cref="EventType"/> maps to a column named <c>Type</c>, so a <c>SELECT *</c> will not
/// bind it without an alias.</para>
///
/// <para><b>Usage:</b> A data carrier. Author-supplied text bound for a public page — escape on
/// render.</para>
/// </remarks>
public class UserEvent
{
    /// <summary>
    /// Surrogate key (<c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long EventID  { get; set; }

    /// <summary>
    /// Site-relative path to the event or employer logo. Empty renders the entry without a mark.
    /// </summary>
    public string LogoIconPath { get; set; } = string.Empty;

    /// <summary>
    /// Title of the talk the user gave, distinct from <see cref="EventTitle"/> — the conference is
    /// the event, this is the session within it. Empty on experience-timeline rows, which have no
    /// session.
    /// </summary>
    public string SessionTitle { get; set; } = string.Empty;

    /// <summary>
    /// Name of the conference, meetup or — on an experience row — the employer. At most 350
    /// characters.
    /// </summary>
    public string EventTitle { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the event page or company site. Points off-site; treat as untrusted.
    /// </summary>
    public string EventUrl { get; set; } = string.Empty;

    /// <summary>
    /// Free-text classification — "Conference", "Meetup", "Webinar" and so on. Maps to the column
    /// named <c>Type</c>, not <c>EventType</c>. No lookup table constrains the value, and it is
    /// what distinguishes a talk row from an experience row, so it must be spelled consistently.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// For a talk, the date it was delivered. For an experience row this is the <i>end</i> date and
    /// pairs with <see cref="StartDate"/>; when <see cref="IsCurrent"/> is set it is not meaningful.
    /// </summary>
    public DateTime EventDate { get; set; }

    /// <summary>
    /// Owning <c>BlogUser</c>. The column is nullable, so an orphaned row surfaces here as <c>0</c>.
    /// </summary>
    public long UserID { get; set; }

    /// <summary>
    /// A page heading carried alongside the record. <b>No such column exists</b> in
    /// <c>UserEvents</c>, and nothing in <c>source/</c> reads or writes it — always empty. A
    /// deletion candidate; the identically named and equally unused property on <c>BlogPost</c> is
    /// the same leftover.
    /// </summary>
    public string UIPageTitle { get; set; } = string.Empty;

    /// <summary>
    /// Experience rows only: when the position began. Null on talk rows, where
    /// <see cref="EventDate"/> alone carries the date.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Optional detail about the role or talk. Unbounded <c>TEXT</c>; null when none was supplied.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Sort position in the timeline, ascending. Defaults to <c>0</c>.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Marks an ongoing position, which the timeline renders as "Present" instead of an end date.
    /// When <c>true</c>, <see cref="EventDate"/> should be ignored. Nothing enforces that only one
    /// row per user is current.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// Speaking rows only: where an upcoming session can be registered for
    /// (<c>RegistrationUrl VARCHAR(500)</c>, migration 031).
    /// </summary>
    /// <remarks>
    /// <para><b>Null is meaningful and must stay null.</b> A past talk has nowhere to register and an
    /// <c>Experience</c> row never did, so this is <c>null</c> for both — the Speaker Profile page
    /// renders the Registration column only in its Future Sessions table and shows a dash where the
    /// value is absent. Writing an empty string instead would make "no link" and "a link I have not
    /// filled in yet" indistinguishable at the database, which is exactly the distinction the admin
    /// screen needs in order to leave the field blank.</para>
    /// <para>Never rendered as a bare interpolation — it is owner-supplied and leaves the site, so it
    /// goes out with <c>rel="noopener noreferrer"</c> like every other outbound link here.</para>
    /// </remarks>
    public string? RegistrationUrl { get; set; }

    /// <summary>
    /// True when this engagement is still in the future, and therefore belongs in the Speaker
    /// Profile page's "Future Sessions" table rather than "Past Sessions".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Derived from <see cref="EventDate"/> rather than stored, so the
    /// split cannot go stale — see migration 031 for why no <c>IsPast</c> column exists. The
    /// comparison is against <b>today's date</b>, not <c>DateTime.Now</c>: a session is "upcoming"
    /// for the whole of the day it runs on, so a talk at 09:00 does not silently move itself into
    /// the past table while it is still being delivered.</para>
    /// <para><b>A row with no date counts as PAST.</b> The alternative — treating an unset date as
    /// upcoming — would put every legacy row with a missing date into Future Sessions, where a
    /// visitor would read them as announcements.</para>
    /// </remarks>
    public bool IsUpcoming => EventDate.Date >= DateTime.Today;
}
