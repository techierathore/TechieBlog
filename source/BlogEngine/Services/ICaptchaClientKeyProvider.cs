namespace BlogEngine.Services;

/// <summary>
/// Supplies the identity the captcha rate limiter counts against. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> "Per client" has to mean something concrete before a cap can be enforced,
/// and getting it wrong breaks the feature in one of two ways: too coarse and one visitor locks
/// out a whole office, too easily rotated and the cap is decorative. This interface isolates that
/// judgement in one place so it can be reviewed and replaced.</para>
///
/// <para><b>Code Flow:</b> <c>RateLimitedCaptchaSvc</c> asks for the key on every issuance and
/// every validation, and hands it straight to <c>ICaptchaRateLimiter</c>.</para>
///
/// <para><b>Dependencies:</b> Implementation-specific.</para>
///
/// <para><b>Usage:</b> Register per scope. In Blazor Server a scope is a circuit, so a scoped
/// implementation may resolve the key once and hold it for the connection's life.</para>
/// </remarks>
public interface ICaptchaClientKeyProvider
{
    /// <summary>
    /// Returns the rate-limiting identity of the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The key must be stable for one client and never derived from
    /// anything the client controls, or an attacker rotates it and the cap disappears.</para>
    /// <para><b>Side Effects:</b> Implementations may cache the resolved value.</para>
    /// </remarks>
    /// <returns>A non-empty, opaque client key.</returns>
    string GetClientKey();
}
