using BlogEngine.Common;
using BlogModels;
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
/// converted. Note that mutation failures interpolate <c>ex.Message</c>, which is acceptable only
/// because every caller is an admin screen.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogSeriesRepo"/> and <see cref="IBlogPostRepo"/> for
/// data access, <c>SlugGenerator</c> for URL slugs, <see cref="ILogger{TCategoryName}"/> for
/// diagnostics.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Reads serve anonymous
/// public pages; every mutation is reached from an admin screen gated by
/// <c>AppPolicies.EditorOrAbove</c>. This class enforces <b>no</b> policy itself — the calling page
/// owns that check.</para>
/// </remarks>
public class SeriesSvc
{
    private readonly IBlogSeriesRepo seriesRepo;
    private readonly IBlogPostRepo postRepo;
    private readonly ILogger<SeriesSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesSvc"/> class.
    /// </summary>
    /// <param name="seriesRepo">Series data access.</param>
    /// <param name="postRepo">Post data access, used for membership and part numbering.</param>
    /// <param name="logger">Logger for series changes and read failures.</param>
    public SeriesSvc(IBlogSeriesRepo seriesRepo, IBlogPostRepo postRepo, ILogger<SeriesSvc> logger)
    {
        this.seriesRepo = seriesRepo;
        this.postRepo = postRepo;
        this.logger = logger;
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
            return seriesRepo.GetAll();
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
            return seriesRepo.GetAllWithCounts();
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
    /// Resolves the series behind a public <c>/series/{slug}</c> URL and loads its parts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Two reads — the header by slug, then its posts ordered by part
    /// number — combined so the page has everything it needs in one call.</para>
    /// <para><b>Flow:</b> blank guard → resolve by slug → load and attach the posts.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Mutates the returned object's <c>Posts</c>
    /// collection.</para>
    /// <para><b>The attached posts include unpublished parts — deliberately.</b>
    /// <c>IBlogPostRepo.GetPostsBySeries</c> filters only on soft-deletion, not on
    /// <c>Published</c>, and <c>SeriesView.razor</c> relies on that: a draft part renders as an
    /// unlinked "Coming Soon" row so a reader can see the series is unfinished. The consequence is
    /// that <b>a draft's title, abstract and featured image are visible to anonymous visitors</b>
    /// on this page — only its body and its URL are withheld. Do not put embargoed material in a
    /// series post's title or abstract, and do not "fix" the missing filter without also changing
    /// the page, which would silently drop the Coming Soon rows. Contrast
    /// <see cref="GetSeriesNavigation"/>, which <i>does</i> filter to published parts because a
    /// previous/next link must always be openable.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <returns>
    /// The series with <c>Posts</c> populated (published and draft parts alike), or null when the
    /// slug is blank, unknown, or the read failed.
    /// </returns>
    public BlogSeries? GetSeriesBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            var series = seriesRepo.GetBySlug(slug);
            if (series != null)
            {
                series.Posts = postRepo.GetPostsBySeries(series.SeriesId).ToList();
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

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateSlug(series.Name);
        }

        // Check for duplicate slug
        if (seriesRepo.SlugExists(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateUniqueSlug(series.Slug, 1);
            int counter = 2;
            while (seriesRepo.SlugExists(series.Slug) && counter < 100)
            {
                series.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(series.Name), counter);
                counter++;
            }
        }

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
            series.SeriesId = seriesId;
            logger.LogInformation("Created series '{Name}' with ID {SeriesId}", series.Name, seriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create series: {Name}", series.Name);
            return Result<BlogSeries>.Failure($"Failed to create series: {ex.Message}");
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

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateSlug(series.Name);
        }

        // Check for duplicate slug (exclude current series)
        if (seriesRepo.SlugExists(series.Slug, series.SeriesId))
        {
            series.Slug = SlugGenerator.GenerateUniqueSlug(series.Slug, 1);
            int counter = 2;
            while (seriesRepo.SlugExists(series.Slug, series.SeriesId) && counter < 100)
            {
                series.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(series.Name), counter);
                counter++;
            }
        }

        series.UpdatedOn = DateTime.UtcNow;

        try
        {
            seriesRepo.Update(series);
            logger.LogInformation("Updated series '{Name}' with ID {SeriesId}", series.Name, series.SeriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update series ID {SeriesId}: {Name}", series.SeriesId, series.Name);
            return Result<BlogSeries>.Failure($"Failed to update series: {ex.Message}");
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
            logger.LogInformation("Deleted series ID {SeriesId}", seriesId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete series ID {SeriesId}", seriesId);
            return Result.Failure($"Failed to delete series: {ex.Message}");
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
}
