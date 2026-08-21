namespace BlogModels;

/// <summary>
/// Where a subscriber stands in the newsletter consent lifecycle (REQ-FN-059).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces the reading of <c>Subscriber.IsConfirmed</c> as a consent record.
/// That single bit conflated "never completed double opt-in" with "explicitly opted out", so an
/// unsubscribe erased the proof of consent rather than recording a withdrawal. This enumeration is
/// the four-way answer the two facts actually need.</para>
///
/// <para><b>Code Flow:</b> Never stored. It is derived on read by
/// <see cref="Subscriber.ConsentState"/> from the <c>ConfirmedOn</c>, <c>UnsubscribedOn</c> and
/// <c>IsConsentUnknown</c> columns added by migration
/// <c>024-SubscriberConsentAndTokenLifecycle.sql</c>, and the SQL header of that migration carries
/// the same derivation so the two cannot drift.</para>
///
/// <para><b>Dependencies:</b> None — a plain enumeration.</para>
///
/// <para><b>Usage:</b> This is the CONSENT axis, not the mailability axis.
/// <c>Subscriber.IsConfirmed</c> remains the single bit every send query filters on, and it is left
/// untouched by the consent work; a state of <see cref="Confirmed"/> and a true <c>IsConfirmed</c>
/// always agree in practice, but a caller deciding whether to mail must still test
/// <c>IsConfirmed</c> so there is exactly one mailability rule in the codebase.</para>
/// </remarks>
public enum SubscriberConsentState
{
    /// <summary>
    /// Signed up, opt-in link not yet redeemed. No consent has been given, so the address must not
    /// be mailed anything except its confirmation link.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Consent was given and has not since been withdrawn — <c>ConfirmedOn</c> is set and is newer
    /// than any recorded withdrawal. This is the only state a newsletter may be sent to.
    /// </summary>
    Confirmed = 1,

    /// <summary>
    /// Consent was withdrawn. <c>UnsubscribedOn</c> is set and is at least as recent as any
    /// <c>ConfirmedOn</c>. Distinct from <see cref="Pending"/> on purpose: this address made a
    /// decision, so a re-confirmation sweep must leave it alone. Any earlier <c>ConfirmedOn</c> is
    /// preserved, so the row still proves the address once consented.
    /// </summary>
    Withdrawn = 2,

    /// <summary>
    /// The row predates migration 024 and its unconfirmed state could not be interpreted as either
    /// pending or withdrawn. The migration deliberately refused to guess: inventing a
    /// <c>ConfirmedOn</c> would fabricate proof of consent, and inventing an <c>UnsubscribedOn</c>
    /// would fabricate a data-subject action. Treat it as unmailable and as ineligible for any
    /// re-confirmation sweep.
    /// </summary>
    Unknown = 3
}
