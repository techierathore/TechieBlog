/// <summary>
/// Represents a tag entity for blog post classification.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides topic-based categorization for blog posts.</para>
/// <para><b>Usage:</b> Used by BlogTagRepo for data access and TagSvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class BlogTag
{
	/// <summary>
	/// Unique identifier for the tag.
	/// </summary>
	public long TagId { get; set; }

	/// <summary>
	/// Display name of the tag.
	/// </summary>
	public string TagName { get; set; } = string.Empty;

	/// <summary>
	/// URL-friendly identifier auto-generated from name.
	/// Used for SEO-friendly URLs like /tag/csharp.
	/// </summary>
	public string Slug { get; set; } = string.Empty;

	/// <summary>
	/// Number of posts with this tag (computed field).
	/// </summary>
	public int PostCount { get; set; }
}
