using BlogEngine.Common;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for <see cref="LoginThrottle"/> (REQ-NFR-005, BRD-82).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The HTTP rate limiter cannot see sign-ins that arrive over an
/// established Blazor circuit, so this throttle is what actually stops credential stuffing.
/// The tests drive an injected clock so lockout expiry is asserted without sleeping.</para>
/// <para><b>Dependencies:</b> xUnit; no database or host required.</para>
/// </remarks>
public class LoginThrottleTests
{
    private const string Key = "user@techieblog.com";

    /// <summary>
    /// An account with no recorded failures is never blocked and reports no retry delay.
    /// </summary>
    [Fact]
    public void UnknownKeyIsNotBlocked()
    {
        var throttle = new LoginThrottle();

        Assert.False(throttle.IsBlocked(Key, out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    /// <summary>
    /// Failures below the limit are counted but do not lock the account, so an honest typo does
    /// not cost the user fifteen minutes.
    /// </summary>
    [Fact]
    public void FailuresBelowLimitDoNotBlock()
    {
        var throttle = new LoginThrottle();

        for (var attempt = 1; attempt < LoginThrottle.MaxFailuresPerWindow; attempt++)
        {
            Assert.Equal(attempt, throttle.RegisterFailure(Key));
        }

        Assert.False(throttle.IsBlocked(Key, out _));
    }

    /// <summary>
    /// Reaching the failure limit inside one window locks the account and reports a positive
    /// retry delay.
    /// </summary>
    [Fact]
    public void LimitReachedBlocksTheAccount()
    {
        var throttle = new LoginThrottle();

        for (var attempt = 0; attempt < LoginThrottle.MaxFailuresPerWindow; attempt++)
        {
            throttle.RegisterFailure(Key);
        }

        Assert.True(throttle.IsBlocked(Key, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    /// <summary>
    /// A lockout applies only to the offending account; other accounts sign in normally.
    /// </summary>
    [Fact]
    public void LockoutIsScopedToOneKey()
    {
        var throttle = new LoginThrottle();

        for (var attempt = 0; attempt < LoginThrottle.MaxFailuresPerWindow; attempt++)
        {
            throttle.RegisterFailure(Key);
        }

        Assert.True(throttle.IsBlocked(Key, out _));
        Assert.False(throttle.IsBlocked("other@techieblog.com", out _));
    }

    /// <summary>
    /// Throttle keys are matched case-insensitively, so varying the capitalisation of an email
    /// address cannot reset the counter.
    /// </summary>
    [Fact]
    public void KeyMatchingIgnoresCase()
    {
        var throttle = new LoginThrottle();

        for (var attempt = 0; attempt < LoginThrottle.MaxFailuresPerWindow; attempt++)
        {
            throttle.RegisterFailure(Key.ToUpperInvariant());
        }

        Assert.True(throttle.IsBlocked(Key, out _));
    }

    /// <summary>
    /// A successful sign-in clears the failure counter so the next mistyped password starts a
    /// fresh window.
    /// </summary>
    [Fact]
    public void SuccessClearsFailureCounter()
    {
        var throttle = new LoginThrottle();
        throttle.RegisterFailure(Key);
        throttle.RegisterFailure(Key);

        throttle.RegisterSuccess(Key);

        Assert.Equal(1, throttle.RegisterFailure(Key));
    }

    /// <summary>
    /// Once the lockout period has elapsed the account is released without any further action,
    /// proven by advancing the injected clock past the lockout window.
    /// </summary>
    [Fact]
    public void LockoutExpiresAfterTheConfiguredWindow()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var throttle = new LoginThrottle(() => now);

        for (var attempt = 0; attempt < LoginThrottle.MaxFailuresPerWindow; attempt++)
        {
            throttle.RegisterFailure(Key);
        }
        Assert.True(throttle.IsBlocked(Key, out _));

        now = now.AddMinutes(LoginThrottle.LockoutMinutes + 1);

        Assert.False(throttle.IsBlocked(Key, out _));
    }

    /// <summary>
    /// Failures spread further apart than the counting window never accumulate into a lockout,
    /// so a user who mistypes once a day is not punished.
    /// </summary>
    [Fact]
    public void FailuresOutsideTheWindowDoNotAccumulate()
    {
        var now = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);
        var throttle = new LoginThrottle(() => now);

        throttle.RegisterFailure(Key);
        now = now.AddMinutes(LoginThrottle.FailureWindowMinutes + 1);

        Assert.Equal(1, throttle.RegisterFailure(Key));
        Assert.False(throttle.IsBlocked(Key, out _));
    }

    /// <summary>
    /// A blank key is ignored rather than creating a shared bucket every anonymous request would
    /// contend on.
    /// </summary>
    [Fact]
    public void BlankKeyIsIgnored()
    {
        var throttle = new LoginThrottle();

        Assert.Equal(0, throttle.RegisterFailure("  "));
        Assert.False(throttle.IsBlocked("  ", out _));
    }
}
