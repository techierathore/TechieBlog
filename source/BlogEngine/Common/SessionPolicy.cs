using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace BlogEngine.Common;

/// <summary>
/// The two durations that define a signed-in session: how long an access token is usable, and how
/// long it may keep being renewed before the user has to sign in again (REQ-FN-008).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Before this type existed the two numbers were literals in two different
/// files that disagreed with each other — <c>AuthSvc.GenerateJWToken</c> stamped a 15-day
/// <c>exp</c> claim while <c>AuthSvc.IssueSessionAsync</c> wrote a <c>UserLogin</c> row expiring
/// after 2 days — and neither was ever read on the way back in, so a session in practice never
/// ended and never needed refreshing. Naming the two durations in one place is what makes the
/// refresh path meaningful: the short one is what expires, the long one is what authorises the
/// reissue.</para>
///
/// <para><b>Code Flow:</b> <c>BlogSvcInitializer</c> registers a single instance built from
/// <see cref="FromConfiguration"/>; <c>AuthSvc</c> takes it as a constructor dependency and uses
/// <see cref="AccessTokenLifetime"/> when stamping the JWT and <see cref="RefreshWindow"/> when
/// writing or sliding the <c>UserLogin</c> row.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> only, and only inside
/// <see cref="FromConfiguration"/>. The type itself is an immutable value.</para>
///
/// <para><b>Why the access lifetime is configurable at all:</b> a token that lives an hour cannot
/// be observed expiring inside a smoke run, so a build or verification pass sets
/// <c>Auth:AccessTokenMinutes</c> to a fraction of a minute and watches the refresh happen for
/// real rather than asserting it in a unit test. The values are ordinary configuration, not
/// secrets, so they may live in <c>appsettings.Development.json</c> or in the environment
/// (<c>Auth__AccessTokenMinutes</c>).</para>
///
/// <para><b>Usage:</b> Prefer <see cref="Default"/> in tests and any caller that has no
/// configuration to read. Construct explicitly only when a specific pair of durations is the point
/// of the test.</para>
/// </remarks>
public sealed class SessionPolicy
{
    /// <summary>
    /// Configuration path holding the access-token lifetime, in minutes. Fractions are accepted so
    /// a smoke run can ask for a lifetime measured in seconds.
    /// </summary>
    public const string AccessTokenMinutesPath = "Auth:AccessTokenMinutes";

    /// <summary>
    /// Configuration path holding the refresh window, in days.
    /// </summary>
    public const string RefreshWindowDaysPath = "Auth:RefreshWindowDays";

    /// <summary>
    /// Access-token lifetime used when configuration says nothing, in minutes.
    /// </summary>
    public const double DefaultAccessTokenMinutes = 60;

    /// <summary>
    /// The shortest access-token lifetime that can actually be expressed in a JWT.
    /// </summary>
    /// <remarks>
    /// A JWT's <c>exp</c> and <c>nbf</c> claims are whole seconds since the epoch, and
    /// <c>JwtSecurityTokenHandler</c> refuses to build a token whose expiry is not strictly after
    /// its not-before (<c>IDX12401</c>). A lifetime under a second therefore truncates to the same
    /// instant as the issue time and throws — which would take out <b>every sign-in</b>, not just
    /// the refresh path, on nothing worse than a mistyped configuration value.
    /// </remarks>
    public static readonly TimeSpan MinimumAccessTokenLifetime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Refresh window used when configuration says nothing, in days. It matches the 15-day
    /// <c>exp</c> claim the original implementation stamped, because that claim — not the 2-day
    /// <c>UserLogin</c> row — is what the product behaved like in practice.
    /// </summary>
    public const double DefaultRefreshWindowDays = 14;

    /// <summary>
    /// The policy used when no configuration is supplied.
    /// </summary>
    public static SessionPolicy Default { get; } = new(
        TimeSpan.FromMinutes(DefaultAccessTokenMinutes),
        TimeSpan.FromDays(DefaultRefreshWindowDays));

    /// <summary>
    /// How long an issued access token is accepted before it must be refreshed.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; }

    /// <summary>
    /// How long the session may keep renewing itself. Measured from the most recent sign-in or
    /// refresh, so an actively used session slides forward and an abandoned one dies.
    /// </summary>
    public TimeSpan RefreshWindow { get; }

    /// <summary>
    /// Creates a policy from two explicit durations.
    /// </summary>
    /// <param name="accessTokenLifetime">How long an access token stays usable; must be at least
    /// <see cref="MinimumAccessTokenLifetime"/>.</param>
    /// <param name="refreshWindow">How long the session may be renewed; must be at least the
    /// access-token lifetime, otherwise a token would outlive the session authorising its reissue
    /// and every refresh would be refused the moment it became necessary.</param>
    /// <exception cref="ArgumentOutOfRangeException">Either duration is unusable.</exception>
    public SessionPolicy(TimeSpan accessTokenLifetime, TimeSpan refreshWindow)
    {
        if (accessTokenLifetime < MinimumAccessTokenLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessTokenLifetime),
                accessTokenLifetime,
                $"The access-token lifetime must be at least {MinimumAccessTokenLifetime}.");
        }

        if (refreshWindow < accessTokenLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshWindow), refreshWindow,
                "The refresh window must be at least as long as the access-token lifetime.");
        }

        AccessTokenLifetime = accessTokenLifetime;
        RefreshWindow = refreshWindow;
    }

    /// <summary>
    /// Reads the policy from host configuration, falling back to the defaults per value.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Each path is optional and is parsed independently, so setting
    /// only the access-token lifetime — which is what a smoke run does — leaves the refresh window
    /// at its default. A value that is present but not a positive number is treated as absent
    /// rather than throwing: a typo in an operational knob must not stop the host from starting,
    /// and the default it falls back to is the safe one. A lifetime below
    /// <see cref="MinimumAccessTokenLifetime"/> is raised to it rather than rejected, for the same
    /// reason — the constructor throws on that value, and a host that will not start is a worse
    /// answer to "someone asked for half a second" than a host that gives them one.</para>
    /// <para><b>Flow:</b> read both paths → parse each with the invariant culture → construct.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="configuration">The host configuration.</param>
    /// <returns>The configured policy, or <see cref="Default"/> when neither path is set.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    public static SessionPolicy FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var accessMinutes = ReadPositiveNumber(configuration[AccessTokenMinutesPath], DefaultAccessTokenMinutes);
        var refreshDays = ReadPositiveNumber(configuration[RefreshWindowDaysPath], DefaultRefreshWindowDays);

        var requestedLifetime = TimeSpan.FromMinutes(accessMinutes);
        var accessTokenLifetime = requestedLifetime < MinimumAccessTokenLifetime
            ? MinimumAccessTokenLifetime
            : requestedLifetime;
        var refreshWindow = TimeSpan.FromDays(refreshDays);

        return refreshWindow < accessTokenLifetime
            ? new SessionPolicy(accessTokenLifetime, accessTokenLifetime)
            : new SessionPolicy(accessTokenLifetime, refreshWindow);
    }

    /// <summary>
    /// Parses an optional numeric configuration value.
    /// </summary>
    /// <param name="value">The raw configuration value; may be <c>null</c>.</param>
    /// <param name="fallback">The value to use when the setting is absent or unusable.</param>
    /// <returns>The parsed number, or <paramref name="fallback"/>.</returns>
    private static double ReadPositiveNumber(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var parsed = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
        return parsed && number > 0 ? number : fallback;
    }
}
