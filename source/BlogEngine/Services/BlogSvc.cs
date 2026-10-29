using BlogEngine.Common;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for blog post operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the rules that govern a post's life: what makes it valid, how it gets
/// a unique slug (its public URL), and the four states it can occupy — draft, scheduled, published
/// and soft-deleted. Every state transition on this class routes through <see cref="SavePostAsync"/>, so
/// the validation and slug rules apply uniformly however a post is published.</para>
///
/// <para><b>Authorization — this service enforces none.</b> With the single exception of
/// <see cref="GetAllPostsAsync"/>, which narrows its query when the caller says the user is not an
/// Admin or Editor, no member checks who is asking. <see cref="UpdatePostAsync"/>,
/// <see cref="DeletePostAsync"/>, <see cref="PublishPostAsync"/> and their neighbours act on any identifier
/// they are handed. The role and ownership checks live in the pages under
/// <c>BlogUI/Pages/AdminPages</c>, which are gated by the authorization policies; a caller must
/// have made those checks <i>before</i> reaching this class.</para>
///
/// <para><b>Failure convention:</b> reads degrade — a failed query is logged and answered with an
/// empty sequence, <c>null</c> or zero, so a database hiccup blanks a section instead of breaking a
/// public page. Writes report — an invalid request comes back as a failed <c>Result</c> carrying a
/// caller-safe message, and an unexpected persistence error is logged and converted into one.
/// Nothing here throws for an expected outcome.</para>
///
/// <para><b>Exception text never reaches the caller (REQ-NFR-031).</b> Every <c>catch</c> logs the
/// exception through <see cref="ILogger{TCategoryName}"/> and then returns one of the curated
/// constants at the top of this class. The detail is recoverable from the log, where the host's
/// <c>CorrelationIdMiddleware</c> has already attached the request's correlation id to every event
/// (REQ-NFR-015) — so an operator can tie a user's report to the exact stack trace without the
/// message itself disclosing a SQL fragment, a table name or a connection string. Do not reintroduce
/// <c>ex.Message</c> into a <c>Result</c>: these services are admin-gated today but nothing in the
/// class enforces that, and the disclosure would go live the moment one is reached anonymously.</para>
///
/// <para><b>Code Flow:</b> a page calls an <c>…Async</c> member → the member validates and generates
/// slugs → <c>IBlogPostRepo</c> performs the I/O asynchronously → an expected failure comes back as
/// <c>Result</c>/<c>Result&lt;BlogPost&gt;</c> and an unexpected one is logged and converted to one.</para>
///
/// <para><b>Dependencies:</b> IBlogPostRepo for data access, SlugGenerator for URL slugs,
/// <see cref="ICacheService"/> for the public listing cache.</para>
///
/// <para><b>Caching (REQ-NFR-018) — the public reads only.</b> Six reads go through
/// <see cref="ICacheService"/> under <c>CacheTags.Content</c>, with their asynchronous twins:
/// <see cref="GetPublishedPostsAsync"/>, <see cref="GetFeaturedPostAsync"/>,
/// <see cref="GetPublishedPostCountAsync"/>, <see cref="GetPostsByCategoryAsync"/>,
/// <see cref="GetPostCountByCategoryAsync"/> and <see cref="GetPostBySlugAsync"/>. Every one of them returns
/// the same rows to every visitor, which is the property that makes them safe to share from a
/// process-wide cache.</para>
///
/// <para><b>What is deliberately NOT cached, and why each one would be a defect.</b>
/// <see cref="GetAllPostsAsync"/> varies by user and privilege — caching it under a key that does not
/// name the principal would serve one author's unpublished drafts to the next caller.
/// <see cref="GetScheduledPostsAsync"/> and <see cref="GetDueScheduledPostsAsync"/> drive
/// <c>ScheduledPostPublisher</c>, which must see the current state of the queue rather than a
/// snapshot up to ten minutes old. <see cref="SearchPostsAsync"/> is keyed by whatever a visitor types,
/// so caching it hands an anonymous caller an unbounded lever on the cache's memory.
/// <see cref="GetSinglePostAsync"/> is the admin editor's read, where a stale row would be saved back
/// over a newer one.</para>
///
/// <para><b>Invalidation contract:</b> every write path here — insert, update, soft delete, publish,
/// unpublish, quick publish and schedule cancellation — calls <see cref="ServiceCache.InvalidateContent"/> immediately after the row is
/// persisted, which evicts the content <i>and</i> taxonomy tags (a published post changes every
/// category's and tag's post count). <c>SavePostAsync</c>, <c>SaveDraftAsync</c>, <c>PublishPostAsync</c> and
/// <c>SchedulePostAsync</c> inherit it by delegating to create or update. A new write path that skips
/// this leaves a published article invisible on the home page until the expiry lapses.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members; the synchronous twins were removed (REQ-NFR-026).</para>
///
/// <para><b>Async conversion (REQ-NFR-026, Group C):</b> this service fronts the busiest pages on the
/// site — <c>/</c>, <c>/post/{slug}</c>, the search results and the admin post list — so every member
/// here awaits rather than blocks. The <c>Result</c> pattern is unchanged by the conversion: a method
/// that returned <c>Result&lt;BlogPost&gt;</c> returns <c>Task&lt;Result&lt;BlogPost&gt;&gt;</c>, and
/// the <c>try/catch</c> that turns an unexpected exception into a failed <c>Result</c> keeps working
/// verbatim, because an awaited call throws at the <c>await</c> exactly as a blocking call throws at
/// the call.</para>
/// </remarks>
public class BlogSvc
{
    /// <summary>
    /// Prefix used to build an identifier-based slug when a title yields no slug at all.
    /// </summary>
    /// <remarks>
    /// Feeds <c>SlugGenerator.EnsureSlug</c>, which turns it into <c>post-42</c> for a post that
    /// already has an id, or <c>post-{title digest}</c> for one that has not been inserted yet
    /// (REQ-FN-054).
    /// </remarks>
    private const string SlugPrefix = "post";

    /// <summary>
    /// Message returned when a persistence failure has been logged but must not be described to the
    /// caller.
    /// </summary>
    /// <remarks>
    /// REQ-NFR-031: <c>Result.Failure</c>'s own contract forbids leaking internal detail, so the
    /// exception text stays in the log — where the host's <c>CorrelationIdMiddleware</c> has already
    /// stamped the request's correlation id onto every event (REQ-NFR-015) — and the caller sees only
    /// this curated sentence.
    /// </remarks>
    private const string CreateFailureMessage = "Failed to create post. Please try again later.";

    /// <summary>Curated message for an update that could not be persisted. See <see cref="CreateFailureMessage"/>.</summary>
    private const string UpdateFailureMessage = "Failed to update post. Please try again later.";

    /// <summary>Curated message for a delete that could not be persisted. See <see cref="CreateFailureMessage"/>.</summary>
    private const string DeleteFailureMessage = "Failed to delete post. Please try again later.";

    /// <summary>Curated message for a publish that could not be persisted. See <see cref="CreateFailureMessage"/>.</summary>
    private const string PublishFailureMessage = "Failed to publish post. Please try again later.";

    /// <summary>Curated message for an unpublish that could not be persisted. See <see cref="CreateFailureMessage"/>.</summary>
    private const string UnpublishFailureMessage = "Failed to unpublish post. Please try again later.";

    /// <summary>Curated message for a schedule cancellation that could not be persisted. See <see cref="CreateFailureMessage"/>.</summary>
    private const string CancelScheduleFailureMessage = "Failed to cancel schedule. Please try again later.";

    /// <summary>
    /// Message returned when a state transition is refused because the post is soft-deleted.
    /// </summary>
    /// <remarks>
    /// REQ-FN-055: <c>DeletePost</c> already refused to act twice on a deleted row, but the publish,
    /// unpublish and cancel-schedule transitions did not check at all, so a deleted post could be put
    /// back into the Published state and would then be counted and listed as live.
    /// </remarks>
    private const string DeletedPostMessage = "Post is deleted";

    /// <summary>
    /// Message returned when publication is refused because the post is soft-deleted.
    /// </summary>
    /// <remarks>REQ-FN-055 — the publish-side wording of <see cref="DeletedPostMessage"/>.</remarks>
    private const string DeletedPostPublishMessage = "Post is deleted and cannot be published";

    private readonly IBlogPostRepo postRepo;
    private readonly ILogger<BlogSvc> logger;
    private readonly ICacheService? cacheService;

    /// <summary>
    /// Initialises the blog post service.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Pure wiring — the service holds no state of its own beyond
    /// these two dependencies, which is what makes it safe to register transient.</para>
    /// <para><b>Flow:</b> assign and return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postRepo">Blog post data access.</param>
    /// <param name="logger">Logger for query and persistence failures.</param>
    /// <param name="cacheService">
    /// Listing cache (REQ-NFR-018). Optional: omitting it makes every read go to the database, which
    /// is what a unit test that is not exercising caching wants. The host always supplies it — it is
    /// a registered singleton — so the uncached path never runs in the application.
    /// </param>
    public BlogSvc(
        IBlogPostRepo postRepo,
        ILogger<BlogSvc> logger,
        ICacheService? cacheService = null)
    {
        this.postRepo = postRepo;
        this.logger = logger;
        this.cacheService = cacheService;
    }

    // =================================================================================================
    // Async surface — REQ-NFR-026. The only surface.
    // =================================================================================================

    /// <summary>
    /// Gets the posts a user may see, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Row scoping is decided here, not in the page: an admin or editor
    /// sees every post, anyone else sees only their own. Doing it server-side means a page cannot leak
    /// another author's drafts by forgetting to filter. A failed read degrades to an empty sequence so
    /// the grid renders empty rather than taking the page down.</para>
    /// <para><b>Flow:</b> branch on the privilege flag → await the matching repository query → log and
    /// degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="userId">The signed-in user's identifier, used when scoping to one author.</param>
    /// <param name="isAdmin">True when the user has admin or editor privileges.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The posts visible to the user, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetAllPostsAsync(long userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        try
        {
            return isAdmin
                ? await postRepo.GetAllAsync(cancellationToken).ConfigureAwait(false)
                : await postRepo.GetAllByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for user {UserId}, isAdmin: {IsAdmin}", userId, isAdmin);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets a single post by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both "no such post" and "the lookup failed" surface as <c>null</c>;
    /// the failure case is distinguished in the log, not in the return value.</para>
    /// <para><b>Flow:</b> await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post if found, <c>null</c> otherwise.</returns>
    public async Task<BlogPost?> GetSinglePostAsync(long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post by ID: {PostId}", postId);
            return null;
        }
    }

    /// <summary>
    /// Gets a post by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank slug never reaches the database — it can only come from a
    /// malformed route, and the answer is the same <c>null</c> an unknown slug produces, which is what
    /// makes <c>/post/{slug}</c> render its not-found state instead of throwing.</para>
    /// <para><b>Flow:</b> guard the slug → await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post if found, <c>null</c> otherwise.</returns>
    public async Task<BlogPost?> GetPostBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.PostBySlugKey(slug)),
                CacheTags.Content,
                () => postRepo.GetBySlugAsync(slug, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Gets a page of published posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Degrades to an empty sequence on failure so the home page's
    /// latest-articles strip renders empty rather than erroring the whole landing page.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<BlogPost>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.PublishedPostsKey(pageSize, offset)),
                CacheTags.Content,
                () => postRepo.GetPublishedPostsAsync(pageSize, offset, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting published posts. PageSize: {PageSize}, Offset: {Offset}", pageSize, offset);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the dashboard's post-count statistics, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failure yields a zero-count carrier rather than <c>null</c>, so
    /// the dashboard tile shows "0" instead of needing a null branch of its own.</para>
    /// <para><b>Flow:</b> await the repository → log and return a zero carrier on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A post carrying the count statistics.</returns>
    public async Task<BlogPost?> GetBlogCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetTheCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting blog counts");
            return new BlogPost { BlogCount = 0 };
        }
    }

    /// <summary>
    /// Gets the most recent published post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both "nothing published yet" and "the lookup failed" surface as
    /// <c>null</c>, which the home page renders as the absence of a featured card.</para>
    /// <para><b>Flow:</b> await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The newest published post, or <c>null</c>.</returns>
    public async Task<BlogPost?> GetFeaturedPostAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.FeaturedPostKey),
                CacheTags.Content,
                () => postRepo.GetFeaturedPostAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting featured post");
            return null;
        }
    }

    /// <summary>
    /// Gets the total number of published posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failure returns <c>0</c>, which collapses the pager rather than
    /// offering pages that cannot be loaded.</para>
    /// <para><b>Flow:</b> await the repository → log and return zero on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published, non-deleted posts.</returns>
    public async Task<int> GetPublishedPostCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.PublishedPostCountKey),
                CacheTags.Content,
                () => postRepo.GetPublishedPostCountAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting published post count");
            return 0;
        }
    }

    /// <summary>
    /// Gets the published posts related to a post, without blocking the calling thread (REQ-FN-020).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Related means sharing the post's category or a tag; the post itself
    /// and any draft are never returned. A post with no category and no tags has no related posts.</para>
    /// <para><b>Flow:</b> map the post's category (0 means none) → cached repository read → log and
    /// degrade to empty on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="post">The subject post.</param>
    /// <param name="take">Maximum number of related posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Related published posts, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetRelatedPostsAsync(BlogPost post, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        long? categoryId = post.CategoryId > 0 ? post.CategoryId : null;
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<BlogPost>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.RelatedPostsKey(post.PostID, categoryId, take)),
                CacheTags.Content,
                () => postRepo.GetRelatedPostsAsync(post.PostID, categoryId, take, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting related posts for {PostId}", post.PostID);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets a page of published posts in one category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same degrade-to-empty policy as
    /// <see cref="GetPublishedPostsAsync"/>.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category to filter by.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts in the category, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsByCategoryAsync(long categoryId, int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<BlogPost>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.PostsByCategoryKey(categoryId, pageSize, offset)),
                CacheTags.Content,
                () => postRepo.GetPostsByCategoryAsync(categoryId, pageSize, offset, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts by category {CategoryId}", categoryId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the number of published posts in one category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failure returns <c>0</c> so the category page's pager collapses
    /// rather than offering pages that cannot be loaded.</para>
    /// <para><b>Flow:</b> await the repository → log and return zero on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts in the category.</returns>
    public async Task<int> GetPostCountByCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.PostCountByCategoryKey(categoryId)),
                CacheTags.Content,
                () => postRepo.GetPostCountByCategoryAsync(categoryId, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post count for category {CategoryId}", categoryId);
            return 0;
        }
    }

    /// <summary>
    /// Checks the post's free-text fields against their database column widths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> UAT-023 mechanism A. <c>Title</c>/<c>Abstract</c>/<c>Tags</c>/
    /// <c>FeaturedImage</c> are all <c>VARCHAR(550)</c> and nothing validated their length before this
    /// existed — an over-length value reached Npgsql, failed with a raw <c>22001</c>, and
    /// <see cref="CreatePostAsync"/>/<see cref="UpdatePostAsync"/> reported it back as the generic
    /// <see cref="CreateFailureMessage"/>/<see cref="UpdateFailureMessage"/>, silently discarding the
    /// author's edit without saying why. Naming the field and its limit here — before any repository
    /// call — lets every caller of this service, including <c>BlogApp</c> which shares it, surface a
    /// message the author can act on. A unit test would have caught the original defect by asserting
    /// this method (or the editor's own <c>MaxLength</c>) rejects a 551-character abstract.</para>
    /// <para><b>Flow:</b> compare each field's length to its column width in declaration order and
    /// return the first violation found.</para>
    /// </remarks>
    /// <param name="post">The post about to be inserted or updated.</param>
    /// <returns>A failed <see cref="Result{T}"/> naming the offending field and its limit, or
    /// <c>null</c> when every field fits its column.</returns>
    private static Result<BlogPost>? ValidateFieldLengths(BlogPost post)
    {
        var title = post.Title ?? string.Empty;
        if (title.Length > BlogPost.TitleMaxLength)
            return Result<BlogPost>.Failure($"The title is {title.Length} characters; the maximum is {BlogPost.TitleMaxLength}.");

        var abstractText = post.Abstract ?? string.Empty;
        if (abstractText.Length > BlogPost.AbstractMaxLength)
            return Result<BlogPost>.Failure($"The abstract is {abstractText.Length} characters; the maximum is {BlogPost.AbstractMaxLength}.");

        var tags = post.Tags ?? string.Empty;
        if (tags.Length > BlogPost.TagsMaxLength)
            return Result<BlogPost>.Failure($"The tags are {tags.Length} characters; the maximum is {BlogPost.TagsMaxLength}.");

        var featuredImage = post.FeaturedImage ?? string.Empty;
        if (featuredImage.Length > BlogPost.FeaturedImageMaxLength)
            return Result<BlogPost>.Failure($"The featured image path is {featuredImage.Length} characters; the maximum is {BlogPost.FeaturedImageMaxLength}.");

        return null;
    }

    /// <summary>
    /// Creates a new blog post with validation and slug generation, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing slug is derived from the title, and a slug already in use
    /// is suffixed until it is free — capped at 100 attempts so a pathological collision cannot spin
    /// forever. The creation timestamp is stamped here rather than in the page, so every post carries
    /// one regardless of which editor created it. Validation failures are expected outcomes and come
    /// back as a failed <c>Result</c>, not as exceptions.</para>
    /// <para><b>Flow:</b> validate → generate slug → resolve collisions → stamp timestamps → await
    /// insert → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Adds one row; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="post">The post to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result with the created post on success, error message on failure.</returns>
    public async Task<Result<BlogPost>> CreatePostAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        var lengthFailure = ValidateFieldLengths(post);
        if (lengthFailure != null)
            return lengthFailure;

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        post.Slug = SlugGenerator.EnsureSlug(post.Slug, post.Title, SlugPrefix);
        post.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            post.Slug,
            candidate => postRepo.SlugExistsAsync(candidate, 0, cancellationToken)).ConfigureAwait(false);

        post.CreatedOn = DateTime.UtcNow;
        post.IsDeleted = false;

        try
        {
            var postId = await postRepo.InsertToGetIdAsync(post, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            post.PostID = postId;
            logger.LogInformation("Created post '{Title}' with ID {PostId}", post.Title, postId);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post: {Title}", post.Title);
            return Result<BlogPost>.Failure(CreateFailureMessage);
        }
    }

    /// <summary>
    /// Updates an existing blog post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is confirmed to exist before anything is written, so an
    /// edit of a post deleted in another tab reports "not found" instead of silently updating nothing.
    /// Slug collisions exclude the row being edited.</para>
    /// <para><b>Flow:</b> validate → confirm existence → resolve slug → stamp <c>UpdatedOn</c> → await
    /// update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="post">The post to update.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result<BlogPost>> UpdatePostAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (post.PostID <= 0)
            return Result<BlogPost>.Failure("Invalid post ID");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        var lengthFailure = ValidateFieldLengths(post);
        if (lengthFailure != null)
            return lengthFailure;

        var existing = await postRepo.GetSingleAsync(post.PostID, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result<BlogPost>.Failure("Post not found");

        // REQ-FN-055: guard kept from the removed synchronous twin — a soft-deleted row may not be
        // transitioned back into the Published state by any route.
        if (existing.IsDeleted && post.Published)
            return Result<BlogPost>.Failure(DeletedPostPublishMessage);

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        post.Slug = SlugGenerator.EnsureSlug(post.Slug, post.Title, SlugPrefix, post.PostID);
        post.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            post.Slug,
            candidate => postRepo.SlugExistsAsync(candidate, post.PostID, cancellationToken)).ConfigureAwait(false);

        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            logger.LogInformation("Updated post '{Title}' with ID {PostId}", post.Title, post.PostID);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update post ID {PostId}: {Title}", post.PostID, post.Title);
            return Result<BlogPost>.Failure(UpdateFailureMessage);
        }
    }

    /// <summary>
    /// Saves a post — insert or update based on PostID — without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A non-positive identifier means the post has never been persisted,
    /// so the editor can bind one method to its save button regardless of mode.</para>
    /// <para><b>Flow:</b> inspect the key → delegate to create or update.</para>
    /// <para><b>Side Effects:</b> Those of the delegated method.</para>
    /// </remarks>
    /// <param name="post">The post to save.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Task<Result<BlogPost>> SavePostAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Task.FromResult(Result<BlogPost>.Failure("Post cannot be null"));

        return post.PostID <= 0
            ? CreatePostAsync(post, cancellationToken)
            : UpdatePostAsync(post, cancellationToken);
    }

    /// <summary>
    /// Soft-deletes a blog post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Existence is confirmed first so the caller can report "not found"
    /// rather than a success that removed nothing, and an already-deleted post is rejected rather than
    /// re-stamped — a second delete would overwrite the original deletion time.</para>
    /// <para><b>Flow:</b> validate → confirm existence and state → await soft delete → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Marks one row deleted; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="postId">ID of the post to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result> DeletePostAsync(long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var existing = await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result.Failure("Post not found");

        if (existing.IsDeleted)
            return Result.Failure("Post is already deleted");

        try
        {
            await postRepo.SoftDeleteAsync(postId, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            logger.LogInformation("Deleted post ID {PostId}", postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete post ID {PostId}", postId);
            return Result.Failure(DeleteFailureMessage);
        }
    }

    /// <summary>
    /// Saves a post as an unpublished draft, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Clearing <c>Published</c> is what makes this a draft save rather
    /// than a publish; <c>PublishedOn</c> is left alone so a previously published post that is being
    /// reworked keeps its original publication date.</para>
    /// <para><b>Flow:</b> clear the published flag → stamp <c>UpdatedOn</c> → delegate to <see cref="SavePostAsync"/>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated save.</para>
    /// </remarks>
    /// <param name="post">The post to save.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Task<Result<BlogPost>> SaveDraftAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Task.FromResult(Result<BlogPost>.Failure("Post cannot be null"));

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePostAsync(post, cancellationToken);
    }

    /// <summary>
    /// Publishes a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>PublishedOn</c> is set only on the first publish. Re-publishing
    /// after an edit must not move the date, or the article would keep jumping back to the top of a
    /// date-ordered listing every time a typo was fixed.</para>
    /// <para><b>Flow:</b> set the published flag → stamp the first publication date if absent →
    /// delegate to <see cref="SavePostAsync"/>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated save; the post becomes publicly visible.</para>
    /// </remarks>
    /// <param name="post">The post to publish.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Task<Result<BlogPost>> PublishPostAsync(BlogPost post, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Task.FromResult(Result<BlogPost>.Failure("Post cannot be null"));

        // REQ-FN-055: guard kept from the removed synchronous twin.
        if (post.IsDeleted)
            return Task.FromResult(Result<BlogPost>.Failure(DeletedPostPublishMessage));

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;

        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        return SavePostAsync(post, cancellationToken);
    }

    /// <summary>
    /// Unpublishes a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>PublishedOn</c> is deliberately kept, so re-publishing later
    /// restores the original date rather than presenting an old article as new. An already-unpublished
    /// post is rejected so the caller can tell a no-op apart from a success.</para>
    /// <para><b>Flow:</b> validate → load the post → verify it is published → await update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row; the post disappears from every public query.</para>
    /// </remarks>
    /// <param name="postId">ID of the post to unpublish.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result> UnpublishPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post == null)
            return Result.Failure("Post not found");

        // REQ-FN-055: guard and ordering kept from the removed synchronous twin.
        if (post.IsDeleted)
            return Result.Failure(DeletedPostMessage);

        if (!post.Published)
            return Result.Failure("Post is already unpublished");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unpublish post ID {PostId}", postId);
            return Result.Failure(UnpublishFailureMessage);
        }
    }

    /// <summary>
    /// Publishes a post by ID straight from a listing, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Any pending schedule is cleared, because publishing now makes a
    /// future publication time meaningless and would otherwise leave the row looking scheduled while
    /// being live. As with <see cref="PublishPostAsync"/>, the first publication date is preserved.</para>
    /// <para><b>Flow:</b> validate → load the post → verify it is not already published → set flags →
    /// await update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row; the post becomes publicly visible.</para>
    /// </remarks>
    /// <param name="postId">ID of the post to publish.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result> QuickPublishAsync(long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post == null)
            return Result.Failure("Post not found");

        // REQ-FN-055: guard and ordering kept from the removed synchronous twin.
        if (post.IsDeleted)
            return Result.Failure(DeletedPostPublishMessage);

        if (post.Published)
            return Result.Failure("Post is already published");

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;
        post.ScheduledPublishOn = null;
        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish post ID {PostId}", postId);
            return Result.Failure(PublishFailureMessage);
        }
    }

    /// <summary>
    /// Gets every scheduled post for the admin view, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same degrade-to-empty policy as the other listing reads.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts scheduled for future publication, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetScheduledPostsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets posts whose scheduled publish time has arrived, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The current instant is taken here rather than by the caller, so
    /// every cycle of the background publisher compares against a fresh clock. Degrading to an empty
    /// sequence on failure means one bad cycle skips publication instead of stopping the hosted
    /// service.</para>
    /// <para><b>Flow:</b> read the clock → await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts ready to be published, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<BlogPost>> GetDueScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetDueScheduledPostsAsync(DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting due scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Schedules a post for future publication, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A time in the past is rejected rather than accepted and published
    /// immediately, because the two are different intentions and silently doing the second would
    /// surprise an author who mistyped a date. The post is forced unpublished so the scheduler, not
    /// the save, decides when it goes live.</para>
    /// <para><b>Flow:</b> validate the time → set the schedule and clear the published flag →
    /// delegate to <see cref="SavePostAsync"/>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated save.</para>
    /// </remarks>
    /// <param name="post">The post to schedule.</param>
    /// <param name="scheduledUtc">UTC time when the post should be published.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Task<Result<BlogPost>> SchedulePostAsync(BlogPost post, DateTime scheduledUtc, CancellationToken cancellationToken = default)
    {
        if (post == null)
            return Task.FromResult(Result<BlogPost>.Failure("Post cannot be null"));

        if (scheduledUtc <= DateTime.UtcNow)
            return Task.FromResult(Result<BlogPost>.Failure("Scheduled time must be in the future"));

        post.ScheduledPublishOn = scheduledUtc;
        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePostAsync(post, cancellationToken);
    }

    /// <summary>
    /// Cancels a post's schedule, reverting it to a draft, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A post that carries no schedule is rejected so the caller can tell
    /// a no-op apart from a success. Clearing the schedule leaves the post unpublished — it becomes a
    /// plain draft rather than going live.</para>
    /// <para><b>Flow:</b> validate → load the post → verify it is scheduled → clear the schedule →
    /// await update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row; the post leaves the Scheduled tab.</para>
    /// </remarks>
    /// <param name="postId">ID of the post whose schedule is cancelled.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result> CancelScheduleAsync(long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = await postRepo.GetSingleAsync(postId, cancellationToken).ConfigureAwait(false);
        if (post == null)
            return Result.Failure("Post not found");

        // REQ-FN-055: guard and ordering kept from the removed synchronous twin.
        if (post.IsDeleted)
            return Result.Failure(DeletedPostMessage);

        if (!post.ScheduledPublishOn.HasValue)
            return Result.Failure("Post is not scheduled");

        post.ScheduledPublishOn = null;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateContent(cacheService);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel schedule for post ID {PostId}", postId);
            return Result.Failure(CancelScheduleFailureMessage);
        }
    }

    /// <summary>
    /// Searches published posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same degrade-to-empty policy as the other listing reads, so a
    /// failed search renders "no results" rather than an error page.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching published posts, or an empty sequence.</returns>
    public async Task<IEnumerable<BlogPost>> SearchPostsAsync(string query, int pageSize = 10, int offset = 0, CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.SearchPostsAsync(query, pageSize, offset, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching posts with query: {Query}", query);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the number of posts matching a search, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failure returns <c>0</c>, collapsing the search pager rather than
    /// offering pages that cannot be loaded.</para>
    /// <para><b>Flow:</b> await the repository → log and return zero on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of matching published posts.</returns>
    public async Task<int> GetSearchResultCountAsync(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            return await postRepo.GetSearchResultCountAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting search result count for: {Query}", query);
            return 0;
        }
    }
}
