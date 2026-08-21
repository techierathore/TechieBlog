namespace BlogModels;

/// <summary>
/// A newsletter issue — a draft while being composed, a public archive record once sent.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries both the admin-side composition state (title, content, status)
/// and the reader-side publication state (slug, send time, recipient count) for the
/// <c>Newsletter</c> table.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Admin composes an issue — <see cref="Status"/> is <c>draft</c>, <see cref="Slug"/> is empty.</item>
///   <item>Send runs — every active subscriber gets a copy and a row is written to
///         <c>SubscriberNewsletter</c>.</item>
///   <item>On a send that reached at least one subscriber the issue is stamped with
///         <see cref="SentOn"/>, <see cref="RecipientCount"/>, a unique <see cref="Slug"/>,
///         <see cref="Status"/> = <c>sent</c> and <see cref="IsPublic"/> = <c>true</c>, which makes
///         it a public archive record. A send that reached nobody is left as a draft so it can be
///         retried.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None in code — dependency-leaf contract. In the schema: the
/// <c>Newsletter</c> table from <c>001-CreateTables.sql</c>, which originally held only
/// <c>NewsletterId</c>, <c>Title</c>, <c>Content</c>, <c>CreatedOn</c>, <c>ScheduledFor</c> and
/// <c>Status</c>. Everything the public archive needs — <c>Slug</c>, <c>Summary</c>, <c>SentOn</c>,
/// <c>IsPublic</c>, <c>RecipientCount</c> — was added by <c>015-NewsletterAndAnalytics.sql</c>, so
/// a database that has not run migration 015 cannot bind this type.</para>
///
/// <para><b>Usage:</b> Public archive queries must filter on <see cref="Status"/> = <c>sent</c>
/// AND <see cref="IsPublic"/> AND a non-blank <see cref="Slug"/> — exactly the three conditions
/// <see cref="IsPublished"/> encodes. A draft or unsent issue is never publicly reachable.
/// <see cref="IsPublished"/> is a client-side convenience for a loaded instance; it cannot be
/// translated into SQL, so the repository repeats the predicate rather than calling it.</para>
/// </remarks>
public class Newsletter
{
    /// <summary>
    /// The <c>Status</c> value for an issue still being composed. Also the column default, so a row
    /// inserted without an explicit status is a draft — the safe default, since only a
    /// <see cref="StatusSent"/> issue can reach the public archive.
    /// </summary>
    public const string StatusDraft = "draft";

    /// <summary>
    /// The <c>Status</c> value for an issue queued for a future send. Nothing dispatches scheduled
    /// issues automatically — there is no background sender — so this state records intent and
    /// waits for an administrator.
    /// </summary>
    public const string StatusScheduled = "scheduled";

    /// <summary>
    /// The <c>Status</c> value for an issue that has been dispatched. One third of the
    /// published-archive predicate; see <see cref="IsPublished"/>.
    /// </summary>
    public const string StatusSent = "sent";

    /// <summary>
    /// Surrogate key (<c>NewsletterId</c>, <c>BIGSERIAL</c>). Zero until the draft is inserted.
    /// </summary>
    public long NewsletterId { get; set; }

    /// <summary>
    /// Subject line of the outbound message and heading of the archive page (<c>VARCHAR(255)</c>,
    /// required). Author-supplied text that reaches both an email client and a public page — escape
    /// it on render.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The issue body, held in an unbounded <c>TEXT</c> column and required by the schema. It is
    /// author-authored rich content destined for an email body and for the public archive page, so
    /// it is rendered as markup rather than escaped — which makes the composer a trusted surface.
    /// Only roles that may already publish should be able to write it.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Short teaser shown on archive listings (<c>VARCHAR(500)</c>, added by migration 015).
    /// Optional — an empty summary leaves the listing entry showing its title alone rather than
    /// falling back to an excerpt of <see cref="Content"/>.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// When the draft was created. Defaults to <c>CURRENT_TIMESTAMP</c> server-side; server-local
    /// time, not UTC, because the column is <c>TIMESTAMP</c> without a time zone.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// The intended send time for a <see cref="StatusScheduled"/> issue; null for an immediate
    /// send. Advisory only — nothing polls this column, so a scheduled issue does not go out on its
    /// own.
    /// </summary>
    public DateTime? ScheduledFor { get; set; }

    /// <summary>
    /// When the issue was actually dispatched; null until then. This — not
    /// <see cref="CreatedOn"/> — is what orders the public archive and resolves the previous/next
    /// neighbours, so two issues sent in the same instant fall back to <see cref="NewsletterId"/>
    /// to stay deterministic.
    /// </summary>
    public DateTime? SentOn { get; set; }

    /// <summary>
    /// Composition state: <see cref="StatusDraft"/>, <see cref="StatusScheduled"/> or
    /// <see cref="StatusSent"/> (<c>VARCHAR(50)</c>). A database <c>CHECK</c> constraint restricts
    /// the column to those three lower-case spellings, so writing any other value — including a
    /// differently cased one — fails the insert rather than being silently accepted. Compare
    /// against the constants on this type.
    /// </summary>
    public string Status { get; set; } = StatusDraft;

    /// <summary>
    /// URL-friendly identifier for the public archive page (<c>VARCHAR(300)</c>, added by migration
    /// 015), assigned at send time and empty until then. It is the only public handle on an issue —
    /// the archive resolves by slug, never by <see cref="NewsletterId"/> — so changing it after
    /// publication breaks every link already in circulation.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Whether a sent issue appears in the public archive (<c>NOT NULL DEFAULT FALSE</c>). The
    /// column default keeps a draft private, but note that the value is <b>not</b> an editorial
    /// choice at send time: <c>NewsletterSvc.PublishAsync</c> passes <c>true</c> unconditionally to
    /// <c>MarkSentAsync</c>, so any issue that reached at least one subscriber becomes publicly
    /// readable at its slug. There is no "send to the list but keep it off the archive" path today.
    /// Treat every dispatched issue's content as public, and clear the flag afterwards if it should
    /// not have been.
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// How many subscribers the relay accepted the issue for (<c>NOT NULL DEFAULT 0</c>). Stamped
    /// once from <see cref="NewsletterSendReport.SentCount"/>, so it counts acceptances and
    /// excludes failures, and it is never recomputed — a later resend does not update it.
    /// </summary>
    public int RecipientCount { get; set; }

    /// <summary>
    /// True when this issue is a public archive record: sent, marked public and carrying a slug.
    /// Computed, never persisted.
    /// </summary>
    /// <remarks>
    /// All three conditions are load-bearing. Status alone is not enough (a sent-but-private issue
    /// must stay hidden), and the slug check guards the case where a send was interrupted after the
    /// status was written but before the slug was — an issue in that state would otherwise resolve
    /// to an archive URL of nothing.
    /// </remarks>
    public bool IsPublished =>
        Status == StatusSent && IsPublic && !string.IsNullOrWhiteSpace(Slug);
}
