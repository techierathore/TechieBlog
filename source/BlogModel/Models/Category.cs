/// <summary>
/// Represents a blog category entity in the TechieBlog application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Organizes blog posts into logical groupings.</para>
/// <para><b>Usage:</b> Used by CategoryRepo for data access and CategorySvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class Category
{
    /// <summary>
    /// Unique identifier for the category.
    /// </summary>
    public long CategoryId { get; set; }

    /// <summary>
    /// Display name of the category.
    /// </summary>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly identifier auto-generated from name.
    /// Used for SEO-friendly URLs like /category/web-development.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the category.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Number of posts in this category (computed field).
    /// </summary>
    public int PostCount { get; set; }
}
