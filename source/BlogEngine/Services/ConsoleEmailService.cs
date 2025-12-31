using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Development email service that logs emails to console.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> For development/testing - logs emails instead of sending.</para>
/// <para><b>Production:</b> Replace with SmtpEmailService or similar.</para>
/// </remarks>
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _logger;

    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmail(string email, string resetUrl)
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("PASSWORD RESET EMAIL");
        _logger.LogInformation("========================================");
        _logger.LogInformation("To: {Email}", email);
        _logger.LogInformation("Reset URL: {ResetUrl}", resetUrl);
        _logger.LogInformation("========================================");
        _logger.LogInformation("(In production, this would be sent via SMTP)");
        _logger.LogInformation("========================================");

        return Task.CompletedTask;
    }
}
