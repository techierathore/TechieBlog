using BlogEngine.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="ReadingTimeCalculator"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the 200 words-per-minute model, the minimum of one
/// minute, and the exact "N min read" label rendered on every post card.</para>
/// <para><b>Dependencies:</b> None. The calculator is a pure static helper.</para>
/// </remarks>
public class ReadingTimeCalculatorTests
{
    /// <summary>
    /// Builds a content string containing exactly the requested number of words.
    /// </summary>
    /// <param name="wordCount">How many space-separated words to produce.</param>
    /// <returns>A space-separated string of <paramref name="wordCount"/> words.</returns>
    private static string BuildContent(int wordCount)
    {
        return string.Join(' ', Enumerable.Repeat("word", wordCount));
    }

    /// <summary>
    /// Blank content still reports the floor value "1 min read" so the UI never
    /// shows "0 min read".
    /// </summary>
    /// <param name="content">The blank content under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void CalculateReturnsOneMinuteForBlankContent(string content)
    {
        // Arrange, Act
        var label = ReadingTimeCalculator.Calculate(content);

        // Assert
        Assert.Equal("1 min read", label);
    }

    /// <summary>
    /// Exactly 200 words — one full minute at the configured reading speed —
    /// reports one minute, not two.
    /// </summary>
    [Fact]
    public void CalculateReportsOneMinuteAtExactlyOneMinuteOfWords()
    {
        // Arrange
        var content = BuildContent(200);

        // Act
        var label = ReadingTimeCalculator.Calculate(content);

        // Assert
        Assert.Equal("1 min read", label);
    }

    /// <summary>
    /// A single word past the 200-word boundary rounds up to two minutes,
    /// confirming the ceiling behaviour rather than rounding to nearest.
    /// </summary>
    [Fact]
    public void CalculateRoundsPartialMinuteUp()
    {
        // Arrange
        var content = BuildContent(201);

        // Act
        var label = ReadingTimeCalculator.Calculate(content);

        // Assert
        Assert.Equal("2 min read", label);
    }

    /// <summary>
    /// A thousand words is reported as a five minute read.
    /// </summary>
    [Fact]
    public void CalculateScalesWithWordCount()
    {
        // Arrange
        var content = BuildContent(1000);

        // Act
        var label = ReadingTimeCalculator.Calculate(content);

        // Assert
        Assert.Equal("5 min read", label);
    }

    /// <summary>
    /// Word counting splits on spaces, tabs and both newline characters, and
    /// discards the empty entries that repeated separators would otherwise create.
    /// </summary>
    [Fact]
    public void GetWordCountSplitsOnAllWhitespaceKinds()
    {
        // Arrange
        var content = "alpha beta\tgamma\r\ndelta   epsilon";

        // Act
        var wordCount = ReadingTimeCalculator.GetWordCount(content);

        // Assert
        Assert.Equal(5, wordCount);
    }

    /// <summary>
    /// Blank content has a word count of zero — the one place the calculator is
    /// allowed to report less than one.
    /// </summary>
    [Fact]
    public void GetWordCountReturnsZeroForBlankContent()
    {
        // Arrange, Act
        var wordCount = ReadingTimeCalculator.GetWordCount("   ");

        // Assert
        Assert.Equal(0, wordCount);
    }

    /// <summary>
    /// The numeric minutes accessor applies the same one-minute floor as the
    /// formatted label, even for empty content.
    /// </summary>
    [Fact]
    public void GetMinutesAppliesOneMinuteFloor()
    {
        // Arrange, Act
        var minutes = ReadingTimeCalculator.GetMinutes(string.Empty);

        // Assert
        Assert.Equal(1, minutes);
    }

    /// <summary>
    /// The numeric minutes accessor agrees with the formatted label for a
    /// multi-minute article.
    /// </summary>
    [Fact]
    public void GetMinutesMatchesFormattedLabel()
    {
        // Arrange
        var content = BuildContent(650);

        // Act
        var minutes = ReadingTimeCalculator.GetMinutes(content);

        // Assert
        Assert.Equal(4, minutes);
    }
}
