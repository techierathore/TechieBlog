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
/// update writes back to <c>IsConfirmed</c>. The two properties are therefore always equal on a
/// materialised row. <see cref="IsConfirmed"/> is the single MAILABILITY bit and is the only thing
/// a send query may filter on.</para>
///
/// <para><b>[REQ-FN-059] 2026-08-10 — the consent record is no longer that bit.</b> Until migration
/// <c>024-SubscriberConsentAndTokenLifecycle.sql</c> the one bit also carried the consent history,
/// so unsubscribing (<c>UPDATE Subscriber SET IsConfirmed = FALSE</c>) ERASED the proof of consent
/// instead of recording a withdrawal, and an address that deliberately left was indistinguishable
/// from one that never confirmed. Consent now lives in its own columns —
/// <see cref="ConfirmedOn"/>, <see cref="UnsubscribedOn"/> and <see cref="IsConsentUnknown"/> —
/// surfaced as <see cref="ConsentState"/>. Neither timestamp is ever cleared, so a resubscribe
/// keeps the record of the earlier withdrawal and the row can always show <i>when</i> consent was
/// given and <i>when</i> it was withdrawn.</para>
/// <list type="bullet">
///   <item>Deciding whether to MAIL an address: test <see cref="IsConfirmed"/>. Unchanged, and
///         every existing send query keeps working.</item>
///   <item>Deciding whether the address ever CONSENTED, or whether a re-confirmation sweep may
///         touch it: test <see cref="ConsentState"/>. A
///         <see cref="SubscriberConsentState.Withdrawn"/> or
///         <see cref="SubscriberConsentState.Unknown"/> address must never be swept.</item>
///   <item>The two axes are kept in step by the database trigger
///         <c>TrgSubscriberConsentChange</c>, so a writer that only knows about
///         <c>IsConfirmed</c> — including <c>NewsletterRepo.DeactivateSubscriberAsync</c> — still
///         records the withdrawal instead of erasing the consent.</item>
/// </list>
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
    /// <para>It is a credential, not an identifier: whoever holds it can opt this address out
    /// without a login, which is the point — a one-click unsubscribe must work from a mail client.
    /// Do not reuse it to authorise anything else, do not log it, and do not render it anywhere
    /// except inside the unsubscribe URL of an outbound message.</para>
    /// <para><b>[REQ-FN-059] It is no longer unlimited.</b> It is now rotatable, burnable and — once
    /// it carries a recorded issuance — expirable. See <see cref="UnsubscribeTokenIssuedOn"/> and
    /// <see cref="UnsubscribeTokenUsedOn"/>.</para>
    /// <para><b>[REQ-FN-060] It is no longer what a newsletter mails.</b> Since migration 027 a
    /// send issues a token scoped to that one issue into the <c>UnsubscribeToken</c> TABLE, so the
    /// credential in a message authorises that issue and nothing else. This column is now the
    /// FALLBACK: it resolves every unsubscribe link already sitting in a delivered mail, and the
    /// send path uses it only when a per-issue token could not be issued. Two consequences —
    /// a subscriber legitimately holds several live tokens at once, and a value read from this
    /// property is NOT the token that was mailed in any particular issue.</para>
    /// <para><b>Do not write this property back from an entity loaded by
    /// <c>ISubscriberRepo.GetByNewsletterTokenAsync</c>.</b> That read deliberately projects the
    /// matched per-issue token row onto this property and the two issuance stamps, so the entity
    /// describes a token rather than the row; saving it would overwrite the row-level token.</para>
    /// </remarks>
    public string UnsubscribeToken { get; set; } = string.Empty;

    /// <summary>
    /// When consent was most recently GIVEN (<c>ConfirmedOn</c>, nullable). Proof of consent: it is
    /// never cleared, so unsubscribing can no longer erase the fact that the address opted in.
    /// </summary>
    /// <remarks>
    /// Stamped by the double opt-in redemption, by an administrative re-activation and by the
    /// database trigger <c>TrgSubscriberConsentChange</c> for any writer that only flips
    /// <c>IsConfirmed</c>. Migration 024 backfilled it from <see cref="SubscribedOn"/> for rows that
    /// were already mailable — an under-statement of the consent age, never an over-statement.
    /// </remarks>
    public DateTime? ConfirmedOn { get; set; }

    /// <summary>
    /// When consent was most recently WITHDRAWN (<c>UnsubscribedOn</c>, nullable). Proof of
    /// withdrawal, and the fact that used to be lost when <c>IsConfirmed</c> was flipped to false.
    /// </summary>
    /// <remarks>
    /// Never cleared either. A resubscribe sets a newer <see cref="ConfirmedOn"/> rather than
    /// nulling this, so both halves of the history survive and <see cref="ConsentState"/> is decided
    /// by comparing the two timestamps.
    /// </remarks>
    public DateTime? UnsubscribedOn { get; set; }

    /// <summary>
    /// True when this row predates migration 024 and its unconfirmed state could not be interpreted
    /// as either pending or withdrawn (<c>IsConsentUnknown</c>, <c>NOT NULL DEFAULT FALSE</c>).
    /// </summary>
    /// <remarks>
    /// Written once by the migration and never by application code. The migration refused to guess
    /// because inventing a <see cref="ConfirmedOn"/> would fabricate proof of consent; the marker
    /// records the ambiguity honestly so a re-confirmation sweep can exclude exactly these rows.
    /// </remarks>
    public bool IsConsentUnknown { get; set; }

    /// <summary>
    /// When the CURRENT <see cref="UnsubscribeToken"/> was issued (nullable). <c>null</c> means a
    /// legacy token with no recorded issuance, which never expires.
    /// </summary>
    /// <remarks>
    /// A legacy token is already sitting in delivered mail and cannot be recalled, so expiring it
    /// could only ever strand a subscriber with no way off the list. Every token issued through
    /// rotation carries this stamp and expires
    /// <c>SubscriberSvc.UnsubscribeTokenLifetimeDays</c> days later.
    /// </remarks>
    public DateTime? UnsubscribeTokenIssuedOn { get; set; }

    /// <summary>
    /// When the current <see cref="UnsubscribeToken"/> was redeemed (nullable). A burned token
    /// performs no further state change.
    /// </summary>
    /// <remarks>
    /// Stamped in the same UPDATE that records the withdrawal, so one link causes at most one state
    /// change. Cleared — together with a fresh token — on any re-consent, so a subscriber who comes
    /// back always holds a working link.
    /// </remarks>
    public DateTime? UnsubscribeTokenUsedOn { get; set; }

    /// <summary>
    /// Gets where this subscriber stands in the consent lifecycle, derived from
    /// <see cref="ConfirmedOn"/>, <see cref="UnsubscribedOn"/> and <see cref="IsConsentUnknown"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Withdrawal wins a tie. When the two timestamps are equal the
    /// state is <see cref="SubscriberConsentState.Withdrawn"/>, because the safe direction of error
    /// for a consent question is "do not mail". The same derivation is documented in the header of
    /// <c>024-SubscriberConsentAndTokenLifecycle.sql</c>.</para>
    /// <para><b>Side Effects:</b> None; pure. Not a column and never persisted — Dapper leaves it
    /// alone because it has no setter.</para>
    /// </remarks>
    public SubscriberConsentState ConsentState
    {
        get
        {
            if (UnsubscribedOn.HasValue &&
                (!ConfirmedOn.HasValue || UnsubscribedOn.Value >= ConfirmedOn.Value))
                return SubscriberConsentState.Withdrawn;

            if (ConfirmedOn.HasValue)
                return SubscriberConsentState.Confirmed;

            return IsConsentUnknown ? SubscriberConsentState.Unknown : SubscriberConsentState.Pending;
        }
    }
}
