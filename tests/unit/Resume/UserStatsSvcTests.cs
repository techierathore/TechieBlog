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
    public void GetStatsForUserRejectsInvalidId(long userId)
    {
        // Arrange, Act
        var stats = service.GetStatsForUser(userId);

        // Assert
        Assert.Empty(stats);
        statsRepo.DidNotReceive().GetByUserId(Arg.Any<long>());
    }

    /// <summary>
    /// A repository failure during a read is swallowed and reported as an empty list, so the
    /// resume renders a shorter page rather than an error screen.
    /// </summary>
    [Fact]
    public void GetStatsForUserReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        statsRepo.GetByUserId(1L).Throws(new InvalidOperationException("db down"));

        // Act
        var stats = service.GetStatsForUser(1L);

        // Assert
        Assert.Empty(stats);
    }

    /// <summary>
    /// A blank category is treated as "no filter applies" and returns nothing rather than
    /// silently returning every statistic the user owns.
    /// </summary>
    [Fact]
    public void GetStatsForCategoryRejectsBlankCategory()
    {
        // Arrange, Act
        var stats = service.GetStatsForCategory(1L, "   ");

        // Assert
        Assert.Empty(stats);
        statsRepo.DidNotReceive().GetByUserIdAndCategory(Arg.Any<long>(), Arg.Any<string>());
    }

    /// <summary>
    /// A category filter is passed straight through to the repository and its rows are returned.
    /// </summary>
    [Fact]
    public void GetStatsForCategoryReturnsRepositoryRows()
    {
        // Arrange
        var community = new UserStat { StatId = 7, UserId = 1, StatLabel = "Talks", StatValue = "40+", StatCategory = "Community" };
        statsRepo.GetByUserIdAndCategory(1L, "Community").Returns(new[] { community });

        // Act
        var stats = service.GetStatsForCategory(1L, "Community").ToList();

        // Assert
        Assert.Equal(7L, Assert.Single(stats).StatId);
    }

    /// <summary>
    /// A statistic with no owner is meaningless and is refused before any insert is attempted.
    /// </summary>
    [Fact]
    public void CreateStatRejectsMissingOwner()
    {
        // Arrange
        var stat = new UserStat { UserId = 0, StatLabel = "Years", StatValue = "20+" };

        // Act
        var result = service.CreateStat(stat);

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
    public void CreateStatRejectsBlankLabel(string label)
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = label, StatValue = "20+" };

        // Act
        var result = service.CreateStat(stat);

        // Assert
        Assert.StartsWith("Statistic label is required", result.ErrorMessage);
    }

    /// <summary>
    /// A label longer than the 100-character column is refused rather than left to fail in the
    /// database.
    /// </summary>
    [Fact]
    public void CreateStatRejectsOverlongLabel()
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = new string('x', 101), StatValue = "20+" };

        // Act
        var result = service.CreateStat(stat);

        // Assert
        Assert.StartsWith("Statistic label is required", result.ErrorMessage);
    }

    /// <summary>
    /// A blank value is refused — a tile with a label but no figure would render as an empty box.
    /// </summary>
    [Fact]
    public void CreateStatRejectsBlankValue()
    {
        // Arrange
        var stat = new UserStat { UserId = 1, StatLabel = "Years in software", StatValue = "  " };

        // Act
        var result = service.CreateStat(stat);

        // Assert
        Assert.StartsWith("Statistic value is required", result.ErrorMessage);
    }

    /// <summary>
    /// A valid statistic is inserted and the generated identifier is stamped back onto the entity
    /// the caller supplied.
    /// </summary>
    [Fact]
    public void CreateStatStampsGeneratedId()
    {
        // Arrange
        statsRepo.Create(Arg.Any<UserStat>()).Returns(99L);
        var stat = new UserStat { UserId = 1, StatLabel = "Years in software", StatValue = "20+" };

        // Act
        var result = service.CreateStat(stat);

        // Assert
        Assert.Equal(99L, result.Data.StatId);
    }

    /// <summary>
    /// An update aimed at a statistic that has since been deleted is reported as not found rather
    /// than silently re-creating the row.
    /// </summary>
    [Fact]
    public void UpdateStatReportsMissingRow()
    {
        // Arrange
        statsRepo.GetById(5L).Returns((UserStat?)null);
        var stat = new UserStat { StatId = 5, UserId = 1, StatLabel = "Years", StatValue = "20+" };

        // Act
        var result = service.UpdateStat(stat);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// An existing statistic is written through to the repository once its presence is confirmed.
    /// </summary>
    [Fact]
    public void UpdateStatWritesExistingRow()
    {
        // Arrange
        var stat = new UserStat { StatId = 5, UserId = 1, StatLabel = "Years", StatValue = "21+" };
        statsRepo.GetById(5L).Returns(stat);

        // Act
        var result = service.UpdateStat(stat);

        // Assert
        Assert.True(result.IsSuccess);
        statsRepo.Received(1).Update(stat);
    }

    /// <summary>
    /// A statistic with no identifier is routed to create, so the single admin form can serve both
    /// add and edit.
    /// </summary>
    [Fact]
    public void SaveStatCreatesWhenIdIsZero()
    {
        // Arrange
        statsRepo.Create(Arg.Any<UserStat>()).Returns(11L);
        var stat = new UserStat { StatId = 0, UserId = 1, StatLabel = "Articles", StatValue = "200+" };

        // Act
        var result = service.SaveStat(stat);

        // Assert
        Assert.True(result.IsSuccess);
        statsRepo.Received(1).Create(Arg.Any<UserStat>());
        statsRepo.DidNotReceive().Update(Arg.Any<UserStat>());
    }

    /// <summary>
    /// A statistic that already carries an identifier is routed to update.
    /// </summary>
    [Fact]
    public void SaveStatUpdatesWhenIdIsSet()
    {
        // Arrange
        var stat = new UserStat { StatId = 12, UserId = 1, StatLabel = "Articles", StatValue = "201+" };
        statsRepo.GetById(12L).Returns(stat);

        // Act
        var result = service.SaveStat(stat);

        // Assert
        Assert.True(result.IsSuccess);
        statsRepo.Received(1).Update(stat);
        statsRepo.DidNotReceive().Create(Arg.Any<UserStat>());
    }

    /// <summary>
    /// Deleting a statistic that has already gone is reported, so two admins editing at once see
    /// what happened instead of a false success.
    /// </summary>
    [Fact]
    public void DeleteStatReportsMissingRow()
    {
        // Arrange
        statsRepo.GetById(3L).Returns((UserStat?)null);

        // Act
        var result = service.DeleteStat(3L);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// Deleting an existing statistic removes exactly that row.
    /// </summary>
    [Fact]
    public void DeleteStatRemovesExistingRow()
    {
        // Arrange
        statsRepo.GetById(3L).Returns(new UserStat { StatId = 3, UserId = 1 });

        // Act
        var result = service.DeleteStat(3L);

        // Assert
        Assert.True(result.IsSuccess);
        statsRepo.Received(1).Delete(3L);
    }

    /// <summary>
    /// Reordering stamps display order from the supplied sequence, so the caller never has to
    /// compute order numbers itself.
    /// </summary>
    [Fact]
    public void ReorderStatsStampsSequencePositions()
    {
        // Arrange
        var first = new UserStat { StatId = 1, UserId = 7, DisplayOrder = 5 };
        var second = new UserStat { StatId = 2, UserId = 7, DisplayOrder = 9 };
        statsRepo.GetById(1L).Returns(first);
        statsRepo.GetById(2L).Returns(second);

        // Act
        var result = service.ReorderStats(7L, new[] { 2L, 1L });

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
    public void ReorderStatsSkipsForeignRows()
    {
        // Arrange
        var foreign = new UserStat { StatId = 1, UserId = 99, DisplayOrder = 5 };
        statsRepo.GetById(1L).Returns(foreign);

        // Act
        var result = service.ReorderStats(7L, new[] { 1L });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, foreign.DisplayOrder);
        statsRepo.DidNotReceive().Update(Arg.Any<UserStat>());
    }

    /// <summary>
    /// An empty identifier list is refused, because it would silently wipe nothing while
    /// reporting success.
    /// </summary>
    [Fact]
    public void ReorderStatsRejectsEmptySequence()
    {
        // Arrange, Act
        var result = service.ReorderStats(7L, Array.Empty<long>());

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
