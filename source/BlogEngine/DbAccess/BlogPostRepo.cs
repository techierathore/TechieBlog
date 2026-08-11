using BlogEngine.DaCore;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing blog post data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogPost entities using Dapper.</para>
///
/// <para><b>Code Flow:</b> <c>BlogSvc</c>, <c>SeriesSvc</c> and <c>SitemapSvc</c> inject this
/// repository, call an <c>…Async</c> member, and the member routes through the protected helpers on
/// <c>GenericRepository</c>, which open the connection asynchronously and flow the cancellation token
/// into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>Async conversion (REQ-NFR-026, Group C).</b> This repository sits on the hot path for
/// <c>/</c>, <c>/post/{slug}</c>, <c>/series/{slug}</c> and the admin post list, so it carried a
/// large share of the throughput ceiling the requirement exists to lift. Every SQL statement is
/// hoisted into a <c>private const</c> shared by both twins, so the async version cannot drift from
/// the synchronous one it replaces.</para>
///
/// <para><b>Timestamp binding (the <c>42883</c>/timezone trap).</b> Every timestamp column on
/// <c>BlogPost</c> is declared <c>TIMESTAMP</c> — without time zone — while every value this
/// repository binds originates from <c>DateTime.UtcNow</c>, whose <c>Kind</c> is <c>Utc</c>. Npgsql
/// infers the wire type from the <c>Kind</c>, so such a value is sent as <c>timestamptz</c> and
/// PostgreSQL then casts it to the column type using the <i>session</i> time zone, silently shifting
/// the recorded instant on any host that is not running in UTC. Every <c>DateTime</c> bound here
/// therefore passes through <see cref="DbTimestamp.AsTimestamp(DateTime)"/>, which drops the
/// <c>Kind</c> without moving the instant. This matters most to
/// <see cref="GetDueScheduledPostsAsync"/>, where the same shift would compare a scheduled time
/// against the wrong wall clock and publish a post early or late.</para>
///
/// <para><b>REQ-UI-017 projection.</b> <see cref="GetAllAsync"/> and <see cref="GetAllByIdAsync"/>
/// project <c>BlogWriter</c>, <c>PublishedOn</c> and <c>ScheduledPublishOn</c>. Without those columns
/// the admin post list rendered every Author as "Unknown" and its Scheduled tab could never count
/// anything, because <c>BlogPost.IsScheduled</c> derives from <c>ScheduledPublishOn</c>.</para>
///
/// <para><b>Projections are not uniform here — know which one you are getting.</b> This repository
/// has four distinct read shapes, and the differences are load-bearing:</para>
/// <list type="table">
///   <listheader><term>Statement</term><description>What it projects</description></listheader>
///   <item><term><c>SelectAllSql</c>, <c>SelectAllByUserSql</c></term>
///   <description>The full entity plus the joined <c>BlogWriter</c>. Soft-deleted rows excluded,
///   newest first.</description></item>
///   <item><term><c>SelectByIdSql</c>, <c>SelectBySlugSql</c></term>
///   <description>The full entity, <c>BlogWriter</c>, and the joined <c>SeriesName</c> /
///   <c>SeriesSlug</c>. These two are deliberately identical except for the key.</description></item>
///   <item><term><c>SelectPagedSql</c></term>
///   <description>The paged twin of <c>SelectAllSql</c> — identical projection, identical filters,
///   plus <c>LIMIT</c>/<c>OFFSET</c>. It was the narrowest read here until REQ-FN-057; see the
///   comment on the constant for what that cost was waiting to be.</description></item>
///   <item><term><c>SelectPublishedSql</c>, <c>SelectFeaturedSql</c>, <c>SelectByCategorySql</c>,
///   <c>SearchSql</c></term>
///   <description><b>Public read shape.</b> The entity, <c>BlogWriter</c> and <c>PublishedOn</c>,
///   without the soft-delete, schedule or series columns. REQ-FN-057 added <c>PublishedOn</c>: these
///   feed every surface that dates a post as <c>PublishedOn ?? CreatedOn</c>, so without the column
///   the fallback fired on every row and the public site dated posts by when they were drafted.
///   Read-only paths — never write an entity loaded through one of them back.</description></item>
/// </list>
///
/// <para><b>REQ-UI-059 (2026-08-11) — a public listing sorts by the column it dates by.</b> The four
/// public reads above order by <c>COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC</c>, not by
/// <c>CreatedOn</c>. REQ-FN-057 moved every renderer onto <c>PublishedOn ?? CreatedOn</c> but left the
/// <c>ORDER BY</c> on <c>CreatedOn</c>, so a card's printed date and its position in the list came from
/// two different columns: a back-dated import, a long-lived draft or a scheduled publish read as
/// mis-sorted to a visitor, because it was sorted by when somebody started writing it. The
/// <c>p.PostID DESC</c> half is not decoration — <c>COALESCE</c> ties are common (a post published the
/// moment it was created has <c>PublishedOn = CreatedOn</c>, and seed data lands whole batches on the
/// same instant), and PostgreSQL is free to return tied rows in any order per execution, so a paged
/// listing without a unique tiebreaker can show the same post on page 1 and page 2 while another
/// vanishes entirely. Every paged public listing here carries the tiebreaker. The ADMIN reads
/// (<c>SelectAllSql</c>, <c>SelectAllByUserSql</c>, <c>SelectPagedSql</c>) deliberately still order by
/// <c>CreatedOn</c>: they include drafts, whose <c>PublishedOn</c> is <c>NULL</c>, so authoring order
/// is the only order that means anything there. Migration <c>026</c> carries the index that keeps the
/// new public sort off a sequential scan.</para>
///
/// <para><b>REQ-FN-057 (2026-08-10).</b> Three latent projection omissions registered by the
/// completeness gate were closed here: <c>PublishedOn</c> restored to the four public listing reads,
/// and <c>SelectPagedSql</c> widened to its unpaged twin's shape. The narrowings that remain are
/// declared in <c>ProjectionCompletenessTests.DeclaredNarrowProjections</c> and pinned statement by
/// statement in <c>BlogPostRepoProjectionTests</c>, so none of them can drift back silently.</para>
///
/// <para><b>Authoring reads and public reads are separate statements — pick the right one
/// (REQ-FN-015).</b> <c>SelectBySeriesSql</c> filters only on soft-deletion so the admin series
/// editor can list draft parts; <c>SelectPublishedBySeriesSql</c> adds <c>Published = TRUE</c> and is
/// the only one a public page may use. The pair exists because the single unfiltered statement, read
/// straight onto <c>/series/{slug}</c>, published every draft part's title, abstract and featured
/// image to anonymous visitors while the sibling <c>CountBySeriesSql</c> counted published parts only
/// — a listing query and its own COUNT disagreeing about what "a part" means. When you add a public
/// listing here, give it the same filter as the COUNT that pages it, and add a case to
/// <c>BlogPostRepoProjectionTests</c>, which asserts that pairing on the SQL text itself.</para>
///
/// <para><b>Never write back an entity read through a narrow projection.</b> This is the sharp edge,
/// and it has already cost this codebase real data. <see cref="UpdateAsync"/> writes
/// <c>PublishedOn</c> and <c>ScheduledPublishOn</c> unconditionally from the entity it is handed. An
/// entity loaded by a statement that did not select those columns carries <c>null</c> in them — not
/// "unchanged", <c>null</c> — so the update stores <c>NULL</c> and the post's first-publication date
/// and pending schedule are gone, with no error and no failing test. That is exactly why
/// <c>SelectByIdSql</c> now projects both: <c>GetSingle</c> is the load step of every
/// read-modify-write in <c>BlogSvc</c> (<c>PublishPostAsync</c>, <c>UnpublishPostAsync</c>,
/// <c>QuickPublishAsync</c>, <c>SchedulePostAsync</c>). If you add a column to
/// <see cref="UpdateAsync"/>, add it to <c>SelectByIdSql</c> in the same edit.</para>
/// </remarks>
public class BlogPostRepo : GenericRepository<BlogPost>, IBlogPostRepo
{
    private const string SelectAllSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.PublishedOn, p.ScheduledPublishOn, p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.IsDeleted = FALSE OR p.IsDeleted IS NULL
            ORDER BY p.CreatedOn DESC";

    private const string SelectAllByUserSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.PublishedOn, p.ScheduledPublishOn, p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.UserID = @UserID AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.CreatedOn DESC";

    // REQ-FN-057: the PAGED TWIN of SelectAllSql, and it must stay a twin. It used to select a bare
    // thirteen-column list off BlogPost with no join, so a post read here reported Author "Unknown",
    // Status "Draft" whatever the row said, and — the sharp edge — carried NULL in PublishedOn and
    // ScheduledPublishOn, which UpdateSql writes unconditionally. Nothing called it, which is the only
    // reason that never cost data. It is an override of an abstract member, so it can never be deleted
    // and the next caller inherits the landmine; it is now the same projection and the same filters as
    // SelectAllSql, differing only by LIMIT/OFFSET. It is an ADMIN read: no Published filter, exactly
    // like SelectAllSql and SelectCountsSql, so the page and the dashboard count agree. Both halves of
    // that are pinned by ProjectionCompletenessTests.PagedPostReadMatchesTheUnpagedAdminRead and
    // ProjectionCompletenessTests.PagedPostReadStaysAnAdminRead.
    private const string SelectPagedSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.PublishedOn, p.ScheduledPublishOn, p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.IsDeleted = FALSE OR p.IsDeleted IS NULL
            ORDER BY p.CreatedOn DESC
            LIMIT @PageSize OFFSET @OffSet";

    private const string SelectCountsSql = @"
            SELECT COUNT(*) as BlogCount
            FROM BlogPost
            WHERE IsDeleted = FALSE OR IsDeleted IS NULL";

    // REQ-UI-017 / REQ-NFR-008: PublishedOn and ScheduledPublishOn MUST stay in this projection.
    // GetSingle feeds every read-modify-write in BlogSvc (publish, unpublish, schedule, save), and
    // UpdateSql writes both columns. Omitting them here does not merely hide two values — it loads
    // them as NULL and the very next Update stores that NULL, erasing a post's first-publication date
    // and its pending schedule permanently.
    private const string SelectByIdSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.PublishedOn, p.ScheduledPublishOn, p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter,
                   s.Name as SeriesName, s.Slug as SeriesSlug
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            LEFT JOIN BlogSeries s ON p.SeriesId = s.SeriesId
            WHERE p.PostID = @PostID";

    // Kept column-for-column identical to SelectByIdSql, including PublishedOn and
    // ScheduledPublishOn — the two lookups differ only in their key, and letting their projections
    // drift is how one of them silently became a data-losing read.
    private const string SelectBySlugSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.IsDeleted, p.DeletedOn,
                   p.PublishedOn, p.ScheduledPublishOn, p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter,
                   s.Name as SeriesName, s.Slug as SeriesSlug
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            LEFT JOIN BlogSeries s ON p.SeriesId = s.SeriesId
            WHERE p.Slug = @Slug AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)";

    // REQ-FN-057: PublishedOn is part of this projection and must stay in it. Every public surface
    // that dates a post — the RSS pubDate, the sitemap lastmod, the listing cards — resolves the date
    // as `PublishedOn ?? CreatedOn`. Omitting the column does not make those callers fall back
    // knowingly; it makes PublishedOn arrive NULL on every row, so the fallback fires always and the
    // whole site dates posts by when they were drafted rather than by when they went live.
    // REQ-UI-059: and the ORDER BY is the same expression the renderer dates by, with PostID as the
    // unique tiebreaker that makes LIMIT/OFFSET paging repeatable across ties.
    private const string SelectPublishedSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC
            LIMIT @PageSize OFFSET @Offset";

    private const string CountBySlugSql = @"
            SELECT COUNT(1) FROM BlogPost
            WHERE Slug = @Slug AND PostID != @ExcludePostId";

    private const string InsertSql = @"
            INSERT INTO BlogPost (Title, Slug, Abstract, PostContent, UserID, Tags, CategoryId, FeaturedImage, CreatedOn, Published, PublishedOn, ScheduledPublishOn, IsDeleted, SeriesId, SeriesPartNumber)
            VALUES (@Title, @Slug, @Abstract, @PostContent, @UserID, @Tags, @CategoryId, @FeaturedImage, @CreatedOn, @Published, @PublishedOn, @ScheduledPublishOn, FALSE, @SeriesId, @SeriesPartNumber)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING PostID";

    private const string UpdateSql = @"
            UPDATE BlogPost SET
                Title = @Title,
                Slug = @Slug,
                Abstract = @Abstract,
                PostContent = @PostContent,
                Tags = @Tags,
                CategoryId = @CategoryId,
                FeaturedImage = @FeaturedImage,
                UpdatedOn = @UpdatedOn,
                Published = @Published,
                PublishedOn = @PublishedOn,
                ScheduledPublishOn = @ScheduledPublishOn,
                SeriesId = @SeriesId,
                SeriesPartNumber = @SeriesPartNumber
            WHERE PostID = @PostID";

    private const string SoftDeleteSql = @"
            UPDATE BlogPost SET
                IsDeleted = TRUE,
                DeletedOn = @DeletedOn
            WHERE PostID = @PostID";

    // REQ-FN-057: same public shape as SelectPublishedSql, PublishedOn included — the home page's
    // featured card dates the hero post from it. REQ-UI-059: "featured" means the most recently
    // PUBLISHED post, so it must be picked by the same expression the card prints; ordering by
    // CreatedOn made a back-dated import the hero while the card underneath it showed an older date.
    private const string SelectFeaturedSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC
            LIMIT 1";

    private const string CountPublishedSql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL)";

    // REQ-FN-057: same public shape as SelectPublishedSql, PublishedOn included — /category/{slug}
    // dates every card from it. REQ-UI-059: and orders by that same expression, tiebroken on PostID.
    private const string SelectByCategorySql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.CategoryId = @CategoryId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC
            LIMIT @PageSize OFFSET @Offset";

    private const string CountByCategorySql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE CategoryId = @CategoryId
                AND Published = TRUE
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";

    private const string SelectScheduledSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn, p.ScheduledPublishOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = FALSE
                AND p.ScheduledPublishOn IS NOT NULL
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.ScheduledPublishOn ASC";

    private const string SelectDueScheduledSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn, p.ScheduledPublishOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = FALSE
                AND p.ScheduledPublishOn IS NOT NULL
                AND p.ScheduledPublishOn <= @Now
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.ScheduledPublishOn ASC";

    // REQ-FN-015: the AUTHORING projection. It deliberately has no Published filter because the admin
    // series editor must see draft parts. Never serve this to an anonymous visitor — use
    // SelectPublishedBySeriesSql, whose filter matches CountBySeriesSql column for column.
    private const string SelectBySeriesSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.SeriesId = @SeriesId
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.SeriesPartNumber ASC";

    // REQ-FN-015: the PUBLIC projection behind /series/{slug}. Same columns as SelectBySeriesSql plus
    // the Published filter CountBySeriesSql already applied, so the parts a reader is shown and the
    // "N Parts" badge above them can never disagree — the drift that leaked draft titles, abstracts and
    // featured images to anonymous visitors.
    private const string SelectPublishedBySeriesSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   p.SeriesId, p.SeriesPartNumber,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.SeriesId = @SeriesId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY p.SeriesPartNumber ASC";

    private const string CountBySeriesSql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE SeriesId = @SeriesId
                AND Published = TRUE
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";

    private const string MaxPartNumberSql = @"
            SELECT COALESCE(MAX(SeriesPartNumber), 0) FROM BlogPost
            WHERE SeriesId = @SeriesId
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)";

    private const string ClearSeriesSql = @"
            UPDATE BlogPost SET
                SeriesId = NULL,
                SeriesPartNumber = NULL,
                UpdatedOn = @UpdatedOn
            WHERE SeriesId = @SeriesId";

    // REQ-FN-057: same public shape as SelectPublishedSql, PublishedOn included — /search dates every
    // result from it. REQ-UI-059: and orders by that same expression, tiebroken on PostID. Relevance
    // is not modelled here; ILIKE gives no score, so "most recently published first" is the ranking,
    // and it must be the one the result rows print.
    private const string SearchSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published, p.PublishedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
                AND (p.Title ILIKE @Query
                    OR p.Abstract ILIKE @Query
                    OR p.PostContent ILIKE @Query
                    OR p.Tags ILIKE @Query)
            ORDER BY COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC
            LIMIT @PageSize OFFSET @Offset";

    private const string SearchCountSql = @"
            SELECT COUNT(*) FROM BlogPost
            WHERE Published = TRUE
                AND (IsDeleted = FALSE OR IsDeleted IS NULL)
                AND (Title ILIKE @Query
                    OR Abstract ILIKE @Query
                    OR PostContent ILIKE @Query
                    OR Tags ILIKE @Query)";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public BlogPostRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every non-deleted post for the admin view, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Soft-deleted rows are excluded so the admin grid matches what an
    /// editor can act on. REQ-UI-017: the projection carries the author name and the publish/schedule
    /// timestamps because the grid renders an Author column, a published-or-created Date column and a
    /// Scheduled status tab; without them every row read "Unknown" and the Scheduled tab could never
    /// count anything, since <c>BlogPost.IsScheduled</c> derives from <c>ScheduledPublishOn</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → left join the author → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All non-deleted posts, newest first.</returns>
    public override async Task<IEnumerable<BlogPost>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every non-deleted post owned by one author, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the query behind an Author's "My Posts" view — the same
    /// admin list scoped to the caller — so it projects exactly the same columns as
    /// <see cref="GetAllAsync"/>, author name and schedule timestamps included (REQ-UI-017).</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filter by owner → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The author's user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The author's non-deleted posts, newest first.</returns>
    public override async Task<IEnumerable<BlogPost>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectAllByUserSql, new { UserID = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single post by INT ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post, or <c>null</c> when no row carries that key.</returns>
    public override Task<BlogPost?> GetIntSingleAsync(int postId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(postId, cancellationToken);
    }

    /// <summary>
    /// Gets a page of non-deleted posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large archive never crosses the wire
    /// in full. REQ-FN-057: this is the paged twin of <see cref="GetAllAsync"/> and projects exactly
    /// what it projects — author name, publication date and pending schedule included — so a post read
    /// here is a complete entity rather than one that reports Author "Unknown" and would NULL its own
    /// publication date if it were ever written back.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → left join the author → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<BlogPost>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the dashboard's aggregate post count, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The result is a <see cref="BlogPost"/> used purely as a counts
    /// carrier rather than as a real post; only <c>BlogCount</c> is populated.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → aggregate query → first row.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The counts projection, or <c>null</c> when the aggregate yields no row.</returns>
    public async Task<BlogPost?> GetTheCountsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogPost>(SelectCountsSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single post by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The editor needs a soft-deleted row too — it is how "already
    /// deleted" is reported — so this lookup deliberately does not filter <c>IsDeleted</c>. Series
    /// name and slug are joined in so the editor can show the series a post belongs to without a
    /// second round trip.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post, or <c>null</c> when no row carries that key.</returns>
    public override async Task<BlogPost?> GetSingleAsync(long postId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogPost>(
            SelectByIdSql, new { PostID = postId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a post by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The slug is the public identifier used by <c>/post/{slug}</c>, so
    /// an unknown or soft-deleted slug must return <c>null</c> for the page to render its 404 rather
    /// than throw.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post, or <c>null</c> when the slug is unknown.</returns>
    public async Task<BlogPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogPost>(
            SelectBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of published posts for public display, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drafts, scheduled posts and soft-deleted rows are excluded in SQL,
    /// so no caller can leak an unpublished post by forgetting to filter.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts, most recently published first (REQ-UI-059).</returns>
    public async Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectPublishedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a post slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Slugs address a page and must be unique. The exclusion parameter
    /// lets an update ignore the row being edited, so re-saving a post without renaming it does not
    /// collide with itself.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludePostId">Post ID to exclude, for updates.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another post already uses the slug.</returns>
    public async Task<bool> SlugExistsAsync(string slug, long excludePostId = 0, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountBySlugSql, new { Slug = slug, ExcludePostId = excludePostId }, cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Inserts a new blog post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form. Every timestamp is normalised to
    /// <c>DateTimeKind.Unspecified</c> first, so Npgsql sends <c>timestamp</c> rather than
    /// <c>timestamptz</c> and PostgreSQL does not shift the instant through the session time zone.</para>
    /// <para><b>Flow:</b> normalise timestamps → helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogPost</c>.</para>
    /// </remarks>
    /// <param name="post">The post to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(post), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a post and returns the generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key. Shares its parameter set and its statement text
    /// with <see cref="InsertAsync"/>, so the two insert paths cannot drift apart.</para>
    /// <para><b>Flow:</b> normalise timestamps → helper opens the connection asynchronously → INSERT … RETURNING → read the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogPost</c>.</para>
    /// </remarks>
    /// <param name="post">The post to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>PostID</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(post), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing blog post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every editable field is written together; the key, the author and
    /// the creation timestamp are never updated. Timestamps are normalised for the same reason as on
    /// insert.</para>
    /// <para><b>Flow:</b> normalise timestamps → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>PostID</c>.</para>
    /// </remarks>
    /// <param name="post">The post carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(post), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Soft-deletes a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is flagged rather than removed so comments, ratings and
    /// view counts that reference it stay valid and the delete can be reversed. The deletion timestamp
    /// is normalised so it is stored as the UTC instant it represents.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE setting the flag.</para>
    /// <para><b>Side Effects:</b> Marks one row deleted; it disappears from every public query.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been flagged.</returns>
    public async Task SoftDeleteAsync(long postId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            SoftDeleteSql,
            new { PostID = postId, DeletedOn = DbTimestamp.AsTimestamp(DateTime.UtcNow) },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the most recent published post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Featured" is defined as simply the most recently published post,
    /// so the home page never needs a manually curated flag to stay current. REQ-UI-059: "most recently
    /// published" is <c>COALESCE(PublishedOn, CreatedOn)</c> — the same expression the hero card prints —
    /// not <c>CreatedOn</c>, which would hand the slot to whichever post was <i>drafted</i> last.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → ordered query with LIMIT 1 → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The most recently published post, or <c>null</c> when nothing is published.</returns>
    public async Task<BlogPost?> GetFeaturedPostAsync(CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogPost>(SelectFeaturedSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the total number of published posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drives the pager on the public listing, so it must apply exactly
    /// the same filter as <see cref="GetPublishedPostsAsync"/> or the last page comes up empty.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published, non-deleted posts.</returns>
    public async Task<int> GetPublishedPostCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(CountPublishedSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of published posts in one category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs <c>/category/{slug}</c>; the published and not-deleted
    /// filters are applied in SQL for the same reason as on the main listing.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">Category to filter by.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts in the category, most recently published first (REQ-UI-059).</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsByCategoryAsync(long categoryId, int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectByCategorySql,
            new { CategoryId = categoryId, PageSize = pageSize, Offset = offset },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the number of published posts in one category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drives the category page's pager, and matches
    /// <see cref="GetPostsByCategoryAsync"/> filter for filter.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">Category to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts in the category.</returns>
    public async Task<int> GetPostCountByCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            CountByCategorySql, new { CategoryId = categoryId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every scheduled-but-unpublished post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A post counts as scheduled when it is unpublished <i>and</i>
    /// carries a schedule time, which is what separates it from a plain draft. Ordered soonest-first
    /// so the admin Scheduled tab reads as a queue.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Scheduled posts, soonest first.</returns>
    public async Task<IEnumerable<BlogPost>> GetScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(SelectScheduledSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets scheduled posts whose publish time has arrived, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The background publisher calls this on every cycle, so the
    /// comparison decides whether a post goes live early, late or on time. <paramref name="now"/> is
    /// normalised to <c>DateTimeKind.Unspecified</c> before it is bound: the column is
    /// <c>TIMESTAMP</c>, and a <c>Kind = Utc</c> value would be sent as <c>timestamptz</c>, making
    /// PostgreSQL re-interpret the column through the session time zone and shift the comparison by
    /// the host's UTC offset.</para>
    /// <para><b>Flow:</b> normalise the instant → helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="now">The current instant to compare schedules against.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts due for publication, soonest first.</returns>
    public async Task<IEnumerable<BlogPost>> GetDueScheduledPostsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectDueScheduledSql, new { Now = DbTimestamp.AsTimestamp(now) }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every post in a series ordered by part number, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drafts are deliberately included — the admin series editor lists
    /// unpublished parts too — so public callers filter on <c>Published</c> themselves. Ordering by
    /// part number is what makes previous/next navigation meaningful.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered ordered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The series' non-deleted posts in part order.</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsBySeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectBySeriesSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the published posts in a series ordered by part number, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The public counterpart to <see cref="GetPostsBySeriesAsync"/>
    /// (REQ-FN-015). Drafts are excluded in SQL, so <c>/series/{slug}</c> cannot leak an unpublished
    /// part's title, abstract or featured image to an anonymous visitor by forgetting to filter, and
    /// the rows returned always agree with the count from
    /// <see cref="GetPostCountBySeriesAsync"/>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → published-and-not-deleted query
    /// ordered by part number → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The series' published posts in part order.</returns>
    public async Task<IEnumerable<BlogPost>> GetPublishedPostsBySeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectPublishedBySeriesSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the number of published posts in a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts published parts only, so the "N parts" a reader sees equals
    /// the number of parts they can actually open. Its filter is the one
    /// <see cref="GetPublishedPostsBySeriesAsync"/> applies, so the badge and the list agree.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts in the series.</returns>
    public async Task<int> GetPostCountBySeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            CountBySeriesSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the highest part number used in a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drafts count here, unlike <see cref="GetPostCountBySeriesAsync"/> —
    /// an unpublished part still occupies its number, so proposing the next one must account for it.
    /// <c>COALESCE</c> turns the empty-series case into <c>0</c> rather than <c>NULL</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → MAX aggregate.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The highest part number, or <c>0</c> when the series has no posts.</returns>
    public async Task<int> GetMaxPartNumberInSeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            MaxPartNumberSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Detaches every post from a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Run before a series is deleted so its posts survive as standalone
    /// articles instead of pointing at a row that no longer exists. The part number is cleared with the
    /// series reference, because a part number means nothing without one.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Clears the series columns on every matching post and stamps <c>UpdatedOn</c>.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the rows have been written.</returns>
    public async Task ClearSeriesFromPostsAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            ClearSeriesSql,
            new { SeriesId = seriesId, UpdatedOn = DbTimestamp.AsTimestamp(DateTime.UtcNow) },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Searches published posts by title, abstract, content and tags, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank query is answered without touching the database — it can
    /// only come from an empty search box, and the honest answer is "no results" rather than "every
    /// post". <c>ILIKE</c> gives case-insensitive matching in PostgreSQL without a separate index.</para>
    /// <para><b>Flow:</b> guard the query → helper opens the connection asynchronously → ILIKE query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching published posts, most recently published first, or an empty sequence for a blank query.</returns>
    public async Task<IEnumerable<BlogPost>> SearchPostsAsync(string query, int pageSize = 10, int offset = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<BlogPost>();

        return await QueryAsync<BlogPost>(
            SearchSql,
            new { Query = $"%{query}%", PageSize = pageSize, Offset = offset },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the number of posts matching a search, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drives the search pager and applies the same blank-query guard and
    /// the same filters as <see cref="SearchPostsAsync"/>.</para>
    /// <para><b>Flow:</b> guard the query → helper opens the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of matching published posts, or <c>0</c> for a blank query.</returns>
    public async Task<int> GetSearchResultCountAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;

        return await ExecuteScalarAsync<int>(
            SearchCountSql, new { Query = $"%{query}%" }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every non-deleted post for the admin view.
    /// </summary>
    /// <remarks>
    /// REQ-UI-017: the projection carries the author name and the publish/schedule timestamps because
    /// the admin post list renders an Author column, a published-or-created Date column and a
    /// Scheduled status tab.
    /// </remarks>
    /// <returns>All non-deleted posts, newest first.</returns>
    public override IEnumerable<BlogPost> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every non-deleted post owned by one author.
    /// </summary>
    /// <param name="userId">The author's user identifier.</param>
    /// <returns>The author's non-deleted posts, newest first.</returns>
    public override IEnumerable<BlogPost> GetAllById(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectAllByUserSql, new { UserID = userId }).ToList();
    }

    /// <summary>
    /// Gets a single post by INT ID.
    /// </summary>
    /// <param name="postId">The post identifier.</param>
    /// <returns>The post, or <c>null</c> when not found.</returns>
    public override BlogPost? GetIntSingle(int postId)
    {
        return GetSingle(postId);
    }

    /// <summary>
    /// Gets a page of non-deleted posts.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<BlogPost> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Gets the dashboard's aggregate post count.
    /// </summary>
    /// <returns>The counts projection, or <c>null</c> when the aggregate yields no row.</returns>
    public BlogPost? GetTheCounts()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectCountsSql).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single post by ID.
    /// </summary>
    /// <param name="postId">The post identifier.</param>
    /// <returns>The post, or <c>null</c> when not found.</returns>
    public override BlogPost? GetSingle(long postId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectByIdSql, new { PostID = postId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a post by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>The post, or <c>null</c> when the slug is unknown.</returns>
    public BlogPost? GetBySlug(string slug)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectBySlugSql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a page of published posts for public display.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Published posts, most recently published first (REQ-UI-059).</returns>
    public IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectPublishedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Checks whether a post slug is already taken.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludePostId">Post ID to exclude, for updates.</param>
    /// <returns><c>true</c> when another post already uses the slug.</returns>
    public bool SlugExists(string slug, long excludePostId = 0)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountBySlugSql, new { Slug = slug, ExcludePostId = excludePostId }) > 0;
    }

    /// <summary>
    /// Inserts a new blog post.
    /// </summary>
    /// <param name="post">The post to persist.</param>
    public override void Insert(BlogPost post)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(post));
    }

    /// <summary>
    /// Inserts a post and returns the generated ID.
    /// </summary>
    /// <param name="post">The post to persist.</param>
    /// <returns>The generated <c>PostID</c>.</returns>
    public override long InsertToGetId(BlogPost post)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(post));
    }

    /// <summary>
    /// Updates an existing blog post.
    /// </summary>
    /// <param name="post">The post carrying the new values.</param>
    public override void Update(BlogPost post)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(post));
    }

    /// <summary>
    /// Soft-deletes a post by setting its IsDeleted flag.
    /// </summary>
    /// <param name="postId">The post identifier.</param>
    public void SoftDelete(long postId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(
            SoftDeleteSql,
            new { PostID = postId, DeletedOn = DbTimestamp.AsTimestamp(DateTime.UtcNow) });
    }

    /// <summary>
    /// Gets the most recent published post.
    /// </summary>
    /// <returns>The most recently published post, or <c>null</c> when nothing is published.</returns>
    public BlogPost? GetFeaturedPost()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectFeaturedSql).FirstOrDefault();
    }

    /// <summary>
    /// Gets the total number of published posts.
    /// </summary>
    /// <returns>The count of published, non-deleted posts.</returns>
    public int GetPublishedPostCount()
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountPublishedSql);
    }

    /// <summary>
    /// Gets a page of published posts in one category.
    /// </summary>
    /// <param name="categoryId">Category to filter by.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Published posts in the category, most recently published first (REQ-UI-059).</returns>
    public IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(
            SelectByCategorySql, new { CategoryId = categoryId, PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the number of published posts in one category.
    /// </summary>
    /// <param name="categoryId">Category to count.</param>
    /// <returns>The count of published posts in the category.</returns>
    public int GetPostCountByCategory(long categoryId)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountByCategorySql, new { CategoryId = categoryId });
    }

    /// <summary>
    /// Gets every scheduled-but-unpublished post.
    /// </summary>
    /// <returns>Scheduled posts, soonest first.</returns>
    public IEnumerable<BlogPost> GetScheduledPosts()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectScheduledSql).ToList();
    }

    /// <summary>
    /// Gets scheduled posts whose publish time has arrived.
    /// </summary>
    /// <param name="now">The current instant to compare schedules against.</param>
    /// <returns>Posts due for publication, soonest first.</returns>
    public IEnumerable<BlogPost> GetDueScheduledPosts(DateTime now)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(
            SelectDueScheduledSql, new { Now = DbTimestamp.AsTimestamp(now) }).ToList();
    }

    /// <summary>
    /// Gets every post in a series ordered by part number.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series' non-deleted posts in part order.</returns>
    public IEnumerable<BlogPost> GetPostsBySeries(long seriesId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectBySeriesSql, new { SeriesId = seriesId }).ToList();
    }

    /// <summary>
    /// Gets the published posts in a series ordered by part number.
    /// </summary>
    /// <remarks>
    /// REQ-FN-015: the public counterpart to <see cref="GetPostsBySeries"/>. Drafts are excluded in
    /// SQL so <c>/series/{slug}</c> cannot leak an unpublished part to an anonymous visitor.
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series' published posts in part order.</returns>
    public IEnumerable<BlogPost> GetPublishedPostsBySeries(long seriesId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(SelectPublishedBySeriesSql, new { SeriesId = seriesId }).ToList();
    }

    /// <summary>
    /// Gets the number of published posts in a series.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The count of published posts in the series.</returns>
    public int GetPostCountBySeries(long seriesId)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountBySeriesSql, new { SeriesId = seriesId });
    }

    /// <summary>
    /// Gets the highest part number used in a series.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The highest part number, or <c>0</c> when the series has no posts.</returns>
    public int GetMaxPartNumberInSeries(long seriesId)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(MaxPartNumberSql, new { SeriesId = seriesId });
    }

    /// <summary>
    /// Detaches every post from a series.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    public void ClearSeriesFromPosts(long seriesId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(
            ClearSeriesSql,
            new { SeriesId = seriesId, UpdatedOn = DbTimestamp.AsTimestamp(DateTime.UtcNow) });
    }

    /// <summary>
    /// Searches published posts by title, abstract, content and tags.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Matching published posts, or an empty sequence for a blank query.</returns>
    public IEnumerable<BlogPost> SearchPosts(string query, int pageSize = 10, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Enumerable.Empty<BlogPost>();

        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(
            SearchSql, new { Query = $"%{query}%", PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the number of posts matching a search.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <returns>The count of matching published posts, or <c>0</c> for a blank query.</returns>
    public int GetSearchResultCount(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0;

        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(SearchCountSql, new { Query = $"%{query}%" });
    }

    // =================================================================================================
    // Parameter builders — shared by both twins so the sync and async write paths cannot diverge.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c>, <c>PublishedOn</c> and <c>ScheduledPublishOn</c>
    /// all pass through <see cref="DbTimestamp.AsTimestamp(DateTime)"/> because their columns are
    /// <c>TIMESTAMP</c> without time zone. Binding the <c>Kind = Utc</c> value that
    /// <c>DateTime.UtcNow</c> produces would make Npgsql send <c>timestamptz</c>, and PostgreSQL would
    /// then convert it to the column type through the session time zone — recording a different
    /// instant on any host that is not set to UTC.</para>
    /// <para><b>Flow:</b> normalise the three timestamps → project the remaining columns unchanged.</para>
    /// <para><b>Side Effects:</b> None — the post itself is not mutated.</para>
    /// </remarks>
    /// <param name="post">The post being persisted.</param>
    /// <returns>An anonymous parameter object matching the insert statements.</returns>
    private static object BuildInsertParameters(BlogPost post)
    {
        return new
        {
            post.Title,
            post.Slug,
            post.Abstract,
            post.PostContent,
            post.UserID,
            post.Tags,
            post.CategoryId,
            post.FeaturedImage,
            CreatedOn = DbTimestamp.AsTimestamp(post.CreatedOn),
            post.Published,
            PublishedOn = DbTimestamp.AsTimestamp(post.PublishedOn),
            ScheduledPublishOn = DbTimestamp.AsTimestamp(post.ScheduledPublishOn),
            post.SeriesId,
            post.SeriesPartNumber
        };
    }

    /// <summary>
    /// Builds the parameter set the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same timestamp normalisation as
    /// <see cref="BuildInsertParameters"/>; <c>CreatedOn</c> and <c>UserID</c> are absent because a
    /// post's creation time and author are never rewritten by an edit.</para>
    /// <para><b>Flow:</b> normalise the three timestamps → project the editable columns.</para>
    /// <para><b>Side Effects:</b> None — the post itself is not mutated.</para>
    /// </remarks>
    /// <param name="post">The post being updated.</param>
    /// <returns>An anonymous parameter object matching the update statement.</returns>
    private static object BuildUpdateParameters(BlogPost post)
    {
        return new
        {
            post.PostID,
            post.Title,
            post.Slug,
            post.Abstract,
            post.PostContent,
            post.Tags,
            post.CategoryId,
            post.FeaturedImage,
            UpdatedOn = DbTimestamp.AsTimestamp(post.UpdatedOn),
            post.Published,
            PublishedOn = DbTimestamp.AsTimestamp(post.PublishedOn),
            ScheduledPublishOn = DbTimestamp.AsTimestamp(post.ScheduledPublishOn),
            post.SeriesId,
            post.SeriesPartNumber
        };
    }
}
