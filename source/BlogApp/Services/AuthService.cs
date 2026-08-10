using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using System.Text.Json;

namespace BlogApp.Services;

/// <summary>
/// Desktop-side adapter between the shared BlogUI screens and <see cref="AuthSvc"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogUI.CustomAuthStateProvider</c> and <c>LoginPage</c> depend on
/// <see cref="IAuthService"/>. The web head supplies <c>TechieBlog.Services.AuthService</c>; that
/// project is an ASP.NET Core executable and cannot be referenced from a MAUI head, so BlogApp
/// registers this equivalent adapter (REQ-UI-051). Behaviour is identical: the same encrypted
/// envelope, the same engine calls, the same roles — a Reader signing in to BlogApp is refused the
/// admin surfaces by exactly the policies the website applies.</para>
/// <para><b>Code Flow:</b> BlogUI component → this adapter → <see cref="AuthSvc"/> →
/// repositories → the live site database over the stored connection.</para>
/// <para><b>Dependencies:</b> <see cref="AuthSvc"/>, <c>AppEncrypt</c>.</para>
/// <para><b>Usage:</b> Registered as a transient <see cref="IAuthService"/> in
/// <c>MauiProgram</c>.</para>
/// </remarks>
public class AuthService : IAuthService
{
    private readonly AuthSvc authSvc;

    /// <summary>
    /// Creates the adapter.
    /// </summary>
    /// <param name="authSvc">The engine authentication service.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authSvc"/> is <c>null</c>.</exception>
    public AuthService(AuthSvc authSvc)
    {
        this.authSvc = authSvc ?? throw new ArgumentNullException(nameof(authSvc));
    }

    /// <summary>
    /// Resolves the signed-in user behind a stored access token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Called on every circuit start by the authentication state
    /// provider, so an expired or tampered token must read as "signed out" rather than as an error.
    /// A database that has become unreachable also lands here and is treated the same way, which
    /// returns the desktop user to the login screen instead of crashing the shell.</para>
    /// <para><b>Flow:</b> wrap the token → call the engine → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="accessToken">The JWT held in the WebView's local storage.</param>
    /// <returns>The user the token identifies, or <c>null</c> when it is not usable.</returns>
    public async Task<AppUser?> GetUserByAccessTokenAsync(string accessToken)
    {
        try
        {
            var response = await authSvc
                .GetUserByTokenAsync(new SvcData { JwToken = accessToken }).ConfigureAwait(false);
            if (response == null)
            {
                return null;
            }

            var decryptedUser = AppEncrypt.DecryptText(response.ComplexData);
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
    /// <para><b>Business Logic:</b> Applies the encrypted transport envelope the engine expects. A
    /// refused sign-in — wrong password or a throttled account (REQ-NFR-005) — comes back as a null
    /// envelope and is surfaced as a null user, so the shared login screen renders its own
    /// "Invalid email or password" alert.</para>
    /// <para><b>Flow:</b> encrypt → call engine → null check → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> On success the engine records a login row in the site database and
    /// may upgrade the stored password hash.</para>
    /// </remarks>
    /// <param name="aLoginUser">Envelope carrying the plaintext email and password.</param>
    /// <returns>The signed-in user, or <c>null</c> when authentication is refused.</returns>
    public async Task<AppUser?> LoginAsync(SvcData aLoginUser)
    {
        aLoginUser.LoginEmail = AppEncrypt.EncryptText(aLoginUser.LoginEmail);
        aLoginUser.LoginPass = AppEncrypt.EncryptText(aLoginUser.LoginPass);

        var response = await authSvc.AppLoginAsync(aLoginUser).ConfigureAwait(false);
        if (response == null)
        {
            return null;
        }

        var decryptedUser = AppEncrypt.DecryptText(response.ComplexData);
        return JsonSerializer.Deserialize<AppUser>(decryptedUser);
    }

    /// <summary>
    /// Exchanges a refresh token for the user it belongs to.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the web head exactly (REQ-FN-008): the engine treats the
    /// expired access token as its own refresh token, matches it against the <c>UserLogin</c> row
    /// and — while that row is inside its refresh window — rewrites it with a replacement token.
    /// The returned user carries the replacement, and the presented value stops working, so the
    /// caller must store what comes back.</para>
    /// <para><b>Flow:</b> guard → wrap the token → call the engine → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> The engine updates one <c>UserLogin</c> row in the site database.</para>
    /// </remarks>
    /// <param name="refreshRequest">Envelope carrying the refresh token.</param>
    /// <returns>The user with a replacement token, or <c>null</c> when the session is over.</returns>
    public async Task<AppUser?> RefreshTokenAsync(RefreshRequest refreshRequest)
    {
        var refreshToken = refreshRequest?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        try
        {
            var response = await authSvc
                .RefreshSessionAsync(new SvcData { JwToken = refreshToken }).ConfigureAwait(false);
            if (response == null)
            {
                return null;
            }

            var decryptedUser = AppEncrypt.DecryptText(response.ComplexData);
            return JsonSerializer.Deserialize<AppUser>(decryptedUser);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-sends the address-verification email for an account.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Staff accounts in BlogApp are created by an administrator and are
    /// verified on creation, so this desktop head has no verification flow to drive. The call is
    /// accepted and reported as handled, matching the web head's behaviour.</para>
    /// <para><b>Flow:</b> accept and return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="aVerifiEmailData">Envelope carrying the address.</param>
    /// <returns>Always <c>true</c>.</returns>
    public Task<bool> ResendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Resets a password using a token issued by the site's forgot-password flow.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is validated against the site database, so a reset link
    /// mailed by the website can be completed from the desktop app.</para>
    /// <para><b>Flow:</b> call the engine → project the result.</para>
    /// <para><b>Side Effects:</b> Rewrites the account's password hash and consumes the token.</para>
    /// </remarks>
    /// <param name="user">Envelope carrying the reset token and the new password.</param>
    /// <returns><c>true</c> when the password was changed.</returns>
    public async Task<bool> ResetPasswordAsync(SvcData user)
    {
        try
        {
            var result = await authSvc
                .ResetPasswordAsync(user.ResetToken, user.LoginPass).ConfigureAwait(false);
            return result.IsSuccess;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the forgot-password flow for an email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Always reports success so the caller cannot use the response to
    /// discover whether an address has an account.</para>
    /// <para><b>Flow:</b> call the engine → swallow failures → report success.</para>
    /// <para><b>Side Effects:</b> Writes a reset token and queues an email.</para>
    /// </remarks>
    /// <param name="user">Envelope carrying the email address.</param>
    /// <returns>Always <c>true</c>.</returns>
    public async Task<bool> SendPasswordResetEmailAsync(SvcData user)
    {
        try
        {
            await authSvc.RequestPasswordResetAsync(user.LoginEmail).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately ignored: the response must not reveal whether the address exists.
        }

        return true;
    }

    /// <summary>
    /// Updates an account's email address and sends a fresh verification message.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Not driven from the desktop head — see
    /// <see cref="ResendVerifiEmailAsync"/>.</para>
    /// <para><b>Flow:</b> accept and return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="aVerifiEmailData">Envelope carrying the new address.</param>
    /// <returns>Always <c>true</c>.</returns>
    public Task<bool> UpdateNSendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        return Task.FromResult(true);
    }

    /// <summary>
    /// Completes address verification from a mailed token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Verification links open in a browser against the website, not in
    /// the desktop head, so this returns no user.</para>
    /// <para><b>Flow:</b> return null.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="aVerifyEmailData">Envelope carrying the verification token.</param>
    /// <returns>Always <c>null</c>.</returns>
    public Task<AppUser?> VerifyEmailAsync(SvcData aVerifyEmailData)
    {
        return Task.FromResult<AppUser?>(null);
    }
}
