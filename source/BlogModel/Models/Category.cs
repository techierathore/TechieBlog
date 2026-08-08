namespace BlogModels;

/// <summary>
/// The single primary section a post belongs to.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives the site its top-level navigation. Exactly one category per post
/// (<see cref="BlogPost.CategoryId"/>) — for anything a post can have several of, use
/// <see cref="BlogTag"/>.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogEngine.DbAccess.CategoryRepo</c>; rendered in the
/// navigation shell and on the category landing page.</para>
///
/// <para><b>Dependencies:</b> The <c>Category</c> table
/// (<c>PostgresScripts/001-CreateTables.sql</c>, adjusted by <c>005-FixCategoryAndTagTables.sql</c>).
/// The <c>PostCategory</c> junction table from script 001 also still exists, from the era when a post
/// could hold several categories; nothing writes it any more — the scalar
/// <see cref="BlogPost.CategoryId"/> replaced it.</para>
///
/// <para><b>Usage:</b> Note the type-width mismatch across the boundary: <see cref="CategoryId"/> is
/// a <see cref="long"/> here, while the foreign key that references it,
/// <see cref="BlogPost.CategoryId"/>, is an <see cref="int"/>. <see cref="Slug"/> is route-bearing —
/// see the warning on <see cref="BlogTag.Slug"/>.</para>
///
/// <para><b>No referential integrity.</b> <c>BlogPost.CategoryId</c> carries no foreign key to this
/// table, so deleting a category does not fail and does not cascade — it leaves posts pointing at an
/// id that resolves to nothing, which renders as a missing category name rather than an error.
/// Re-assign a category's posts before deleting it.</para>
/// </remarks>
public class Category
{
    /// <summary>
    /// Surrogate primary key (<c>CategoryId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Zero until the row is inserted. Referenced — without a foreign key, see the type remarks — by
    /// <see cref="BlogPost.CategoryId"/>, which narrows it to an <see cref="int"/>, and reported as a
    /// <see cref="long"/> again by <see cref="CategoryEngagement.CategoryId"/>.
    /// </remarks>
    public long CategoryId { get; set; }

    /// <summary>
    /// The section label a reader sees (<c>CategoryName VARCHAR(150) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Author-supplied, therefore untrusted — encode it before rendering, including in the navigation
    /// shell where it appears on every page. Uniqueness is enforced on <see cref="Slug"/> only, not
    /// on the name. It seeds <see cref="Slug"/> at creation and is not kept in step with it
    /// afterwards.
    /// </remarks>
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    /// The category's URL segment, e.g. <c>/category/web-development</c>
    /// (<c>Slug VARCHAR(200)</c>, added by migration 005 with the unique index
    /// <c>IdxCategorySlug</c>).
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="CategoryName"/> by lower-casing, stripping non-alphanumerics and
    /// collapsing whitespace to hyphens; migration 005 back-filled existing rows with the same rule.
    /// Because the column was added by <c>ALTER TABLE</c> it permits <c>NULL</c>, so a category
    /// created outside the application can exist with no slug and therefore no reachable page.
    /// <para>Route-bearing: changing it breaks every existing link to the category, including the
    /// site navigation cached in a visitor's browser.</para>
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Optional blurb shown on the category landing page (<c>Description TEXT</c>, added by
    /// migration 005).
    /// </summary>
    /// <remarks>
    /// Unbounded author-supplied text, published on a public page and emitted as plain text rather
    /// than Markdown, so it must be HTML-encoded on the way out. Nullable in the database but
    /// non-nullable here, so "no description" arrives as an empty string.
    /// </remarks>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// How many live posts are in this category — computed by an aggregate, not a column.
    /// </summary>
    /// <remarks>
    /// Only the "with counts" query populates it; every other read leaves it zero, which is
    /// indistinguishable from a genuinely empty category. When populated it counts <b>published,
    /// not-soft-deleted</b> posts only, so a category holding nothing but drafts reports zero and
    /// must not be treated as safe to delete on that basis.
    /// </remarks>
    public int PostCount { get; set; }
}
