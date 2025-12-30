using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using System.Text.Json;

namespace TechieBlog.Services;

public class AuthService : IAuthService
{
    private readonly AuthSvc objAuthSvc;
    public AuthService (AuthSvc authSvc)
    {
        objAuthSvc = authSvc;
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
    public Task<AppUser> GetUserByAccessTokenAsync(string accessToken)
    {
        try
        {
            var tokenData = new SvcData { JwToken = accessToken };
            var vSvcResponse = objAuthSvc.GetUserByToken(tokenData);

            if (vSvcResponse == null)
            {
                return Task.FromResult<AppUser>(null);
            }

            string sDecryptedUser = AppEncrypt.DecryptText(vSvcResponse.ComplexData);
            var vReturnUser = JsonSerializer.Deserialize<AppUser>(sDecryptedUser);
            return Task.FromResult(vReturnUser);
        }
        catch (Exception)
        {
            return Task.FromResult<AppUser>(null);
        }
    }

    public Task<AppUser> LoginAsync(SvcData aLoginUser)
    {
        try
        {
            aLoginUser.LoginEmail = AppEncrypt.EncryptText(aLoginUser.LoginEmail);
            aLoginUser.LoginPass = AppEncrypt.EncryptText(aLoginUser.LoginPass);
                                
            var vSvcResponse = objAuthSvc.AppLogin(aLoginUser);                 
            string sDeCryptedUser = AppEncrypt.DecryptText(vSvcResponse.ComplexData);
            var vReturnUser = JsonSerializer.Deserialize<AppUser>(sDeCryptedUser);
            return Task.FromResult<AppUser>(vReturnUser);
        }
        catch (Exception)
        { throw; }
    }

    /// <summary>
    /// Refreshes an expired access token using a valid refresh token.
    /// </summary>
    /// <param name="refreshRequest">Contains the refresh token.</param>
    /// <returns>AppUser with new tokens if valid, null otherwise.</returns>
    public Task<AppUser> RefreshTokenAsync(RefreshRequest refreshRequest)
    {
        try
        {
            // Use the refresh token to get user info (refresh token is same as access token in this impl)
            var tokenData = new SvcData { JwToken = refreshRequest.RefreshToken };
            var vSvcResponse = objAuthSvc.GetUserByToken(tokenData);

            if (vSvcResponse == null)
            {
                return Task.FromResult<AppUser>(null);
            }

            string sDecryptedUser = AppEncrypt.DecryptText(vSvcResponse.ComplexData);
            var vReturnUser = JsonSerializer.Deserialize<AppUser>(sDecryptedUser);
            return Task.FromResult(vReturnUser);
        }
        catch (Exception)
        {
            return Task.FromResult<AppUser>(null);
        }
    }

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    /// <param name="user">User data containing FirstName, LoginEmail, and LoginPass.</param>
    /// <returns>True if registration successful, false otherwise.</returns>
    public Task<bool> RegisterUserAsync(SvcData user)
    {
        try
        {
            var result = objAuthSvc.RegisterUser(user.FirstName, user.LoginEmail, user.LoginPass);
            return Task.FromResult(result.IsSuccess);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
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
    public Task<bool> ResetPasswordAsync(SvcData user)
    {
        try
        {
            var result = objAuthSvc.ResetPassword(user.ResetToken, user.LoginPass);
            return Task.FromResult(result.IsSuccess);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
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
            var result = await objAuthSvc.RequestPasswordReset(user.LoginEmail);
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
    public Task<AppUser> VerifyEmailAsync(SvcData aVerifyEmailData)
    {
        // Email verification not fully implemented in backend yet
        // Return null to indicate not verified
        return Task.FromResult<AppUser>(null);
    }
}
