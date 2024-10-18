
namespace BlogSvc.Controllers;

[Route("[controller]")]
public class AuthSvc : ControllerBase
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
    [HttpPost("AppSignUp")]
    public IActionResult AppSignUp([FromBody] SvcData aSignUpData)
    {
        if (aSignUpData == null)
        { return BadRequest(); }
        try
        {
            var vUserDataJson = AppEncrypt.DecryptText(aSignUpData.ComplexData);
            BlogUser vNewUser = JsonSerializer.Deserialize<BlogUser>(vUserDataJson);
            string sJwToken;
            var vCheckUserByEmail = UserRepo.GetUserByEmail(vNewUser.EmailId);
            if (vCheckUserByEmail != null) return BadRequest("User with this Email already present use login or Forgot Password (if you had forgotten the password) ");
            //vNewUser.LoginPass = AppEncrypt.CreateHash(vNewUser.PasswordHash);
            vNewUser.UserRole = AppConstants.AppUseRole;
            var vNewUserId = UserRepo.InsertToGetId(vNewUser);
            if (vNewUserId <= 0) return BadRequest("Unable to Save New User");
            
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
            return Ok(vReturnData);
        }
        catch (Exception ex)
        {
            AppLogger.LogCritical(ex.Message);
            return BadRequest(ex);
        }
    }
    [HttpPost("AppLogin")]
    public IActionResult AppLogin([FromBody] SvcData aLoginData)
    {
        if (aLoginData == null)
        { return BadRequest(); }
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
            }
            else
            { return BadRequest("User Not Found"); }
            string vRetData = JsonSerializer.Serialize(vValidatedUser);
            string sEncryptedData = AppEncrypt.EncryptText(vRetData);
            SvcData vReturnData = new()
            {
                ComplexData = sEncryptedData,
                JwToken = sJwToken
            };
            return Ok(vReturnData);
        }
        catch (Exception ex)
        {
            AppLogger.LogCritical(ex.Message);
            return BadRequest(ex);
        }
    }

    [HttpPost("GetUserByToken")]
    public IActionResult GetUserByToken([FromBody] SvcData aTokenData)
    {
        if (aTokenData == null)
        { return BadRequest(); }
        var vUserID = SvcUtils.GetUserIDFromToken(aTokenData.JwToken);
        var vValidatedToken = LoginRepo.GetUserByToken(vUserID, aTokenData.JwToken);
        if (vValidatedToken != null)
        {
            var vValidatedUser = UserRepo.GetSingle(vUserID);
            if (vValidatedUser == null)
            {
                return BadRequest("User Not Found");
            }
            vValidatedUser.AccessToken = aTokenData.JwToken;
            vValidatedUser.RefreshToken = aTokenData.JwToken;
            string vRetData = JsonSerializer.Serialize(vValidatedUser);
            string sEncryptedData = AppEncrypt.EncryptText(vRetData);
            SvcData vReturnData = new()
            {
                ComplexData = sEncryptedData,
                JwToken = aTokenData.JwToken
            };
            return Ok(vReturnData);
        }
        else { return BadRequest("User Not Found"); }

    }

   private string GenerateJWToken(BlogUser aLoggedInUser)
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
