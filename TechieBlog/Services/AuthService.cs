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
   public Task<AppUser> GetUserByAccessTokenAsync(string accessToken)
    {
        throw new NotImplementedException();
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
