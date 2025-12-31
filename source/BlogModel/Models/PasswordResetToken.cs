namespace BlogModels.Models;

/// <summary>
/// Represents a password reset token for account recovery.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores secure tokens for password reset requests.</para>
/// <para><b>Usage:</b> Used by AuthSvc for password reset flow.</para>
/// </remarks>
public class PasswordResetToken
{
    /// <summary>
    /// Unique identifier for the token record.
    /// </summary>
    public long TokenId { get; set; }

    /// <summary>
    /// Foreign key to AppUser requesting the reset.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Cryptographically secure reset token string.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when token was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when token expires (24 hours from creation).
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has been used.
    /// </summary>
    public bool IsUsed { get; set; }
}
