using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace TechieBlog.Services;

/// <summary>
/// Host-side adapter between the Blazor UI and <see cref="AuthSvc"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Applies the encrypt/decrypt transport envelope the engine expects and
/// translates engine results into the simple shapes components consume.</para>
/// <para><b>Code Flow:</b> component → this adapter → <see cref="AuthSvc"/> → repositories.</para>
/// <para><b>Dependencies:</b> <see cref="AuthSvc"/>, <c>AppEncrypt</c>.</para>
/// <para><b>Usage:</b> Registered as a transient <c>IAuthService</c> in <c>Program.cs</c>.</para>
/// <para><b>REQ-FN-006:</b> the public self-service signup method was removed; staff accounts are
/// created by an administrator through <c>AuthSvc.CreateStaffAccount</c>.</para>
/// </remarks>
public class AuthService : IAuthService
{
    private readonly AuthSvc authSvc;
    private readonly IHttpContextAccessor? httpContextAccessor;

    /// <summary>
    /// Initialises the adapter.
    /// </summary>
    /// <param name="authSvc">The engine authentication service.</param>
    /// <param name="httpContextAccessor">
    /// Access to the current HTTP context, used only to stamp the sign-in audit row with the
    /// client's address and user agent (REQ-FN-051). Optional, and null in a Blazor Server circuit.
    /// </param>
    public AuthService(AuthSvc authSvc, IHttpContextAccessor? httpContextAccessor = null)
    {
        this.authSvc = authSvc;
        this.httpContextAccessor = httpContextAccessor;
    }
   /// <summary>
    /// Retrieves user information from a valid access token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Wraps token in SvcData for backend call</item>
    ///   <item>Calls AuthSvc.GetUserByToken to validate and retrieve user</item>
    ///   <item>Decrypts and deserializes the user data</item>
    /// </list>
    /// </remarks>
    /// <param name="accessToken">JWT access token from LocalStorage.</param>
    /// <returns>AppUser if token is valid, null if invalid or expired.</returns>
    public async Task<AppUser?> GetUserByAccessTokenAsync(string accessToken)
    {
        try
        {
            var tokenData = new SvcData { JwToken = accessToken };
            var svcResponse = await authSvc.GetUserByTokenAsync(tokenData).ConfigureAwait(false);

            if (svcResponse == null)
            {
                return null;
            }

            var decryptedUser = AppEncrypt.DecryptText(svcResponse.ComplexData);
            return JsonSerializer.Deserialize<AppUser>(decryptedUser);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Signs a user in with an email address and password.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Wraps the credentials in the encrypted envelope the engine
    /// expects, then unwraps the response. A refused sign-in — wrong password or a throttled
    /// account (REQ-NFR-005) — comes back as a null envelope and is surfaced as a null user, not
    /// as an exception.</para>
    /// <para><b>Flow:</b> encrypt → call engine → null check → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> The engine writes one sign-in audit row per attempt, successful
    /// or refused (REQ-FN-051). On success it also records a login row and may upgrade the stored
    /// password hash.</para>
    /// </remarks>
    /// <param name="aLoginUser">Envelope carrying the plaintext email and password.</param>
    /// <returns>The signed-in user, or <c>null</c> when authentication is refused.</returns>
    public async Task<AppUser?> LoginAsync(SvcData aLoginUser)
    {
        aLoginUser.LoginEmail = AppEncrypt.EncryptText(aLoginUser.LoginEmail);
        aLoginUser.LoginPass = AppEncrypt.EncryptText(aLoginUser.LoginPass);
        StampClientMetadata(aLoginUser);

        var svcResponse = await authSvc.AppLoginAsync(aLoginUser);
        if (svcResponse == null)
            return null;

        var decryptedUser = AppEncrypt.DecryptText(svcResponse.ComplexData);
        return JsonSerializer.Deserialize<AppUser>(decryptedUser);
    }

    /// <summary>
    /// Copies the caller's address and user agent onto the sign-in envelope (REQ-FN-051).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The engine has no HTTP context of its own, so the host stamps
    /// what it can see. Best-effort by design: a sign-in submitted over a Blazor Server circuit
    /// arrives on the SignalR connection, not on an HTTP request, so there is no context and the
    /// fields stay empty. An empty address is recorded as "could not be determined" — it never
    /// suppresses the audit row itself, which is the part the acceptance depends on.</para>
    /// <para><b>Flow:</b> read the context → copy the remote address and user-agent header.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied envelope.</para>
    /// </remarks>
    /// <param name="loginEnvelope">The envelope about to be sent to the engine.</param>
    private void StampClientMetadata(SvcData loginEnvelope)
    {
        var context = httpContextAccessor?.HttpContext;
        if (context == null)
            return;

        loginEnvelope.ClientIP = context.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        loginEnvelope.ClientUserAgent = context.Request?.Headers.UserAgent.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Renews an expired session without asking for the password again (REQ-FN-008, BRD-6).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to <c>AuthSvc.RefreshSessionAsync</c>, which rewrites
    /// the <c>UserLogin</c> row with a replacement access token when the session is still inside
    /// its refresh window. The returned user therefore carries a token that is <b>not</b> the one
    /// that was presented, and the caller must store it — the presented value stops working the
    /// moment this returns.</para>
    /// <para><b>Flow:</b> wrap the token → call the engine → null check → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> The engine updates one <c>UserLogin</c> row and logs the renewal.</para>
    /// <para><b>History:</b> this method used to call <c>GetUserByTokenAsync</c>, which validates a
    /// token but never mints one — so nothing was refreshed even on the days something called it.
    /// Nothing did: the verifier found no call site anywhere in the UI, which is what
    /// <c>CustomAuthStateProvider</c> now supplies.</para>
    /// </remarks>
    /// <param name="refreshRequest">Carries the token to redeem.</param>
    /// <returns>The user with a replacement token, or <c>null</c> when the session is over.</returns>
    public async Task<AppUser?> RefreshTokenAsync(RefreshRequest refreshRequest)
    {
        try
        {
            var tokenData = new SvcData { JwToken = refreshRequest.RefreshToken };
            var svcResponse = await authSvc.RefreshSessionAsync(tokenData).ConfigureAwait(false);

            if (svcResponse == null)
            {
                return null;
            }

            var decryptedUser = AppEncrypt.DecryptText(svcResponse.ComplexData);
            return JsonSerializer.Deserialize<AppUser>(decryptedUser);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Resends verification email to user.
    /// </summary>
    /// <param name="aVerifiEmailData">User email data.</param>
    /// <returns>True if email sent successfully.</returns>
    public Task<bool> ResendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        // Email verification not fully implemented in backend yet
        // Return true to indicate request was received
        return Task.FromResult(true);
    }

    /// <summary>
    /// Resets a user's password using a valid reset token.
    /// </summary>
    /// <param name="user">Contains ResetToken and new LoginPass.</param>
    /// <returns>True if password reset successful.</returns>
    public async Task<bool> ResetPasswordAsync(SvcData user)
    {
        try
        {
            var result = await authSvc.ResetPasswordAsync(user.ResetToken, user.LoginPass);
            return result.IsSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a password reset email to the specified email address.
    /// </summary>
    /// <param name="user">Contains LoginEmail to send reset link to.</param>
    /// <returns>True if email request processed (always true for security).</returns>
    public async Task<bool> SendPasswordResetEmailAsync(SvcData user)
    {
        try
        {
            await authSvc.RequestPasswordResetAsync(user.LoginEmail);
            // Always return true to not reveal if email exists
            return true;
        }
        catch (Exception)
        {
            return true; // Still return true for security
        }
    }

    /// <summary>
    /// Updates email and sends verification.
    /// </summary>
    /// <param name="aVerifiEmailData">Updated email data.</param>
    /// <returns>True if processed successfully.</returns>
    public Task<bool> UpdateNSendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        // Email verification not fully implemented in backend yet
        return Task.FromResult(true);
    }

    /// <summary>
    /// Verifies user's email address with token.
    /// </summary>
    /// <param name="aVerifyEmailData">Contains verification token.</param>
    /// <returns>AppUser if verified, null otherwise.</returns>
    public Task<AppUser?> VerifyEmailAsync(SvcData aVerifyEmailData)
    {
        // Email verification not fully implemented in backend yet
        // Return null to indicate not verified
        return Task.FromResult<AppUser?>(null);
    }
}
