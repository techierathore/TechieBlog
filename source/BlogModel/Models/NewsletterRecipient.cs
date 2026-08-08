namespace BlogModels;

/// <summary>
/// One row of a newsletter's send history — the delivery outcome for a single subscriber.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the <c>SubscriberNewsletter</c> table so an administrator can see
/// exactly who an issue reached and which addresses failed, rather than only a headline count.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The send loop writes one row per targeted subscriber before or immediately after the
///         SMTP attempt.</item>
///   <item><see cref="SendStatus"/> records the outcome; a failure also fills
///         <see cref="ErrorMessage"/> so nothing is silently swallowed.</item>
///   <item>The admin history screen reads these rows back for one newsletter.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None in code — dependency-leaf contract. In the schema: the
/// <c>SubscriberNewsletter</c> table from <c>001-CreateTables.sql</c>, whose
/// <c>SendStatus</c> and <c>ErrorMessage</c> columns were added by
/// <c>015-NewsletterAndAnalytics.sql</c>. Note that <see cref="Email"/> is <b>not</b> a column on
/// that table — the history query joins <c>Subscriber</c> for it — so an instance built by hand,
/// or read through any query that omits the join, has an empty address.</para>
///
/// <para><b>Usage:</b> Read through <c>INewsletterService.GetSendHistoryAsync</c>. Because the
/// address is joined live rather than snapshotted, the history shows the subscriber's <i>current</i>
/// address, not the one the message was actually delivered to; a subsequent address change
/// retroactively rewrites what the history appears to say.</para>
///
/// <para><b>Security:</b> every row carries a subscriber's email address, so this type is
/// admin-surface only. It must never reach a public page or an unauthenticated endpoint — a send
/// history is a complete dump of the mailing list.</para>
/// </remarks>
public class NewsletterRecipient
{
    /// <summary>
    /// The value written to <c>SendStatus</c> when the SMTP server accepted the message. Also the
    /// column default, so a row inserted without an explicit status reads as successful — which is
    /// only safe because the send path always supplies one.
    /// </summary>
    /// <remarks>
    /// "Accepted by the relay" is not "delivered to the inbox". A later bounce is invisible here;
    /// nothing in this application processes bounce notifications.
    /// </remarks>
    public const string StatusSent = "sent";

    /// <summary>
    /// The value written to <c>SendStatus</c> when the relay rejected the message or the send threw.
    /// </summary>
    public const string StatusFailed = "failed";

    /// <summary>
    /// Surrogate key of the send-history row (<c>Id</c>, <c>BIGSERIAL</c>). Used only as the
    /// tie-break in the history ordering when several attempts share a timestamp.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The issue that was dispatched. Foreign key to <c>Newsletter</c>; the whole history view is a
    /// filter on this column, which <c>IdxSubscriberNewsletterNewsletterId</c> supports.
    /// </summary>
    public long NewsletterId { get; set; }

    /// <summary>
    /// The subscriber the attempt was made against. Foreign key to <c>Subscriber</c>. Nothing
    /// enforces one row per subscriber per issue, so a resend adds a second row rather than
    /// replacing the first.
    /// </summary>
    public long SubscriberId { get; set; }

    /// <summary>
    /// The subscriber's email address, joined from <c>Subscriber.Email</c> at read time. Not stored
    /// on the history row, so it reflects the address as it is <i>now</i> rather than as it was at
    /// send time, and it is empty on any instance not produced by the joining query.
    /// </summary>
    /// <remarks>Personal data — admin surface only; never render it publicly.</remarks>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// When the attempt was made. Nullable because the column has always allowed null; the current
    /// send path always supplies a value, so a null here means the row predates that path.
    /// </summary>
    public DateTime? SentOn { get; set; }

    /// <summary>
    /// When the recipient opened the message. Requires a tracking pixel that this application does
    /// not emit, so it is always null — do not build an open-rate figure on it.
    /// </summary>
    public DateTime? OpenedOn { get; set; }

    /// <summary>
    /// When the recipient clicked a link. Requires link rewriting that this application does not
    /// do, so it is always null. Same caveat as <see cref="OpenedOn"/>.
    /// </summary>
    public DateTime? ClickedOn { get; set; }

    /// <summary>
    /// Delivery outcome — <see cref="StatusSent"/> or <see cref="StatusFailed"/>
    /// (<c>VARCHAR(20)</c>). Stored as free text with no check constraint, so compare against the
    /// constants on this type rather than against literals typed at the call site.
    /// </summary>
    public string SendStatus { get; set; } = StatusSent;

    /// <summary>
    /// The relay's rejection text when <see cref="SendStatus"/> is <see cref="StatusFailed"/>;
    /// empty otherwise (the column is nullable and the query coalesces it). Written so a failure is
    /// diagnosable rather than merely counted.
    /// </summary>
    /// <remarks>
    /// This is third-party text taken verbatim from an SMTP response. Escape it on render, and keep
    /// it on the admin surface — a relay error can quote the recipient address and internal host
    /// names.
    /// </remarks>
    public string ErrorMessage { get; set; } = string.Empty;
}
