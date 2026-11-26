using BlogModels;

namespace BlogUI.Common;

/// <summary>
/// The figures "Copy numbers" formats — REQ-UI-070 (BRD-110).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> one immutable bundle handed from the Analytics page to
/// <see cref="AnalyticsNumbersFormatter"/>, so the formatter stays pure and testable.</para>
/// <para><b>Dependencies:</b> <see cref="PopularPost"/>, <see cref="ReferrerEngagement"/>.</para>
/// <para><b>Usage:</b> built by <c>AnalyticsDashboard</c> from what it has already loaded.</para>
/// </remarks>
/// <param name="CopiedAtUtc">When the copy was made (UTC).</param>
/// <param name="RangeCaption">The page's range in words, for example "23 Oct – 21 Nov 2026 (30 days)".</param>
/// <param name="ViewsToday">Views recorded today (UTC).</param>
/// <param name="ViewsLast7Days">Views in the last 7 days, today included.</param>
/// <param name="ViewsLast30Days">Views in the last 30 days, today included.</param>
/// <param name="TopPosts">The most read posts in the page's range, most read first.</param>
/// <param name="Referrers">The referring sites in the page's range, busiest first.</param>
public sealed record AnalyticsNumbers(
    DateTime CopiedAtUtc,
    string RangeCaption,
    int ViewsToday,
    int ViewsLast7Days,
    int ViewsLast30Days,
    IReadOnlyList<PopularPost> TopPosts,
    IReadOnlyList<ReferrerEngagement> Referrers);
