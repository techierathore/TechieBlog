namespace BlogModels;

/// <summary>
/// An email address enrolled in the newsletter.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Holds the newsletter mailing list and the consent state that decides
/// whether a given address may actually be mailed.</para>
///
/// <para><b>Code Flow:</b> Created when a visitor submits the subscribe form, promoted to confirmed
/// when they follow the double opt-in link, and demoted again when they follow the unsubscribe link
/// built from <see cref="UnsubscribeToken"/>. Read by <c>BlogEngine.DbAccess.SubscriberRepo</c> and
/// by the newsletter send path in <c>NewsletterRepo.GetRecipientsAsync</c>.</para>
///
/// <para><b>Dependencies:</b> The <c>Subscriber</c> table
/// (<c>PostgresScripts/001-CreateTables.sql</c>), extended with the unsubscribe token by
/// <c>015-NewsletterAndAnalytics.sql</c>. <see cref="Email"/> is uniquely indexed. Columns:
/// <c>SubscriberId</c>, <c>Email</c>, <c>Name</c>, <c>SubscribedOn</c>, <c>IsConfirmed</c>,
/// <c>Preferences</c>, <c>UnsubscribeToken</c>.</para>
///
/// <para><b>Usage — read this before writing a send query.</b> <see cref="IsConfirmed"/> and
/// <see cref="IsActive"/> look like two independent flags and are <i>not</i>. There is no
/// <c>IsActive</c> column: <c>SubscriberRepo</c> selects <c>IsConfirmed AS IsActive</c> and its
/// update writes back to <c>IsConfirmed</c>, and unsubscribing runs
/// <c>UPDATE Subscriber SET IsConfirmed = FALSE</c>. The two properties are therefore always equal
/// on a materialised row, and one bit is carrying two different meanings — "never completed double
/// opt-in" and "explicitly opted out". Consequences to design around:</para>
/// <list type="bullet">
///   <item>An unsubscribe erases the proof of consent rather than recording a withdrawal, so the
///         row can no longer show that the address ever opted in.</item>
///   <item>An unsubscribed address is indistinguishable from one that never confirmed, so a
///         "resend confirmation" sweep would mail people who explicitly left.</item>
///   <item>Testing both flags (<c>IsConfirmed &amp;&amp; IsActive</c>) is harmless but buys nothing;
///         it reads as a safety check that is not actually checking anything.</item>
/// </list>
/// <para>Separating the two needs a migration that adds a real column, so it is not a local fix.
/// Until then, treat <see cref="IsConfirmed"/> as the single mailability bit and prefer it in new
/// code.</para>
/// </remarks>
public class Subscriber
{
    /// <summary>
    /// Surrogate key (<c>SubscriberId</c>, <c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long SubscriberId { get; set; }

    /// <summary>
    /// The subscribed address (<c>VARCHAR(255)</c>, uniquely indexed — an address can appear on the
    /// list at most once). Visitor-supplied and therefore untrusted input.
    /// </summary>
    /// <remarks>
    /// Personal data. It legitimately appears on the admin subscriber screen and as the envelope
    /// recipient of a send; it must never be rendered on a public page, and it must not be echoed
    /// back by the subscribe endpoint in a way that reveals whether the address was already on the
    /// list, which would turn the form into a membership oracle.
    /// </remarks>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name used to personalise the greeting in outbound mail (<c>VARCHAR(255)</c>,
    /// <c>NOT NULL</c> — empty rather than absent when the visitor supplied none). Visitor-supplied
    /// free text; it is never an identity and nothing is authorised by it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When the address was added to the list. Recorded once at sign-up and not refreshed by a
    /// later re-subscribe, so it is the first-seen date rather than the current-consent date; it
    /// also drives the recipient ordering, newest first.
    /// </summary>
    public DateTime SubscribedOn { get; set; }

    /// <summary>
    /// Whether the address completed double opt-in and has not since unsubscribed
    /// (<c>IsConfirmed</c>, defaulting to <c>FALSE</c>). This is the single bit that decides
    /// mailability — <c>NewsletterRepo.GetRecipientsAsync</c> filters on
    /// <c>COALESCE(IsConfirmed, FALSE) = TRUE</c> — and it carries both meanings described in the
    /// remarks on the type.
    /// </summary>
    public bool IsConfirmed { get; set; }

    /// <summary>
    /// An alias of <see cref="IsConfirmed"/>, not an independent flag. No <c>IsActive</c> column
    /// exists; <c>SubscriberRepo</c> selects <c>IsConfirmed AS IsActive</c> and writes updates back
    /// to <c>IsConfirmed</c>. The <c>= true</c> initialiser applies only to an instance built in
    /// memory and is overwritten by whatever the query returns, so it must not be read as "active
    /// unless proven otherwise". See the remarks on the type before relying on this property.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional topic preferences, stored as a JSON document in a <c>TEXT</c> column rather than as
    /// typed columns. Nothing in the send path parses it today, so no filtering actually honours it
    /// — treat it as captured-but-unused until a reader exists.
    /// </summary>
    public string Preferences { get; set; } = string.Empty;

    /// <summary>
    /// Opaque per-subscriber token used to build the unsubscribe link carried by every newsletter
    /// message (REQ-FN-032). <c>VARCHAR(64)</c>; migration 015 backfilled existing rows per-row and
    /// set a column default, so <c>SubscriberRepo</c> never has to supply one on insert.
    /// </summary>
    /// <remarks>
    /// Long-lived and never rotated: the same value ships in every issue the subscriber ever
    /// receives, so anyone who obtains one message can unsubscribe that address at any time. That
    /// is the accepted trade — a one-click unsubscribe must work without a login — but it means the
    /// token is a credential, not an identifier. Do not reuse it to authorise anything other than
    /// unsubscribing, do not log it, and do not render it anywhere except inside the unsubscribe
    /// URL of an outbound message.
    /// </remarks>
    public string UnsubscribeToken { get; set; } = string.Empty;
}
