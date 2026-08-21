using Microsoft.Extensions.Configuration;

namespace BlogEngine.Common;

/// <summary>
/// The two caps that bound captcha abuse: how many challenges a client may be issued, and how
/// many validations it may fail, inside a rolling window. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Single-use challenges and a five-minute expiry stop the replay of ONE
/// challenge, but nothing stopped a client asking for challenges forever (a cheap way to grow the
/// server-side answer cache) or grinding guesses against a stream of fresh ones. These are the
/// numbers that close both holes.</para>
///
/// <para><b>Code Flow:</b> <see cref="FromConfiguration"/> reads the four keys out of the
/// <c>RateLimiting</c> section that <c>REQ-NFR-005</c> already established for the authentication
/// endpoints, so a deployment tunes every limiter in one place. Anything missing, unparsable or
/// non-positive falls back to the compiled default rather than disabling the cap.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> only.</para>
///
/// <para><b>Usage:</b> Built once at composition time and handed to
/// <see cref="CaptchaRateLimiter"/>, which is a singleton.</para>
/// </remarks>
public class CaptchaRateLimitOptions
{
    /// <summary>Configuration section shared with the authentication limiter (REQ-NFR-005).</summary>
    public const string ConfigurationSection = "RateLimiting";

    /// <summary>Configuration key for <see cref="IssuePermitLimit"/>.</summary>
    public const string IssuePermitLimitKey = "RateLimiting:CaptchaIssuePermitLimit";

    /// <summary>Configuration key for <see cref="IssueWindowSeconds"/>.</summary>
    public const string IssueWindowSecondsKey = "RateLimiting:CaptchaIssueWindowSeconds";

    /// <summary>Configuration key for <see cref="FailurePermitLimit"/>.</summary>
    public const string FailurePermitLimitKey = "RateLimiting:CaptchaFailurePermitLimit";

    /// <summary>Configuration key for <see cref="FailureWindowSeconds"/>.</summary>
    public const string FailureWindowSecondsKey = "RateLimiting:CaptchaFailureWindowSeconds";

    /// <summary>
    /// Challenges a client may be issued per window before issuance is refused.
    /// </summary>
    /// <remarks>
    /// Sized against real use, not against the attacker: opening a post page issues one
    /// challenge, and a visitor who comments, rates and reloads the image a few times might reach
    /// six or seven. Twenty a minute leaves a wide margin for a legitimate visitor while capping
    /// a scripted client at a rate that cannot exhaust the answer cache.
    /// </remarks>
    public const int DefaultIssuePermitLimit = 20;

    /// <summary>Length of the issuance window, in seconds.</summary>
    public const int DefaultIssueWindowSeconds = 60;

    /// <summary>
    /// Failed validations a client may accumulate per window before every further attempt is
    /// refused without even consulting the challenge store.
    /// </summary>
    /// <remarks>
    /// Five wrong answers in five minutes is far beyond a human misreading the image. Against a
    /// brute-forcer the arithmetic is decisive: the visual alphabet has 27 characters over 5
    /// positions, so five guesses per five minutes explores 14.3 million possibilities at a rate
    /// that would take centuries.
    /// </remarks>
    public const int DefaultFailurePermitLimit = 5;

    /// <summary>Length of the failure window, in seconds.</summary>
    public const int DefaultFailureWindowSeconds = 300;

    /// <summary>
    /// Gets or sets the challenges a client may be issued per <see cref="IssueWindowSeconds"/>.
    /// </summary>
    public int IssuePermitLimit { get; set; } = DefaultIssuePermitLimit;

    /// <summary>
    /// Gets or sets the length of the issuance window, in seconds.
    /// </summary>
    public int IssueWindowSeconds { get; set; } = DefaultIssueWindowSeconds;

    /// <summary>
    /// Gets or sets the failed validations a client may accumulate per
    /// <see cref="FailureWindowSeconds"/>.
    /// </summary>
    public int FailurePermitLimit { get; set; } = DefaultFailurePermitLimit;

    /// <summary>
    /// Gets or sets the length of the failure window, in seconds.
    /// </summary>
    public int FailureWindowSeconds { get; set; } = DefaultFailureWindowSeconds;

    /// <summary>
    /// Gets the issuance window as a <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan IssueWindow => TimeSpan.FromSeconds(IssueWindowSeconds);

    /// <summary>
    /// Gets the failure window as a <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan FailureWindow => TimeSpan.FromSeconds(FailureWindowSeconds);

    /// <summary>
    /// Reads the caps from the application's <c>RateLimiting</c> configuration section.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A security limit must never be silently switched off by a
    /// typo, so a missing, unparsable, zero or negative value falls back to the compiled default
    /// instead of being taken literally.</para>
    /// <para><b>Flow:</b> read each of the four keys → validate → build the options object.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="configuration">The application configuration, or null in a unit test.</param>
    /// <returns>The configured caps, with defaults filled in.</returns>
    public static CaptchaRateLimitOptions FromConfiguration(IConfiguration? configuration)
    {
        return new CaptchaRateLimitOptions
        {
            IssuePermitLimit = ReadPositive(configuration, IssuePermitLimitKey, DefaultIssuePermitLimit),
            IssueWindowSeconds = ReadPositive(configuration, IssueWindowSecondsKey, DefaultIssueWindowSeconds),
            FailurePermitLimit = ReadPositive(configuration, FailurePermitLimitKey, DefaultFailurePermitLimit),
            FailureWindowSeconds = ReadPositive(configuration, FailureWindowSecondsKey, DefaultFailureWindowSeconds)
        };
    }

    /// <summary>
    /// Reads one positive integer setting, falling back when it is absent or nonsensical.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Zero would mean "permit nothing", which locks every visitor
    /// out of commenting; a negative value is meaningless. Both are treated as unset.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="configuration">The application configuration, or null.</param>
    /// <param name="key">The fully qualified configuration key.</param>
    /// <param name="fallback">The compiled default.</param>
    /// <returns>The configured value, or the fallback.</returns>
    private static int ReadPositive(IConfiguration? configuration, string key, int fallback)
    {
        var raw = configuration?[key];
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
