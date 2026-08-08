using BlogEngine.DbAccess;
using BlogModels;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="SubscriberRepo"/> against a real PostgreSQL
/// instance created by <see cref="PostgresFixture"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Verifies that the Dapper SQL in the repository matches the
/// schema DbUp actually creates — the class of defect a mocked repository can
/// never catch (REQ-NFR-016).</para>
/// <para><b>Opt-in:</b> every test here is skipped unless
/// <c>TechieBlogIntegrationTests=true</c>; see <see cref="PostgresFixture"/>.</para>
/// <para><b>Dependencies:</b> <see cref="PostgresFixture"/> (shared container).</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SubscriberRepoIntegrationTests
{
    private readonly PostgresFixture fixture;

    /// <summary>
    /// Captures the shared PostgreSQL fixture for this test class.
    /// </summary>
    /// <param name="postgresFixture">The container fixture supplied by xUnit.</param>
    public SubscriberRepoIntegrationTests(PostgresFixture postgresFixture)
    {
        fixture = postgresFixture;
    }

    /// <summary>
    /// True when container-backed integration tests are switched on. Referenced by
    /// each test's <c>SkipUnless</c> so the suite is skipped, not failed, when
    /// Docker or the opt-in switch is absent.
    /// </summary>
    public static bool IntegrationTestsEnabled => PostgresFixture.IntegrationTestsEnabled;

    /// <summary>
    /// Builds a repository bound to the container's connection string.
    /// </summary>
    /// <returns>A repository pointing at the containerised database.</returns>
    private SubscriberRepo CreateRepo()
    {
        Assert.SkipWhen(fixture.ConnectionString is null, fixture.SkipReason ?? "No container.");
        return new SubscriberRepo(fixture.ConnectionString);
    }

    /// <summary>
    /// A subscriber inserted through the repository can be read back by its
    /// generated identifier, proving the INSERT and SELECT SQL agree with the
    /// migrated schema.
    /// </summary>
    [Fact(SkipUnless = nameof(IntegrationTestsEnabled), Skip = "Requires Docker; see PostgresFixture.")]
    public void InsertedSubscriberIsReadBackById()
    {
        // Arrange
        var repo = CreateRepo();
        var subscriber = new Subscriber
        {
            Email = "integration@example.com",
            Name = "Integration",
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = true,
            IsActive = true
        };

        // Act
        var newId = repo.InsertToGetId(subscriber);
        var found = repo.GetSingle(newId);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("integration@example.com", found.Email);
    }

    /// <summary>
    /// The existence check finds an address that was just inserted, exercising the
    /// case-normalised lookup path the subscribe flow relies on.
    /// </summary>
    [Fact(SkipUnless = nameof(IntegrationTestsEnabled), Skip = "Requires Docker; see PostgresFixture.")]
    public void EmailExistsFindsInsertedSubscriber()
    {
        // Arrange
        var repo = CreateRepo();
        repo.InsertToGetId(new Subscriber
        {
            Email = "exists@example.com",
            Name = "Exists",
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = true,
            IsActive = true
        });

        // Act
        var exists = repo.EmailExists("exists@example.com");

        // Assert
        Assert.True(exists);
    }

    /// <summary>
    /// The migrated schema is queryable through the repository's list method, which
    /// is the smoke test that every DbUp script applied cleanly.
    /// </summary>
    [Fact(SkipUnless = nameof(IntegrationTestsEnabled), Skip = "Requires Docker; see PostgresFixture.")]
    public void GetAllQueriesMigratedSchema()
    {
        // Arrange
        var repo = CreateRepo();

        // Act
        var all = repo.GetAll();

        // Assert
        Assert.NotNull(all);
    }
}
