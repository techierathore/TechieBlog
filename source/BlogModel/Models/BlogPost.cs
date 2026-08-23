namespace BlogModels;

/// <summary>
/// An article: the central content entity of the blog.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries one row of the <c>BlogPost</c> table plus the joined and computed
/// fields the UI needs, so a post list can render authors, tags, series position and comment counts
/// without a second round trip.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogEngine.DbAccess.BlogPostRepo</c> from the
/// PostgreSQL stored functions, passed through <c>BlogSvc</c> for the publish/schedule rules, and
/// bound directly by the public post pages and the admin grid. <see cref="PostContent"/> is stored
/// as Markdown and rendered to HTML at display time by <c>MarkdownRenderer</c> — never at write
/// time.</para>
///
/// <para><b>Dependencies:</b> Column parity with <c>BlogPost</c> in
/// <c>PostgresScripts/001-CreateTables.sql</c> and <c>004-FixPostTable.sql</c>; foreign keys to
/// <c>BlogUser</c>, <c>Category</c> and optionally <c>BlogSeries</c>.</para>
///
/// <para><b>Usage:</b> Not every property comes from the post's own row. <see cref="BlogWriter"/>,
/// <see cref="Tags"/>, <see cref="CommentCount"/>, <see cref="SeriesName"/> and
/// <see cref="SeriesSlug"/> are join projections that only specific queries populate, and
/// <see cref="IsSelected"/> is transient UI state — check the query you used before trusting any of
/// them. Deletion is soft: a row with <see cref="IsDeleted"/> set is still returned by any query
/// that does not filter it out.</para>
///
/// <para><b>Table history matters here.</b> The table was created as <c>Post</c> in script 001 and
/// renamed to <c>BlogPost</c> by 004, which also bolted on the slug, soft-delete, scheduling,
/// category and series columns. Two consequences survive in the schema and neither is visible from
/// this class: the original <c>ScheduledFor</c> column is still there and is <b>not</b> what
/// <see cref="ScheduledPublishOn"/> maps to, and <c>SeoTitle</c>/<c>SeoDescription</c> exist as
/// columns with no property on this type, so nothing in the application ever reads or writes
/// them.</para>
///
/// <para><b>Timestamps.</b> Every <c>DateTime</c> here maps to a bare PostgreSQL <c>TIMESTAMP</c>
/// with no time zone. The repository normalises on the way in through
/// <c>BlogEngine.DaCore.DbTimestamp.AsTimestamp</c>, so stored instants are UTC, but they come back
/// with <see cref="DateTimeKind.Unspecified"/>. Compare them against <see cref="DateTime.UtcNow"/>
/// (as <see cref="IsScheduled"/> does) and convert for display explicitly; never call
/// <c>ToLocalTime</c> on a value read from the database, which would treat an already-UTC instant as
/// local.</para>
/// </remarks>
public class BlogPost
{
    /// <summary>
    /// Column width of <see cref="Title"/> — <c>Title VARCHAR(550)</c>.
    /// </summary>
    /// <remarks>
    /// UAT-023 mechanism A: nothing checked this before an over-length value reached Npgsql and
    /// failed with a raw <c>22001</c>, which the caller then reported as a generic failure with no
    /// indication of which field or limit was at fault. Both <c>BlogSvc</c> (service-side, shared by
    /// the website and BlogApp) and <c>ManagePost.razor</c> (the editor's own <c>MaxLength</c> and
    /// character-count indicator) read this single constant so the two layers cannot drift apart.
    /// </remarks>
    public const int TitleMaxLength = 550;

    /// <summary>
    /// Column width of <see cref="Abstract"/> — <c>Abstract VARCHAR(550)</c>. See
    /// <see cref="TitleMaxLength"/> for why this lives here rather than in each caller.
    /// </summary>
    public const int AbstractMaxLength = 550;

    /// <summary>
    /// Column width of <see cref="Tags"/> — <c>Tags VARCHAR(550)</c>. See
    /// <see cref="TitleMaxLength"/> for why this lives here rather than in each caller.
    /// </summary>
    public const int TagsMaxLength = 550;

    /// <summary>
    /// Column width of <see cref="FeaturedImage"/> — <c>FeaturedImage VARCHAR(550)</c>. See
    /// <see cref="TitleMaxLength"/> for why this lives here rather than in each caller.
    /// </summary>
    public const int FeaturedImageMaxLength = 550;

    /// <summary>
    /// Surrogate primary key (<c>PostId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Zero until the row is inserted — <c>BlogPostRepo</c> returns the generated value. Referenced
    /// by <c>BlogComment.PostId</c>, <c>PostTag.PostId</c>, <c>PostViews</c>, <c>PostRating</c> and
    /// <c>PostCategory</c>. It never appears in a public URL; <see cref="Slug"/> does.
    /// </remarks>
    public long PostID { get; set; }

    /// <summary>
    /// The article headline (<c>Title VARCHAR(550) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Author-supplied and therefore untrusted — encode it wherever it is rendered, including inside
    /// the page <c>&lt;title&gt;</c> and meta tags. It seeds <see cref="Slug"/> at creation time but
    /// is not kept in step with it afterwards: editing a title does not re-slug the post, which is
    /// deliberate (see <see cref="Slug"/>).
    /// </remarks>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The post's permanent URL segment, e.g. <c>/blog/my-post-title</c>
    /// (<c>Slug VARCHAR(300)</c>, added by migration 004 with a unique index).
    /// </summary>
    /// <remarks>
    /// Generated from <see cref="Title"/> by lower-casing, stripping non-alphanumerics and collapsing
    /// whitespace to hyphens; migration 004 back-filled it for pre-existing rows with the same rule.
    /// <para>Unique across the whole table (<c>IdxBlogPostSlug</c>), so a title collision surfaces as
    /// a unique-violation on insert, not as a second post at the same address. This is the only
    /// public identifier of a post: changing it breaks every inbound link, bookmark and search-engine
    /// result, so treat it as immutable once the post has been published.</para>
    /// </remarks>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Unused. There is no <c>UIPageTitle</c> column on <c>BlogPost</c> and nothing in
    /// <c>source/</c> or <c>tests/</c> reads or writes this property.
    /// </summary>
    /// <remarks>
    /// It survives from an earlier design in which a post could carry a browser-tab title distinct
    /// from <see cref="Title"/>; the equivalent property on <c>UserEvent</c> is the one still in use.
    /// Because Dapper silently ignores a property with no matching column, it is always the empty
    /// string — do not start reading it expecting persisted data. A deletion candidate; kept for now
    /// so no binding breaks.
    /// </remarks>
    public string UIPageTitle { get; set; } = string.Empty;

    /// <summary>
    /// Short summary shown in listings, cards and the SEO meta description
    /// (<c>Abstract VARCHAR(550)</c>).
    /// </summary>
    /// <remarks>
    /// Author-supplied plain text, not Markdown — it is emitted as-is rather than through
    /// <c>MarkdownRenderer</c>, so any markup in it renders literally and must still be encoded.
    /// Nullable in the database but non-nullable here, so a post without one arrives as an empty
    /// string; a listing should fall back to a truncation of <see cref="PostContent"/> rather than
    /// render a blank card.
    /// </remarks>
    public string Abstract { get; set; } = string.Empty;

    /// <summary>
    /// The full article body, stored as <b>Markdown source</b> (<c>PostContent TEXT NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Never HTML. It is converted at display time by <c>MarkdownRenderer</c>, which is also where
    /// sanitisation happens — so this value must never be emitted with a raw-HTML directive before it
    /// has been through that renderer. Storing the source rather than the rendered output is what
    /// lets a renderer or sanitiser upgrade take effect on existing posts without a data migration.
    /// <para>Unbounded <c>TEXT</c>, so a post list that selects this column pulls every article body
    /// over the wire; the listing queries deliberately project a narrower column set.</para>
    /// </remarks>
    public string PostContent { get; set; } = string.Empty;

    /// <summary>
    /// When the post record was first created — not when it went live
    /// (<c>CreatedOn</c>, defaulted to <c>CURRENT_TIMESTAMP</c>).
    /// </summary>
    /// <remarks>
    /// A draft written months before release has an early <c>CreatedOn</c> and a late
    /// <see cref="PublishedOn"/>. Public "posted on" labels must use <see cref="PublishedOn"/>;
    /// using this instead back-dates the article. See the timestamp note on the type.
    /// </remarks>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Timestamp of the last edit (<c>UpdatedOn TIMESTAMP</c>, nullable in the database).
    /// </summary>
    /// <remarks>
    /// The column allows <c>NULL</c> for a never-edited post while this property is non-nullable, so
    /// such a row materialises as <see cref="DateTime.MinValue"/> — treat a year of 0001 as "never
    /// edited" instead of rendering it as an update date.
    /// </remarks>
    public DateTime UpdatedOn { get; set; }

    /// <summary>
    /// The author (<c>UserId BIGINT NOT NULL REFERENCES BlogUser(UserId)</c>).
    /// </summary>
    /// <remarks>
    /// Required by the foreign key, so a post cannot be orphaned and an author cannot be deleted
    /// while any post references them. This is the id that authorisation checks compare against the
    /// signed-in user for "edit your own post" rules; <see cref="BlogWriter"/> is only a display
    /// projection of it and must never be used for an ownership decision.
    /// </remarks>
    public long UserID { get; set; }

    /// <summary>
    /// A denormalised, comma-separated copy of the post's tag names
    /// (<c>Tags VARCHAR(550)</c>) — kept for display and for search only.
    /// </summary>
    /// <remarks>
    /// <b>This is not the authoritative tag association.</b> That lives in the <c>PostTag</c> junction
    /// table created by migration 007, which is what tag pages and tag counts query. Both
    /// representations are written independently, so they can drift: a tag renamed in <c>BlogTag</c>
    /// leaves the old name embedded in every post's copy of this string.
    /// <para>Search matches this column with <c>ILIKE '%term%'</c>, so a search for a short tag also
    /// matches any tag that contains it as a substring. Being a single <c>VARCHAR(550)</c>, it also
    /// caps how many tags a post can carry before the insert fails on length.</para>
    /// </remarks>
    public string Tags { get; set; } = string.Empty;

    /// <summary>
    /// The post's single primary section (<c>CategoryId</c>, added by migration 004).
    /// </summary>
    /// <remarks>
    /// <b>Width mismatch:</b> the column is <c>BIGINT</c> and <see cref="Category.CategoryId"/> is a
    /// <see cref="long"/>, but this property is an <see cref="int"/>. It works because no realistic
    /// installation exceeds <see cref="int.MaxValue"/> categories, not because the types agree.
    /// <para>Zero means uncategorised — the column has no <c>NOT NULL</c> constraint and no foreign
    /// key, so an id pointing at a deleted category is possible and shows up as a missing name rather
    /// than an error. Analytics reports such posts under a single "Uncategorised" row (see
    /// <see cref="CategoryEngagement"/>).</para>
    /// <para>Note the legacy <c>PostCategory</c> junction table also still exists from script 001;
    /// this scalar column is what the application actually uses.</para>
    /// </remarks>
    public int CategoryId { get; set; }

    /// <summary>
    /// The author's display name, projected by a join on <c>BlogUser</c> — not a column on
    /// <c>BlogPost</c>.
    /// </summary>
    /// <remarks>
    /// Only the queries that join <c>BlogUser</c> populate it; everywhere else it is the empty
    /// string, which is why the repository documents which selects project it. A rendered byline
    /// should fall back to a lookup on <see cref="UserID"/> rather than printing a blank author.
    /// Never use it for an ownership check — see <see cref="UserID"/>.
    /// </remarks>
    public string BlogWriter { get; set; } = string.Empty;

    /// <summary>
    /// Storage path of the hero image shown on the post and in listings
    /// (<c>FeaturedImage VARCHAR(550)</c>).
    /// </summary>
    /// <remarks>
    /// A locator produced by <c>IFileStorage</c> — the same kind of value as
    /// <c>BlogImage.ImagePath</c> — so its interpretation is provider-dependent and it is not
    /// necessarily resolvable against the web root. Empty when no image was chosen; render a
    /// placeholder rather than emitting <c>src=""</c>. Nothing enforces that the referenced file
    /// still exists, so a broken hero image is the expected result of deleting a media-library item
    /// that a post points at.
    /// </remarks>
    public string FeaturedImage { get; set; } = string.Empty;

    /// <summary>
    /// Whether the post is live (<c>Published BOOLEAN NOT NULL DEFAULT FALSE</c>).
    /// </summary>
    /// <remarks>
    /// The single gate every public query filters on, together with <see cref="IsDeleted"/>. False
    /// covers both a draft and a not-yet-due scheduled post — the two are distinguished only by
    /// <see cref="ScheduledPublishOn"/>. New posts default to unpublished, so forgetting to set it
    /// hides a post rather than leaking one.
    /// </remarks>
    public bool Published { get; set; }

    /// <summary>
    /// When the post first went live; null while it has never been published
    /// (<c>PublishedOn TIMESTAMP</c>, added by migration 004).
    /// </summary>
    /// <remarks>
    /// This — not <see cref="CreatedOn"/> — is the date a reader should see, and the ordering key for
    /// the public archive. It records the <i>first</i> publication, so unpublishing and republishing
    /// does not move the article to the top of the feed unless the caller explicitly restamps it.
    /// </remarks>
    public DateTime? PublishedOn { get; set; }

    /// <summary>
    /// The instant at which a scheduled post becomes due; null when it is not scheduled
    /// (<c>ScheduledPublishOn TIMESTAMP</c>, added by migration 004).
    /// </summary>
    /// <remarks>
    /// Stored as UTC (see the timestamp note on the type) and polled by the repository's
    /// "due scheduled" query, which selects unpublished, undeleted rows whose value is at or before
    /// the current instant. Setting it does <b>not</b> publish the post by itself — something must
    /// run that query and flip <see cref="Published"/>, so a schedule set while no scheduler is
    /// running simply never fires.
    /// <para><b>Not the <c>ScheduledFor</c> column.</b> Script 001 created <c>ScheduledFor</c> on the
    /// original <c>Post</c> table and migration 004 added this separate column beside it. Nothing
    /// reads <c>ScheduledFor</c> any more, but it is still in the schema — a value written there by
    /// hand or by an old tool has no effect whatsoever.</para>
    /// </remarks>
    public DateTime? ScheduledPublishOn { get; set; }

    /// <summary>
    /// True when the post is waiting for a future scheduled publication. Computed; not persisted.
    /// </summary>
    /// <remarks>
    /// Requires all three of: not yet <see cref="Published"/>, a <see cref="ScheduledPublishOn"/>
    /// value, and that instant still in the future. It is evaluated against
    /// <see cref="DateTime.UtcNow"/> at the moment of the call, so a post flips from scheduled to
    /// merely unpublished the second its time passes — and stays that way until whatever publishes
    /// due posts actually runs. A post that is due but not yet processed therefore reports
    /// <c>Draft</c> from <see cref="Status"/>, which is the honest answer: it is not live.
    /// </remarks>
    public bool IsScheduled => !Published && ScheduledPublishOn.HasValue && ScheduledPublishOn > DateTime.UtcNow;

    /// <summary>
    /// The post's state as a display string: <c>"Published"</c>, <c>"Scheduled"</c> or
    /// <c>"Draft"</c>. Computed; not persisted.
    /// </summary>
    /// <remarks>
    /// A presentation label derived from <see cref="Published"/> and <see cref="IsScheduled"/>, in
    /// that order — a published post reports <c>Published</c> even if a stale
    /// <see cref="ScheduledPublishOn"/> is still set. It says nothing about
    /// <see cref="IsDeleted"/>: a soft-deleted post still reports its former state, so an admin grid
    /// must filter deleted rows itself rather than expecting a fourth label.
    /// <para>These are literal strings compared by value where they are consumed, so do not localise
    /// or reword them without checking the call sites.</para>
    /// </remarks>
    public string Status => Published ? "Published" : IsScheduled ? "Scheduled" : "Draft";

    /// <summary>
    /// Soft-delete marker (<c>IsDeleted BOOLEAN DEFAULT FALSE</c>, added by migration 004).
    /// </summary>
    /// <remarks>
    /// The row and all its comments, ratings and views survive, so a delete is recoverable and
    /// historical analytics stay intact. <b>Every</b> query that reaches readers must exclude these
    /// rows explicitly — nothing in the schema does it — and the predicate must tolerate
    /// <c>NULL</c> as well as <c>FALSE</c>, because the column was added with a default that did not
    /// back-fill rows created before migration 004. That is why the repository's live-post predicate
    /// reads <c>IsDeleted = FALSE OR IsDeleted IS NULL</c> rather than a bare equality.
    /// </remarks>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// When the post was soft-deleted; null while it is live (<c>DeletedOn TIMESTAMP</c>, added by
    /// migration 004).
    /// </summary>
    /// <remarks>
    /// Audit information only — no query keys off it, and nothing purges rows on the strength of it,
    /// so a soft-deleted post is retained indefinitely. It is not authoritative for visibility:
    /// <see cref="IsDeleted"/> is.
    /// </remarks>
    public DateTime? DeletedOn { get; set; }

    /// <summary>
    /// Number of comments on this post, projected by an aggregate in the listing queries — not a
    /// column on <c>BlogPost</c>.
    /// </summary>
    /// <remarks>
    /// Zero on any instance loaded by a query that does not compute it, which is indistinguishable
    /// from a genuinely uncommented post. Whether it counts only approved comments depends on the
    /// query that produced it, so do not render it next to a comment list and assume the two agree.
    /// </remarks>
    public long CommentCount { get; set; }

    /// <summary>
    /// Site-wide post total. <b>Not a property of this post</b> — a scalar smuggled back inside a
    /// <c>BlogPost</c> instance.
    /// </summary>
    /// <remarks>
    /// <c>BlogSvc.GetBlogCounts</c> returns a <c>BlogPost</c> in which this is the only meaningful
    /// value and every other property is default; on a real post loaded by any normal query it is
    /// zero. The pattern predates <see cref="AdminCounts"/>, which is where a dashboard should get
    /// its totals from now. Never read it off a post you fetched for its content.
    /// </remarks>
    public int BlogCount { get; set; }

    /// <summary>
    /// The series this post belongs to, or null when it stands alone (<c>SeriesId BIGINT</c>, added
    /// by migration 004).
    /// </summary>
    /// <remarks>
    /// Membership is owned by the post, not by <see cref="BlogSeries"/> — that type holds only the
    /// series' own metadata. Set this and <see cref="SeriesPartNumber"/> together; a post with a
    /// series but no part number has no defined position in the reading order.
    /// </remarks>
    public long? SeriesId { get; set; }

    /// <summary>
    /// The post's 1-based position within its series (<c>SeriesPartNumber INT</c>, added by
    /// migration 004).
    /// </summary>
    /// <remarks>
    /// Ordinal, not an offset — part 1 is the first article. Nothing enforces uniqueness or
    /// contiguity within a series, so duplicate or gapped part numbers are possible and surface as
    /// confusing previous/next navigation (see <see cref="SeriesNavigation"/>) rather than as an
    /// error. Null when <see cref="SeriesId"/> is null, and meaningless if set without it.
    /// </remarks>
    public int? SeriesPartNumber { get; set; }

    /// <summary>
    /// The series' display name, projected by a join on <c>BlogSeries</c> — not a column on
    /// <c>BlogPost</c>.
    /// </summary>
    /// <remarks>
    /// Empty unless the query joined the series table, so an empty value means "not fetched" as often
    /// as "no series". Test <see cref="SeriesId"/> to decide whether a post is part of a series.
    /// </remarks>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// The series' URL segment, projected by the same join as <see cref="SeriesName"/>.
    /// </summary>
    /// <remarks>
    /// Empty unless that join ran; rendering a series link from an unjoined instance produces a link
    /// to <c>/series/</c>. Same caveat as <see cref="SeriesName"/>.
    /// </remarks>
    public string SeriesSlug { get; set; } = string.Empty;

    /// <summary>
    /// Transient checkbox state for bulk operations in the admin grid. Never persisted.
    /// </summary>
    /// <remarks>
    /// Marked <c>[NotMapped]</c> and matched by no column, so it survives only as long as the
    /// in-memory instance the grid is bound to — re-fetching the list clears every selection. It is
    /// view state living on a domain entity: never let it influence a query, a permission check or
    /// anything written back to the database.
    /// </remarks>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsSelected { get; set; }
}
