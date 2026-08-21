namespace BlogModels;

/// <summary>
/// What actually happened when a newsletter unsubscribe link was followed (REQ-FN-032, BRD-59).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the anonymous <c>/unsubscribe/{token}</c> page tell the reader the
/// truth. "You have been removed" and "you were already removed" are both successes and must not
/// be reported with the same wording, and neither may be confused with a link that did not
/// resolve.</para>
///
/// <para><b>Code Flow:</b> <c>NewsletterSvc.UnsubscribeAsync</c> resolves the token, decides which
/// member applies and returns it inside a <c>Result&lt;UnsubscribeOutcome&gt;</c>; the page maps
/// the member onto one confirmation screen.</para>
///
/// <para><b>Dependencies:</b> None — a plain enumeration.</para>
///
/// <para><b>Usage:</b> A token that resolves to nobody is <b>not</b> represented here: it comes
/// back as a failed <c>Result</c> carrying a deliberately vague message, so an unknown token and a
/// malformed one are indistinguishable to a caller probing the route.
/// <see cref="NotRecognised"/> is the zero value purely so the <c>Data</c> of a failed result is
/// never mistaken for a real outcome.</para>
/// </remarks>
public enum UnsubscribeOutcome
{
    /// <summary>
    /// No subscriber was resolved. Only ever seen as the default <c>Data</c> of a failed result —
    /// the service never returns it as a success.
    /// </summary>
    NotRecognised = 0,

    /// <summary>
    /// The subscriber was receiving mail and has now been opted out by this request.
    /// </summary>
    Unsubscribed = 1,

    /// <summary>
    /// The token resolved, but the subscriber was already opted out, so nothing changed. Following
    /// the same link twice is a no-op rather than an error.
    /// </summary>
    AlreadyUnsubscribed = 2
}
