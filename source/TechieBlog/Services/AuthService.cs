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

    public Task<AppUser> RefreshTokenAsync(RefreshRequest refreshRequest)
    {
        throw new NotImplementedException();
    }

    public Task<bool> RegisterUserAsync(SvcData user)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ResendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ResetPasswordAsync(SvcData user)
    {
        throw new NotImplementedException();
    }

    public Task<bool> SendPasswordResetEmailAsync(SvcData user)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdateNSendVerifiEmailAsync(SvcData aVerifiEmailData)
    {
        throw new NotImplementedException();
    }

    public Task<AppUser> VerifyEmailAsync(SvcData aVerifyEmailData)
    {
        throw new NotImplementedException();
    }
}
