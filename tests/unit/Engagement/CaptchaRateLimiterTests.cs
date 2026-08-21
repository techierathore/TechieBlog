using System.Net;
using BlogEngine.Common;
using BlogEngine.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the per-client captcha issuance and failure caps. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the three properties the requirement names - issuance is capped,
/// failures are capped, and both windows reset - plus the two that make the caps usable rather
/// than merely present: a client under the cap is never touched, and one client's abuse never
/// spends another client's budget.</para>
/// <para><b>Code Flow:</b> Every test drives an injectable clock, so a five-minute window is
/// exercised without a five-minute test.</para>
/// <para><b>Dependencies:</b> xUnit only - the limiter holds its counters in process.</para>
/// <para><b>Usage:</b> Pure unit tests; no database, no HTTP, no waiting.</para>
/// </remarks>
public class CaptchaRateLimiterTests
{
    /// <summary>The client identity used by every single-client test.</summary>
    private const string ClientKey = "captcha-ip:203.0.113.7";

    /// <summary>A second identity, used to prove the caps are per client.</summary>
    private const string OtherClientKey = "captcha-ip:198.51.100.4";

    /// <summary>Fixed instant every test starts from, so no test depends on the wall clock.</summary>
    private static readonly DateTime StartTime = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A client is issued exactly as many challenges as the cap permits, and the last permitted
    /// request is not refused - a limiter that blocks normal use is a defect, not a defence.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceIsPermittedUpToTheCap()
    {
        var options = BuildOptions(issuePermitLimit: 3);
        var limiter = BuildLimiter(options, () => StartTime);

        for (var attempt = 1; attempt <= options.IssuePermitLimit; attempt++)
        {
            Assert.True(limiter.TryIssue(ClientKey, out _), $"attempt {attempt} should have been permitted");
        }
    }

    /// <summary>
    /// The request after the cap is refused and carries a positive wait, so the visitor can be
    /// told how long to hold off.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceIsRefusedPastTheCap()
    {
        var options = BuildOptions(issuePermitLimit: 3);
        var limiter = BuildLimiter(options, () => StartTime);
        ConsumeIssuePermits(limiter, options.IssuePermitLimit);

        var isPermitted = limiter.TryIssue(ClientKey, out var retryAfter);

        Assert.False(isPermitted);
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    /// <summary>
    /// Once the issuance window has fully elapsed the client starts a new window and is served
    /// again, which is what makes the cap a rate limit rather than a permanent ban.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceWindowResets()
    {
        var options = BuildOptions(issuePermitLimit: 3, issueWindowSeconds: 60);
        var now = StartTime;
        var limiter = BuildLimiter(options, () => now);
        ConsumeIssuePermits(limiter, options.IssuePermitLimit);
        Assert.False(limiter.TryIssue(ClientKey, out _));

        now = StartTime.AddSeconds(61);

        Assert.True(limiter.TryIssue(ClientKey, out _));
    }

    /// <summary>
    /// One client exhausting its issuance budget leaves every other client untouched, so a shared
    /// network or a single abusive visitor cannot take the site's captcha down for everyone.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceCapIsPerClient()
    {
        var options = BuildOptions(issuePermitLimit: 2);
        var limiter = BuildLimiter(options, () => StartTime);
        ConsumeIssuePermits(limiter, options.IssuePermitLimit);
        Assert.False(limiter.TryIssue(ClientKey, out _));

        Assert.True(limiter.TryIssue(OtherClientKey, out _));
    }

    /// <summary>
    /// A client is not blocked while its failure count is still below the cap, so an ordinary
    /// visitor who misreads the distorted image a couple of times keeps working.
    /// </summary>
    [Fact]
    public void CaptchaFailuresAreToleratedUpToTheCap()
    {
        var options = BuildOptions(failurePermitLimit: 3);
        var limiter = BuildLimiter(options, () => StartTime);

        for (var failure = 1; failure < options.FailurePermitLimit; failure++)
        {
            limiter.RegisterFailure(ClientKey);
            Assert.False(limiter.IsFailureBlocked(ClientKey, out _), $"failure {failure} should not have blocked");
        }
    }

    /// <summary>
    /// The failure that reaches the cap blocks the client and reports a positive wait.
    /// </summary>
    [Fact]
    public void CaptchaFailuresBlockAtTheCap()
    {
        var options = BuildOptions(failurePermitLimit: 3);
        var limiter = BuildLimiter(options, () => StartTime);
        RegisterFailures(limiter, options.FailurePermitLimit);

        var isBlocked = limiter.IsFailureBlocked(ClientKey, out var retryAfter);

        Assert.True(isBlocked);
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    /// <summary>
    /// Once the failure window has fully elapsed the lockout lifts by itself, with no operator
    /// action and no restart.
    /// </summary>
    [Fact]
    public void CaptchaFailureWindowResets()
    {
        var options = BuildOptions(failurePermitLimit: 3, failureWindowSeconds: 300);
        var now = StartTime;
        var limiter = BuildLimiter(options, () => now);
        RegisterFailures(limiter, options.FailurePermitLimit);
        Assert.True(limiter.IsFailureBlocked(ClientKey, out _));

        now = StartTime.AddSeconds(301);

        Assert.False(limiter.IsFailureBlocked(ClientKey, out _));
    }

    /// <summary>
    /// The wait reported to a blocked client shrinks as its window runs down, so the message a
    /// visitor sees on a second attempt is not a fresh full-length delay.
    /// </summary>
    [Fact]
    public void CaptchaFailureRetryAfterCountsDown()
    {
        var options = BuildOptions(failurePermitLimit: 2, failureWindowSeconds: 300);
        var now = StartTime;
        var limiter = BuildLimiter(options, () => now);
        RegisterFailures(limiter, options.FailurePermitLimit);
        limiter.IsFailureBlocked(ClientKey, out var firstRetry);

        now = StartTime.AddSeconds(100);
        limiter.IsFailureBlocked(ClientKey, out var laterRetry);

        Assert.True(laterRetry < firstRetry);
    }

    /// <summary>
    /// One client's failures never lock out another, which is the property that keeps a shared
    /// address from becoming a denial of service against innocent visitors.
    /// </summary>
    [Fact]
    public void CaptchaFailureCapIsPerClient()
    {
        var options = BuildOptions(failurePermitLimit: 2);
        var limiter = BuildLimiter(options, () => StartTime);
        RegisterFailures(limiter, options.FailurePermitLimit);

        Assert.True(limiter.IsFailureBlocked(ClientKey, out _));
        Assert.False(limiter.IsFailureBlocked(OtherClientKey, out _));
    }

    /// <summary>
    /// A caller that supplies no identity is still counted rather than exempted, because an
    /// unattributable request would otherwise be the cheapest way around the cap.
    /// </summary>
    [Fact]
    public void CaptchaBlankClientKeyIsStillCounted()
    {
        var options = BuildOptions(issuePermitLimit: 1);
        var limiter = BuildLimiter(options, () => StartTime);

        Assert.True(limiter.TryIssue(string.Empty, out _));
        Assert.False(limiter.TryIssue(string.Empty, out _));
    }

    /// <summary>
    /// The caps are read from the RateLimiting section that REQ-NFR-005 established, so a
    /// deployment tunes every limiter in one place.
    /// </summary>
    [Fact]
    public void CaptchaLimitsComeFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CaptchaRateLimitOptions.IssuePermitLimitKey] = "7",
                [CaptchaRateLimitOptions.IssueWindowSecondsKey] = "30",
                [CaptchaRateLimitOptions.FailurePermitLimitKey] = "3",
                [CaptchaRateLimitOptions.FailureWindowSecondsKey] = "600"
            })
            .Build();

        var options = CaptchaRateLimitOptions.FromConfiguration(configuration);

        Assert.Equal(7, options.IssuePermitLimit);
        Assert.Equal(30, options.IssueWindowSeconds);
        Assert.Equal(3, options.FailurePermitLimit);
        Assert.Equal(600, options.FailureWindowSeconds);
    }

    /// <summary>
    /// A missing, unparsable or non-positive setting falls back to the compiled default instead of
    /// being taken literally, so a typo can never switch a security cap off.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-5")]
    public void CaptchaLimitsFallBackWhenMisconfigured(string? configuredValue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CaptchaRateLimitOptions.IssuePermitLimitKey] = configuredValue
            })
            .Build();

        var options = CaptchaRateLimitOptions.FromConfiguration(configuration);

        Assert.Equal(CaptchaRateLimitOptions.DefaultIssuePermitLimit, options.IssuePermitLimit);
    }

    /// <summary>
    /// An IPv6 client is bucketed by its /64 prefix, so rotating the host bits of an allocation
    /// does not mint a new identity per request.
    /// </summary>
    [Fact]
    public void CaptchaClientKeyMasksIpV6ToPrefix()
    {
        var first = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"));
        var second = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:2:1111:2222:3333:4444"));

        Assert.Equal(first, second);
        Assert.EndsWith("/64", first);
    }

    /// <summary>
    /// An IPv4 client keeps its whole address, and the IPv4-mapped IPv6 form a dual-stack Kestrel
    /// reports resolves to the same bucket rather than a second one.
    /// </summary>
    [Fact]
    public void CaptchaClientKeyUnwrapsMappedIpV4()
    {
        var plain = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("203.0.113.7"));
        var mapped = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("::ffff:203.0.113.7"));

        Assert.Equal("203.0.113.7", plain);
        Assert.Equal(plain, mapped);
    }

    /// <summary>
    /// Builds a limiter over the supplied caps and clock.
    /// </summary>
    /// <param name="options">The caps under test.</param>
    /// <param name="clock">The test clock.</param>
    /// <returns>The limiter.</returns>
    private static CaptchaRateLimiter BuildLimiter(CaptchaRateLimitOptions options, Func<DateTime> clock)
    {
        return new CaptchaRateLimiter(options, clock, NullLogger<CaptchaRateLimiter>.Instance);
    }

    /// <summary>
    /// Builds a caps object with small, readable numbers.
    /// </summary>
    /// <param name="issuePermitLimit">Challenges permitted per issuance window.</param>
    /// <param name="issueWindowSeconds">Length of the issuance window.</param>
    /// <param name="failurePermitLimit">Failures permitted per failure window.</param>
    /// <param name="failureWindowSeconds">Length of the failure window.</param>
    /// <returns>The caps.</returns>
    private static CaptchaRateLimitOptions BuildOptions(
        int issuePermitLimit = 20,
        int issueWindowSeconds = 60,
        int failurePermitLimit = 5,
        int failureWindowSeconds = 300)
    {
        return new CaptchaRateLimitOptions
        {
            IssuePermitLimit = issuePermitLimit,
            IssueWindowSeconds = issueWindowSeconds,
            FailurePermitLimit = failurePermitLimit,
            FailureWindowSeconds = failureWindowSeconds
        };
    }

    /// <summary>
    /// Uses up the given number of issuance permits for the standard client.
    /// </summary>
    /// <param name="limiter">The limiter under test.</param>
    /// <param name="count">How many permits to consume.</param>
    private static void ConsumeIssuePermits(ICaptchaRateLimiter limiter, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            limiter.TryIssue(ClientKey, out _);
        }
    }

    /// <summary>
    /// Records the given number of failures for the standard client.
    /// </summary>
    /// <param name="limiter">The limiter under test.</param>
    /// <param name="count">How many failures to record.</param>
    private static void RegisterFailures(ICaptchaRateLimiter limiter, int count)
    {
        for (var failure = 0; failure < count; failure++)
        {
            limiter.RegisterFailure(ClientKey);
        }
    }
}
