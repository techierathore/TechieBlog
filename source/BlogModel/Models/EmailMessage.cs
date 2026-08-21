namespace BlogModels;

/// <summary>
/// A single outbound email message handed to <c>IEmailService</c> for delivery.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives every caller of the email service one transport-neutral shape to
/// fill in, so newsletter dispatch, double opt-in verification and password reset all travel the
/// same code path and are logged identically.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A caller (NewsletterSvc, AuthSvc, a verification service) builds an EmailMessage.</item>
///   <item>The message is passed to <c>IEmailService.SendAsync</c>.</item>
///   <item>The configured implementation (SMTP in production, console in Development) renders and
///         delivers it, returning a <c>Result</c> that never swallows a failure.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — this is a plain contract in the dependency leaf project.</para>
///
/// <para><b>Usage:</b> Always set <see cref="ToAddress"/>, <see cref="Subject"/> and at least one
/// body. Set <see cref="UnsubscribeUrl"/> for bulk mail so the sender can emit the
/// <c>List-Unsubscribe</c> header required by bulk-mail reputation rules.</para>
///
/// <para><b>One message, one recipient.</b> There is no CC, BCC, attachment or reply-to surface, and
/// no collection of addresses — a newsletter send builds and dispatches one of these per subscriber.
/// That is what keeps recipients from ever seeing each other's addresses, and it is why the
/// per-recipient <see cref="UnsubscribeUrl"/> can be personalised at all.</para>
///
/// <para><b>Exposure.</b> Everything on this object is destined for one specific inbox, and several
/// fields are sensitive on the way there: <see cref="ToAddress"/> is personal data and
/// <see cref="UnsubscribeUrl"/> and any verification or reset link inside a body are bearer
/// credentials. The dev-mode console sender writes the address and unsubscribe URL to the log by
/// design — which is exactly why it must never be the configured sender outside
/// Development.</para>
/// </remarks>
public class EmailMessage
{
    /// <summary>
    /// The single recipient address. Required — a message without one is rejected before any
    /// transport work happens.
    /// </summary>
    /// <remarks>
    /// Parsed into a <c>MailAddress</c> by the SMTP sender, so a malformed value throws there rather
    /// than being silently dropped. It reaches a mail header, so it must never carry a newline;
    /// letting an unvalidated address through is header injection.
    /// <para><b>Exposure:</b> personal data, and the SMTP sender logs it on both success and failure
    /// — acceptable in a server log, not in anything a visitor can reach.</para>
    /// </remarks>
    public string ToAddress { get; set; } = string.Empty;

    /// <summary>
    /// Recipient display name, used for the friendly part of the <c>To:</c> header. Optional.
    /// </summary>
    /// <remarks>
    /// When empty the sender falls back to a bare address, so leaving it unset degrades presentation
    /// and nothing else. It is frequently a visitor-supplied name (a commenter's
    /// <c>DisplayName</c>), so it is untrusted input landing in a header — the same newline caveat as
    /// <see cref="ToAddress"/> applies, more sharply.
    /// </remarks>
    public string ToName { get; set; } = string.Empty;

    /// <summary>
    /// Subject line. Required.
    /// </summary>
    /// <remarks>
    /// A header, so it carries the same injection caveat as the address fields. Composed by the
    /// caller, not by the sender, which is why every message type is free to phrase its own — and why
    /// nothing centrally prevents two features from sending indistinguishable subjects.
    /// </remarks>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML body. When present it is the body that is sent, and the message goes out as HTML.
    /// </summary>
    /// <remarks>
    /// Assembled by string composition in the calling service, so any visitor-supplied value
    /// interpolated into it — a display name, a comment excerpt — must be HTML-encoded first.
    /// <para>Setting this makes <see cref="TextBody"/> unused (see <see cref="IsHtml"/>): the sender
    /// picks one body, it does not build a multipart alternative. A plain-text client therefore sees
    /// the raw markup, so keep <see cref="TextBody"/> populated as documentation of intent even
    /// though it will not be sent.</para>
    /// </remarks>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text body. Sent only when <see cref="HtmlBody"/> is empty.
    /// </summary>
    /// <remarks>
    /// Not a fallback that the recipient's client can choose — it is an either/or decision made
    /// server-side by <see cref="IsHtml"/>. A message with neither body sends an empty one; nothing
    /// validates that at least one is set.
    /// </remarks>
    public string TextBody { get; set; } = string.Empty;

    /// <summary>
    /// One-click unsubscribe URL for bulk messages. When set the sender adds a
    /// <c>List-Unsubscribe</c> header.
    /// </summary>
    /// <remarks>
    /// Carries the recipient's unsubscribe token, so it is personal to one subscriber and is a bearer
    /// credential: anyone holding the URL can unsubscribe that address. Never reuse one message's URL
    /// for another recipient, and never log it in production.
    /// <para>Empty for transactional mail — a verification or password-reset message must not offer
    /// unsubscription, because the recipient did not subscribe to anything. Leaving it empty on a
    /// bulk send is the more damaging mistake: the header is what keeps a newsletter out of spam
    /// folders.</para>
    /// </remarks>
    public string UnsubscribeUrl { get; set; } = string.Empty;

    /// <summary>
    /// True when <see cref="HtmlBody"/> carries the content and the message should be sent as HTML.
    /// </summary>
    /// <remarks>
    /// Computed from whether <see cref="HtmlBody"/> has non-whitespace content — there is no way to
    /// force a plain-text send while an HTML body is set, other than clearing it. The sender uses it
    /// for both the body choice and the <c>IsBodyHtml</c> flag, so the two can never disagree and a
    /// text body cannot be mislabelled as HTML.
    /// </remarks>
    public bool IsHtml => !string.IsNullOrWhiteSpace(HtmlBody);
}
