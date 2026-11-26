using System.Globalization;
using System.Text;
using BlogModels;

namespace BlogUI.Common;

/// <summary>
/// Builds the plain text the Analytics page's "Copy numbers" button puts on the clipboard.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-UI-070 (BRD-110). The owner pastes this into a planning assistant every
/// week, so it is plain text with one fact per line, no markup, no tables and no colour — something
/// a person or a language model reads the same way.</para>
///
/// <para><b>Code Flow:</b> header → views today / last 7 / last 30 days → the ten most read posts in
/// the page's range with views and rating → the referring sites in the same range.</para>
///
/// <para><b>Dependencies:</b> <see cref="PopularPost"/> and <see cref="ReferrerEngagement"/> only.
/// Pure: no service, no clock, no culture — the caller passes the time, and numbers are written with
/// the invariant culture, so a unit test pins the exact output.</para>
///
/// <para><b>Usage:</b> <c>AnalyticsDashboard.CopyNumbersAsync</c> passes what the page already
/// loaded, so the clipboard always matches the screen.</para>
/// </remarks>
public static class AnalyticsNumbersFormatter
{
    /// <summary>
    /// The most posts listed; the owner asked for the ten most read.
    /// </summary>
    public const int MaxPosts = 10;

    /// <summary>
    /// The most referring sites listed.
    /// </summary>
    public const int MaxReferrers = 10;

    /// <summary>
    /// Formats the page's numbers as plain text.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> ratings are the confirmed average and confirmed count
    /// (REQ-FN-063); a post with no confirmed rating says "no ratings yet" rather than "0.0".</para>
    /// <para><b>Flow:</b> append each block in order; empty lists say so in words.</para>
    /// <para><b>Side Effects:</b> none.</para>
    /// </remarks>
    /// <param name="numbers">The figures to format.</param>
    /// <returns>Plain text, lines separated by <c>\n</c>.</returns>
    public static string Format(AnalyticsNumbers numbers)
    {
        var text = new StringBuilder();
        text.Append("Blog analytics, copied ")
            .Append(numbers.CopiedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" UTC\n\n");

        text.Append("Views\n");
        text.Append("- Today (UTC): ").Append(Number(numbers.ViewsToday)).Append('\n');
        text.Append("- Last 7 days: ").Append(Number(numbers.ViewsLast7Days)).Append('\n');
        text.Append("- Last 30 days: ").Append(Number(numbers.ViewsLast30Days)).Append("\n\n");

        AppendPosts(text, numbers);
        AppendReferrers(text, numbers);
        return text.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// Describes one post's confirmed rating in words.
    /// </summary>
    /// <param name="averageRating">Confirmed average, 0 when unrated.</param>
    /// <param name="ratingCount">Confirmed count.</param>
    /// <returns>For example <c>rating 4.5 from 3 ratings</c>, or <c>no ratings yet</c>.</returns>
    public static string DescribeRating(double averageRating, int ratingCount)
    {
        if (ratingCount <= 0 || averageRating <= 0)
            return "no ratings yet";

        var noun = ratingCount == 1 ? "rating" : "ratings";
        return $"rating {averageRating.ToString("0.0", CultureInfo.InvariantCulture)} from {Number(ratingCount)} {noun}";
    }

    private static void AppendPosts(StringBuilder text, AnalyticsNumbers numbers)
    {
        text.Append("Ten most read posts (").Append(numbers.RangeCaption).Append(")\n");
        var posts = numbers.TopPosts.Take(MaxPosts).ToList();
        if (posts.Count == 0)
            text.Append("- No posts were read in this range.\n");

        for (var index = 0; index < posts.Count; index++)
        {
            var post = posts[index];
            text.Append(index + 1).Append(". ").Append(post.Title).Append(" — ")
                .Append(Number(post.TotalViews)).Append(post.TotalViews == 1 ? " view, " : " views, ")
                .Append(DescribeRating(post.AverageRating, post.RatingCount)).Append('\n');
        }

        text.Append('\n');
    }

    private static void AppendReferrers(StringBuilder text, AnalyticsNumbers numbers)
    {
        text.Append("Where readers came from (").Append(numbers.RangeCaption).Append(")\n");
        var referrers = numbers.Referrers.Take(MaxReferrers).ToList();
        if (referrers.Count == 0)
            text.Append("- No views were recorded in this range.\n");

        foreach (var referrer in referrers)
        {
            text.Append("- ").Append(referrer.DisplayName).Append(": ")
                .Append(Number(referrer.TotalViews)).Append(referrer.TotalViews == 1 ? " view\n" : " views\n");
        }
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
