using System.Text.RegularExpressions;

namespace BlogEngine.Common;

/// <summary>
/// Utility class for generating URL-friendly slugs from text.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Converts titles and text to SEO-friendly URL slugs.</para>
/// <para><b>Usage:</b> Used by BlogSvc when creating or updating blog posts.</para>
/// </remarks>
/// <example>
/// <code>
/// var slug = SlugGenerator.GenerateSlug("My Blog Post Title!");
/// // Result: "my-blog-post-title"
/// </code>
/// </example>
public static class SlugGenerator
{
    /// <summary>
    /// Generates a URL-friendly slug from the given title.
    /// </summary>
    /// <remarks>
    /// <para><b>Transformation Rules:</b></para>
    /// <list type="number">
    ///   <item>Convert to lowercase</item>
    ///   <item>Remove special characters (keep only letters, numbers, spaces, hyphens)</item>
    ///   <item>Replace spaces with hyphens</item>
    ///   <item>Remove multiple consecutive hyphens</item>
    ///   <item>Trim hyphens from start and end</item>
    /// </list>
    /// </remarks>
    /// <param name="title">The title to convert to a slug.</param>
    /// <returns>URL-friendly slug string, or empty string if title is null/whitespace.</returns>
    public static string GenerateSlug(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        // Convert to lowercase
        var slug = title.ToLowerInvariant();

        // Remove special characters (keep only letters, numbers, spaces, hyphens)
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");

        // Replace spaces with hyphens
        slug = Regex.Replace(slug, @"\s+", "-");

        // Remove multiple consecutive hyphens
        slug = Regex.Replace(slug, @"-+", "-");

        // Trim hyphens from ends
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Generates a unique slug by appending a number if the base slug already exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Usage:</b> Call this when a duplicate slug is detected.</para>
    /// <para><b>Format:</b> Appends "-2", "-3", etc. to the base slug.</para>
    /// </remarks>
    /// <param name="baseSlug">The original slug that has duplicates.</param>
    /// <param name="existingCount">Number of existing posts with similar slugs.</param>
    /// <returns>Unique slug with number suffix.</returns>
    public static string GenerateUniqueSlug(string baseSlug, int existingCount)
    {
        if (existingCount <= 0)
            return baseSlug;

        return $"{baseSlug}-{existingCount + 1}";
    }
}
