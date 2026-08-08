using BlogEngine.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="PasswordValidator"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Guards the registration and password-reset complexity
/// policy — minimum length eight, at least one uppercase letter, one lowercase
/// letter and one digit — and the shape of the aggregated error message.</para>
/// <para><b>Dependencies:</b> None. The validator is a pure static helper.</para>
/// </remarks>
public class PasswordValidatorTests
{
    /// <summary>
    /// A password meeting every rule validates successfully and carries no errors.
    /// </summary>
    [Fact]
    public void ValidateAcceptsCompliantPassword()
    {
        // Arrange
        var password = "Str0ngPass";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.True(result.IsValid);
    }

    /// <summary>
    /// A compliant password produces an empty error collection, so the UI has
    /// nothing to render beneath the field.
    /// </summary>
    [Fact]
    public void ValidateReportsNoErrorsForCompliantPassword()
    {
        // Arrange
        var password = "Str0ngPass";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A null or empty password short-circuits with the single "required" error
    /// rather than listing every unmet complexity rule.
    /// </summary>
    /// <param name="password">The missing password under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateReportsSingleErrorForMissingPassword(string password)
    {
        // Arrange, Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Equal(new[] { "Password is required" }, result.Errors);
    }

    /// <summary>
    /// Seven characters is one short of the minimum and is rejected with the
    /// length error, even though the other three rules are satisfied.
    /// </summary>
    [Fact]
    public void ValidateRejectsPasswordShorterThanMinimum()
    {
        // Arrange
        var password = "Ab1cdef";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Contains("Password must be at least 8 characters", result.Errors);
    }

    /// <summary>
    /// An all-lowercase password of sufficient length is rejected for the missing
    /// uppercase letter.
    /// </summary>
    [Fact]
    public void ValidateRejectsPasswordWithoutUppercase()
    {
        // Arrange
        var password = "str0ngpass";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Contains("Password must contain an uppercase letter", result.Errors);
    }

    /// <summary>
    /// An all-uppercase password of sufficient length is rejected for the missing
    /// lowercase letter.
    /// </summary>
    [Fact]
    public void ValidateRejectsPasswordWithoutLowercase()
    {
        // Arrange
        var password = "STR0NGPASS";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Contains("Password must contain a lowercase letter", result.Errors);
    }

    /// <summary>
    /// A mixed-case password with no digit is rejected for the missing number.
    /// </summary>
    [Fact]
    public void ValidateRejectsPasswordWithoutDigit()
    {
        // Arrange
        var password = "StrongPass";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Contains("Password must contain a number", result.Errors);
    }

    /// <summary>
    /// A password failing several rules at once accumulates one error per broken
    /// rule instead of stopping at the first.
    /// </summary>
    [Fact]
    public void ValidateAccumulatesEveryBrokenRule()
    {
        // Arrange
        var password = "abc";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Equal(3, result.Errors.Count);
    }

    /// <summary>
    /// The convenience message joins the individual errors with ". " so a single
    /// validation summary line can be shown.
    /// </summary>
    [Fact]
    public void ErrorMessageJoinsErrorsWithPeriodSeparator()
    {
        // Arrange
        var password = "abcdefgh";

        // Act
        var result = PasswordValidator.Validate(password);

        // Assert
        Assert.Equal(
            "Password must contain an uppercase letter. Password must contain a number",
            result.ErrorMessage);
    }
}
