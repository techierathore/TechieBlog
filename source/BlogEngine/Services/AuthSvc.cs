using BlogEngine.Common;
using BlogModels;
using BlogModels.Models;
using BlogSvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlogEngine.Services;
public class AuthSvc
{
    private readonly IBlogUserRepo UserRepo;
    private readonly ILogger<AuthSvc> AppLogger;
    private readonly IUserLoginRepository LoginRepo;
    private readonly IPasswordResetTokenRepo TokenRepo;
    private readonly IEmailService EmailService;

    public AuthSvc(IBlogUserRepo aUserRepo,
        IUserLoginRepository aUserLogins,
        IPasswordResetTokenRepo aTokenRepo,
        IEmailService aEmailService,
        ILogger<AuthSvc> aLogger)
    {
        UserRepo = aUserRepo;
        LoginRepo = aUserLogins;
        TokenRepo = aTokenRepo;
        EmailService = aEmailService;
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
            throw;
        }
    }

    public SvcData AppLogin(SvcData aLoginData)
    {
        try
        {
            string sJwToken;
            var vEmail = AppEncrypt.DecryptText(aLoginData.LoginEmail);
            var vPass = AppEncrypt.DecryptText(aLoginData.LoginPass);
            //vPass = AppEncrypt.CreateHash(vPass);
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
            throw;
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

    /// <summary>
    /// Registers a new user with the specified credentials.
    /// </summary>
    /// <param name="displayName">User's display name.</param>
    /// <param name="email">User's email address.</param>
    /// <param name="password">User's password (will be hashed).</param>
    /// <returns>Result containing the created user or an error message.</returns>
    public Result<AppUser> RegisterUser(string displayName, string email, string password)
    {
        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(displayName))
                return Result<AppUser>.Failure("Display name is required");

            if (string.IsNullOrWhiteSpace(email))
                return Result<AppUser>.Failure("Email is required");

            // Validate password strength
            var passwordResult = PasswordValidator.Validate(password);
            if (!passwordResult.IsValid)
                return Result<AppUser>.Failure(passwordResult.ErrorMessage);

            // Check email uniqueness
            var existingUser = UserRepo.GetUserByEmail(email);
            if (existingUser != null)
                return Result<AppUser>.Failure("An account with this email already exists");

            // Parse display name into first/last name
            var nameParts = displayName.Trim().Split(' ', 2);
            var firstName = nameParts[0];
            var lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

            // Hash password
            var hashedPassword = AppEncrypt.CreateHash(password);

            // Create new user
            var newUser = new AppUser
            {
                FirstName = firstName,
                LastName = lastName,
                EmailId = email.ToLowerInvariant().Trim(),
                LoginPass = hashedPassword,
                UserRole = AppConstants.AppUseRole,
                CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow,
                IsConfirmed = false
            };

            // Insert user
            var userId = UserRepo.InsertToGetId(newUser);
            if (userId <= 0)
                return Result<AppUser>.Failure("Registration failed. Please try again.");

            newUser.UserId = userId;
            AppLogger.LogInformation("New user registered: {Email} with ID {UserId}", email, userId);

            return Result<AppUser>.Success(newUser);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error registering user: {Email}", email);
            return Result<AppUser>.Failure("An error occurred during registration. Please try again.");
        }
    }

    /// <summary>
    /// Requests a password reset for the given email address.
    /// </summary>
    /// <param name="email">Email address to send reset link to.</param>
    /// <param name="baseUrl">Base URL for building the reset link.</param>
    /// <returns>Result with token (for dev logging) or error message.</returns>
    public async Task<Result<string>> RequestPasswordReset(string email, string baseUrl = "")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return Result<string>.Failure("Email is required");

            var user = UserRepo.GetUserByEmail(email.ToLowerInvariant().Trim());

            // Always return success to avoid revealing if email exists
            if (user == null)
            {
                AppLogger.LogInformation("Password reset requested for non-existent email: {Email}", email);
                return Result<string>.Success(null);
            }

            // Generate secure token
            var token = GenerateSecureToken();

            // Create reset token record
            var resetToken = new PasswordResetToken
            {
                UserId = user.UserId,
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsUsed = false
            };

            TokenRepo.Insert(resetToken);

            // Build reset URL
            var resetUrl = string.IsNullOrEmpty(baseUrl)
                ? $"/reset-password/{token}"
                : $"{baseUrl.TrimEnd('/')}/reset-password/{token}";

            // Send email (logs to console in dev)
            await EmailService.SendPasswordResetEmail(email, resetUrl);

            AppLogger.LogInformation("Password reset token created for user {UserId}", user.UserId);

            return Result<string>.Success(token);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error requesting password reset for: {Email}", email);
            return Result<string>.Failure("An error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Resets a user's password using a valid reset token.
    /// </summary>
    /// <param name="token">The reset token from the email link.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <returns>Result indicating success or failure with message.</returns>
    public Result<bool> ResetPassword(string token, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return Result<bool>.Failure("Invalid reset link");

            var resetToken = TokenRepo.GetByToken(token);

            if (resetToken == null)
                return Result<bool>.Failure("Invalid reset link. Please request a new password reset.");

            if (resetToken.ExpiresAt < DateTime.UtcNow)
                return Result<bool>.Failure("This reset link has expired. Please request a new password reset.");

            if (resetToken.IsUsed)
                return Result<bool>.Failure("This reset link has already been used. Please request a new password reset.");

            // Validate password strength
            var passwordResult = PasswordValidator.Validate(newPassword);
            if (!passwordResult.IsValid)
                return Result<bool>.Failure(passwordResult.ErrorMessage);

            // Get user
            var user = UserRepo.GetSingle(resetToken.UserId);
            if (user == null)
                return Result<bool>.Failure("User not found");

            // Update password
            user.LoginPass = AppEncrypt.CreateHash(newPassword);
            user.UpdatedOn = DateTime.UtcNow;
            UserRepo.Update(user);

            // Mark token as used
            TokenRepo.MarkUsed(resetToken.TokenId);

            AppLogger.LogInformation("Password reset successful for user {UserId}", user.UserId);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error resetting password with token");
            return Result<bool>.Failure("An error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Validates if a reset token is valid without using it.
    /// </summary>
    /// <param name="token">The token to validate.</param>
    /// <returns>Result with validation status.</returns>
    public Result<bool> ValidateResetToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result<bool>.Failure("Invalid reset link");

        var resetToken = TokenRepo.GetByToken(token);

        if (resetToken == null)
            return Result<bool>.Failure("Invalid reset link");

        if (resetToken.ExpiresAt < DateTime.UtcNow)
            return Result<bool>.Failure("This reset link has expired");

        if (resetToken.IsUsed)
            return Result<bool>.Failure("This reset link has already been used");

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Generates a cryptographically secure token for password reset.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    /// <summary>
    /// Gets a user's profile by their ID.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>AppUser if found, null otherwise.</returns>
    public AppUser? GetUserProfile(long userId)
    {
        try
        {
            return UserRepo.GetSingle(userId);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error getting user profile for ID: {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Updates a user's profile information.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="firstName">First name.</param>
    /// <param name="lastName">Last name.</param>
    /// <param name="profileDescription">Bio/description.</param>
    /// <param name="twitterUrl">Twitter profile URL.</param>
    /// <param name="linkedInUrl">LinkedIn profile URL.</param>
    /// <param name="gitHubUrl">GitHub profile URL.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<bool> UpdateProfile(
        long userId,
        string firstName,
        string lastName,
        string? profileDescription,
        string? twitterUrl,
        string? linkedInUrl,
        string? gitHubUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstName))
                return Result<bool>.Failure("First name is required");

            if (string.IsNullOrWhiteSpace(lastName))
                return Result<bool>.Failure("Last name is required");

            var user = UserRepo.GetSingle(userId);
            if (user == null)
                return Result<bool>.Failure("User not found");

            user.FirstName = firstName.Trim();
            user.LastName = lastName.Trim();
            user.ProfileDescription = profileDescription?.Trim() ?? string.Empty;
            user.TwiiterUrl = twitterUrl?.Trim() ?? string.Empty;
            user.LinkedInUrl = linkedInUrl?.Trim() ?? string.Empty;
            user.GitHubUrl = gitHubUrl?.Trim() ?? string.Empty;
            user.UpdatedOn = DateTime.UtcNow;

            UserRepo.Update(user);

            AppLogger.LogInformation("Profile updated for user {UserId}", userId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error updating profile for user: {UserId}", userId);
            return Result<bool>.Failure("An error occurred while updating your profile.");
        }
    }

    /// <summary>
    /// Changes a user's password.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="currentPassword">Current password for verification.</param>
    /// <param name="newPassword">New password to set.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<bool> ChangePassword(long userId, string currentPassword, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
                return Result<bool>.Failure("Current password is required");

            if (string.IsNullOrWhiteSpace(newPassword))
                return Result<bool>.Failure("New password is required");

            var user = UserRepo.GetSingle(userId);
            if (user == null)
                return Result<bool>.Failure("User not found");

            // Verify current password
            var hashedCurrentPassword = AppEncrypt.CreateHash(currentPassword);
            if (user.LoginPass != hashedCurrentPassword)
                return Result<bool>.Failure("Current password is incorrect");

            // Validate new password strength
            var validationResult = PasswordValidator.Validate(newPassword);
            if (!validationResult.IsValid)
                return Result<bool>.Failure(validationResult.ErrorMessage);

            // Update password
            user.LoginPass = AppEncrypt.CreateHash(newPassword);
            user.UpdatedOn = DateTime.UtcNow;
            UserRepo.Update(user);

            AppLogger.LogInformation("Password changed for user {UserId}", userId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            AppLogger.LogError(ex, "Error changing password for user: {UserId}", userId);
            return Result<bool>.Failure("An error occurred while changing your password.");
        }
    }
}
