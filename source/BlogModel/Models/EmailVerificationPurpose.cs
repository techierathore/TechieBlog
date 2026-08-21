namespace BlogModels;

/// <summary>
/// The legal values of <see cref="EmailVerificationToken.Purpose"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Tells the verification service which pending row to promote when a
/// token is consumed, and lets the emailed copy explain what is being confirmed. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> The issuing service sets the purpose; <c>EmailVerificationSvc.Consume</c>
/// switches on it to call the matching promotion routine.</para>
///
/// <para><b>Dependencies:</b> None - plain string constants so the value round-trips through
/// the <c>VARCHAR(30)</c> database column without a converter.</para>
///
/// <para><b>Usage:</b> Compare with <see cref="string.Equals(string, string, StringComparison)"/>
/// using <see cref="StringComparison.OrdinalIgnoreCase"/>.</para>
/// </remarks>
public static class EmailVerificationPurpose
{
    /// <summary>Confirms the address behind a pending anonymous comment.</summary>
    public const string Comment = "Comment";

    /// <summary>Confirms the address behind a pending anonymous rating.</summary>
    public const string Rating = "Rating";

    /// <summary>Confirms the address behind a pending newsletter subscription.</summary>
    public const string Subscription = "Subscription";
}
