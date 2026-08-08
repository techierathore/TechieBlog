using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Tests for the sign-in audit trail written by <see cref="AuthSvc"/> (REQ-FN-051).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The audit trail used to be structurally incapable of recording a failed
/// sign-in — <c>LoginLogRepo</c> hard-coded <c>success = true</c> and an empty attempted address,
/// and the <see cref="LoginLog"/> model exposed neither column. A brute-force run therefore left
/// no evidence at all, which is the one thing the trail exists for. These tests hold the repaired
/// behaviour in place: the outcome is recorded honestly on every arm of the login method, the
/// address is always attributable, and the attempted password never reaches the row.</para>
/// <para><b>Dependencies:</b> xUnit and NSubstitute; no database or host required. The repository
/// is substituted, so what is proved here is the contract <c>AuthSvc</c> hands it — the SQL that
/// stores it is proved against live PostgreSQL in the smoke run.</para>
/// </remarks>
public class LoginAuditTests
{
    private const string SamplePassword = "Str0ngPassword";
    private const string SampleEmail = "audited@techieblog.test";
    private const string UnknownEmail = "nobody@techieblog.test";

    private readonly IBlogUserRepo userRepo = Substitute.For<IBlogUserRepo>();
    private readonly IUserCredentialRepo credentialRepo = Substitute.For<IUserCredentialRepo>();
    private readonly IUserLoginRepository loginRepo = Substitute.For<IUserLoginRepository>();
    private readonly IPasswordResetTokenRepo tokenRepo = Substitute.For<IPasswordResetTokenRepo>();
    private readonly ILoginLogRepo loginLogRepo = Substitute.For<ILoginLogRepo>();
    private readonly ILoginThrottle loginThrottle = Substitute.For<ILoginThrottle>();
    private readonly IEmailService emailService = Substitute.For<IEmailService>();

    private readonly List<LoginLog> recorded = [];

    /// <summary>
    /// Captures every row the service hands to the audit repository.
    /// </summary>
    public LoginAuditTests()
    {
        TestAppSecrets.EnsureInitialised();

        loginLogRepo
            .When(repo => repo.InsertAsync(Arg.Any<LoginLog>(), Arg.Any<CancellationToken>()))
            .Do(call => recorded.Add(call.ArgAt<LoginLog>(0)));
    }

    /// <summary>
    /// Signing in with the wrong password against a real account writes an audit row flagged as a
    /// failure and carrying the address that was tried — the row the old hard-coded INSERT could
    /// never produce.
    /// </summary>
    [Fact]
    public async Task FailedSignInRecordsFailureWithAttemptedAddress()
    {
        GivenAccount();

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, "wrong-password"));

        Assert.Null(result);
        var row = Assert.Single(recorded);
        Assert.False(row.Success);
        Assert.Equal(SampleEmail, row.AttemptedEmail);
    }

    /// <summary>
    /// A failed attempt against an address that does own an account is attributed to that account,
    /// so an investigation can tell "someone is guessing this user's password" apart from noise.
    /// </summary>
    [Fact]
    public async Task FailedSignInAgainstKnownAccountIsAttributed()
    {
        GivenAccount();

        await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, "wrong-password"));

        Assert.Equal(7L, Assert.Single(recorded).LoginUserId);
    }

    /// <summary>
    /// A failed attempt against an address that matches no account is still recorded, with no user
    /// id at all — the nullable foreign key is what lets the row exist rather than be rejected.
    /// </summary>
    [Fact]
    public async Task FailedSignInAgainstUnknownAddressIsRecordedWithoutUser()
    {
        credentialRepo.GetByEmailAsync(UnknownEmail, Arg.Any<CancellationToken>())
            .Returns((UserCredential?)null);

        await BuildService().AppLoginAsync(BuildEnvelope(UnknownEmail, SamplePassword));

        var row = Assert.Single(recorded);
        Assert.False(row.Success);
        Assert.Null(row.LoginUserId);
        Assert.Equal(UnknownEmail, row.AttemptedEmail);
    }

    /// <summary>
    /// A correct sign-in writes a row flagged as a success against the account that authenticated,
    /// so the trail distinguishes the two outcomes rather than recording everything as a success.
    /// </summary>
    [Fact]
    public async Task SuccessfulSignInRecordsSuccess()
    {
        GivenAccount();

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.NotNull(result);
        var row = Assert.Single(recorded);
        Assert.True(row.Success);
        Assert.Equal(7L, row.LoginUserId);
        Assert.Equal(SampleEmail, row.AttemptedEmail);
    }

    /// <summary>
    /// An attempt refused by the per-account throttle (REQ-NFR-005) is audited too. Without it the
    /// trail would go quiet exactly when an attack is at its most obvious, because the throttle
    /// returns before the credential is ever checked.
    /// </summary>
    [Fact]
    public async Task ThrottledSignInIsRecordedAsFailure()
    {
        loginThrottle
            .IsBlocked(SampleEmail.ToLowerInvariant(), out Arg.Any<TimeSpan>())
            .Returns(call =>
            {
                call[1] = TimeSpan.FromMinutes(5);
                return true;
            });

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.Null(result);
        var row = Assert.Single(recorded);
        Assert.False(row.Success);
        Assert.Equal(SampleEmail, row.AttemptedEmail);
    }

    /// <summary>
    /// A run of wrong-password attempts against one address leaves one failure row per attempt, all
    /// carrying the same address — which is what makes a brute-force sequence visible in the log
    /// rather than invisible behind a column that was always true.
    /// </summary>
    [Fact]
    public async Task BruteForceSequenceIsVisibleAsRunOfFailures()
    {
        GivenAccount();
        var service = BuildService();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await service.AppLoginAsync(BuildEnvelope(SampleEmail, $"guess-{attempt}"));
        }

        Assert.Equal(5, recorded.Count);
        Assert.All(recorded, row => Assert.False(row.Success));
        Assert.All(recorded, row => Assert.Equal(SampleEmail, row.AttemptedEmail));
    }

    /// <summary>
    /// The audit row never carries the password that was tried, on either outcome. Recording it
    /// would turn the security log into the richest credential store in the system.
    /// </summary>
    [Fact]
    public async Task AuditRowNeverCarriesAttemptedPassword()
    {
        GivenAccount();
        var service = BuildService();

        await service.AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));
        await service.AppLoginAsync(BuildEnvelope(SampleEmail, "wrong-password"));

        Assert.Equal(2, recorded.Count);
        Assert.All(recorded, row =>
        {
            Assert.DoesNotContain(SamplePassword, row.AttemptedEmail, StringComparison.Ordinal);
            Assert.DoesNotContain(SamplePassword, row.ClientIP, StringComparison.Ordinal);
            Assert.DoesNotContain(SamplePassword, row.UserAgent, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The best-effort client metadata the host stamps onto the envelope reaches the audit row, so
    /// a trail that does have an address and a user agent keeps them.
    /// </summary>
    [Fact]
    public async Task ClientMetadataReachesTheAuditRow()
    {
        GivenAccount();
        var envelope = BuildEnvelope(SampleEmail, SamplePassword);
        envelope.ClientIP = "203.0.113.7";
        envelope.ClientUserAgent = "SmokeAgent/1.0";

        await BuildService().AppLoginAsync(envelope);

        var row = Assert.Single(recorded);
        Assert.Equal("203.0.113.7", row.ClientIP);
        Assert.Equal("SmokeAgent/1.0", row.UserAgent);
    }

    /// <summary>
    /// An audit repository that throws does not fail the sign-in. A logging outage must not become
    /// a product outage; the service logs the error instead.
    /// </summary>
    [Fact]
    public async Task AuditFailureDoesNotBreakSignIn()
    {
        GivenAccount();
        loginLogRepo
            .InsertAsync(Arg.Any<LoginLog>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("audit table unavailable"));

        var result = await BuildService().AppLoginAsync(BuildEnvelope(SampleEmail, SamplePassword));

        Assert.NotNull(result);
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
    /// Arranges an account whose stored credential is a current PBKDF2 hash of the sample password.
    /// </summary>
    private void GivenAccount()
    {
        credentialRepo.GetByEmailAsync(SampleEmail, Arg.Any<CancellationToken>()).Returns(new UserCredential
        {
            UserId = 7L,
            EmailId = SampleEmail,
            LoginPass = PasswordHasher.HashPassword(SamplePassword),
            UserRole = "Author",
            MustChangePassword = false
        });

        userRepo.GetSingleAsync(7L, Arg.Any<CancellationToken>()).Returns(new AppUser
        {
            UserId = 7L,
            FirstName = "Audited",
            LastName = "Member",
            EmailId = SampleEmail,
            UserRole = "Author"
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
}
