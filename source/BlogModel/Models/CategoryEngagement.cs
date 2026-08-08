namespace BlogModels;

/// <summary>
/// A category together with the share of readership its posts attracted in a date range.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the "engagement by category" panel of BRD-61, which answers what
/// subjects readers actually spend their time on rather than which single post won.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IAnalyticsRepo.GetCategoryEngagementAsync</c> joins <c>PostViews</c> to
///         <c>BlogPost.CategoryId</c> inside the range and groups by category.</item>
///   <item>The caller ranks by <see cref="TotalViews"/> and renders each row with its own label and
///         value, so the identity of a bar is never carried by colour alone.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Consumed by the admin analytics dashboard (REQ-UI-044). Posts with no
/// category are reported under a single "Uncategorised" row rather than being dropped.</para>
/// </remarks>
public class CategoryEngagement
{
    /// <summary>
    /// The category identifier, or zero for uncategorised posts.
    /// </summary>
    public long CategoryId { get; set; }

    /// <summary>
    /// Display name of the category.
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Recorded views for the category's posts inside the range.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct visitors for the category's posts inside the range.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// Published posts in the category that were viewed at least once inside the range.
    /// </summary>
    public int PostCount { get; set; }
}
