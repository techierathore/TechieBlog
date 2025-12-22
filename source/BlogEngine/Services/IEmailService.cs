namespace BlogEngine.Services;

/// <summary>
/// Interface for email sending operations.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a password reset email with the reset link.
    /// </summary>
    /// <param name="email">Recipient email address.</param>
    /// <param name="resetUrl">Password reset URL with token.</param>
    Task SendPasswordResetEmail(string email, string resetUrl);
}
