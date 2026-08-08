using BlogModels.Models;
namespace BlogModels.Interfaces;

/// <summary>
/// Authentication surface consumed by the UI layer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Wraps <c>BlogEngine.Services.AuthSvc</c> for Blazor components,
/// handling the encrypt/decrypt envelope the engine expects.</para>
/// <para><b>Code Flow:</b> component → <c>IAuthService</c> → <c>AuthSvc</c> → repositories.</para>
/// <para><b>Dependencies:</b> Implemented by <c>TechieBlog.Services.AuthService</c> in the host.</para>
/// <para><b>Usage:</b> Inject into components; never call <c>AuthSvc</c> from the UI directly.</para>
/// <para><b>REQ-FN-006 (BRD-1 retired / BRD-3 rev):</b> the public self-service signup method was
/// removed. Staff accounts are created by an administrator through
/// <c>AuthSvc.CreateStaffAccount</c>, which still enforces <c>PasswordValidator</c>.</para>
/// </remarks>
public interface IAuthService
{
    /// <summary>
    /// Signs a user in from an encrypted credential envelope.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Credentials travel encrypted, so the implementation decrypts the
    /// envelope before delegating to the engine. A failed sign-in is reported as <c>null</c> rather
    /// than an exception, and the reason is deliberately not disclosed to the caller.</para>
    /// <para><b>Side Effects:</b> Records a login row and updates the per-account failure throttle.</para>
    /// </remarks>
    /// <param name="user">Envelope carrying the encrypted email and password.</param>
    /// <returns>The authenticated user, or <c>null</c> when the credentials do not match.</returns>
    Task<AppUser?> LoginAsync(SvcData user);

    /// <summary>
    /// Resolves the user behind a previously issued access token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used to restore a session on reconnect. An expired, revoked or
    /// unrecognised token yields <c>null</c> so the caller falls back to the anonymous state.</para>
    /// <para><b>Side Effects:</b> None beyond the token lookup.</para>
    /// </remarks>
    /// <param name="accessToken">The bearer token issued at sign-in.</param>
    /// <returns>The token's owner, or <c>null</c> when the token is not valid.</returns>
    Task<AppUser?> GetUserByAccessTokenAsync(string accessToken);

    /// <summary>
    /// Exchanges a refresh token for a new session.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets a session outlive its access token without forcing the user
    /// to re-enter credentials. A refresh token that has expired or already been spent yields
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> Issues a replacement token pair and records the new session.</para>
    /// </remarks>
    /// <param name="refreshRequest">The refresh token to redeem.</param>
    /// <returns>The user with a refreshed token pair, or <c>null</c> when the token is not valid.</returns>
    Task<AppUser?> RefreshTokenAsync(RefreshRequest refreshRequest);

    /// <summary>
    /// Starts the forgotten-password flow for an email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reports success even when the address has no account, so the
    /// response cannot be used to discover which addresses are registered.</para>
    /// <para><b>Side Effects:</b> Persists a reset token and sends an email when the account exists.</para>
    /// </remarks>
    /// <param name="user">Envelope carrying the target email address.</param>
    /// <returns><c>true</c> when the request was accepted for processing.</returns>
    Task<bool> SendPasswordResetEmailAsync(SvcData user);

    /// <summary>
    /// Completes the forgotten-password flow by redeeming a reset token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token works exactly once and only inside its validity window;
    /// the replacement password must satisfy the password policy.</para>
    /// <para><b>Side Effects:</b> Rewrites the stored password hash and marks the token used.</para>
    /// </remarks>
    /// <param name="user">Envelope carrying the reset token and the new password.</param>
    /// <returns><c>true</c> when the password was changed.</returns>
    Task<bool> ResetPasswordAsync(SvcData user);

    /// <summary>
    /// Confirms an email address from a verification link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Redeems a single-use verification token and promotes whatever the
    /// token was issued for — a pending comment, rating or subscription.</para>
    /// <para><b>Side Effects:</b> Marks the token consumed and flips the target row to confirmed.</para>
    /// </remarks>
    /// <param name="verifyEmailData">Envelope carrying the verification token.</param>
    /// <returns>The verified user, or <c>null</c> when the token is spent or unknown.</returns>
    Task<AppUser?> VerifyEmailAsync(SvcData verifyEmailData);

    /// <summary>
    /// Re-sends an outstanding verification email.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Covers a mail that was lost or expired. The previously issued
    /// token is superseded so only the newest link works.</para>
    /// <para><b>Side Effects:</b> Issues a fresh token and sends an email.</para>
    /// </remarks>
    /// <param name="resendEmailData">Envelope identifying the address to re-verify.</param>
    /// <returns><c>true</c> when a new verification mail was sent.</returns>
    Task<bool> ResendVerifiEmailAsync(SvcData resendEmailData);

    /// <summary>
    /// Changes a pending address and sends verification to the new one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used when the original address was mistyped, so the pending
    /// record is corrected before a new link is issued against the corrected address.</para>
    /// <para><b>Side Effects:</b> Updates the pending address, issues a fresh token and sends an email.</para>
    /// </remarks>
    /// <param name="correctedEmailData">Envelope carrying the corrected address.</param>
    /// <returns><c>true</c> when the address was updated and a verification mail was sent.</returns>
    Task<bool> UpdateNSendVerifiEmailAsync(SvcData correctedEmailData);
}
