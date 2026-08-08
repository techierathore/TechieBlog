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
    public void SubscribeRejectsBlankEmail(string email)
    {
        // Arrange, Act
        var result = service.Subscribe(email);

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
    public void SubscribeRejectsMalformedEmail(string email)
    {
        // Arrange, Act
        var result = service.Subscribe(email);

        // Assert
        Assert.Equal("Please enter a valid email address.", result.ErrorMessage);
    }

    /// <summary>
    /// A brand-new address is persisted and the returned subscriber carries the
    /// identifier the repository handed back.
    /// </summary>
    [Fact]
    public void SubscribeStoresNewSubscriber()
    {
        // Arrange
        subscriberRepo.EmailExists("new@example.com").Returns(false);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(42L);

        // Act
        var result = service.Subscribe("new@example.com", "New Reader");

        // Assert
        Assert.Equal(42L, result.Data.SubscriberId);
    }

    /// <summary>
    /// The stored email is normalised to lower case and trimmed, so the same
    /// address entered with different casing cannot subscribe twice.
    /// </summary>
    [Fact]
    public void SubscribeNormalisesEmailToLowerCase()
    {
        // Arrange
        subscriberRepo.EmailExists(Arg.Any<string>()).Returns(false);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(1L);

        // Act
        var result = service.Subscribe("  Mixed.Case@Example.COM  ");

        // Assert
        Assert.Equal("mixed.case@example.com", result.Data.Email);
    }

    /// <summary>
    /// When no display name is supplied the local part of the address is used as
    /// the subscriber name.
    /// </summary>
    [Fact]
    public void SubscribeDefaultsNameToEmailLocalPart()
    {
        // Arrange
        subscriberRepo.EmailExists(Arg.Any<string>()).Returns(false);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Returns(1L);

        // Act
        var result = service.Subscribe("reader@example.com");

        // Assert
        Assert.Equal("reader", result.Data.Name);
    }

    /// <summary>
    /// Subscribing an address that is already active fails rather than creating a
    /// duplicate row.
    /// </summary>
    [Fact]
    public void SubscribeRejectsAlreadyActiveEmail()
    {
        // Arrange
        subscriberRepo.EmailExists("dup@example.com").Returns(true);
        subscriberRepo.GetByEmail("dup@example.com")
            .Returns(new Subscriber { SubscriberId = 7, Email = "dup@example.com", IsActive = true });

        // Act
        var result = service.Subscribe("dup@example.com");

        // Assert
        Assert.Equal("This email is already subscribed.", result.ErrorMessage);
    }

    /// <summary>
    /// Re-subscribing an address that had previously unsubscribed reactivates the
    /// existing row instead of inserting a new one.
    /// </summary>
    [Fact]
    public void SubscribeReactivatesLapsedSubscriber()
    {
        // Arrange
        subscriberRepo.EmailExists("back@example.com").Returns(true);
        subscriberRepo.GetByEmail("back@example.com")
            .Returns(new Subscriber { SubscriberId = 9, Email = "back@example.com", IsActive = false });

        // Act
        service.Subscribe("back@example.com");

        // Assert
        subscriberRepo.Received(1).UpdateStatus(9L, true);
    }

    /// <summary>
    /// A repository failure during insert is caught and surfaced as a friendly
    /// failure result rather than an exception escaping to the UI.
    /// </summary>
    [Fact]
    public void SubscribeReturnsFailureWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.EmailExists(Arg.Any<string>()).Returns(false);
        subscriberRepo.InsertToGetId(Arg.Any<Subscriber>()).Throws(new InvalidOperationException("db down"));

        // Act
        var result = service.Subscribe("boom@example.com");

        // Assert
        Assert.Equal("Failed to subscribe. Please try again later.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing a blank address is rejected before the repository is touched.
    /// </summary>
    [Fact]
    public void UnsubscribeRejectsBlankEmail()
    {
        // Arrange, Act
        var result = service.Unsubscribe("  ");

        // Assert
        Assert.Equal("Email address is required.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing an address that was never subscribed reports "not found".
    /// </summary>
    [Fact]
    public void UnsubscribeReportsUnknownEmail()
    {
        // Arrange
        subscriberRepo.GetByEmail("ghost@example.com").Returns((Subscriber?)null);

        // Act
        var result = service.Unsubscribe("ghost@example.com");

        // Assert
        Assert.Equal("Email not found in subscribers list.", result.ErrorMessage);
    }

    /// <summary>
    /// Unsubscribing a known address flips its active flag off rather than
    /// deleting the row, preserving the audit trail.
    /// </summary>
    [Fact]
    public void UnsubscribeDeactivatesKnownSubscriber()
    {
        // Arrange
        subscriberRepo.GetByEmail("bye@example.com")
            .Returns(new Subscriber { SubscriberId = 11, Email = "bye@example.com", IsActive = true });

        // Act
        service.Unsubscribe("bye@example.com");

        // Assert
        subscriberRepo.Received(1).UpdateStatus(11L, false);
    }

    /// <summary>
    /// Updating the status of an identifier the repository does not know reports
    /// "not found" and performs no write.
    /// </summary>
    [Fact]
    public void UpdateSubscriberStatusRejectsUnknownId()
    {
        // Arrange
        subscriberRepo.GetSingle(99L).Returns((Subscriber?)null);

        // Act
        var result = service.UpdateSubscriberStatus(99L, false);

        // Assert
        Assert.Equal("Subscriber not found.", result.ErrorMessage);
    }

    /// <summary>
    /// An empty search query falls back to listing every subscriber rather than
    /// running an unbounded LIKE.
    /// </summary>
    [Fact]
    public void SearchSubscribersFallsBackToAllForBlankQuery()
    {
        // Arrange
        subscriberRepo.GetAll().Returns(new[] { new Subscriber { SubscriberId = 1 } });

        // Act
        var found = service.SearchSubscribers("   ");

        // Assert
        Assert.Single(found);
    }

    /// <summary>
    /// A repository failure while listing subscribers degrades to an empty
    /// sequence so the admin page still renders.
    /// </summary>
    [Fact]
    public void GetAllSubscribersReturnsEmptyWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.GetAll().Throws(new InvalidOperationException("db down"));

        // Act
        var found = service.GetAllSubscribers();

        // Assert
        Assert.Empty(found);
    }

    /// <summary>
    /// The statistics tuple reports the total and active counts the repository
    /// supplies.
    /// </summary>
    [Fact]
    public void GetSubscriberStatsReportsRepositoryCounts()
    {
        // Arrange
        subscriberRepo.GetTotalCount().Returns(10);
        subscriberRepo.GetActiveCount().Returns(6);

        // Act
        var stats = service.GetSubscriberStats();

        // Assert
        Assert.Equal((10, 6), stats);
    }

    /// <summary>
    /// A repository failure while gathering statistics degrades to zeroes rather
    /// than breaking the dashboard.
    /// </summary>
    [Fact]
    public void GetSubscriberStatsReturnsZeroesWhenRepositoryThrows()
    {
        // Arrange
        subscriberRepo.GetTotalCount().Throws(new InvalidOperationException("db down"));

        // Act
        var stats = service.GetSubscriberStats();

        // Assert
        Assert.Equal((0, 0), stats);
    }
}
