namespace BlogModels.Interfaces;

/// <summary>
/// Throttles repeated authentication attempts for a single account (REQ-NFR-005, BRD-82).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The ASP.NET Core rate limiter guards the HTTP surface, but a Blazor
/// Server login runs over an already-established SignalR circuit and never crosses a
/// rate-limited endpoint again. This contract adds the per-account lockout that actually stops
/// credential stuffing on the interactive path.</para>
///
/// <para><b>Code Flow:</b> <c>AuthSvc.AppLogin</c> calls <see cref="IsBlocked"/> before touching
/// the database, then <see cref="RegisterFailure"/> or <see cref="RegisterSuccess"/> depending
/// on the outcome.</para>
///
/// <para><b>Dependencies:</b> Implemented in-process by <c>BlogEngine.Common.LoginThrottle</c>.
/// A multi-instance deployment should swap in a distributed implementation — the interface
/// exists so that substitution needs no change in <c>AuthSvc</c>.</para>
///
/// <para><b>Usage:</b> Register as a singleton so counters are shared across circuits.</para>
/// </remarks>
public interface ILoginThrottle
{
    /// <summary>
    /// Indicates whether further attempts for the key are currently refused.
    /// </summary>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    /// <param name="retryAfter">Receives the remaining lockout duration when blocked.</param>
    /// <returns><c>true</c> when the caller must be refused without checking the password.</returns>
    bool IsBlocked(string key, out TimeSpan retryAfter);

    /// <summary>
    /// Records a failed authentication attempt, starting a lockout once the limit is reached.
    /// </summary>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    /// <returns>The number of failures recorded inside the current window.</returns>
    int RegisterFailure(string key);

    /// <summary>
    /// Clears the failure counter after a successful authentication.
    /// </summary>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    void RegisterSuccess(string key);
}
