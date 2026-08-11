using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Blog series: CRUD, slug allocation, part numbering and previous/next navigation.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A series is an ordered collection of posts — "Part 3 of 7" — so unlike a
/// category or a tag it carries <i>sequence</i>. This class owns that sequence: which post is part
/// one, what number the next post should take, and what "previous" and "next" mean on a post
/// page.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Reads back the series index, the public <c>/series/{slug}</c> page (which also loads its
///     posts) and the admin list.</item>
///   <item><see cref="GetNextPartNumber"/> is called by the post editor to pre-fill the part number
///     when an author adds a post to a series.</item>
///   <item>Writes validate, allocate a unique slug and persist;
///     <see cref="DeleteSeries"/> detaches the posts before removing the series.</item>
///   <item><see cref="GetSeriesNavigation"/> builds the previous/next strip shown on a post that
///     belongs to a series.</item>
/// </list>
///
/// <para><b>Two rules worth knowing before changing anything here:</b></para>
/// <list type="bullet">
///   <item><b>Deleting a series never deletes its posts.</b> <see cref="DeleteSeries"/> clears the
///     series reference from every member post first, then removes the series row. Losing the
///     grouping must not lose the writing.</item>
///   <item><b>Navigation counts published posts only.</b> <see cref="GetSeriesNavigation"/> filters
///     on <c>Published</c>, so a draft sitting at part 4 does not appear as "next" to an anonymous
///     reader and does not inflate the "of N" total. This is the disclosure-sensitive rule in the
///     class.</item>
/// </list>
///
/// <para><b>Error contract:</b> reads swallow and log, returning an empty sequence or null — a
/// broken series strip must not take a post page down. Mutations return <c>Result</c>: an expected
/// failure (missing name, unknown id) is returned, and an unexpected one is caught, logged and
/// converted.</para>
///
/// <para><b>Exception text never reaches the caller (REQ-NFR-031).</b> Mutation failures used to
/// interpolate <c>ex.Message</c>, defended on the grounds that every caller is an admin screen —
/// but nothing in this class enforces that, so the disclosure would have gone live the moment a
/// mutation was reached anonymously. The exception now stays in the log, where the host's
/// <c>CorrelationIdMiddleware</c> has already attached the request's correlation id to every event
/// (REQ-NFR-015), and the caller sees only the curated constants below.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogSeriesRepo"/> and <see cref="IBlogPostRepo"/> for
/// data access, <c>SlugGenerator</c> for URL slugs, <see cref="ILogger{TCategoryName}"/> for
/// diagnostics.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Reads serve anonymous
/// public pages; every mutation is reached from an admin screen gated by
/// <c>AppPolicies.EditorOrAbove</c>. This class enforces <b>no</b> policy itself — the calling page
/// owns that check.</para>
///
/// <para><b>Async conversion (REQ-NFR-026, stage 3):</b> every member below has an <c>…Async</c> twin
/// placed immediately after it, routing the same reads and writes through the repositories'
/// <c>…Async</c> members with the caller's <see cref="CancellationToken"/> flowed in. <b>Call the
/// async twin.</b> The behaviour, ordering, <c>Published</c>/<c>IsDeleted</c> filtering and
/// <c>Result</c> failure strings are identical between the pair by design — the twins differ only in
/// whether they park a thread for the round trip. <c>Result&lt;T&gt;</c> is unchanged by the
/// conversion: it simply travels inside a task, so a member returning <c>Result&lt;BlogSeries&gt;</c>
/// returns <c>Task&lt;Result&lt;BlogSeries&gt;&gt;</c>. The synchronous surface is retained only until
/// the last Blazor call site migrates and is <b>deleted in stage 4</b>; do not add new callers of
/// it.</para>
/// </remarks>
public class SeriesSvc
{
    /// <summary>
    /// Prefix used to build an identifier-based slug when a series name yields no slug at all.
    /// </summary>
    /// <remarks>
    /// Feeds <c>SlugGenerator.EnsureSlug</c>, which turns it into <c>series-3</c> for a series that
    /// already has an id, or <c>series-{name digest}</c> for one being inserted (REQ-FN-054).
    /// </remarks>
    private const string SlugPrefix = "series";

    /// <summary>Curated message for an insert that could not be persisted (REQ-NFR-031).</summary>
    private const string CreateFailureMessage = "Failed to create series. Please try again later.";

    /// <summary>Curated message for an update that could not be persisted (REQ-NFR-031).</summary>
    private const string UpdateFailureMessage = "Failed to update series. Please try again later.";

    /// <summary>Curated message for a delete that could not be persisted (REQ-NFR-031).</summary>
    private const string DeleteFailureMessage = "Failed to delete series. Please try again later.";

    private readonly IBlogSeriesRepo seriesRepo;
    private readonly IBlogPostRepo postRepo;
    private readonly ILogger<SeriesSvc> logger;
    private readonly ICacheService? cacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesSvc"/> class.
    /// </summary>
    /// <param name="seriesRepo">Series data access.</param>
    /// <param name="postRepo">Post data access, used for membership and part numbering.</param>
    /// <param name="logger">Logger for series changes and read failures.</param>
    /// <param name="cacheService">
    /// Taxonomy cache (REQ-NFR-018) holding the two series-wide listings. Optional: omitting it makes
    /// every read go to the database, which is what a unit test that is not exercising caching wants.
    /// The host always supplies it — it is a registered singleton.
    /// </param>
    public SeriesSvc(
        IBlogSeriesRepo seriesRepo,
        IBlogPostRepo postRepo,
        ILogger<SeriesSvc> logger,
        ICacheService? cacheService = null)
    {
        this.seriesRepo = seriesRepo;
        this.postRepo = postRepo;
        this.logger = logger;
        this.cacheService = cacheService;
    }

    /// <summary>
    /// Lists every series, ordered by name.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unfiltered, including series with no posts yet — an author
    /// creates the series before writing part one, and it has to be selectable in the editor from
    /// that moment.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <returns>Every series; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogSeries> GetAllSeries()
    {
        try
        {
            return ServiceCache.Read<IEnumerable<BlogSeries>>(
                cacheService,
                ServiceCache.SeriesAllKey,
                CacheTags.Taxonomy,
                () => seriesRepo.GetAll());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all series");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Lists every series, ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllSeries"/> and identical to it in
    /// every observable respect. Unfiltered, including series with no posts yet — an author creates
    /// the series before writing part one, and it has to be selectable in the editor from that
    /// moment. A read that fails is logged and degraded to an empty sequence rather than thrown, so a
    /// broken series list cannot take the surrounding page down.</para>
    /// <para><b>Flow:</b> await the repository read → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query; a cancellation surfaces through the
    /// <c>catch</c> exactly as any other failure does, returning an empty sequence.</param>
    /// <returns>Every series; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogSeries>> GetAllSeriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<BlogSeries>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.SeriesAllKey),
                CacheTags.Taxonomy,
                () => seriesRepo.GetAllAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all series");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Lists every series with the number of posts it contains.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One aggregate query, so the series index can show "7 parts"
    /// without a query per row.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <returns>Series with <c>PostCount</c> populated; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogSeries> GetAllWithCounts()
    {
        try
        {
            return ServiceCache.Read<IEnumerable<BlogSeries>>(
                cacheService,
                ServiceCache.SeriesWithCountsKey,
                CacheTags.Taxonomy,
                () => seriesRepo.GetAllWithCounts());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series with counts");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Lists every series with the number of posts it contains, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllWithCounts"/>. One aggregate query,
    /// so the series index can show "7 parts" without a query per row; the count the repository
    /// projects covers published, non-deleted posts only, and this service does not alter it. Same
    /// degrade-to-empty policy as <see cref="GetAllSeriesAsync"/>.</para>
    /// <para><b>Flow:</b> await the aggregate read → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query; a cancellation is caught with every other
    /// failure and yields an empty sequence.</param>
    /// <returns>Series with <c>PostCount</c> populated; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogSeries>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<BlogSeries>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.SeriesWithCountsKey),
                CacheTags.Taxonomy,
                () => seriesRepo.GetAllWithCountsAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series with counts");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Loads one series by its identifier, for the edit form.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Header only — the member posts are <i>not</i> loaded. Use
    /// <see cref="GetPostsInSeries"/> when they are needed.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series, or null when it does not exist or the read failed.</returns>
    public BlogSeries? GetSeries(long seriesId)
    {
        try
        {
            return seriesRepo.GetSingle(seriesId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series by ID: {SeriesId}", seriesId);
            return null;
        }
    }

    /// <summary>
    /// Loads one series by its identifier, for the edit form, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSeries"/>. Header only — the member
    /// posts are <i>not</i> loaded. Use <see cref="GetPostsInSeriesAsync"/> when they are needed. Both
    /// "no such series" and "the lookup failed" surface as <c>null</c>; the difference is recorded in
    /// the log, not in the return value.</para>
    /// <para><b>Flow:</b> await the keyed read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation is caught with every other
    /// failure and yields <c>null</c>.</param>
    /// <returns>The series, or null when it does not exist or the read failed.</returns>
    public async Task<BlogSeries?> GetSeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await seriesRepo.GetSingleAsync(seriesId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series by ID: {SeriesId}", seriesId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the series behind a public <c>/series/{slug}</c> URL and loads its parts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Two reads — the header by slug, then its posts ordered by part
    /// number — combined so the page has everything it needs in one call.</para>
    /// <para><b>Flow:</b> blank guard → resolve by slug → load and attach the posts.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Mutates the returned object's <c>Posts</c>
    /// collection.</para>
    /// <para><b>Published parts only, unless the caller asks otherwise (REQ-FN-015).</b> This
    /// overload attaches <i>published</i> parts, because it is the read behind the anonymous
    /// <c>/series/{slug}</c> page and the default has to be the safe one. It previously attached
    /// every non-deleted part, which put each draft part's title, abstract and featured image in
    /// front of anonymous visitors as an unlinked "Coming Soon" row while the header badge — fed by
    /// the published-only <c>PostCount</c> — counted fewer parts than the page listed. A caller that
    /// has already established the visitor may see unpublished work calls
    /// <see cref="GetSeriesBySlug(string, bool)"/> with <c>includeDrafts: true</c>. Compare
    /// <see cref="GetSeriesNavigation"/>, which has always filtered to published parts because a
    /// previous/next link must be openable.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <returns>
    /// The series with <c>Posts</c> populated from its published parts, or null when the slug is
    /// blank, unknown, or the read failed.
    /// </returns>
    public BlogSeries? GetSeriesBySlug(string slug)
    {
        return GetSeriesBySlug(slug, includeDrafts: false);
    }

    /// <summary>
    /// Resolves the series behind a public <c>/series/{slug}</c> URL and loads its published parts,
    /// without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSeriesBySlug(string)"/>, and like it a
    /// pure delegation: it forwards to
    /// <see cref="GetSeriesBySlugAsync(string, bool, CancellationToken)"/> with
    /// <c>includeDrafts: false</c>. The default has to be the safe one because this is the read behind
    /// the anonymous <c>/series/{slug}</c> page — attaching every non-deleted part would put each
    /// draft part's title, abstract and featured image in front of anonymous visitors while the header
    /// badge, fed by the published-only <c>PostCount</c>, counted fewer parts than the page listed
    /// (REQ-FN-015).</para>
    /// <para><b>Flow:</b> forward the task from the two-argument overload without awaiting it — there
    /// is nothing to do after it completes, so no state machine is warranted (async conversion pattern
    /// §4).</para>
    /// <para><b>Side Effects:</b> Those of the delegated member — none beyond logging, though it
    /// mutates the returned object's <c>Posts</c> collection.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <param name="cancellationToken">Cancels the two underlying queries; passed straight through to
    /// the delegated member.</param>
    /// <returns>
    /// The series with <c>Posts</c> populated from its published parts, or null when the slug is
    /// blank, unknown, or the read failed.
    /// </returns>
    public Task<BlogSeries?> GetSeriesBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return GetSeriesBySlugAsync(slug, includeDrafts: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolves the series behind a <c>/series/{slug}</c> URL and loads its parts, choosing whether
    /// unpublished parts are included.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Two reads — the header by slug, then its parts ordered by part
    /// number — combined so the page has everything it needs in one call.
    /// <paramref name="includeDrafts"/> selects between the authoring read
    /// (<c>IBlogPostRepo.GetPostsBySeries</c>) and the public one
    /// (<c>IBlogPostRepo.GetPublishedPostsBySeries</c>), so the <c>Published</c> filter is applied in
    /// SQL rather than by whichever page happens to remember (REQ-FN-015).</para>
    /// <para><b>Flow:</b> blank guard → resolve by slug → load the matching part list → attach.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Mutates the returned object's <c>Posts</c>
    /// collection.</para>
    /// <para><b>Only pass <c>true</c> after establishing the caller may see unpublished work.</b>
    /// A draft part's title, abstract and featured image are embargoed content; the series header's
    /// <c>PostCount</c> still counts published parts only, so a page that asks for drafts is
    /// responsible for explaining the difference to the reader.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <param name="includeDrafts">
    /// <c>true</c> to attach unpublished parts as well — for authenticated authoring surfaces only;
    /// <c>false</c> for anything an anonymous visitor can reach.
    /// </param>
    /// <returns>
    /// The series with <c>Posts</c> populated, or null when the slug is blank, unknown, or the read
    /// failed.
    /// </returns>
    public BlogSeries? GetSeriesBySlug(string slug, bool includeDrafts)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            var series = seriesRepo.GetBySlug(slug);
            if (series != null)
            {
                series.Posts = includeDrafts
                    ? postRepo.GetPostsBySeries(series.SeriesId).ToList()
                    : postRepo.GetPublishedPostsBySeries(series.SeriesId).ToList();
            }
            return series;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Resolves the series behind a <c>/series/{slug}</c> URL and loads its parts, choosing whether
    /// unpublished parts are included, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSeriesBySlug(string, bool)"/> and
    /// identical to it filter for filter. Two reads — the header by slug, then its parts ordered by
    /// part number — combined so the page has everything it needs in one call.
    /// <paramref name="includeDrafts"/> selects between the authoring read
    /// (<c>IBlogPostRepo.GetPostsBySeriesAsync</c>) and the public one
    /// (<c>IBlogPostRepo.GetPublishedPostsBySeriesAsync</c>), so the <c>Published</c> filter is applied
    /// in SQL rather than by whichever page happens to remember (REQ-FN-015). A blank slug never
    /// reaches the database.</para>
    /// <para><b>Flow:</b> blank guard → await the slug lookup → await the matching part list →
    /// attach.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Mutates the returned object's <c>Posts</c>
    /// collection.</para>
    /// <para><b>Only pass <c>true</c> after establishing the caller may see unpublished work.</b>
    /// A draft part's title, abstract and featured image are embargoed content; the series header's
    /// <c>PostCount</c> still counts published parts only, so a page that asks for drafts is
    /// responsible for explaining the difference to the reader.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <param name="includeDrafts">
    /// <c>true</c> to attach unpublished parts as well — for authenticated authoring surfaces only;
    /// <c>false</c> for anything an anonymous visitor can reach.
    /// </param>
    /// <param name="cancellationToken">Cancels both queries; a cancellation is caught with every other
    /// failure and yields <c>null</c>.</param>
    /// <returns>
    /// The series with <c>Posts</c> populated, or null when the slug is blank, unknown, or the read
    /// failed.
    /// </returns>
    public async Task<BlogSeries?> GetSeriesBySlugAsync(
        string slug,
        bool includeDrafts,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            var series = await seriesRepo.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
            if (series != null)
            {
                var parts = includeDrafts
                    ? await postRepo.GetPostsBySeriesAsync(series.SeriesId, cancellationToken).ConfigureAwait(false)
                    : await postRepo.GetPublishedPostsBySeriesAsync(series.SeriesId, cancellationToken).ConfigureAwait(false);
                series.Posts = parts.ToList();
            }
            return series;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Lists a series' parts in part-number order.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ordering is by <c>SeriesPartNumber</c>, not by publication date
    /// — a series is read in the author's intended sequence even if part 5 was published before a
    /// back-filled part 4.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Includes drafts.</b> Same unfiltered repository read as
    /// <see cref="GetSeriesBySlug"/>; a caller rendering to anonymous visitors must apply its own
    /// <c>Published</c> filter.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series' posts in part order; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogPost> GetPostsInSeries(long seriesId)
    {
        try
        {
            return postRepo.GetPostsBySeries(seriesId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for series ID: {SeriesId}", seriesId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Lists a series' parts in part-number order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostsInSeries"/>. Ordering is by
    /// <c>SeriesPartNumber</c>, not by publication date — a series is read in the author's intended
    /// sequence even if part 5 was published before a back-filled part 4.</para>
    /// <para><b>Flow:</b> await the series part list → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Includes drafts.</b> Deliberately the same unfiltered repository read the synchronous
    /// twin performs (<c>GetPostsBySeriesAsync</c>, not <c>GetPublishedPostsBySeriesAsync</c>); a
    /// caller rendering to anonymous visitors must apply its own <c>Published</c> filter, or call
    /// <see cref="GetSeriesBySlugAsync(string, CancellationToken)"/> instead, which filters in
    /// SQL.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation is caught with every other
    /// failure and yields an empty sequence.</param>
    /// <returns>The series' posts in part order; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsInSeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetPostsBySeriesAsync(seriesId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for series ID: {SeriesId}", seriesId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Suggests the part number a newly added post should take.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Highest existing part number plus one, so numbering keeps
    /// climbing even after a middle part is deleted — reusing a freed number would silently
    /// renumber a series readers have already been linked to. An empty series (max of 0) yields
    /// part 1.</para>
    /// <para><b>Flow:</b> read the maximum part number → add one.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Nothing is reserved: this is a
    /// <i>suggestion</i> for the editor, not an allocation, and two authors adding a part at the
    /// same moment will both be offered the same number. Uniqueness is not enforced here.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The suggested next part number; 1 if the read failed.</returns>
    public int GetNextPartNumber(long seriesId)
    {
        try
        {
            return postRepo.GetMaxPartNumberInSeries(seriesId) + 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting next part number for series ID: {SeriesId}", seriesId);
            return 1;
        }
    }

    /// <summary>
    /// Suggests the part number a newly added post should take, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetNextPartNumber"/>. Highest existing
    /// part number plus one, so numbering keeps climbing even after a middle part is deleted —
    /// reusing a freed number would silently renumber a series readers have already been linked to.
    /// An empty series (max of 0) yields part 1, and a failed read also yields 1.</para>
    /// <para><b>Flow:</b> await the maximum part number → add one → log and fall back to 1 on
    /// failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Nothing is reserved: this is a
    /// <i>suggestion</i> for the editor, not an allocation, and two authors adding a part at the same
    /// moment will both be offered the same number. Uniqueness is not enforced here.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation is caught with every other
    /// failure and yields 1.</param>
    /// <returns>The suggested next part number; 1 if the read failed.</returns>
    public async Task<int> GetNextPartNumberAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        try
        {
            var maxPartNumber = await postRepo
                .GetMaxPartNumberInSeriesAsync(seriesId, cancellationToken)
                .ConfigureAwait(false);
            return maxPartNumber + 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting next part number for series ID: {SeriesId}", seriesId);
            return 1;
        }
    }

    /// <summary>
    /// Creates a series, allocating a free slug and default status.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A name is mandatory; a slug is derived from it when the
    /// administrator leaves it blank, and a taken slug gains a numeric suffix retried up to 99
    /// times. Status defaults to <c>In Progress</c> — the honest state for a series whose first part
    /// has not been written yet, and the value the public page shows in its header.</para>
    /// <para><b>Flow:</b> null and name guards → derive slug → resolve collisions → stamp created
    /// and updated timestamps → default the status → insert.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogSeries</c> row, <b>mutates the caller's
    /// object</b> (<c>Slug</c>, <c>CreatedOn</c>, <c>UpdatedOn</c>, <c>Status</c> and
    /// <c>SeriesId</c> are all written back), and logs the creation. Timestamps are UTC.</para>
    /// </remarks>
    /// <param name="series">The series to create; several fields are assigned in place.</param>
    /// <returns>Success carrying the persisted series, or a failure naming the problem.</returns>
    public Result<BlogSeries> CreateSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        series.Slug = SlugGenerator.EnsureSlug(series.Slug, series.Name, SlugPrefix);
        series.Slug = SlugGenerator.ResolveUniqueSlug(
            series.Slug,
            candidate => seriesRepo.SlugExists(candidate));

        // Set timestamps
        series.CreatedOn = DateTime.UtcNow;
        series.UpdatedOn = DateTime.UtcNow;

        // Set default status if not provided
        if (string.IsNullOrWhiteSpace(series.Status))
        {
            series.Status = "In Progress";
        }

        try
        {
            var seriesId = seriesRepo.InsertToGetId(series);
            ServiceCache.InvalidateTaxonomy(cacheService);
            series.SeriesId = seriesId;
            logger.LogInformation("Created series '{Name}' with ID {SeriesId}", series.Name, seriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create series: {Name}", series.Name);
            return Result<BlogSeries>.Failure(CreateFailureMessage);
        }
    }

    /// <summary>
    /// Creates a series, allocating a free slug and default status, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="CreateSeries"/>, with the same guards, the
    /// same slug allocation and the same failure strings. A name is mandatory; a slug is derived from
    /// it when the administrator leaves it blank, and a taken slug gains a numeric suffix retried up to
    /// 99 times. Status defaults to <c>In Progress</c> — the honest state for a series whose first part
    /// has not been written yet, and the value the public page shows in its header. Validation failures
    /// are expected outcomes returned as a failed <c>Result</c>; only the insert is wrapped in
    /// <c>try</c>, so a failure of the slug-uniqueness read propagates exactly as it does in the
    /// synchronous twin.</para>
    /// <para><b>Flow:</b> null and name guards → derive slug → await the collision checks → stamp
    /// created and updated timestamps → default the status → await the insert.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogSeries</c> row, <b>mutates the caller's
    /// object</b> (<c>Slug</c>, <c>CreatedOn</c>, <c>UpdatedOn</c>, <c>Status</c> and <c>SeriesId</c>
    /// are all written back), and logs the creation. Timestamps are UTC.</para>
    /// <para><b>Slug collision race:</b> the uniqueness check and the insert are separate statements,
    /// so two simultaneous creations of the same name can both pass the check; the database constraint
    /// is the real guard.</para>
    /// </remarks>
    /// <param name="series">The series to create; several fields are assigned in place.</param>
    /// <param name="cancellationToken">Cancels the uniqueness checks and the insert. Cancellation
    /// faults the returned task rather than producing a failed <c>Result</c>.</param>
    /// <returns>Success carrying the persisted series, or a failure naming the problem.</returns>
    public async Task<Result<BlogSeries>> CreateSeriesAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        series.Slug = SlugGenerator.EnsureSlug(series.Slug, series.Name, SlugPrefix);
        series.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            series.Slug,
            candidate => seriesRepo.SlugExistsAsync(candidate, 0, cancellationToken)).ConfigureAwait(false);

        // Set timestamps
        series.CreatedOn = DateTime.UtcNow;
        series.UpdatedOn = DateTime.UtcNow;

        // Set default status if not provided
        if (string.IsNullOrWhiteSpace(series.Status))
        {
            series.Status = "In Progress";
        }

        try
        {
            var seriesId = await seriesRepo.InsertToGetIdAsync(series, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateTaxonomy(cacheService);
            series.SeriesId = seriesId;
            logger.LogInformation("Created series '{Name}' with ID {SeriesId}", series.Name, seriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create series: {Name}", series.Name);
            return Result<BlogSeries>.Failure(CreateFailureMessage);
        }
    }

    /// <summary>
    /// Saves changes to an existing series.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same rules as <see cref="CreateSeries"/>, plus two: the row must
    /// already exist, and the slug-uniqueness check excludes the series being edited so re-saving it
    /// unchanged does not renumber its own slug. <c>CreatedOn</c> is left alone; only
    /// <c>UpdatedOn</c> is restamped.</para>
    /// <para><b>Flow:</b> null, id and name guards → confirm existence → derive slug if absent →
    /// resolve collisions against every <i>other</i> series → stamp <c>UpdatedOn</c> → update.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogSeries</c> row and mutates the caller's object.
    /// <b>Changing the slug breaks the published <c>/series/{old-slug}</c> URL</b>; no redirect is
    /// written.</para>
    /// </remarks>
    /// <param name="series">The series carrying updated values; its <c>Slug</c> may be rewritten.</param>
    /// <returns>Success carrying the saved series, or a failure naming the problem.</returns>
    public Result<BlogSeries> UpdateSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (series.SeriesId <= 0)
            return Result<BlogSeries>.Failure("Invalid series ID");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Check if series exists
        var existing = seriesRepo.GetSingle(series.SeriesId);
        if (existing == null)
            return Result<BlogSeries>.Failure("Series not found");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        series.Slug = SlugGenerator.EnsureSlug(series.Slug, series.Name, SlugPrefix, series.SeriesId);
        series.Slug = SlugGenerator.ResolveUniqueSlug(
            series.Slug,
            candidate => seriesRepo.SlugExists(candidate, series.SeriesId));

        series.UpdatedOn = DateTime.UtcNow;

        try
        {
            seriesRepo.Update(series);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Updated series '{Name}' with ID {SeriesId}", series.Name, series.SeriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update series ID {SeriesId}: {Name}", series.SeriesId, series.Name);
            return Result<BlogSeries>.Failure(UpdateFailureMessage);
        }
    }

    /// <summary>
    /// Saves changes to an existing series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="UpdateSeries"/>, with the same guards and
    /// the same failure strings. Same rules as <see cref="CreateSeriesAsync"/>, plus two: the row must
    /// already exist, and the slug-uniqueness check excludes the series being edited so re-saving it
    /// unchanged does not renumber its own slug. <c>CreatedOn</c> is left alone; only <c>UpdatedOn</c>
    /// is restamped. Only the update statement is wrapped in <c>try</c> — a failure of the existence
    /// read or a uniqueness check propagates, exactly as in the synchronous twin.</para>
    /// <para><b>Flow:</b> null, id and name guards → await the existence check → derive slug if absent
    /// → await the collision checks against every <i>other</i> series → stamp <c>UpdatedOn</c> → await
    /// the update.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogSeries</c> row and mutates the caller's object.
    /// <b>Changing the slug breaks the published <c>/series/{old-slug}</c> URL</b>; no redirect is
    /// written.</para>
    /// </remarks>
    /// <param name="series">The series carrying updated values; its <c>Slug</c> may be rewritten.</param>
    /// <param name="cancellationToken">Cancels the existence check, the uniqueness checks and the
    /// update. Cancellation faults the returned task rather than producing a failed <c>Result</c>.</param>
    /// <returns>Success carrying the saved series, or a failure naming the problem.</returns>
    public async Task<Result<BlogSeries>> UpdateSeriesAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (series.SeriesId <= 0)
            return Result<BlogSeries>.Failure("Invalid series ID");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Check if series exists
        var existing = await seriesRepo.GetSingleAsync(series.SeriesId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result<BlogSeries>.Failure("Series not found");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        series.Slug = SlugGenerator.EnsureSlug(series.Slug, series.Name, SlugPrefix, series.SeriesId);
        series.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            series.Slug,
            candidate => seriesRepo.SlugExistsAsync(candidate, series.SeriesId, cancellationToken)).ConfigureAwait(false);

        series.UpdatedOn = DateTime.UtcNow;

        try
        {
            await seriesRepo.UpdateAsync(series, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Updated series '{Name}' with ID {SeriesId}", series.Name, series.SeriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update series ID {SeriesId}: {Name}", series.SeriesId, series.Name);
            return Result<BlogSeries>.Failure(UpdateFailureMessage);
        }
    }

    /// <summary>
    /// Creates or updates a series depending on whether it already has an identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets one admin form serve both add and edit; a non-positive
    /// <c>SeriesId</c> means "new".</para>
    /// <para><b>Flow:</b> null guard → delegate to <see cref="CreateSeries"/> or
    /// <see cref="UpdateSeries"/>.</para>
    /// <para><b>Side Effects:</b> Whatever the delegated method does — one row inserted or
    /// updated.</para>
    /// </remarks>
    /// <param name="series">The series to persist.</param>
    /// <returns>Success carrying the saved series, or a failure naming the problem.</returns>
    public Result<BlogSeries> SaveSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (series.SeriesId <= 0)
        {
            return CreateSeries(series);
        }
        else
        {
            return UpdateSeries(series);
        }
    }

    /// <summary>
    /// Creates or updates a series depending on whether it already has an identifier, without blocking
    /// the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SaveSeries"/>. Lets one admin form serve
    /// both add and edit; a non-positive <c>SeriesId</c> means "new". The null guard needs no I/O, so
    /// it is answered with an already-completed task rather than turning this member into a state
    /// machine.</para>
    /// <para><b>Flow:</b> null guard → return the task from
    /// <see cref="CreateSeriesAsync"/> or <see cref="UpdateSeriesAsync"/> directly, without awaiting
    /// it (async conversion pattern §4).</para>
    /// <para><b>Side Effects:</b> Whatever the delegated method does — one row inserted or
    /// updated.</para>
    /// </remarks>
    /// <param name="series">The series to persist.</param>
    /// <param name="cancellationToken">Cancels the delegated operation; passed straight through.</param>
    /// <returns>Success carrying the saved series, or a failure naming the problem.</returns>
    public Task<Result<BlogSeries>> SaveSeriesAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        if (series == null)
            return Task.FromResult(Result<BlogSeries>.Failure("Series cannot be null"));

        return series.SeriesId <= 0
            ? CreateSeriesAsync(series, cancellationToken)
            : UpdateSeriesAsync(series, cancellationToken);
    }

    /// <summary>
    /// Removes a series, leaving its posts intact but ungrouped.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The posts are detached <b>before</b> the series row is removed.
    /// Order matters twice over: it keeps a foreign key from refusing the delete, and it guarantees
    /// that if the second step fails the posts are already free rather than pointing at a row that
    /// is about to vanish. Nothing here deletes a post — losing the grouping must never lose the
    /// writing.</para>
    /// <para><b>Flow:</b> id guard → confirm existence → clear <c>SeriesId</c> from every member
    /// post → delete the series row.</para>
    /// <para><b>Side Effects:</b> Updates every member post (clearing its series reference) and
    /// deletes one <c>BlogSeries</c> row; logs the deletion. <b>Not transactional</b> — the two
    /// steps are separate calls, so a failure between them leaves the posts detached while the
    /// series survives. That state is recoverable (delete again), but the part numbers on the
    /// detached posts are not restored. Published <c>/series/{slug}</c> URLs become 404s.</para>
    /// </remarks>
    /// <param name="seriesId">Identifier of the series to remove.</param>
    /// <returns>Success, or a failure when the series is unknown or the delete failed.</returns>
    public Result DeleteSeries(long seriesId)
    {
        if (seriesId <= 0)
            return Result.Failure("Invalid series ID");

        var existing = seriesRepo.GetSingle(seriesId);
        if (existing == null)
            return Result.Failure("Series not found");

        try
        {
            // First remove series association from all posts
            postRepo.ClearSeriesFromPosts(seriesId);

            // Then delete the series
            seriesRepo.Delete(seriesId);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Deleted series ID {SeriesId}", seriesId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete series ID {SeriesId}", seriesId);
            return Result.Failure(DeleteFailureMessage);
        }
    }

    /// <summary>
    /// Removes a series, leaving its posts intact but ungrouped, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="DeleteSeries"/>, with the same guards and
    /// the same failure strings. The posts are detached <b>before</b> the series row is removed. Order
    /// matters twice over: it keeps a foreign key from refusing the delete, and it guarantees that if
    /// the second step fails the posts are already free rather than pointing at a row that is about to
    /// vanish. Nothing here deletes a post — losing the grouping must never lose the writing. The
    /// existence check sits outside the <c>try</c>, so a failure of that read propagates rather than
    /// becoming a "Failed to delete series" result, exactly as in the synchronous twin.</para>
    /// <para><b>Flow:</b> id guard → await the existence check → await clearing <c>SeriesId</c> from
    /// every member post → await the series row delete.</para>
    /// <para><b>Side Effects:</b> Updates every member post (clearing its series reference) and
    /// deletes one <c>BlogSeries</c> row; logs the deletion. <b>Not transactional</b> — the two steps
    /// are separate calls, so a failure <i>or a cancellation</i> between them leaves the posts detached
    /// while the series survives. That state is recoverable (delete again), but the part numbers on the
    /// detached posts are not restored. Published <c>/series/{slug}</c> URLs become 404s.</para>
    /// </remarks>
    /// <param name="seriesId">Identifier of the series to remove.</param>
    /// <param name="cancellationToken">Cancels the existence check, the detach and the delete. Because
    /// the two writes are not transactional, cancelling between them leaves the posts detached — see
    /// Side Effects.</param>
    /// <returns>Success, or a failure when the series is unknown or the delete failed.</returns>
    public async Task<Result> DeleteSeriesAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        if (seriesId <= 0)
            return Result.Failure("Invalid series ID");

        var existing = await seriesRepo.GetSingleAsync(seriesId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result.Failure("Series not found");

        try
        {
            // First remove series association from all posts
            await postRepo.ClearSeriesFromPostsAsync(seriesId, cancellationToken).ConfigureAwait(false);

            // Then delete the series
            await seriesRepo.DeleteAsync(seriesId, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Deleted series ID {SeriesId}", seriesId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete series ID {SeriesId}", seriesId);
            return Result.Failure(DeleteFailureMessage);
        }
    }

    /// <summary>
    /// Builds the "Part N of M, previous / next" strip for a post inside a series.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only <b>published</b> parts are considered, and that single
    /// filter carries three consequences worth stating: a draft part is never offered as a next
    /// link (which would 404 or leak); <c>TotalParts</c> is the number a reader can actually open,
    /// not the number that exist; and previous/next skip over an unpublished middle part rather
    /// than dead-ending on it. Ordering is by part number, so navigation follows the author's
    /// sequence rather than publication dates.</para>
    /// <para><b>Flow:</b> load the post → return null when it belongs to no series → load the
    /// series → load its published parts in order → locate the current post → project the
    /// neighbours.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Costs three reads, so call it once per page
    /// render rather than per component.</para>
    /// <para><b>Null is the "no strip" signal</b> and covers four distinct cases: the post does not
    /// exist, it belongs to no series, its series row has gone, or the post itself is unpublished
    /// and so absent from the published list. A caller only has to check for null once.</para>
    /// </remarks>
    /// <param name="postId">The post currently being viewed.</param>
    /// <returns>
    /// The navigation model, or null when the post is not part of a navigable series (see above).
    /// </returns>
    public SeriesNavigation? GetSeriesNavigation(long postId)
    {
        try
        {
            var post = postRepo.GetSingle(postId);
            if (post?.SeriesId == null)
                return null;

            var series = seriesRepo.GetSingle(post.SeriesId.Value);
            if (series == null)
                return null;

            var seriesPosts = postRepo.GetPostsBySeries(post.SeriesId.Value)
                .Where(p => p.Published)
                .OrderBy(p => p.SeriesPartNumber)
                .ToList();

            var currentIndex = seriesPosts.FindIndex(p => p.PostID == postId);
            if (currentIndex < 0)
                return null;

            return new SeriesNavigation
            {
                SeriesName = series.Name,
                SeriesSlug = series.Slug,
                CurrentPart = post.SeriesPartNumber ?? 0,
                TotalParts = seriesPosts.Count,
                PreviousPost = currentIndex > 0 ? seriesPosts[currentIndex - 1] : null,
                NextPost = currentIndex < seriesPosts.Count - 1 ? seriesPosts[currentIndex + 1] : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series navigation for post ID: {PostId}", postId);
            return null;
        }
    }

    /// <summary>
    /// Builds the "Part N of M, previous / next" strip for a post inside a series, without blocking the
    /// calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSeriesNavigation"/> and identical to it
    /// filter for filter. Only <b>published</b> parts are considered, and that single filter carries
    /// three consequences worth stating: a draft part is never offered as a next link (which would 404
    /// or leak); <c>TotalParts</c> is the number a reader can actually open, not the number that exist;
    /// and previous/next skip over an unpublished middle part rather than dead-ending on it. The
    /// filtering is done in memory over <c>GetPostsBySeriesAsync</c> — the same read and the same
    /// in-memory <c>Published</c> predicate the synchronous twin uses, deliberately kept rather than
    /// switched to the SQL-filtered <c>GetPublishedPostsBySeriesAsync</c>, so the pair cannot diverge
    /// before stage 4. Ordering is by part number, so navigation follows the author's sequence rather
    /// than publication dates.</para>
    /// <para><b>Flow:</b> await the post → return null when it belongs to no series → await the series
    /// → await its parts, filter to published and order by part number → locate the current post →
    /// project the neighbours.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Costs three reads, so call it once per page
    /// render rather than per component.</para>
    /// <para><b>Null is the "no strip" signal</b> and covers four distinct cases: the post does not
    /// exist, it belongs to no series, its series row has gone, or the post itself is unpublished and
    /// so absent from the published list. A caller only has to check for null once.</para>
    /// </remarks>
    /// <param name="postId">The post currently being viewed.</param>
    /// <param name="cancellationToken">Cancels the three queries; a cancellation is caught with every
    /// other failure and yields <c>null</c>.</param>
    /// <returns>
    /// The navigation model, or null when the post is not part of a navigable series (see above).
    /// </returns>
    public async Task<SeriesNavigation?> GetSeriesNavigationAsync(long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            var post = await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
            if (post?.SeriesId == null)
                return null;

            var series = await seriesRepo.GetSingleAsync(post.SeriesId.Value, cancellationToken).ConfigureAwait(false);
            if (series == null)
                return null;

            var allSeriesPosts = await postRepo
                .GetPostsBySeriesAsync(post.SeriesId.Value, cancellationToken)
                .ConfigureAwait(false);

            var seriesPosts = allSeriesPosts
                .Where(p => p.Published)
                .OrderBy(p => p.SeriesPartNumber)
                .ToList();

            var currentIndex = seriesPosts.FindIndex(p => p.PostID == postId);
            if (currentIndex < 0)
                return null;

            return new SeriesNavigation
            {
                SeriesName = series.Name,
                SeriesSlug = series.Slug,
                CurrentPart = post.SeriesPartNumber ?? 0,
                TotalParts = seriesPosts.Count,
                PreviousPost = currentIndex > 0 ? seriesPosts[currentIndex - 1] : null,
                NextPost = currentIndex < seriesPosts.Count - 1 ? seriesPosts[currentIndex + 1] : null
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting series navigation for post ID: {PostId}", postId);
            return null;
        }
    }
}
