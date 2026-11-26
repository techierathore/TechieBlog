using BlogEngine.Common;
using BlogModels;
using BlogUI.Common;
using Xunit;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// REQ-UI-071 (referring site) and REQ-UI-070 (Copy numbers) — the two pure pieces behind them.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> pins that only a site name is ever kept from a <c>Referer</c> header, and
/// the exact plain text the Analytics page puts on the clipboard.</para>
/// <para><b>Code Flow:</b> table-driven calls to <see cref="ReferrerHostParser"/> and one full
/// formatting of <see cref="AnalyticsNumbersFormatter"/>.</para>
/// <para><b>Dependencies:</b> xUnit only.</para>
/// <para><b>Usage:</b> pure unit tests.</para>
/// </remarks>
public class ReferrerAndCopyNumbersTests
{
    /// <summary>
    /// REQ-UI-071: the referring site is reduced to a lower-cased host without <c>www.</c>; the
    /// path and query (which can carry search phrases) are dropped; the site itself, no header and
    /// non-web schemes count as direct or unknown.
    /// </summary>
    [Theory]
    [InlineData("https://www.Google.com/search?q=private+words", "blog.example", "google.com")]
    [InlineData("http://news.ycombinator.com/item?id=1", "blog.example", "news.ycombinator.com")]
    [InlineData("https://www.blog.example/post/other", "blog.example", null)]
    [InlineData("https://blog.example/", "www.blog.example", null)]
    [InlineData("", "blog.example", null)]
    [InlineData("not a url", "blog.example", null)]
    [InlineData("android-app://com.google.android.gm/", "blog.example", null)]
    [InlineData("/relative/path", "blog.example", null)]
    public void ReferrerKeepsSiteNameOnly(string header, string ownHost, string? expected)
    {
        Assert.Equal(expected, ReferrerHostParser.Parse(header, ownHost));
    }

    /// <summary>
    /// REQ-UI-070: the clipboard text carries views today / 7 / 30 days, the most read posts with
    /// views and confirmed rating, and the referring sites — one fact per line, plain text.
    /// </summary>
    [Fact]
    public void CopyNumbersTextHasEveryBlock()
    {
        var numbers = new AnalyticsNumbers(
            new DateTime(2026, 11, 22, 7, 5, 0, DateTimeKind.Utc),
            "23 Oct – 21 Nov 2026 (30 days)",
            12, 140, 1610,
            new List<PopularPost>
            {
                new() { Title = "Dapper tips", TotalViews = 120, AverageRating = 4.5, RatingCount = 2 },
                new() { Title = "MAUI notes", TotalViews = 1, AverageRating = 0, RatingCount = 0 }
            },
            new List<ReferrerEngagement>
            {
                new() { ReferrerHost = "google.com", TotalViews = 52 },
                new() { ReferrerHost = "", TotalViews = 1 }
            });

        var text = AnalyticsNumbersFormatter.Format(numbers);

        Assert.Equal(
            "Blog analytics, copied 2026-11-22 07:05 UTC\n\n" +
            "Views\n- Today (UTC): 12\n- Last 7 days: 140\n- Last 30 days: 1,610\n\n" +
            "Ten most read posts (23 Oct – 21 Nov 2026 (30 days))\n" +
            "1. Dapper tips — 120 views, rating 4.5 from 2 ratings\n" +
            "2. MAUI notes — 1 view, no ratings yet\n\n" +
            "Where readers came from (23 Oct – 21 Nov 2026 (30 days))\n" +
            "- google.com: 52 views\n- Direct or unknown: 1 view\n",
            text);
    }

    /// <summary>
    /// REQ-UI-070: never more than ten posts, as the owner asked for "the ten most read".
    /// </summary>
    [Fact]
    public void CopyNumbersListsAtMostTenPosts()
    {
        var posts = Enumerable.Range(1, 14)
            .Select(index => new PopularPost { Title = $"Post {index}", TotalViews = 100 - index })
            .ToList();
        var numbers = new AnalyticsNumbers(DateTime.UtcNow, "range", 0, 0, 0, posts, new List<ReferrerEngagement>());

        var text = AnalyticsNumbersFormatter.Format(numbers);

        Assert.Contains("10. Post 10", text);
        Assert.DoesNotContain("11. Post 11", text);
    }
}
