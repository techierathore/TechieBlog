namespace BlogModels;

/// <summary>
/// Reduces an email address to its first letter and its domain, for staff screens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-063 (BRD-109). The ratings dialog shows who rated only as
/// <c>r•••@gmail.com</c> — enough to tell two raters apart, not enough to contact or identify one.</para>
///
/// <para><b>Code Flow:</b> split at the last <c>@</c>; keep the first character of the local part
/// and the whole domain; anything malformed becomes <see cref="Hidden"/>, never the raw input.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Called inside <c>RatingSvc</c> before a row leaves the service.</para>
/// </remarks>
public static class EmailMasker
{
    /// <summary>
    /// What a malformed or empty address is shown as.
    /// </summary>
    public const string Hidden = "•••";

    /// <summary>
    /// Masks one address to first letter + <c>•••@</c> + domain.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> the local part beyond its first character is never returned.
    /// An address with no local part, no domain, or no <c>@</c> returns <see cref="Hidden"/>, so a
    /// malformed value is never echoed back.</para>
    /// <para><b>Flow:</b> trim → find the last <c>@</c> → compose.</para>
    /// <para><b>Side Effects:</b> none.</para>
    /// </remarks>
    /// <param name="email">The address to mask; may be <c>null</c>.</param>
    /// <returns>The masked form, for example <c>r•••@gmail.com</c>.</returns>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Hidden;

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at < 1 || at == trimmed.Length - 1)
            return Hidden;

        var firstLetter = char.ToLowerInvariant(trimmed[0]);
        var domain = trimmed[(at + 1)..].ToLowerInvariant();
        return $"{firstLetter}{Hidden}@{domain}";
    }
}
