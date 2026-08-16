namespace BlogModels;

/// <summary>
/// A named, ordered collection of posts that read as a multi-part series.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Groups related posts so a reader can move through them in sequence.
/// Membership and ordering live on the post side, in <see cref="BlogPost.SeriesId"/> and
/// <see cref="BlogPost.SeriesPartNumber"/> — this type holds only the series' own metadata.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogEngine.DbAccess.BlogSeriesRepo</c>; the series
/// page fills <see cref="Posts"/> in a separate call, and <see cref="SeriesNavigation"/> supplies
/// the previous/next links on an individual post.</para>
///
/// <para><b>Dependencies:</b> The <c>BlogSeries</c> table, created by
/// <c>PostgresScripts/004-FixPostTable.sql</c> and completed by
/// <c>007-FixBlogSeriesAndPostTag.sql</c>, which added <see cref="AuthorId"/> and
/// <see cref="Status"/>. Script 004's original <c>IsActive BOOLEAN DEFAULT TRUE</c> column is still
/// in the schema but has no property here and no reader anywhere — <see cref="Status"/> replaced
/// it.</para>
///
/// <para><b>Usage:</b> <see cref="Posts"/> is empty unless the caller explicitly loaded it — an
/// empty list means "not fetched" as often as it means "no posts", so do not use it to test whether
/// a series is populated; use <see cref="PostCount"/>. <see cref="Status"/> is free text, but every
/// producer and consumer goes through <see cref="SeriesStatus"/> — never compare it to a literal
/// (REQ-UI-024).</para>
/// </remarks>
public class BlogSeries
{
    /// <summary>
    /// Surrogate primary key (<c>SeriesId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Zero until the row is inserted. Referenced by <see cref="BlogPost.SeriesId"/>, which carries
    /// no foreign key of its own in the schema, so deleting a series can leave posts pointing at an
    /// id that no longer exists.
    /// </remarks>
    public long SeriesId { get; set; }

    /// <summary>
    /// The series' display title (<c>Name VARCHAR(255) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Author-supplied, therefore untrusted — encode it before rendering. It seeds <see cref="Slug"/>
    /// at creation and is not kept in step with it afterwards; renaming a series deliberately leaves
    /// its URL alone.
    /// </remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The series' permanent URL segment, e.g. <c>/series/getting-started-with-dotnet</c>
    /// (<c>Slug VARCHAR(300) NOT NULL UNIQUE</c>).
    /// </summary>
    /// <remarks>
    /// Unique at the database level, so a slug collision fails the insert rather than producing two
    /// series at one address; the repository also checks with a count query first so the user sees a
    /// validation message instead of an exception. Route-bearing: changing it breaks every existing
    /// link to the series, including the cross-links rendered on each member post.
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Introductory blurb shown at the head of the series page (<c>Description TEXT</c>).
    /// </summary>
    /// <remarks>
    /// Unbounded author-supplied text, published on a public page. It is emitted as plain text rather
    /// than run through <c>MarkdownRenderer</c>, so it must be HTML-encoded on the way out. Nullable
    /// in the database but non-nullable here, so "no description" arrives as an empty string.
    /// </remarks>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Editorial state of the series (<c>Status VARCHAR(50) DEFAULT 'In Progress'</c>, migration
    /// 007).
    /// </summary>
    /// <remarks>
    /// One of the two canonical literals held by <see cref="SeriesStatus"/> —
    /// <see cref="SeriesStatus.InProgress"/> or <see cref="SeriesStatus.Completed"/>. There is no
    /// enum and no lookup table, so the constants plus the check constraint added by
    /// <c>029-NormalizeSeriesStatus.sql</c> are what keep the value honest; the write path runs every
    /// value through <see cref="SeriesStatus.Normalize"/>. The C# default here matches the column
    /// default, so a new series starts in progress either way.
    /// <para>Before REQ-UI-024 the code compared against the never-stored spelling <c>"Complete"</c>
    /// while the database held <c>"Completed"</c>, which made a finished series render as "In
    /// Progress" and count zero under its own filter tab. Do not reintroduce a bare literal.</para>
    /// <para>Purely informational — it does not hide the series or its posts from readers.</para>
    /// </remarks>
    public string Status { get; set; } = SeriesStatus.InProgress;

    /// <summary>
    /// The user who owns the series (<c>AuthorId BIGINT</c>, migration 007, foreign key to
    /// <c>BlogUser</c>).
    /// </summary>
    /// <remarks>
    /// Used to list "my series" on the admin surface. Individual posts keep their own
    /// <see cref="BlogPost.UserID"/>, so a series author is not necessarily the author of every post
    /// in it.
    /// <para><b>Nullability mismatch.</b> The column was added by <c>ALTER TABLE</c> without
    /// <c>NOT NULL</c> and its foreign key is declared <c>ON DELETE SET NULL</c>, so the database can
    /// legitimately store <c>NULL</c> here — but this property is a non-nullable
    /// <see cref="long"/> and the repository selects the column raw, with no <c>COALESCE</c>. A
    /// series whose author row is deleted therefore stops materialising at all rather than showing an
    /// unknown author. Deleting a user who owns a series is the trigger.</para>
    /// </remarks>
    public long AuthorId { get; set; }

    /// <summary>
    /// When the series was created (<c>CreatedOn TIMESTAMP DEFAULT CURRENT_TIMESTAMP</c>).
    /// </summary>
    /// <remarks>
    /// A bare <c>TIMESTAMP</c> with no time zone, so it materialises with
    /// <see cref="DateTimeKind.Unspecified"/>; see the timestamp note on <see cref="BlogPost"/>.
    /// </remarks>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Timestamp of the last edit to the series' own metadata (<c>UpdatedOn TIMESTAMP</c>, nullable
    /// in the database).
    /// </summary>
    /// <remarks>
    /// Adding or removing a post does not touch it — membership lives on the post side — so this is
    /// not a "series last changed" signal. A never-edited row materialises as
    /// <see cref="DateTime.MinValue"/> because the column allows <c>NULL</c> while the property does
    /// not.
    /// </remarks>
    public DateTime UpdatedOn { get; set; }

    /// <summary>
    /// The owning author's display name, projected by a <c>LEFT JOIN</c> on <c>BlogUser</c> — not a
    /// column on <c>BlogSeries</c>.
    /// </summary>
    /// <remarks>
    /// The join is outer, so an unmatched author yields an empty name rather than dropping the
    /// series. Display only; ownership decisions belong to <see cref="AuthorId"/>.
    /// </remarks>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// How many posts belong to the series, computed by an aggregate in the listing query — not a
    /// column.
    /// </summary>
    /// <remarks>
    /// Only the counting queries populate it; elsewhere it is zero. Whether it counts unpublished or
    /// soft-deleted posts depends on the query that produced it, so an admin figure and a public
    /// figure need not agree. This — not <see cref="Posts"/> — is the right way to ask whether a
    /// series has any content.
    /// </remarks>
    public int PostCount { get; set; }

    /// <summary>
    /// The series' posts, in reading order. Populated only by the series-detail call; empty
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// A separate query fills this, so an empty list is ambiguous between "no posts" and "not
    /// fetched" — use <see cref="PostCount"/> to tell them apart. It is a mutable <c>List</c> with a
    /// public setter on a shared entity: mutating it changes what any other holder of the same
    /// instance sees. Ordering comes from the query (by
    /// <see cref="BlogPost.SeriesPartNumber"/>), not from this property.
    /// </remarks>
    public List<BlogPost> Posts { get; set; } = new();

    /// <summary>
    /// True when <see cref="Status"/> means the series is finished. Computed; not persisted.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="SeriesStatus.IsCompleted"/>, which trims, ignores case and still
    /// accepts the legacy <c>"Complete"</c> spelling, so no row can be mis-rendered by a stray
    /// variation. It is a reader-facing badge only and grants or denies nothing.
    /// </remarks>
    public bool IsComplete => SeriesStatus.IsCompleted(Status);
}

/// <summary>
/// The previous/next links and position indicator shown on a post that belongs to a series.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a post page render "Part 2 of 5" with working navigation without
/// loading the whole <see cref="BlogSeries"/> and all of its posts.</para>
///
/// <para><b>Code Flow:</b> Built by <c>SeriesSvc.GetSeriesNavigation(postId)</c>, which returns null
/// when the post is not part of a series; consumed by the post view.</para>
///
/// <para><b>Dependencies:</b> <see cref="BlogPost.SeriesId"/> and
/// <see cref="BlogPost.SeriesPartNumber"/> for membership and order; <see cref="BlogSeries"/> for the
/// name and slug it copies.</para>
///
/// <para><b>Usage:</b> A projection, never persisted — nothing here maps to a column. It is built at
/// request time, so it reflects the series as it was for that one render. Because part numbers are
/// neither unique nor guaranteed contiguous, <see cref="CurrentPart"/> and <see cref="TotalParts"/>
/// can disagree with the number of links actually available; treat them as a label, not as a
/// count you can navigate by.</para>
/// </remarks>
public class SeriesNavigation
{
    /// <summary>
    /// Display name of the series this post belongs to, copied from <see cref="BlogSeries.Name"/>.
    /// Author-supplied; encode before rendering.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// URL segment of the series, copied from <see cref="BlogSeries.Slug"/> — what the "view the
    /// whole series" link is built from.
    /// </summary>
    public string SeriesSlug { get; set; } = string.Empty;

    /// <summary>
    /// The current post's 1-based position, taken from <see cref="BlogPost.SeriesPartNumber"/>.
    /// </summary>
    /// <remarks>
    /// Zero when the post has a series but no part number, which renders as "Part 0" if shown
    /// unguarded. Not an index into anything — nothing indexes by it.
    /// </remarks>
    public int CurrentPart { get; set; }

    /// <summary>
    /// How many parts the reader can see, used as the denominator of the "Part N of M" label.
    /// </summary>
    /// <remarks>
    /// Counts published parts only, so a series with drafts in the middle shows a smaller total than
    /// the highest <see cref="CurrentPart"/> — "Part 5 of 3" is a possible and expected rendering
    /// while later parts are still drafts.
    /// </remarks>
    public int TotalParts { get; set; }

    /// <summary>
    /// The preceding post in the series, or null when this is the first available part.
    /// </summary>
    /// <remarks>
    /// Null also means "the previous part exists but is not visible" — an unpublished or
    /// soft-deleted part is skipped rather than linked, so navigation stays inside what a reader may
    /// see. Only the fields the link needs are populated on the referenced post; do not treat it as a
    /// fully loaded <see cref="BlogPost"/>.
    /// </remarks>
    public BlogPost? PreviousPost { get; set; }

    /// <summary>
    /// The following post in the series, or null when this is the last available part. Same
    /// visibility and partial-population caveats as <see cref="PreviousPost"/>.
    /// </summary>
    public BlogPost? NextPost { get; set; }
}
