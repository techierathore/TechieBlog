namespace BlogEngine.Common;

/// <summary>
/// Utility class for calculating estimated reading time.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Calculates reading time based on word count.</para>
/// <para><b>Usage:</b> Used by blog pages to display estimated reading time.</para>
/// </remarks>
public static class ReadingTimeCalculator
{
    /// <summary>
    /// Average words per minute for reading speed.
    /// </summary>
    private const int WordsPerMinute = 200;

    /// <summary>
    /// Calculates the estimated reading time for the given content.
    /// </summary>
    /// <param name="content">The text content to analyze.</param>
    /// <returns>Formatted reading time string (e.g., "5 min read").</returns>
    public static string Calculate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "1 min read";

        var wordCount = content.Split(
            new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries
        ).Length;

        var minutes = Math.Max(1, (int)Math.Ceiling(wordCount / (double)WordsPerMinute));
        return $"{minutes} min read";
    }

    /// <summary>
    /// Gets the word count for the given content.
    /// </summary>
    /// <param name="content">The text content to analyze.</param>
    /// <returns>Number of words.</returns>
    public static int GetWordCount(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        return content.Split(
            new[] { ' ', '\n', '\r', '\t' },
            StringSplitOptions.RemoveEmptyEntries
        ).Length;
    }

    /// <summary>
    /// Gets the estimated reading time in minutes.
    /// </summary>
    /// <param name="content">The text content to analyze.</param>
    /// <returns>Number of minutes to read.</returns>
    public static int GetMinutes(string content)
    {
        var wordCount = GetWordCount(content);
        return Math.Max(1, (int)Math.Ceiling(wordCount / (double)WordsPerMinute));
    }
}
