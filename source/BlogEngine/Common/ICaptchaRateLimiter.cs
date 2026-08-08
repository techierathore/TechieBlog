namespace BlogEngine.Common;

/// <summary>
/// Caps how many captcha challenges a client may be issued and how many it may fail. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The captcha's single-use rule and five-minute expiry protect one
/// challenge; they say nothing about volume. Without this, a client could mint challenges without
/// limit (each one a server-side cache entry) or grind guesses against an endless supply of fresh
/// ones. This is the volume control, and it is the captcha counterpart of the authentication
/// limiter established by REQ-NFR-005.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>RateLimitedCaptchaSvc</c> calls <see cref="IsFailureBlocked"/> first - a client in
///     failure lockout is refused before any work is done, including the issuance of a new
///     challenge.</item>
///   <item>Issuance then consumes a permit through <see cref="TryIssue"/>.</item>
///   <item>Every rejected answer is recorded with <see cref="RegisterFailure"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None beyond the BCL - counters are held in process.</para>
///
/// <para><b>Scale-out note:</b> like <see cref="LoginThrottle"/>, the counters are per process, so
/// a multi-instance deployment divides every cap by the instance count. Register a distributed
/// implementation of this interface and nothing else has to change.</para>
///
/// <para><b>Usage:</b> Registered as a singleton so counters outlive a single circuit.</para>
/// </remarks>
public interface ICaptchaRateLimiter
{
    /// <summary>
    /// Consumes one issuance permit for a client.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Fixed window per client. The attempt is counted whether or not
    /// it is permitted, so a client that keeps hammering keeps its own window pinned rather than
    /// slipping back under the cap by retrying.</para>
    /// <para><b>Flow:</b> normalise the key → advance or restart the window → compare to the cap.</para>
    /// <para><b>Side Effects:</b> Mutates the shared issuance counters; logs when a cap trips.</para>
    /// </remarks>
    /// <param name="clientKey">The client identity from <c>ICaptchaClientKeyProvider</c>.</param>
    /// <param name="retryAfter">Receives the wait before the window reopens, or <see cref="TimeSpan.Zero"/>.</param>
    /// <returns><c>true</c> when a challenge may be issued.</returns>
    bool TryIssue(string clientKey, out TimeSpan retryAfter);

    /// <summary>
    /// Indicates whether a client has failed too many validations to be served at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Read-only, so it is safe to call on every request. An expired
    /// window is dropped on read, which is also what keeps the counter map from growing without
    /// bound for clients that never come back.</para>
    /// <para><b>Flow:</b> normalise the key → look up → expire or compare to the cap.</para>
    /// <para><b>Side Effects:</b> May remove an expired counter.</para>
    /// </remarks>
    /// <param name="clientKey">The client identity from <c>ICaptchaClientKeyProvider</c>.</param>
    /// <param name="retryAfter">Receives the remaining lockout, or <see cref="TimeSpan.Zero"/>.</param>
    /// <returns><c>true</c> while the client must be refused.</returns>
    bool IsFailureBlocked(string clientKey, out TimeSpan retryAfter);

    /// <summary>
    /// Records one failed captcha validation against a client.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every rejected answer counts, including an answer to a
    /// challenge that had already expired or been consumed - replaying a burned id is exactly what
    /// an attacker does.</para>
    /// <para><b>Flow:</b> normalise the key → advance or restart the window.</para>
    /// <para><b>Side Effects:</b> Mutates the shared failure counters; logs when the cap trips.</para>
    /// </remarks>
    /// <param name="clientKey">The client identity from <c>ICaptchaClientKeyProvider</c>.</param>
    void RegisterFailure(string clientKey);
}
