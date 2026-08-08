using BlogEngine.Common;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for <see cref="PasswordValidator"/> (REQ-FN-006, BRD-5/BRD-10).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Public self-service signup was retired, but password strength is still
/// enforced on the two paths that remain — an administrator creating a staff account and a user
/// choosing a new password during a reset. These tests pin the rules those paths rely on.</para>
/// <para><b>Dependencies:</b> xUnit; no database or host required.</para>
/// </remarks>
public class PasswordValidatorTests
{
    /// <summary>
    /// A password with at least eight characters, an uppercase letter, a lowercase letter and a
    /// digit is accepted with no error message.
    /// </summary>
    [Fact]
    public void AcceptsCompliantPassword()
    {
        var result = PasswordValidator.Validate("Str0ngPassword");

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// A null or empty password is rejected with a single "required" error rather than a list of
    /// every unmet rule.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RejectsMissingPassword(string password)
    {
        var result = PasswordValidator.Validate(password);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    /// <summary>
    /// A password shorter than eight characters is rejected even when it satisfies every
    /// character-class rule.
    /// </summary>
    [Fact]
    public void RejectsShortPassword()
    {
        var result = PasswordValidator.Validate("Ab1cdef");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("8 characters"));
    }

    /// <summary>
    /// A long password missing an uppercase letter is rejected.
    /// </summary>
    [Fact]
    public void RejectsPasswordWithoutUppercase()
    {
        var result = PasswordValidator.Validate("str0ngpassword");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("uppercase"));
    }

    /// <summary>
    /// A long password missing a lowercase letter is rejected.
    /// </summary>
    [Fact]
    public void RejectsPasswordWithoutLowercase()
    {
        var result = PasswordValidator.Validate("STR0NGPASSWORD");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("lowercase"));
    }

    /// <summary>
    /// A long password missing a digit is rejected.
    /// </summary>
    [Fact]
    public void RejectsPasswordWithoutDigit()
    {
        var result = PasswordValidator.Validate("StrongPassword");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("number"));
    }

    /// <summary>
    /// A password failing several rules reports all of them in one combined message, so the user
    /// is not corrected one rule at a time.
    /// </summary>
    [Fact]
    public void ReportsEveryUnmetRuleTogether()
    {
        var result = PasswordValidator.Validate("abc");

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(".", result.ErrorMessage);
    }
}
