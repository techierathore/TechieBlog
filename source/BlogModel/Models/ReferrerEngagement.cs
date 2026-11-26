namespace BlogModels;

/// <summary>
/// One referring site together with the views it sent in a date range.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the "Where readers came from" card of the Analytics page and the
/// referring-sites block of "Copy numbers" (REQ-UI-071, REQ-UI-070; BRD-111, BRD-110).</para>
///
/// <para><b>Code Flow:</b> <c>IAnalyticsRepo.GetReferrersAsync</c> groups the range's
/// <c>PostViews</c> by <c>ReferrerHost</c>; views without one form a single row whose
/// <see cref="ReferrerHost"/> is empty.</para>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Read-only projection. Use <see cref="DisplayName"/> for anything a person
/// reads, so the empty group is always labelled the same way.</para>
/// </remarks>
public class ReferrerEngagement
{
    /// <summary>
    /// The label shown for views that carried no referring site.
    /// </summary>
    public const string DirectOrUnknownLabel = "Direct or unknown";

    /// <summary>
    /// The referring site's host name (for example <c>google.com</c>); empty for direct or unknown.
    /// </summary>
    public string ReferrerHost { get; set; } = string.Empty;

    /// <summary>
    /// Views this site sent inside the range.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct visitors among those views.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// The host name, or <see cref="DirectOrUnknownLabel"/> when there is none.
    /// </summary>
    public string DisplayName => string.IsNullOrWhiteSpace(ReferrerHost) ? DirectOrUnknownLabel : ReferrerHost;
}
