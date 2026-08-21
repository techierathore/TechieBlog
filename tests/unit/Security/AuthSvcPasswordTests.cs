using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Service-level tests for the password paths of <see cref="AuthSvc"/> (REQ-NFR-002, REQ-FN-006).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="PasswordHasher"/> only reports that a stored credential is
/// stale; it is <see cref="AuthSvc"/> that has to act on that and persist the upgrade. These
/// tests prove the re-hash actually reaches the repository on a successful login, and that the
/// two surviving password-setting paths — an administrator creating a staff account and a user
/// completing a reset — refuse a weak password before any hash is written.</para>
/// <para><b>Dependencies:</b> xUnit and NSubstitute; no database or host required.</para>
/// </remarks>
public class AuthSvcPasswordTests
{
    private const string SamplePassword = "Str0ngPassword";
    private const string SampleEmail = "legacy@techieblog.test";

    private readonly IBlogUserRepo userRepo = Substitute.For<IBlogUserRepo>();
    private readonly IUserCredentialRepo credentialRepo = Substitute.For<IUserCredentialRepo>();
    private readonly IUserLoginRepository loginRepo = Substitute.For<IUserLoginRepository>();
    private readonly IPasswordResetTokenRepo tokenRepo = Substitute.For<IPasswordResetTokenRepo>();
    private readonly ILoginLogRepo loginLogRepo = Substitute.For<ILoginLogRepo>();
    private readonly ILoginThrottle loginThrottle = Substitute.For<ILoginThrottle>();
    private readonly IEmailService emailService = Substitute.For<IEmailService>();

    /// <summary>
    /// Loads the cryptographic secrets this process needs (REQ-NFR-027).
    /// </summary>
    /// <remarks>
    /// The signing and encryption keys moved out of source, and <see cref="AppSecrets"/> has no
    /// fallback by design, so every test here that builds a sign-in envelope through
    /// <see cref="AppEncrypt"/> needs the test fixtures published first.
    /// </remarks>
    public AuthSvcPasswordTests()
    {
        TestAppSecrets.EnsureInitialised();
    }

    /// <summary>
    /// Signing in with a credential still stored as the retired MD5 digest succeeds and writes a
    /// current PBKDF2 hash back through the credential repository, so the account is migrated
    /// without the user noticing and never has to be reset.
    /// </summary>
    [Fact]
    public async Task LoginUpgradesLegacyHashOnSuccess()
    {
        GivenStoredCredential(ComputeRetiredDigest(SamplePassword));

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.NotNull(result);
        await credentialRepo.Received(1).UpdatePasswordHashAsync(
            7L,
            Arg.Is<string>(hash => hash != null && PasswordHasher.IsCurrentFormat(hash)),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The hash written back by the upgrade verifies the very password that was just used, which
    /// is what guarantees the account still works on the following sign-in.
    /// </summary>
    [Fact]
    public async Task UpgradedHashVerifiesSamePassword()
    {
        GivenStoredCredential(ComputeRetiredDigest(SamplePassword));
        var upgraded = string.Empty;
        credentialRepo
            .When(repo => repo.UpdatePasswordHashAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()))
            .Do(call => upgraded = call.ArgAt<string>(1));

        await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.Equal(PasswordVerifyResult.Success, PasswordHasher.Verify(SamplePassword, upgraded));
    }

    /// <summary>
    /// Signing in against a credential that is already in the current format leaves the stored
    /// hash alone, so a routine login does not rewrite the row on every request.
    /// </summary>
    [Fact]
    public async Task LoginLeavesCurrentHashUntouched()
    {
        GivenStoredCredential(PasswordHasher.HashPassword(SamplePassword));

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.NotNull(result);
        await credentialRepo.DidNotReceive().UpdatePasswordHashAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A wrong password against a legacy credential fails the login and, critically, does not
    /// trigger the upgrade — the migration path cannot be used to overwrite a credential.
    /// </summary>
    [Fact]
    public async Task FailedLoginDoesNotUpgradeHash()
    {
        GivenStoredCredential(ComputeRetiredDigest(SamplePassword));

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, "wrong-password"));

        Assert.Null(result);
        await credentialRepo.DidNotReceive().UpdatePasswordHashAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An administrator creating a staff account with a password that breaks the strength rules
    /// is refused with the validator's message and no user row is inserted (BRD-10).
    /// </summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    public async Task CreateStaffAccountRejectsWeakPassword(string password)
    {
        var result = await BuildService().CreateStaffAccountAsync("Test Member", "new@techieblog.test", password, "Author");

        Assert.True(result.IsFailure);
        userRepo.DidNotReceive().InsertToGetIdAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A compliant password is accepted, stored as a PBKDF2 hash rather than anything reversible,
    /// and the new account is flagged so the member must choose their own password at first login.
    /// </summary>
    [Fact]
    public async Task CreateStaffAccountStoresPbkdf2HashAndForcesChange()
    {
        userRepo.GetUserByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUser?)null);
        userRepo.InsertToGetIdAsync(Arg.Any<AppUser>(), Arg.Any<CancellationToken>()).Returns(42L);

        var result = await BuildService().CreateStaffAccountAsync("Test Member", "new@techieblog.test", SamplePassword, "Author");

        Assert.True(result.IsSuccess);
        Assert.True(PasswordHasher.IsCurrentFormat(result.Data.LoginPass));
        Assert.DoesNotContain(SamplePassword, result.Data.LoginPass, StringComparison.Ordinal);
        await credentialRepo.Received(1).UpdatePasswordHashAsync(
            42L, result.Data.LoginPass, true, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Completing a password reset with a weak password is refused and the stored credential is
    /// left untouched, so the reset link cannot be used to weaken an account (BRD-5).
    /// </summary>
    [Fact]
    public async Task ResetPasswordRejectsWeakPassword()
    {
        GivenValidResetToken();

        var result = await BuildService().ResetPasswordAsync("valid-token", "abc");

        Assert.True(result.IsFailure);
        await credentialRepo.DidNotReceive().UpdatePasswordHashAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A compliant reset password is stored as a PBKDF2 hash and clears the forced-change flag,
    /// because the user has just chosen the password themselves.
    /// </summary>
    [Fact]
    public async Task ResetPasswordStoresPbkdf2HashAndClearsForcedChange()
    {
        GivenValidResetToken();

        var result = await BuildService().ResetPasswordAsync("valid-token", SamplePassword);

        Assert.True(result.IsSuccess);
        await credentialRepo.Received(1).UpdatePasswordHashAsync(
            7L,
            Arg.Is<string>(hash => hash != null && PasswordHasher.IsCurrentFormat(hash)),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A signed-in user changing their own password must supply a replacement that satisfies the
    /// strength rules; this is the check behind the forced first-login change (REQ-NFR-023).
    /// </summary>
    [Fact]
    public async Task ChangePasswordRejectsWeakReplacement()
    {
        credentialRepo.GetByUserIdAsync(7L, Arg.Any<CancellationToken>()).Returns(new UserCredential
        {
            UserId = 7L,
            EmailId = SampleEmail,
            LoginPass = PasswordHasher.HashPassword(SamplePassword),
            UserRole = "Author",
            MustChangePassword = true
        });

        var result = await BuildService().ChangePasswordAsync(7L, SamplePassword, "abc");

        Assert.True(result.IsFailure);
        await credentialRepo.DidNotReceive().UpdatePasswordHashAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Changing the password with a compliant replacement rotates the hash and clears the
    /// forced-change flag, completing the first-login flow.
    /// </summary>
    [Fact]
    public async Task ChangePasswordRotatesHashAndClearsForcedChange()
    {
        credentialRepo.GetByUserIdAsync(7L, Arg.Any<CancellationToken>()).Returns(new UserCredential
        {
            UserId = 7L,
            EmailId = SampleEmail,
            LoginPass = PasswordHasher.HashPassword(SamplePassword),
            UserRole = "Author",
            MustChangePassword = true
        });

        var result = await BuildService().ChangePasswordAsync(7L, SamplePassword, "N3wStrongPass");

        Assert.True(result.IsSuccess);
        await credentialRepo.Received(1).UpdatePasswordHashAsync(
            7L,
            Arg.Is<string>(hash => hash != null && PasswordHasher.Verify("N3wStrongPass", hash) == PasswordVerifyResult.Success),
            false,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds the service under test over the substituted repositories.
    /// </summary>
    /// <returns>A ready-to-use <see cref="AuthSvc"/>.</returns>
    private AuthSvc BuildService()
    {
        return new AuthSvc(
            userRepo,
            credentialRepo,
            loginRepo,
            tokenRepo,
            loginLogRepo,
            loginThrottle,
            emailService,
            NullLogger<AuthSvc>.Instance);
    }

    /// <summary>
    /// Arranges a user whose stored credential is the supplied value.
    /// </summary>
    /// <param name="storedHash">The value the credential repository should return.</param>
    private void GivenStoredCredential(string storedHash)
    {
        credentialRepo.GetByEmailAsync(SampleEmail, Arg.Any<CancellationToken>()).Returns(new UserCredential
        {
            UserId = 7L,
            EmailId = SampleEmail,
            LoginPass = storedHash,
            UserRole = "Author",
            MustChangePassword = false
        });

        userRepo.GetSingleAsync(7L, Arg.Any<CancellationToken>()).Returns(new AppUser
        {
            UserId = 7L,
            FirstName = "Legacy",
            LastName = "Member",
            EmailId = SampleEmail,
            UserRole = "Author"
        });
    }

    /// <summary>
    /// Arranges a reset token that is neither expired nor already used.
    /// </summary>
    private void GivenValidResetToken()
    {
        tokenRepo.GetByTokenAsync("valid-token", Arg.Any<CancellationToken>()).Returns(new PasswordResetToken
        {
            TokenId = 1L,
            UserId = 7L,
            Token = "valid-token",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false
        });
    }

    /// <summary>
    /// Builds the encrypted transport envelope the UI sends to <see cref="AuthSvc.AppLoginAsync"/>.
    /// </summary>
    /// <param name="email">The login email address.</param>
    /// <param name="password">The plaintext password.</param>
    /// <returns>The populated envelope.</returns>
    private static SvcData BuildEnvelope(string email, string password)
    {
        return new SvcData
        {
            LoginEmail = AppEncrypt.EncryptText(email),
            LoginPass = AppEncrypt.EncryptText(password)
        };
    }

    /// <summary>
    /// Reproduces the retired MD5 + fixed-salt digest, so the migration is exercised against a
    /// genuine legacy value rather than a stand-in.
    /// </summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>The lowercase hexadecimal MD5 digest.</returns>
    private static string ComputeRetiredDigest(string password)
    {
        const string legacySalt = "TeleM3t3IS@lt";
        var bytes = System.Security.Cryptography.MD5.HashData(
            Encoding.UTF32.GetBytes(legacySalt + password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
