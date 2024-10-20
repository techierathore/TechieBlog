using BlogModels.Models;
namespace BlogModels.Interfaces;

public interface IAuthService
{
    Task<BlogUser> LoginAsync(SvcData user);
    Task<bool> RegisterUserAsync(SvcData user);
    Task<BlogUser> GetUserByAccessTokenAsync(string accessToken);
    Task<BlogUser> RefreshTokenAsync(RefreshRequest refreshRequest);

    Task<bool> SendPasswordResetEmailAsync(SvcData user);
    Task<bool> ResetPasswordAsync(SvcData user);
    Task<BlogUser> VerifyEmailAsync(SvcData aVerifyEmailData);

    Task<bool> ResendVerifiEmailAsync(SvcData aVerifiEmailData);
    Task<bool> UpdateNSendVerifiEmailAsync(SvcData aVerifiEmailData);
}
