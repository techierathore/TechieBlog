namespace BlogEngine.Common;

/// <summary>
/// Estimates how long a post takes to read, for the "5 min read" badge on listings and articles.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A reading-time estimate is a reader-facing affordance, not a measurement:
/// it sets an expectation before someone commits to an article. Keeping the arithmetic in one place
/// means every surface that shows it agrees.</para>
///
/// <para><b>Code Flow:</b> a page passes the post body to <see cref="Calculate"/> for the formatted
/// badge, or to <see cref="GetMinutes"/> / <see cref="GetWordCount"/> when it needs the raw numbers
/// (structured data, sorting, an analytics field).</para>
///
/// <para><b>Dependencies:</b> None — pure BCL string splitting, no configuration and no I/O.</para>
///
/// <para><b>Accuracy, honestly.</b> <see cref="WordsPerMinute"/> is 200, a conventional average for
/// prose. Two things follow. First, the input is <b>Markdown source</b>, so syntax characters,
/// link URLs and fenced code blocks are all counted as words — a code-heavy technical post is
/// over-estimated, which for this blog's subject matter is the common case. Second, nobody reads
/// code at prose speed anyway, so a more precise word count would not make the estimate more
/// truthful. It is an approximation presented as one, and no caller should treat it as data.</para>
///
/// <para><b>Usage:</b> Static and cheap; call it at render time. The minimum result is one minute,
/// so a very short post reads "1 min read" rather than "0 min read".</para>
/// </remarks>
public static class ReadingTimeCalculator
{
    /// <summary>
    /// Average words per minute for reading speed.
    /// </summary>
    private const int WordsPerMinute = 200;

    /// <summary>
    /// Produces the display-ready reading-time badge for a piece of content.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The result is rounded UP and floored at one minute, so a short
    /// post never renders "0 min read" and a post just over a boundary is never under-promised.</para>
    /// <para><b>Flow:</b> guard blank content → count words → divide by
    /// <see cref="WordsPerMinute"/> → round up, floor at 1 → format.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="content">The content to analyse, normally Markdown source. May be null.</param>
    /// <returns>A phrase such as <c>"5 min read"</c>; <c>"1 min read"</c> for blank content.</returns>
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
    /// Counts the whitespace-separated words in a piece of content.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Word" means "run of non-whitespace", which is the only
    /// definition that is language-agnostic and needs no dictionary. Markdown syntax and URLs
    /// therefore count as words — see the type remarks for why that is accepted.</para>
    /// <para><b>Flow:</b> guard blank content → split on space, newline, carriage return and tab,
    /// discarding empty entries → count.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="content">The content to analyse. May be null.</param>
    /// <returns>The number of words; zero for null or whitespace-only content.</returns>
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
    /// Gets the estimated reading time as a number of minutes, without formatting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The same arithmetic <see cref="Calculate"/> uses, exposed for
    /// callers that need the value rather than the phrase — structured data, sorting, or a UI that
    /// words the badge differently.</para>
    /// <para><b>Flow:</b> count words → divide by <see cref="WordsPerMinute"/> → round up, floor
    /// at 1.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="content">The content to analyse. May be null.</param>
    /// <returns>Estimated minutes, never less than 1.</returns>
    public static int GetMinutes(string content)
    {
        var wordCount = GetWordCount(content);
        return Math.Max(1, (int)Math.Ceiling(wordCount / (double)WordsPerMinute));
    }
}
