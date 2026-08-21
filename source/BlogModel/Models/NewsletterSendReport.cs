namespace BlogModels;

/// <summary>
/// The outcome of one newsletter dispatch: who it reached and who it did not.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives the admin screen a single value describing the send, and gives the
/// verifier a countable acceptance signal ("a newsletter reaches a test subscriber; the send is
/// logged").</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>NewsletterSvc.SendAsync</c> resolves the audience and initialises the report.</item>
///   <item>Each SMTP attempt increments <see cref="SentCount"/> or <see cref="FailedCount"/>.</item>
///   <item>The completed report is returned inside a <c>Result</c> and written to the log.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> A send with <see cref="SentCount"/> greater than zero is treated as a
/// successful publication even when individual addresses bounced.</para>
/// </remarks>
public class NewsletterSendReport
{
    /// <summary>
    /// The issue that was dispatched.
    /// </summary>
    public long NewsletterId { get; set; }

    /// <summary>
    /// The slug stamped on the issue when it became a public archive record. Empty when the send
    /// reached nobody and the issue was therefore never published.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// How many subscribers the <see cref="NewsletterAudience"/> resolved to — the denominator.
    /// <see cref="SentCount"/> plus <see cref="FailedCount"/> should equal it; a shortfall means the
    /// send was interrupted part way, which is the one case the counts alone would otherwise hide.
    /// </summary>
    public int TargetedCount { get; set; }

    /// <summary>
    /// How many messages the relay accepted. Acceptance is not delivery — a message accepted here
    /// can still bounce later, and nothing in this application processes bounces — so this is an
    /// upper bound on readership, not a confirmed one.
    /// </summary>
    public int SentCount { get; set; }

    /// <summary>
    /// How many attempts failed. Each is also written to <c>SubscriberNewsletter</c> with its
    /// address and the relay's error text, so this number is a summary of recoverable detail rather
    /// than the only record — a non-zero value is diagnosable after the fact.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// When the dispatch finished. Also stamped onto the issue itself, where it becomes the archive
    /// ordering key.
    /// </summary>
    public DateTime SentOn { get; set; }

    /// <summary>
    /// True when at least one message was accepted. This — not the absence of failures — is the
    /// success test: a send that reached most of the list and bounced on a handful of dead
    /// addresses is a successful publication, and treating any failure as a failed send would leave
    /// the issue unpublished over one bad address.
    /// </summary>
    public bool HasReachedAnyone => SentCount > 0;
}
