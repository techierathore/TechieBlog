using BlogModels;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="SeriesStatus"/> and <see cref="BlogSeries.IsComplete"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the two canonical <c>BlogSeries.Status</c> literals to the exact
/// strings the database stores. REQ-UI-024 was a silent render failure caused by the C# side
/// comparing against <c>"Complete"</c> while every row held <c>"Completed"</c>: the admin grid
/// showed a finished series as "In Progress" and its filter tab counted zero. Because the values
/// are plain text on both sides, only an assertion on the literal itself can stop that drift
/// returning — a typo here fails the build instead of the screen.</para>
/// <para><b>Dependencies:</b> None. <see cref="SeriesStatus"/> is a pure static helper and
/// <see cref="BlogSeries"/> is a plain entity.</para>
/// </remarks>
public class SeriesStatusTests
{
    /// <summary>
    /// The in-progress literal is exactly the string stored by the column default from
    /// <c>007-FixBlogSeriesAndPostTag.sql</c> — capital I, capital P, one space.
    /// </summary>
    [Fact]
    public void InProgressMatchesTheStoredLiteral()
    {
        // Assert
        Assert.Equal("In Progress", SeriesStatus.InProgress);
    }

    /// <summary>
    /// The completed literal is exactly the string seeded by <c>019-SampleData.sql</c> and
    /// enforced by <c>029-NormalizeSeriesStatus.sql</c> — "Completed", not "Complete".
    /// </summary>
    [Fact]
    public void CompletedMatchesTheStoredLiteral()
    {
        // Assert
        Assert.Equal("Completed", SeriesStatus.Completed);
    }

    /// <summary>
    /// The picker's option list is exactly the two canonical values, in editor order, and nothing
    /// else — no legacy spelling is ever offered for writing.
    /// </summary>
    [Fact]
    public void AllContainsOnlyTheTwoCanonicalValues()
    {
        // Assert
        Assert.Equal(new[] { "In Progress", "Completed" }, SeriesStatus.All);
    }

    /// <summary>
    /// The canonical completed literal reports as complete — the assertion the pre-fix code failed.
    /// </summary>
    [Fact]
    public void IsCompletedAcceptsTheCanonicalValue()
    {
        // Assert
        Assert.True(SeriesStatus.IsCompleted("Completed"));
    }

    /// <summary>
    /// The superseded "Complete" spelling still reads as complete, so a row written by the pre-fix
    /// editor renders correctly instead of silently reverting to "In Progress".
    /// </summary>
    [Fact]
    public void IsCompletedAcceptsTheLegacySpelling()
    {
        // Assert
        Assert.True(SeriesStatus.IsCompleted("Complete"));
    }

    /// <summary>
    /// Casing and surrounding whitespace do not change the verdict, unlike the original ordinal,
    /// case-sensitive equality.
    /// </summary>
    [Theory]
    [InlineData("completed")]
    [InlineData("COMPLETED")]
    [InlineData("  Completed  ")]
    public void IsCompletedIgnoresCaseAndWhitespace(string status)
    {
        // Assert
        Assert.True(SeriesStatus.IsCompleted(status));
    }

    /// <summary>
    /// In-progress, blank, null and unrecognised text are all "not complete".
    /// </summary>
    [Theory]
    [InlineData("In Progress")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Abandoned")]
    public void IsCompletedRejectsEverythingElse(string? status)
    {
        // Assert
        Assert.False(SeriesStatus.IsCompleted(status));
    }

    /// <summary>
    /// Normalisation rewrites the legacy spelling to the canonical one, so the write path can only
    /// persist "Completed".
    /// </summary>
    [Fact]
    public void NormalizeRewritesLegacySpellingToCanonical()
    {
        // Assert
        Assert.Equal(SeriesStatus.Completed, SeriesStatus.Normalize("Complete"));
    }

    /// <summary>
    /// Normalisation leaves an already-canonical value untouched, byte for byte.
    /// </summary>
    [Theory]
    [InlineData("In Progress")]
    [InlineData("Completed")]
    public void NormalizeIsIdempotentOnCanonicalValues(string status)
    {
        // Assert
        Assert.Equal(status, SeriesStatus.Normalize(status));
    }

    /// <summary>
    /// Null, blank and unrecognised text all normalise to In Progress — the honest default for a
    /// series whose completion was never asserted.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Halted")]
    public void NormalizeFallsBackToInProgress(string? status)
    {
        // Assert
        Assert.Equal(SeriesStatus.InProgress, SeriesStatus.Normalize(status));
    }

    /// <summary>
    /// A newly constructed series starts in progress, matching the column default so an insert that
    /// omits the status agrees with the database either way.
    /// </summary>
    [Fact]
    public void NewSeriesDefaultsToInProgress()
    {
        // Arrange
        var series = new BlogSeries();

        // Assert
        Assert.Equal(SeriesStatus.InProgress, series.Status);
        Assert.False(series.IsComplete);
    }

    /// <summary>
    /// A series carrying the value the database actually stores reports complete — the exact
    /// REQ-UI-024 regression, expressed as a test.
    /// </summary>
    [Fact]
    public void SeriesWithStoredCompletedValueIsComplete()
    {
        // Arrange
        var series = new BlogSeries { Status = "Completed" };

        // Assert
        Assert.True(series.IsComplete);
    }
}
