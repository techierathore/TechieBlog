namespace BlogEngine.Common;

/// <summary>
/// Validates password strength requirements.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enforces password complexity rules for user registration.</para>
/// <para><b>Requirements:</b> Min 8 chars, uppercase, lowercase, and digit.</para>
/// </remarks>
public static class PasswordValidator
{
    /// <summary>
    /// Validates a password against strength requirements.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <returns>ValidationResult with success status and any error messages.</returns>
    public static PasswordValidationResult Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required");
            return new PasswordValidationResult(false, errors);
        }

        if (password.Length < 8)
            errors.Add("Password must be at least 8 characters");

        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter");

        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter");

        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain a number");

        return new PasswordValidationResult(errors.Count == 0, errors);
    }
}

/// <summary>
/// Result of password validation.
/// </summary>
public record PasswordValidationResult(bool IsValid, List<string> Errors)
{
    /// <summary>
    /// Gets a combined error message from all validation errors.
    /// </summary>
    public string ErrorMessage => string.Join(". ", Errors);
}
