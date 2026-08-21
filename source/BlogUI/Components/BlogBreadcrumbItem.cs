namespace BlogUI.Components;

/// <summary>
/// A single entry in a <see cref="BlogBreadcrumb"/> trail.
/// </summary>
/// <remarks>
/// Declared as a top-level type rather than nested inside the component so that
/// pages can construct it without qualifying through the component name, and so
/// it can never be confused with TrBlazeUI's own <c>BreadcrumbItem</c> component.
/// </remarks>
public class BlogBreadcrumbItem
{
    /// <summary>
    /// Text shown for this step of the trail.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Destination for this step. Ignored for the final (current-page) entry,
    /// which renders as plain text.
    /// </summary>
    public string Url { get; set; } = "/";
}
