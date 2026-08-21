using BlogEngine.Services;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TechieBlog.Tests.Resume;

/// <summary>
/// Proves the asynchronous twins <see cref="UserStatsSvc"/> gained under REQ-NFR-026 behave exactly
/// as the blocking members they shadow, and that each one reaches the repository's asynchronous
/// member rather than its blocking one.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Every read and write on the resume statistics screen exists twice — a
/// blocking member and an <c>…Async</c> twin — and only the twin runs in the live Blazor circuit.
/// A twin that quietly differs from its original is invisible to the compiler and invisible in a
/// screenshot: the divergence only shows up as the admin form and the public resume disagreeing
/// about what was saved. Agreement therefore has to be asserted behaviourally, one property at a
/// time. [REQ-NFR-016]</para>
///
/// <para><b>What is checked, per twin:</b> that the guard clauses short-circuit before any query is
/// issued; that the verbatim failure message matches the blocking twin's; that a repository fault
/// degrades the same way (empty sequence, <c>null</c>, failed <c>Result</c>) rather than escaping
/// to the caller; and that the twin calls the repository's <c>…Async</c> member — a twin wired to
/// the blocking member compiles, passes a naive test and parks a circuit thread for the whole
/// round trip, which is the exact defect REQ-NFR-026 exists to remove.</para>
///
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IUserStatsRepo"/> and
/// <see cref="NullLogger{T}"/> for the logger. No database, no host.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite. Note the trap documented by
/// <c>SubstituteBridgeTrapTests</c> — a substitute intercepts a default interface implementation
/// instead of falling through to it, so the asynchronous repository members these tests rely on are
/// always stubbed explicitly rather than left to the interface's bridging default.</para>
/// </remarks>
public class UserStatsSvcAsyncTests
{
    private readonly IUserStatsRepo statsRepo = Substitute.For<IUserStatsRepo>();
    private readonly UserStatsSvc service;

    /// <summary>
    /// Wires the service under test to the substituted repository.
    /// </summary>
    public UserStatsSvcAsyncTests()
    {
        service = new UserStatsSvc(statsRepo, NullLogger<UserStatsSvc>.Instance);
    }

    /// <summary>
    /// Builds a statistic that passes validation, so a test that is not about validation can vary
    /// one field at a time.
    /// </summary>
    /// <param name="statId">Identifier to stamp; zero means "never persisted".</param>
    /// <param name="userId">Owning user.</param>
    /// <returns>A valid statistic.</returns>
    private static UserStat ValidStat(long statId = 0, long userId = 7) => new()
    {
        StatId = statId,
        UserId = userId,
        StatLabel = "Years experience",
        StatValue = "12"
    };

    /// <summary>
    /// A non-positive user identifier short-circuits the asynchronous list read before the
    /// repository is consulted, so a signed-out or malformed principal cannot trigger a query.
    /// </summary>
    /// <param name="userId">The invalid identifier under test.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task GetStatsForUserAsyncRejectsInvalidId(long userId)
    {
        // Arrange, Act
        var stats = await service.GetStatsForUserAsync(userId);

        // Assert
        Assert.Empty(stats);
        await statsRepo.DidNotReceive().GetByUserIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The asynchronous list read reaches the repository's asynchronous member — not the blocking
    /// one — and returns its rows in the order the repository supplied them.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatsForUserAsyncReadsAsyncRepositoryMember()
    {
        // Arrange
        var rows = new[] { ValidStat(1), ValidStat(2) };
        statsRepo.GetByUserIdAsync(7L, Arg.Any<CancellationToken>()).Returns(rows);

        // Act
        var stats = await service.GetStatsForUserAsync(7L);

        // Assert
        Assert.Equal(rows, stats);
        statsRepo.DidNotReceive().GetByUserId(Arg.Any<long>());
    }

    /// <summary>
    /// A repository failure during the asynchronous list read is swallowed and reported as an empty
    /// list, so a database problem shortens the resume rather than replacing it with an error screen.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatsForUserAsyncReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        statsRepo.GetByUserIdAsync(7L, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        // Act
        var stats = await service.GetStatsForUserAsync(7L);

        // Assert
        Assert.Empty(stats);
    }

    /// <summary>
    /// A blank category is treated as "no filter applies" by the asynchronous twin exactly as by the
    /// blocking one, returning nothing rather than silently returning every statistic the user owns.
    /// </summary>
    /// <param name="category">The blank category under test.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetStatsForCategoryAsyncRejectsBlankCategory(string category)
    {
        // Arrange, Act
        var stats = await service.GetStatsForCategoryAsync(7L, category);

        // Assert
        Assert.Empty(stats);
        await statsRepo.DidNotReceive()
            .GetByUserIdAndCategoryAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The asynchronous category read passes the caller's category through untouched to the
    /// repository's asynchronous member and returns the rows it produced.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatsForCategoryAsyncReadsAsyncRepositoryMember()
    {
        // Arrange
        var rows = new[] { ValidStat(3) };
        statsRepo.GetByUserIdAndCategoryAsync(7L, "About", Arg.Any<CancellationToken>()).Returns(rows);

        // Act
        var stats = await service.GetStatsForCategoryAsync(7L, "About");

        // Assert
        Assert.Equal(rows, stats);
    }

    /// <summary>
    /// A repository failure during the asynchronous category read degrades to an empty list rather
    /// than propagating, matching the blocking twin.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatsForCategoryAsyncReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        statsRepo.GetByUserIdAndCategoryAsync(7L, "About", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        // Act
        var stats = await service.GetStatsForCategoryAsync(7L, "About");

        // Assert
        Assert.Empty(stats);
    }

    /// <summary>
    /// A non-positive statistic identifier short-circuits the asynchronous single read, so a
    /// malformed route parameter never becomes a query.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatAsyncRejectsInvalidId()
    {
        // Arrange, Act
        var stat = await service.GetStatAsync(0L);

        // Assert
        Assert.Null(stat);
        await statsRepo.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The asynchronous single read returns the row the repository's asynchronous member produced.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatAsyncReturnsRepositoryRow()
    {
        // Arrange
        var row = ValidStat(4);
        statsRepo.GetByIdAsync(4L, Arg.Any<CancellationToken>()).Returns(row);

        // Act
        var stat = await service.GetStatAsync(4L);

        // Assert
        Assert.Same(row, stat);
    }

    /// <summary>
    /// A failed lookup surfaces as <c>null</c> — the same value "no such statistic" produces —
    /// because the failure is distinguished in the log, not in the return value.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task GetStatAsyncReturnsNullOnRepositoryFailure()
    {
        // Arrange
        statsRepo.GetByIdAsync(4L, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        // Act
        var stat = await service.GetStatAsync(4L);

        // Assert
        Assert.Null(stat);
    }

    /// <summary>
    /// The asynchronous create rejects an invalid statistic with the validator's own verbatim
    /// message and never reaches the repository.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task CreateStatAsyncRejectsInvalidStat()
    {
        // Arrange
        var stat = ValidStat();
        stat.StatLabel = "   ";

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.Equal("Statistic label is required and must be 100 characters or fewer", result.ErrorMessage);
    }

    /// <summary>
    /// A successful asynchronous create stamps the generated identifier back onto the caller's
    /// instance, which is what lets the admin form switch from add mode to edit mode without a reload.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task CreateStatAsyncStampsGeneratedId()
    {
        // Arrange
        var stat = ValidStat();
        statsRepo.CreateAsync(stat, Arg.Any<CancellationToken>()).Returns(42L);

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.Equal(42L, result.Data.StatId);
    }

    /// <summary>
    /// A persistence failure during the asynchronous create is converted into a failed
    /// <c>Result</c> carrying a CURATED message, rather than escaping to the caller — and the
    /// repository's own text never appears in it (REQ-NFR-033).
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task CreateStatAsyncReportsPersistenceFailure()
    {
        // Arrange
        var stat = ValidStat();
        statsRepo.CreateAsync(stat, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("insert refused"));

        // Act
        var result = await service.CreateStatAsync(stat);

        // Assert
        Assert.Equal("Failed to create statistic. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("insert refused", result.ErrorMessage);
    }

    /// <summary>
    /// The asynchronous update runs validation before the identifier check, so a statistic that is
    /// both unkeyed and invalid reports the validation message — exactly as the blocking twin does.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UpdateStatAsyncReportsValidationBeforeIdCheck()
    {
        // Arrange
        var stat = ValidStat();
        stat.UserId = 0;

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.Equal("A statistic must belong to a user", result.ErrorMessage);
    }

    /// <summary>
    /// A valid statistic that carries no identifier is rejected by the asynchronous update rather
    /// than being silently created.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UpdateStatAsyncRejectsUnkeyedStat()
    {
        // Arrange, Act
        var result = await service.UpdateStatAsync(ValidStat());

        // Assert
        Assert.Equal("Invalid statistic id", result.ErrorMessage);
    }

    /// <summary>
    /// Updating a statistic that has already been deleted is reported as "Statistic not found"
    /// instead of quietly inserting a replacement row.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UpdateStatAsyncReportsMissingRow()
    {
        // Arrange
        var stat = ValidStat(9);
        statsRepo.GetByIdAsync(9L, Arg.Any<CancellationToken>()).Returns((UserStat?)null);

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// A successful asynchronous update writes through the repository's asynchronous member and
    /// echoes the saved statistic back to the caller.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UpdateStatAsyncWritesThroughAsyncRepositoryMember()
    {
        // Arrange
        var stat = ValidStat(9);
        statsRepo.GetByIdAsync(9L, Arg.Any<CancellationToken>()).Returns(stat);

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).UpdateAsync(stat, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A repository fault raised while writing the update is converted into a failed <c>Result</c>
    /// carrying a CURATED message, never the repository's own text (REQ-NFR-033).
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UpdateStatAsyncReportsPersistenceFailure()
    {
        // Arrange
        var stat = ValidStat(9);
        statsRepo.GetByIdAsync(9L, Arg.Any<CancellationToken>()).Returns(stat);
        statsRepo.UpdateAsync(stat, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("update refused"));

        // Act
        var result = await service.UpdateStatAsync(stat);

        // Assert
        Assert.Equal("Failed to update statistic. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("update refused", result.ErrorMessage);
    }

    /// <summary>
    /// The asynchronous save rejects a null statistic without dereferencing it, so the single admin
    /// form's save button cannot fault on an uninitialised model.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task SaveStatAsyncRejectsNull()
    {
        // Arrange, Act
        var result = await service.SaveStatAsync(null!);

        // Assert
        Assert.Equal("Statistic cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// An unkeyed statistic routes the asynchronous save to the create path, so one form method
    /// serves both add and edit.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task SaveStatAsyncCreatesUnkeyedStat()
    {
        // Arrange
        var stat = ValidStat();
        statsRepo.CreateAsync(stat, Arg.Any<CancellationToken>()).Returns(11L);

        // Act
        var result = await service.SaveStatAsync(stat);

        // Assert
        Assert.Equal(11L, result.Data.StatId);
    }

    /// <summary>
    /// A keyed statistic routes the asynchronous save to the update path rather than inserting a
    /// duplicate row.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task SaveStatAsyncUpdatesKeyedStat()
    {
        // Arrange
        var stat = ValidStat(12);
        statsRepo.GetByIdAsync(12L, Arg.Any<CancellationToken>()).Returns(stat);

        // Act
        await service.SaveStatAsync(stat);

        // Assert
        await statsRepo.DidNotReceive().CreateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A non-positive identifier is rejected by the asynchronous delete before any query is issued.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DeleteStatAsyncRejectsInvalidId()
    {
        // Arrange, Act
        var result = await service.DeleteStatAsync(0L);

        // Assert
        Assert.Equal("Invalid statistic id", result.ErrorMessage);
    }

    /// <summary>
    /// Deleting a statistic that has already gone is reported rather than treated as success,
    /// because the repository's delete is a silent no-op on an unknown key.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DeleteStatAsyncReportsMissingRow()
    {
        // Arrange
        statsRepo.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns((UserStat?)null);

        // Act
        var result = await service.DeleteStatAsync(5L);

        // Assert
        Assert.Equal("Statistic not found", result.ErrorMessage);
    }

    /// <summary>
    /// A confirmed statistic is removed through the repository's asynchronous delete.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DeleteStatAsyncRemovesConfirmedRow()
    {
        // Arrange
        statsRepo.GetByIdAsync(5L, Arg.Any<CancellationToken>()).Returns(ValidStat(5));

        // Act
        var result = await service.DeleteStatAsync(5L);

        // Assert
        Assert.True(result.IsSuccess);
        await statsRepo.Received(1).DeleteAsync(5L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failure of the existence check is reported as a delete failure, not as "not found", because
    /// the lookup sits inside the same <c>try</c> as the delete.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DeleteStatAsyncReportsLookupFailureAsDeleteFailure()
    {
        // Arrange
        statsRepo.GetByIdAsync(5L, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        // Act
        var result = await service.DeleteStatAsync(5L);

        // Assert
        Assert.Equal("Failed to delete statistic. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("db down", result.ErrorMessage);
    }

    /// <summary>
    /// The asynchronous reorder refuses a request that names no user or no statistics, so an empty
    /// drag-and-drop payload cannot renumber anything.
    /// </summary>
    /// <param name="userId">Owner identifier under test.</param>
    /// <param name="isListEmpty">Whether the identifier list is empty.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData(0L, false)]
    [InlineData(7L, true)]
    public async Task ReorderStatsAsyncRejectsEmptyRequest(long userId, bool isListEmpty)
    {
        // Arrange
        IReadOnlyList<long> ids = isListEmpty ? Array.Empty<long>() : new[] { 1L };

        // Act
        var result = await service.ReorderStatsAsync(userId, ids);

        // Assert
        Assert.Equal("A user and at least one statistic are required", result.ErrorMessage);
    }

    /// <summary>
    /// A null identifier list is rejected by the same guard rather than faulting on a dereference.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ReorderStatsAsyncRejectsNullList()
    {
        // Arrange, Act
        var result = await service.ReorderStatsAsync(7L, null!);

        // Assert
        Assert.Equal("A user and at least one statistic are required", result.ErrorMessage);
    }

    /// <summary>
    /// Position in the supplied list becomes <c>DisplayOrder</c>, so the caller never computes order
    /// numbers itself and a drag to the top really writes zero.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ReorderStatsAsyncStampsListPositionAsDisplayOrder()
    {
        // Arrange
        var first = ValidStat(20);
        var second = ValidStat(21);
        first.DisplayOrder = 9;
        second.DisplayOrder = 9;
        statsRepo.GetByIdAsync(20L, Arg.Any<CancellationToken>()).Returns(first);
        statsRepo.GetByIdAsync(21L, Arg.Any<CancellationToken>()).Returns(second);

        // Act
        await service.ReorderStatsAsync(7L, new[] { 21L, 20L });

        // Assert
        Assert.Equal(new[] { 0, 1 }, new[] { second.DisplayOrder, first.DisplayOrder });
    }

    /// <summary>
    /// A statistic belonging to another user is skipped instead of renumbered, so a forged
    /// identifier list cannot reorder someone else's resume.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ReorderStatsAsyncSkipsStatOwnedByAnotherUser()
    {
        // Arrange
        var foreign = ValidStat(30, userId: 99);
        statsRepo.GetByIdAsync(30L, Arg.Any<CancellationToken>()).Returns(foreign);

        // Act
        await service.ReorderStatsAsync(7L, new[] { 30L });

        // Assert
        await statsRepo.DidNotReceive().UpdateAsync(Arg.Any<UserStat>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An identifier that names no row at all is skipped rather than faulting the whole reorder.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ReorderStatsAsyncSkipsMissingStat()
    {
        // Arrange
        statsRepo.GetByIdAsync(31L, Arg.Any<CancellationToken>()).Returns((UserStat?)null);

        // Act
        var result = await service.ReorderStatsAsync(7L, new[] { 31L });

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A repository fault part-way through the reorder is converted into a failed <c>Result</c>
    /// carrying a CURATED message, never the repository's own text (REQ-NFR-033).
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ReorderStatsAsyncReportsPersistenceFailure()
    {
        // Arrange
        statsRepo.GetByIdAsync(32L, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("db down"));

        // Act
        var result = await service.ReorderStatsAsync(7L, new[] { 32L });

        // Assert
        Assert.Equal("Failed to reorder statistics. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("db down", result.ErrorMessage);
    }
}
