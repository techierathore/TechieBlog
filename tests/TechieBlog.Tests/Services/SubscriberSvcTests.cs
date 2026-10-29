using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SubscriberSvc"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Exercises the newsletter subscription business rules —
/// email validation, duplicate handling, reactivation of a lapsed subscriber,
/// unsubscribe, and the swallow-and-degrade behaviour on repository failure —
/// with the repository replaced by an NSubstitute double so no database is needed.</para>
/// <para><b>Dependencies:</b> NSubstitute for <c>ISubscriberRepo</c>;
/// <see cref="NullLogger{T}"/> for the logger.</para>
/// </remarks>
public class SubscriberSvcTests
{
    private readonly ISubscriberRepo subscriberRepo = Substitute.For<ISubscriberRepo>();
    private readonly SubscriberSvc service;

    /// <summary>
    /// Wires the service under test to the substituted repository.
    /// </summary>
    public SubscriberSvcTests()
    {
        service = new SubscriberSvc(subscriberRepo, NullLogger<SubscriberSvc>.Instance);
    }

    /// <summary>
    /// A blank email address is rejected before the repository is consulted.
    /// </summary>
    /// <param name="email">The blank email under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SubscribeRejectsBlankEmail(string email)
    {
        // Arrange, Act
        var result = await service.SubscribeAsync(email, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Email address is required.", result.ErrorMessage);
    }

    /// <summary>
    /// An address that does not match the local@domain.tld shape is rejected with
    /// the invalid-format message.
    /// </summary>
    /// <param name="email">The malformed email under test.</param>
    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    [InlineData("two@@at.com")]
    [InlineData("spaces in@example.com")]
    public async Task SubscribeRejectsMalformedEmail(string email)
    {
        // Arrange, Act
        var result = await service.SubscribeAsync(email, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Please enter a valid email address.", result.ErrorMessage);
    }

    /// <summary>
    /// A brand-new address is persisted and the returned subscriber carries the
    /// identifier the repository handed back.
    /// </summary>
    [Fact]
    public async Task SubscribeStoresNewSubscriber()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync("new@example.com", Arg.Any<CancellationToken>()).Returns(false);
        subscriberRepo.InsertToGetIdAsync(Arg.Any<Subscriber>(), Arg.Any<CancellationToken>()).Returns(42L);

        // Act
        var result = await service.SubscribeAsync("new@example.com", "New Reader", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(42L, result.Data.SubscriberId);
    }

    /// <summary>
    /// The stored email is normalised to lower case and trimmed, so the same
    /// address entered with different casing cannot subscribe twice.
    /// </summary>
    [Fact]
    public async Task SubscribeNormalisesEmailToLowerCase()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        subscriberRepo.InsertToGetIdAsync(Arg.Any<Subscriber>(), Arg.Any<CancellationToken>()).Returns(1L);

        // Act
        var result = await service.SubscribeAsync("  Mixed.Case@Example.COM  ", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("mixed.case@example.com", result.Data.Email);
    }

    /// <summary>
    /// When no display name is supplied the local part of the address is used as
    /// the subscriber name.
    /// </summary>
    [Fact]
    public async Task SubscribeDefaultsNameToEmailLocalPart()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        subscriberRepo.InsertToGetIdAsync(Arg.Any<Subscriber>(), Arg.Any<CancellationToken>()).Returns(1L);

        // Act
        var result = await service.SubscribeAsync("reader@example.com", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("reader", result.Data.Name);
    }

    /// <summary>
    /// Subscribing an address that is already active fails rather than creating a
    /// duplicate row.
    /// </summary>
    [Fact]
    public async Task SubscribeRejectsAlreadyActiveEmail()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync("dup@example.com", Arg.Any<CancellationToken>()).Returns(true);
        subscriberRepo.GetByEmailAsync("dup@example.com", Arg.Any<CancellationToken>())
            .Returns(new Subscriber { SubscriberId = 7, Email = "dup@example.com", IsActive = true });

        // Act
        var result = await service.SubscribeAsync("dup@example.com", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("This email is already subscribed.", result.ErrorMessage);
    }

    /// <summary>
    /// Re-subscribing an address that had previously unsubscribed reactivates the
    /// existing row instead of inserting a new one, recording the re-consent and
    /// issuing a fresh unsubscribe token.
    /// </summary>
    /// <remarks>
    /// [REQ-FN-059] This assertion used to demand a bare <c>UpdateStatus(id, true)</c>, which is
    /// what the blocking overload did while <c>SubscribeAsync</c> already went through
    /// <c>RecordConsentAsync</c>. The two overloads therefore wrote different consent records for
    /// the same reactivation, and the blocking one left the subscriber holding the burned token
    /// that had removed them. The async behaviour is the correct one and both now share it.
    /// </remarks>
    [Fact]
    public async Task SubscribeReactivatesLapsedSubscriber()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync("back@example.com", Arg.Any<CancellationToken>()).Returns(true);
        subscriberRepo.GetByEmailAsync("back@example.com", Arg.Any<CancellationToken>())
            .Returns(new Subscriber { SubscriberId = 9, Email = "back@example.com", IsActive = false });

        // Act
        var result = await service.SubscribeAsync("back@example.com", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await subscriberRepo.Received(1).RecordConsentAsync(9L, Arg.Is<string>(token => token.Length == 64), Arg.Any<CancellationToken>());
        await subscriberRepo.DidNotReceive().UpdateStatusAsync(9L, true, Arg.Any<CancellationToken>());
        Assert.True(result.IsSuccess);
        Assert.True(result.Data.IsConfirmed);
        Assert.NotNull(result.Data.ConfirmedOn);
        Assert.Null(result.Data.UnsubscribeTokenUsedOn);
        Assert.Equal(64, result.Data.UnsubscribeToken.Length);
    }

    /// <summary>
    /// A repository failure during insert is caught and surfaced as a friendly
    /// failure result rather than an exception escaping to the UI.
    /// </summary>
    [Fact]
    public async Task SubscribeReturnsFailureWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.EmailExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        subscriberRepo.InsertToGetIdAsync(Arg.Any<Subscriber>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var result = await service.SubscribeAsync("boom@example.com", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Failed to subscribe. Please try again later.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing a blank address is rejected before the repository is touched.
    /// </summary>
    [Fact]
    public async Task UnsubscribeRejectsBlankEmail()
    {
        // Arrange, Act
        var result = await service.UnsubscribeAsync("  ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Email address is required.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing an address that was never subscribed reports "not found".
    /// </summary>
    [Fact]
    public async Task UnsubscribeReportsUnknownEmail()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync("ghost@example.com", Arg.Any<CancellationToken>()).Returns((Subscriber?)null);

        // Act
        var result = await service.UnsubscribeAsync("ghost@example.com", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Email not found in subscribers list.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing a known address flips its active flag off rather than
    /// deleting the row, preserving the audit trail.
    /// </summary>
    [Fact]
    public async Task UnsubscribeDeactivatesKnownSubscriber()
    {
        // Arrange
        subscriberRepo.GetByEmailAsync("bye@example.com", Arg.Any<CancellationToken>())
            .Returns(new Subscriber { SubscriberId = 11, Email = "bye@example.com", IsActive = true });

        // Act
        await service.UnsubscribeAsync("bye@example.com", TestContext.Current.CancellationToken);

        // Assert
        await subscriberRepo.Received(1).UpdateStatusAsync(11L, false, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Updating the status of an identifier the repository does not know reports
    /// "not found" and performs no write.
    /// </summary>
    [Fact]
    public async Task UpdateSubscriberStatusRejectsUnknownId()
    {
        // Arrange
        subscriberRepo.GetSingleAsync(99L, Arg.Any<CancellationToken>()).Returns((Subscriber?)null);

        // Act
        var result = await service.UpdateSubscriberStatusAsync(99L, false, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Subscriber not found.", result.ErrorMessage);
    }

    /// <summary>
    /// An empty search query falls back to listing every subscriber rather than
    /// running an unbounded LIKE.
    /// </summary>
    [Fact]
    public async Task SearchSubscribersFallsBackToAllForBlankQuery()
    {
        // Arrange
        subscriberRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { new Subscriber { SubscriberId = 1 } }.AsEnumerable());

        // Act
        var found = await service.SearchSubscribersAsync("   ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(found);
    }

    /// <summary>
    /// A repository failure while listing subscribers degrades to an empty
    /// sequence so the admin page still renders.
    /// </summary>
    [Fact]
    public async Task GetAllSubscribersReturnsEmptyWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.GetAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var found = await service.GetAllSubscribersAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(found);
    }

    /// <summary>
    /// The statistics tuple reports the total and active counts the repository
    /// supplies.
    /// </summary>
    [Fact]
    public async Task GetSubscriberStatsReportsRepositoryCounts()
    {
        // Arrange
        subscriberRepo.GetTotalCountAsync(Arg.Any<CancellationToken>()).Returns(10);
        subscriberRepo.GetActiveCountAsync(Arg.Any<CancellationToken>()).Returns(6);

        // Act
        var stats = await service.GetSubscriberStatsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((10, 6), stats);
    }

    /// <summary>
    /// A repository failure while gathering statistics degrades to zeroes rather
    /// than breaking the dashboard.
    /// </summary>
    [Fact]
    public async Task GetSubscriberStatsReturnsZeroesWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.GetTotalCountAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var stats = await service.GetSubscriberStatsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((0, 0), stats);
    }
}
