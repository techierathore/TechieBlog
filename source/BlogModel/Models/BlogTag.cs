namespace BlogModels;

/// <summary>
/// A free-form topic label attached to posts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides the cross-cutting classification axis. Tags are many-to-many with
/// posts and unlimited per post, which is what distinguishes them from <see cref="Category"/> —
/// a post has exactly one category but any number of tags.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogEngine.DbAccess.BlogTagRepo</c>; the association
/// itself lives in the <c>PostTag</c> join table, not on this type.</para>
///
/// <para><b>Dependencies:</b> <b>The table is called <c>Tag</c>, not <c>BlogTag</c></b> — the class
/// name and the table name deliberately differ, so every query names <c>Tag</c> explicitly and none
/// of them can rely on convention-based mapping. Created by
/// <c>PostgresScripts/001-CreateTables.sql</c>, given its <see cref="Slug"/> by
/// <c>005-FixCategoryAndTagTables.sql</c>, and joined to posts through the <c>PostTag</c> junction
/// table added by <c>007-FixBlogSeriesAndPostTag.sql</c>.</para>
///
/// <para><b>Usage:</b> <see cref="Slug"/> is what appears in the URL and is the value routes match
/// on; renaming a tag without regenerating the slug breaks every existing link to it.</para>
///
/// <para><b>The second copy.</b> A post also carries a denormalised comma-separated
/// <see cref="BlogPost.Tags"/> string. That copy is written independently of <c>PostTag</c> and is
/// what free-text search matches, so renaming a tag here leaves the old name embedded in every post
/// that used it. Keep both in mind before assuming a tag rename is complete.</para>
/// </remarks>
public class BlogTag
{
    /// <summary>
    /// Surrogate primary key (<c>Tag.TagId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Zero until the row is inserted. Referenced by <c>PostTag.TagId</c>, whose foreign key is
    /// <c>ON DELETE CASCADE</c> — deleting a tag therefore silently removes it from every post that
    /// carried it, with no confirmation from the database.
    /// </remarks>
    public long TagId { get; set; }

    /// <summary>
    /// The label a reader sees (<c>TagName VARCHAR(150) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Author-supplied, therefore untrusted — encode it before rendering. Nothing enforces
    /// uniqueness: the unique index is on <see cref="Slug"/>, not on the name, so two tags may share
    /// a display name as long as their slugs differ. It seeds <see cref="Slug"/> at creation and is
    /// not kept in step with it afterwards.
    /// </remarks>
    public string TagName { get; set; } = string.Empty;

    /// <summary>
    /// The tag's URL segment, e.g. <c>/tag/csharp</c> (<c>Slug VARCHAR(200)</c>, added by migration
    /// 005 with the unique index <c>IdxTagSlug</c>).
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="TagName"/> by lower-casing, stripping non-alphanumerics and collapsing
    /// whitespace to hyphens; migration 005 back-filled it with exactly that rule. Because it strips
    /// rather than transliterates, two visually distinct names can collapse onto the same slug and
    /// the second insert then fails on the unique index.
    /// <para>Route-bearing and effectively immutable once published: this is the only identifier a
    /// tag page is reachable by.</para>
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// How many live posts carry this tag — computed by an aggregate, not a column.
    /// </summary>
    /// <remarks>
    /// Only the "with counts" query populates it; every other read leaves it zero, which is
    /// indistinguishable from a genuinely unused tag. When it is populated it counts <b>published,
    /// not-soft-deleted</b> posts only, so a tag applied solely to drafts reports zero — that is the
    /// figure a reader-facing tag cloud wants, but it is not the number of rows in <c>PostTag</c> and
    /// must not be used to decide whether deleting the tag is safe.
    /// </remarks>
    public int PostCount { get; set; }
}
