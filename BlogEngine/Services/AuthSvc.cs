using BlogModels.ViewModel;
using BlogSvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace BlogEngine.Services;
public class AuthSvc 
{
    private readonly IBlogUserRepo UserRepo;
    private readonly ILogger<AuthSvc> AppLogger;
    private readonly IUserLoginRepository LoginRepo;

    public AuthSvc(IBlogUserRepo aUserRepo,
        IUserLoginRepository aUserLogins, ILogger<AuthSvc> aLogger)
    {
        UserRepo = aUserRepo;
        LoginRepo = aUserLogins;
        AppLogger = aLogger;
    }

    /// <summary>
    /// This Method Will be used by the UI for Logging into 
    /// the Application.
    /// </summary>
    /// <param name="aSignUpData"></param>
    /// <returns>Object Containing Current Logged In User</returns>
    public SvcData AppSignUp(SvcData aSignUpData)
    {
        try
        {
            var vUserDataJson = AppEncrypt.DecryptText(aSignUpData.ComplexData);
            AppUser vNewUser = JsonSerializer.Deserialize<AppUser>(vUserDataJson);
            string sJwToken;
            var vCheckUserByEmail = UserRepo.GetUserByEmail(vNewUser.EmailId);
            if (vCheckUserByEmail != null) return null;
            //vNewUser.LoginPass = AppEncrypt.CreateHash(vNewUser.PasswordHash);
            vNewUser.UserRole = AppConstants.AppUseRole;
            var vNewUserId = UserRepo.InsertToGetId(vNewUser);
            if (vNewUserId <= 0) return null;

            vNewUser.UserId = vNewUserId;
            sJwToken = GenerateJWToken(vNewUser);
            var vUserLogins = new UserLogin()
            {
                LoginToken = sJwToken,
                IssueDate = DateTime.Today,
                LoginDate = DateTime.Today,
                ExipryDate = DateTime.Today.AddDays(2),
                TokenStatus = TokenStatus.ValidToken.ToString(),
                UserId = vNewUser.UserId
            };
            LoginRepo.Insert(vUserLogins);
            vNewUser.AccessToken = sJwToken;
            vNewUser.RefreshToken = sJwToken;
            string vRetData = JsonSerializer.Serialize(vNewUser);
            string sEncryptedData = AppEncrypt.EncryptText(vRetData);
            SvcData vReturnData = new()
            {
                ComplexData = sEncryptedData,
                JwToken = sJwToken
            };
            return vReturnData;
        }
        catch (Exception ex)
        {
            AppLogger.LogCritical(ex.Message);
            throw ex;
        }
    }
    
    public SvcData AppLogin(SvcData aLoginData)
    {
        try
        {
            string sJwToken;
            var vEmail = AppEncrypt.DecryptText(aLoginData.LoginEmail);
            var vPass = AppEncrypt.DecryptText(aLoginData.LoginPass);
            vPass = AppEncrypt.CreateHash(vPass);
            var vValidatedUser = UserRepo.GetLoginUser(vEmail, vPass);
            if (vValidatedUser != null)
            {
                sJwToken = GenerateJWToken(vValidatedUser);
                var vUserLogins = new UserLogin()
                {
                    LoginToken = sJwToken,
                    IssueDate = DateTime.Today,
                    LoginDate = DateTime.Today,
                    ExipryDate = DateTime.Today.AddDays(2),
                    TokenStatus = TokenStatus.ValidToken.ToString(),
                    UserId = vValidatedUser.UserId
                };
                LoginRepo.Insert(vUserLogins);
                vValidatedUser.AccessToken = sJwToken;
                vValidatedUser.RefreshToken = sJwToken;
                string vRetData = JsonSerializer.Serialize(vValidatedUser);
                string sEncryptedData = AppEncrypt.EncryptText(vRetData);
                SvcData vReturnData = new()
                {
                    ComplexData = sEncryptedData,
                    JwToken = sJwToken
                };
                return vReturnData;
            }
            else
            { return null; }
        }
        catch (Exception ex)
        {
            AppLogger.LogCritical(ex.Message);
            throw ex;
        }
    }

    public SvcData GetUserByToken(SvcData aTokenData)
    {
        var vUserID = SvcUtils.GetUserIDFromToken(aTokenData.JwToken);
        var vValidatedToken = LoginRepo.GetUserByToken(vUserID, aTokenData.JwToken);
        if (vValidatedToken != null)
        {
            var vValidatedUser = UserRepo.GetSingle(vUserID);
            if (vValidatedUser != null)
            {
                vValidatedUser.AccessToken = aTokenData.JwToken;
                vValidatedUser.RefreshToken = aTokenData.JwToken;
                string vRetData = JsonSerializer.Serialize(vValidatedUser);
                string sEncryptedData = AppEncrypt.EncryptText(vRetData);
                SvcData vReturnData = new()
                {
                    ComplexData = sEncryptedData,
                    JwToken = aTokenData.JwToken
                };
                return vReturnData;
            }
            else { return null; }
        }
        else { return null; }

    }

    private string GenerateJWToken(AppUser aLoggedInUser)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(AppConstants.JWTTokenGenKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.PrimarySid,Convert.ToString(aLoggedInUser.UserId)),
                new Claim(ClaimTypes.Name, aLoggedInUser.FullName),
                new Claim(ClaimTypes.Email, aLoggedInUser.EmailId),
                new Claim(ClaimTypes.Role, aLoggedInUser.UserRole)
            }),
            Expires = DateTime.UtcNow.AddDays(15),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }


}
