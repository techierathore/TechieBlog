using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TechieBlog.Tests.Resume;

/// <summary>
/// Unit tests for <see cref="UserStatsSvc"/> — the service behind the resume's headline statistics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers REQ-FN-027 / BRD-51: validation of label, value and owner, the
/// create-or-update split used by the single admin form, delete-of-missing reporting, the
/// ownership-checked reorder, and the read-never-throws contract that lets a database problem
/// degrade one resume block instead of the whole page.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IUserStatsRepo"/>;
/// <see cref="NullLogger{T}"/> for the logger. No database is touched.</para>
/// </remarks>
public class UserStatsSvcTests
{
    private readonly IUserStatsRepo statsRepo = Substitute.For<IUserStatsRepo>();
    private readonly UserStatsSvc service;

    /// <summary>
    /// Wires the service under test to the substituted repository.
    /// </summary>
    public UserStatsSvcTests()
    {
        service = new UserStatsSvc(statsRepo, NullLogger<UserStatsSvc>.Instance);
    }

    /// <summary>
    /// A non-positive user identifier short-circuits before the repository is consulted, so a
    /// signed-out or malformed principal cannot trigger a query.
    /// </summary>
    /// <param name="userId">The invalid identifier under test.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task GetStatsForUserRejectsInvalidId(long userId)
    {
        // Arrange, Act
        var stats = await service.GetStatsForUserAsync(userId);

        // Assert
        Assert.Empty(stats);
        await statsRepo.DidNotReceive().GetByUserIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A repository failure during a read is swallowed and reported as an empty list, so the
    /// resume renders a shorter page rather than an error screen.
    /// </summary>
    [Fact]
    public async Task GetStatsForUserReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        statsRepo.GetByUserIdAsync(1L, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("db down"));

        // Act
        var stats = await service.GetStatsForUserAsync(1L);

        // Assert
        Assert.Empty(stats);
    }

    /// <summary>
    /// A blank category is treated as "no filter applies" and returns nothing rather than
    /// silently returning every statistic the user owns.
    /// </summary>
    [Fact]
    public async Task GetStatsForCategoryRejectsBlankCategory()
    {
        // Arrange, Act
        var stats = await service.GetStatsForCategoryAsync(1L, "   ");

        // Assert
        Assert.Empty(stats);
        await statsRepo.DidNotReceive().GetByUserIdAndCategoryAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A category filter is passed straight through to the repository and its rows are returned.
    /// </summary>
    [Fact]
    public async Task GetStatsForCategoryReturnsRepositoryRows()
    {
        // Arrange
        var community = new UserStat { StatId = 7, UserId = 1, StatLabel = "Talks", StatValue = "40+", StatCategory = "Community" };
        statsRepo.GetByUserIdAndCategoryAsync(1L, "Community", Arg.Any<CancellationToken>()).Returns(new[] { community });

        // Act
        var stats = (await service.GetStatsForCategoryAsync(1L, "Community")).ToList();

        // Assert
        Assert.Equal(7L, Assert.Single(stats).StatId);
    }

    /// <summary>
    /// A statistic with no owner is meaningless and is refused before any insert is attempted.
    /// </summary>
    [Fact]
    public async Task CreateStatRejectsMissingOwner()
    {
        // Arrange
        var stat = new UserStat { UserId = 0, StatLabel = "Years", StatValue = "20+" };

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.Equal("A statistic must belong to a user", result.ErrorMessage);
    }

    /// <summary>
    /// A blank or over-long label is refused with the label message, matching the column width
    /// declared in migration 012.
    /// </summary>
    /// <param name="label">The invalid label under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateStatRejectsBlankLabel(string label)
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = label, StatValue = "20+" };

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.StartsWith("Statistic label is required", result.ErrorMessage);
    }

    /// <summary>
    /// A label longer than the 100-character column is refused rather than left to fail in the
    /// database.
    /// </summary>
    [Fact]
    public async Task CreateStatRejectsOverlongLabel()
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = new string('x', 101), StatValue = "20+" };

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.StartsWith("Statistic label is required", result.ErrorMessage);
    }

    /// <summary>
    /// A blank value is refused — a tile with a label but no figure would render as an empty box.
    /// </summary>
    [Fact]
    public async Task CreateStatRejectsBlankValue()
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = "Years in software", StatValue = "  " };

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.StartsWith("Statistic value is required", result.ErrorMessage);
    }

    /// <summary>
    /// A valid statistic is inserted and the generated identifier is stamped back onto the entity
    /// the caller supplied.
    /// </summary>
    [Fact]
    public async Task CreateStatStampsGeneratedId()
    {
        // Arrange
        statsRepo.CreateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>()).Returns(99L);
        var stat = new UserStat { UserId = 1, StatLabel = "Years in software", StatValue = "20+" };

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.Equal(99L, result.Data.StatId);
    }

    /// <summary>
    /// An update aimed at a statistic that has since been deleted is reported as not found rather
    /// than silently re-creating the row.
    /// </summary>
    [Fact]
    public async Task UpdateStatReportsMissingRow()
    {
        // Arrange
        statsRepo.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns((UserStat?)null);
        var stat = new UserStat { StatId = 5, UserId = 1, StatLabel = "Years", StatValue = "20+" };

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// An existing statistic is written through to the repository once its presence is confirmed.
    /// </summary>
    [Fact]
    public async Task UpdateStatWritesExistingRow()
    {
        // Arrange
        var stat = new UserStat { StatId = 5, UserId = 1, StatLabel = "Years", StatValue = "21+" };
        statsRepo.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns(stat);

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).UpdateAsync(stat, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A statistic with no identifier is routed to create, so the single admin form can serve both
    /// add and edit.
    /// </summary>
    [Fact]
    public async Task SaveStatCreatesWhenIdIsZero()
    {
        // Arrange
        statsRepo.CreateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>()).Returns(11L);
        var stat = new UserStat { StatId = 0, UserId = 1, StatLabel = "Articles", StatValue = "200+" };

        // Act
        var result = await service.SaveStatAsync(stat);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).CreateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
        await statsRepo.DidNotReceive().UpdateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A statistic that already carries an identifier is routed to update.
    /// </summary>
    [Fact]
    public async Task SaveStatUpdatesWhenIdIsSet()
    {
        // Arrange
        var stat = new UserStat { StatId = 12, UserId = 1, StatLabel = "Articles", StatValue = "201+" };
        statsRepo.GetByIdAsync(12L, Arg.Any<CancellationToken>()).Returns(stat);

        // Act
        var result = await service.SaveStatAsync(stat);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).UpdateAsync(stat, Arg.Any<CancellationToken>());
        await statsRepo.DidNotReceive().CreateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deleting a statistic that has already gone is reported, so two admins editing at once see
    /// what happened instead of a false success.
    /// </summary>
    [Fact]
    public async Task DeleteStatReportsMissingRow()
    {
        // Arrange
        statsRepo.GetByIdAsync(3L, Arg.Any<CancellationToken>()).Returns((UserStat?)null);

        // Act
        var result = await service.DeleteStatAsync(3L);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// Deleting an existing statistic removes exactly that row.
    /// </summary>
    [Fact]
    public async Task DeleteStatRemovesExistingRow()
    {
        // Arrange
        statsRepo.GetByIdAsync(3L, Arg.Any<CancellationToken>()).Returns(new UserStat { StatId = 3, UserId = 1 });

        // Act
        var result = await service.DeleteStatAsync(3L);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).DeleteAsync(3L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Reordering stamps display order from the supplied sequence, so the caller never has to
    /// compute order numbers itself.
    /// </summary>
    [Fact]
    public async Task ReorderStatsStampsSequencePositions()
    {
        // Arrange
        var first = new UserStat { StatId = 1, UserId = 7, DisplayOrder = 5 };
        var second = new UserStat { StatId = 2, UserId = 7, DisplayOrder = 9 };
        statsRepo.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(first);
        statsRepo.GetByIdAsync(2L, Arg.Any<CancellationToken>()).Returns(second);

        // Act
        var result = await service.ReorderStatsAsync(7L, new[] { 2L, 1L });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, second.DisplayOrder);
        Assert.Equal(1, first.DisplayOrder);
    }

    /// <summary>
    /// A statistic belonging to another user is skipped, so a forged identifier list cannot
    /// reorder someone else's resume.
    /// </summary>
    [Fact]
    public async Task ReorderStatsSkipsForeignRows()
    {
        // Arrange
        var foreign = new UserStat { StatId = 1, UserId = 99, DisplayOrder = 5 };
        statsRepo.GetByIdAsync(1L, Arg.Any<CancellationToken>()).Returns(foreign);

        // Act
        var result = await service.ReorderStatsAsync(7L, new[] { 1L });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, foreign.DisplayOrder);
        await statsRepo.DidNotReceive().UpdateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An empty identifier list is refused, because it would silently wipe nothing while
    /// reporting success.
    /// </summary>
    [Fact]
    public async Task ReorderStatsRejectsEmptySequence()
    {
        // Arrange, Act
        var result = await service.ReorderStatsAsync(7L, Array.Empty<long>());

        // Assert
        Assert.Equal("A user and at least one statistic are required", result.ErrorMessage);
    }

    /// <summary>
    /// Validation is exposed as a static helper so a form can pre-check without a service
    /// instance; a fully valid statistic passes it.
    /// </summary>
    [Fact]
    public void ValidateStatAcceptsCompleteStatistic()
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = "Conference talks", StatValue = "40+" };

        // Act
        var result = UserStatsSvc.ValidateStat(stat);

        // Assert
        Assert.True(result.IsSuccess);
    }
}
