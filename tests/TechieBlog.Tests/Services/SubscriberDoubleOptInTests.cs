using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SubscriberSvc.SubscribePendingAsync"/> — the double opt-in
/// signup path every public subscribe form is required to use. [REQ-UI-056 / REQ-FN-048]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the security contract that the sidebar form broke: a public
/// subscription must be written UNCONFIRMED and must issue a verification token, so an address
/// nobody owns cannot be put on the list by a stranger or a script. The repository and the
/// verification service are NSubstitute doubles, so no database and no SMTP are involved.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="ISubscriberRepo"/> and
/// <see cref="IEmailVerificationService"/>; <see cref="NullLogger{T}"/> for the logger.</para>
///
/// <para><b>Stub the ASYNC repository member, not the synchronous twin (REQ-NFR-026 stage 3).</b>
/// These tests originally stubbed <c>GetByEmail</c>, and they passed only because
/// <c>SubscribePendingAsync</c> was reaching the database through that blocking member. Now that it
/// awaits <c>GetByEmailAsync</c>, a <c>GetByEmail</c> stub is never consulted — Castle
/// DynamicProxy intercepts the interface's default implementations too, so the substitute's
/// <c>GetByEmailAsync</c> does NOT fall through to <c>RepoSyncBridge</c> and returns a completed
/// task holding <c>null</c> instead. The visible symptom is not a missing stub: it is the service
/// behaving as though the address were unknown, inserting a duplicate row and mailing a second
/// confirmation link. If a stub here looks correct but the assertion disagrees, check which twin
/// the service under test actually calls before changing the assertion.</para>
/// </remarks>
public class SubscriberDoubleOptInTests
{
    private readonly ISubscriberRepo subscriberRepo = Substitute.For<ISubscriberRepo>();
    private readonly IEmailVerificationService emailVerification = Substitute.For<IEmailVerificationService>();
    private readonly SubscriberSvc service;

    /// <summary>
    /// Wires the service under test to both substitutes and makes token issue succeed by default.
    /// </summary>
    public SubscriberDoubleOptInTests()
    {
        emailVerification
            .IssueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>())
            .Returns(Result<EmailVerificationToken>.Success(new EmailVerificationToken()));

        service = new SubscriberSvc(subscriberRepo, NullLogger<SubscriberSvc>.Instance, emailVerification);
    }

    /// <summary>
    /// A brand-new address is inserted with IsConfirmed and IsActive both false, so nothing is
    /// ever mailed to it until the confirmation link is redeemed.
    /// </summary>
    [Fact]
    public async Task SubscribePendingWritesUnconfirmedRow()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber)null);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(42L);

        // Act
        var result = await service.SubscribePendingAsync("new@example.com");

        // Assert
        Assert.True(result.IsSuccess);
        subscriberRepo.Received(1).InsertToGetId(Arg.Is<Subscriber>(s => !s.IsConfirmed && !s.IsActive));
    }

    /// <summary>
    /// The pending row's id is handed to the verification service under the Subscription purpose,
    /// which is what makes the opt-in double rather than merely deferred.
    /// </summary>
    [Fact]
    public async Task SubscribePendingIssuesSubscriptionToken()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber)null);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(7L);

        // Act
        var result = await service.SubscribePendingAsync("reader@example.com");

        // Assert
        Assert.True(result.Data);
        await emailVerification.Received(1).IssueAsync(
            "reader@example.com",
            Arg.Any<string>(),
            EmailVerificationPurpose.Subscription,
            7L,
            Arg.Any<string>());
    }

    /// <summary>
    /// Re-submitting an address that is still pending re-sends the link against the SAME row
    /// instead of inserting a duplicate subscriber.
    /// </summary>
    [Fact]
    public async Task SubscribePendingReusesExistingPendingRow()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync("pending@example.com", Arg.Any<CancellationToken>())
            .Returns(new Subscriber { SubscriberId = 11, Email = "pending@example.com", IsConfirmed = false });

        // Act
        var result = await service.SubscribePendingAsync("pending@example.com");

        // Assert
        Assert.True(result.IsSuccess);
        subscriberRepo.DidNotReceive().InsertToGetId(Arg.Any<Subscriber>());
        await emailVerification.Received(1).IssueAsync(
            "pending@example.com", Arg.Any<string>(), EmailVerificationPurpose.Subscription, 11L, Arg.Any<string>());
    }

    /// <summary>
    /// An address that has already confirmed is never mailed a second link, so the form cannot be
    /// used to re-send confirmation mail to a stranger over and over.
    /// </summary>
    [Fact]
    public async Task SubscribePendingNeverMailsAConfirmedAddressAgain()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync("known@example.com", Arg.Any<CancellationToken>())
            .Returns(new Subscriber { SubscriberId = 5, Email = "known@example.com", IsConfirmed = true });

        // Act
        var result = await service.SubscribePendingAsync("known@example.com");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
        subscriberRepo.DidNotReceive().InsertToGetId(Arg.Any<Subscriber>());
        await emailVerification.DidNotReceive().IssueAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>());
    }

    /// <summary>
    /// A malformed address is refused before anything is written.
    /// </summary>
    /// <param name="email">The malformed address under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@domain")]
    public async Task SubscribePendingRejectsMalformedEmail(string email)
    {
        // Act
        var result = await service.SubscribePendingAsync(email);

        // Assert
        Assert.True(result.IsFailure);
        subscriberRepo.DidNotReceive().InsertToGetId(Arg.Any<Subscriber>());
    }

    /// <summary>
    /// Without the verification service the call FAILS rather than falling back to the
    /// auto-confirming path — silently degrading a consent gate is worse than an outage.
    /// </summary>
    [Fact]
    public async Task SubscribePendingRefusesWhenVerificationIsUnavailable()
    {
        // Arrange
        var bare = new SubscriberSvc(subscriberRepo, NullLogger<SubscriberSvc>.Instance);

        // Act
        var result = await bare.SubscribePendingAsync("nobody@example.com");

        // Assert
        Assert.True(result.IsFailure);
        subscriberRepo.DidNotReceive().InsertToGetId(Arg.Any<Subscriber>());
    }

    /// <summary>
    /// A failure to issue or mail the token is reported to the caller with the service's own
    /// visitor-safe message rather than being reported as a successful subscription.
    /// </summary>
    [Fact]
    public async Task SubscribePendingReportsTokenIssueFailure()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber)null);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(3L);
        emailVerification
            .IssueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>())
            .Returns(Result<EmailVerificationToken>.Failure("Too many confirmation emails."));

        // Act
        var result = await service.SubscribePendingAsync("flood@example.com");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Too many confirmation emails.", result.ErrorMessage);
    }

    /// <summary>
    /// The address is normalised to lower case and trimmed, so one person cannot occupy two rows
    /// by varying the capitalisation.
    /// </summary>
    [Fact]
    public async Task SubscribePendingNormalisesTheAddress()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber)null);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(9L);

        // Act
        await service.SubscribePendingAsync("  Mixed.Case@Example.COM  ");

        // Assert
        subscriberRepo.Received(1).InsertToGetId(
            Arg.Is<Subscriber>(s => s.Email == "mixed.case@example.com"));
    }
}
