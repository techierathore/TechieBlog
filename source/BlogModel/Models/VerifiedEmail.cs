namespace BlogModels;

/// <summary>
/// An email address that has completed double opt-in at least once.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The registry that lets a returning visitor skip confirmation.
/// Without it every comment from a regular reader would demand another inbox round trip.
/// [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> Written by <c>RecordVerifiedEmail</c> when a token is consumed;
/// read by <c>EmailVerificationSvc.IsAddressVerified</c> before a submission is queued.</para>
///
/// <para><b>Dependencies:</b> Persisted by <c>VerifiedEmailRepo</c> against the
/// <c>VerifiedEmail</c> table created in migration script 014.</para>
///
/// <para><b>Usage:</b> A row with <see cref="IsBlocked"/> set counts as NOT verified, which
/// is how an administrator bans an abusive address without losing its history.</para>
///
/// <para><b>Exposure:</b> the whole table is a list of addresses that have engaged with the site,
/// so this type is admin-surface only. Nothing that answers an unauthenticated request may reveal
/// whether a given address has a row — "is this address verified?" asked by an anonymous caller is
/// an account-enumeration oracle, and the answer must only ever change what the server does, never
/// what it says.</para>
/// </remarks>
public class VerifiedEmail
{
    /// <summary>
    /// Gets or sets the primary key of the registry row.
    /// </summary>
    public long VerifiedEmailId { get; set; }

    /// <summary>
    /// Gets or sets the confirmed address — the natural key of the registry, matched
    /// case-insensitively so that one person cannot accumulate several rows (and several
    /// verifications) by varying capitalisation. Personal data; see the exposure note on the type.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the most recent display name seen for this address, refreshed each time the
    /// address submits something. Cosmetic only — it prefills the name field on a comment or rating
    /// form and identifies nobody, so a change to it grants and revokes nothing.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC instant at which the address <i>first</i> completed double opt-in. Not
    /// refreshed by later submissions, so it is the consent date; the recency signal is
    /// <see cref="LastUsedOn"/>.
    /// </summary>
    public DateTime VerifiedOn { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant at which the address last submitted a comment or rating. Null
    /// when the address has confirmed but not yet been used again since.
    /// </summary>
    public DateTime? LastUsedOn { get; set; }

    /// <summary>
    /// Gets or sets whether an administrator has banned this address. A blocked row is treated as
    /// <b>not verified</b> rather than as absent, which is deliberate: the ban survives, the
    /// history survives, and re-confirming the address does not lift it — so an abusive visitor
    /// cannot clear a block simply by clicking a fresh verification link.
    /// </summary>
    public bool IsBlocked { get; set; }
}
