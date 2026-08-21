namespace BlogModels.Models;

/// <summary>
/// The minimal credential projection used by the authentication path.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Login must compare a plaintext password against a salted PBKDF2 hash
/// in application code (REQ-NFR-002) — the comparison can no longer happen in SQL, so the
/// stored hash has to be read out. This type carries only what the check needs, keeping the
/// hash away from the richer <see cref="AppUser"/> projections that flow to the UI.</para>
///
/// <para><b>Code Flow:</b> <c>UserCredentialRepo.GetByEmail</c> materialises it →
/// <c>AuthSvc.AppLogin</c> verifies the password → on success the full <see cref="AppUser"/>
/// is loaded for the session.</para>
///
/// <para><b>Dependencies:</b> Mapped by Dapper from the <c>GetUserCredentialByEmail</c>
/// PostgreSQL function (script <c>017-SecurityAndTokenPersistence.sql</c>).</para>
///
/// <para><b>Usage:</b> Never serialise this type to the browser — it contains the password hash.</para>
/// </remarks>
public class UserCredential
{
    /// <summary>
    /// Gets or sets the owning <c>BlogUser</c>. This is what the full <see cref="AppUser"/> is
    /// loaded by once the password check has passed — never re-resolve the account from the
    /// submitted address after that point, or a race with a concurrent email change could bind the
    /// session to a different row than the one whose hash was verified.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Gets or sets the user's email address, which doubles as the login name. Matched
    /// case-insensitively by <c>GetUserCredentialByEmail</c> (migration
    /// <c>020-CaseInsensitiveEmailLookup.sql</c>), so it is returned in the casing the account was
    /// registered with, not the casing that was typed at the login form.
    /// </summary>
    public string EmailId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the stored credential — a salted PBKDF2 hash, or a legacy value awaiting
    /// upgrade on next successful sign-in.
    /// </summary>
    /// <remarks>
    /// Never a plaintext password, and it must never be compared with <c>==</c>: verification goes
    /// through <c>PasswordHasher</c>, which selects the right algorithm for the stored format and
    /// compares in constant time. This is also the property that makes the whole type
    /// server-side-only — see the usage note on the class.
    /// </remarks>
    public string LoginPass { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's role name, read here so the authentication ticket can be built in
    /// the same round trip as the password check. It must match an <see cref="AppRoles"/> constant
    /// exactly — comparisons are ordinal — and it is what every policy in
    /// <see cref="AppPolicies.PolicyRoleMap"/> is evaluated against. An unrecognised value
    /// authenticates the user but authorises nothing.
    /// </summary>
    public string UserRole { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the user must change their password before doing anything else
    /// (REQ-NFR-023). Set on the seeded bootstrap administrator and on admin-created staff
    /// accounts. It is carried on this projection so the forced-change redirect can be decided at
    /// sign-in rather than after a session has already been established; the sign-in itself still
    /// succeeds, so the redirect must be enforced by the host, not inferred from a failure.
    /// </summary>
    public bool MustChangePassword { get; set; }
}
