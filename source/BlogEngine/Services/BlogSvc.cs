using BlogEngine.Common;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for blog post operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the rules that govern a post's life: what makes it valid, how it gets
/// a unique slug (its public URL), and the four states it can occupy — draft, scheduled, published
/// and soft-deleted. Every state transition on this class routes through <see cref="SavePost"/>, so
/// the validation and slug rules apply uniformly however a post is published.</para>
///
/// <para><b>Authorization — this service enforces none.</b> With the single exception of
/// <see cref="GetAllPosts"/>, which narrows its query when the caller says the user is not an
/// Admin or Editor, no member checks who is asking. <see cref="UpdatePost"/>,
/// <see cref="DeletePost"/>, <see cref="PublishPost"/> and their neighbours act on any identifier
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
/// <para><b>Code Flow:</b> a page calls an <c>…Async</c> member → the member validates and generates
/// slugs → <c>IBlogPostRepo</c> performs the I/O asynchronously → an expected failure comes back as
/// <c>Result</c>/<c>Result&lt;BlogPost&gt;</c> and an unexpected one is logged and converted to one.</para>
///
/// <para><b>Dependencies:</b> IBlogPostRepo for data access, SlugGenerator for URL slugs.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only while
/// the rest of the call sites migrate (REQ-NFR-026).</para>
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
    private readonly IBlogPostRepo postRepo;
    private readonly ILogger<BlogSvc> logger;

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
    public BlogSvc(IBlogPostRepo postRepo, ILogger<BlogSvc> logger)
    {
        this.postRepo = postRepo;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the posts one user is entitled to see in the admin list.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An administrator or editor sees every post; anyone else sees
    /// only their own. This is the query-level half of the authorship rule — an Author cannot list
    /// another Author's drafts because the rows are never fetched, not because the UI hides
    /// them.</para>
    /// <para><b>Flow:</b> branch on the privilege flag → read all rows or the caller's rows → log
    /// and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// <para><b>Authorization — the caller decides, this method obeys.</b>
    /// <paramref name="isAdmin"/> is taken on trust; nothing here consults the signed-in user. The
    /// caller must have resolved it from the authenticated principal (<c>BlogsList.razor.cs</c>
    /// derives it from the Admin/Editor roles) and must pass the same principal's id as
    /// <paramref name="userId"/>. Passing <c>true</c> from a page that has not made that check
    /// discloses every author's unpublished drafts.</para>
    /// </remarks>
    /// <param name="userId">Identifier of the signed-in user whose posts should be listed.</param>
    /// <param name="isAdmin">True when the caller has already established Admin or Editor rights.</param>
    /// <returns>The posts visible to that user, or an empty sequence on failure.</returns>
    public IEnumerable<BlogPost> GetAllPosts(long userId, bool isAdmin)
    {
        try
        {
            return isAdmin ? postRepo.GetAll() : postRepo.GetAllById(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for user {UserId}, isAdmin: {IsAdmin}", userId, isAdmin);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets a single post by identifier, in any state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the row whatever its state — draft, scheduled,
    /// unpublished or soft-deleted — because this is the admin and editor lookup. It is NOT a
    /// public-page read; use <see cref="GetPostBySlug"/> for that.</para>
    /// <para><b>Flow:</b> read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// <para><b>Authorization:</b> none is applied. A caller that renders the result to a visitor
    /// must check <c>Published</c> and <c>IsDeleted</c> itself, and a caller acting for an Author
    /// must confirm <c>UserID</c> matches.</para>
    /// </remarks>
    /// <param name="postId">Post identifier.</param>
    /// <returns>The post if found, <c>null</c> when it does not exist or the read failed.</returns>
    public BlogPost? GetSinglePost(long postId)
    {
        try
        {
            return postRepo.GetSingle(postId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post by ID: {PostId}", postId);
            return null;
        }
    }

    /// <summary>
    /// Gets a published post by its URL slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The public post page's entry point. A blank slug is answered
    /// <c>null</c> without a round trip — it can only come from a malformed route, and the answer
    /// is the same one an unknown slug gets, so an attacker learns nothing from the difference.</para>
    /// <para><b>Flow:</b> guard the slug → read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>The post if found, <c>null</c> otherwise.</returns>
    public BlogPost? GetPostBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            return postRepo.GetBySlug(slug);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Gets a page of published posts for public display.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The repository filters on published and not-deleted, so this is
    /// safe to render to anonymous visitors. Paging arguments are passed through unclamped — the
    /// caller owns the page size.</para>
    /// <para><b>Flow:</b> read the page → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>The requested page of published posts, or an empty sequence on failure.</returns>
    public IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset)
    {
        try
        {
            return postRepo.GetPublishedPosts(pageSize, offset);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting published posts. PageSize: {PageSize}, Offset: {Offset}", pageSize, offset);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the post count statistics shown on the admin dashboard.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The counts arrive on a <see cref="BlogPost"/> because the
    /// aggregate query reuses the post projection; only its count fields are meaningful, and the
    /// rest of the object must not be read. On failure a zeroed instance is returned so the
    /// dashboard tile renders "0" rather than the whole screen failing.</para>
    /// <para><b>Flow:</b> read the aggregate → log and return zeroes on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>A post-shaped carrier holding the count statistics; zeroed on failure.</returns>
    public BlogPost? GetBlogCounts()
    {
        try
        {
            return postRepo.GetTheCounts();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting blog counts");
            return new BlogPost { BlogCount = 0 };
        }
    }

    /// <summary>
    /// Gets the most recently published post, used as the home page's featured item.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Featured" is not an editorial flag — it simply means the newest
    /// published post. Publishing anything therefore changes what the home page leads with.</para>
    /// <para><b>Flow:</b> read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>The newest published post, or <c>null</c> when there is none or the read failed.</returns>
    public BlogPost? GetFeaturedPost()
    {
        try
        {
            return postRepo.GetFeaturedPost();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting featured post");
            return null;
        }
    }

    /// <summary>
    /// Gets the total number of published posts, for pager arithmetic.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts published, non-deleted posts only, so it matches what
    /// <see cref="GetPublishedPosts"/> pages over. A failure returns 0, which collapses the pager
    /// rather than throwing on a public page.</para>
    /// <para><b>Flow:</b> read the count → log and return 0 on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>The number of published, non-deleted posts; 0 on failure.</returns>
    public int GetPublishedPostCount()
    {
        try
        {
            return postRepo.GetPublishedPostCount();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting published post count");
            return 0;
        }
    }

    /// <summary>
    /// Gets a page of published posts within one category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Publication filtering happens in SQL alongside the category
    /// filter, so a draft in a category is never counted or shown.</para>
    /// <para><b>Flow:</b> read the page → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category to filter by.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>The requested page of published posts in that category, or an empty sequence.</returns>
    public IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset)
    {
        try
        {
            return postRepo.GetPostsByCategory(categoryId, pageSize, offset);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts by category {CategoryId}", categoryId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the number of published posts in one category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The counterpart to <see cref="GetPostsByCategory"/> and filtered
    /// identically, so the pager and the page agree.</para>
    /// <para><b>Flow:</b> read the count → log and return 0 on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category to count.</param>
    /// <returns>The number of published posts in the category; 0 on failure.</returns>
    public int GetPostCountByCategory(long categoryId)
    {
        try
        {
            return postRepo.GetPostCountByCategory(categoryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post count for category {CategoryId}", categoryId);
            return 0;
        }
    }

    /// <summary>
    /// Creates a new blog post, deriving a unique slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Title and content are mandatory. A missing slug is derived from
    /// the title, and a slug already in use is suffixed until it is free — capped at 100 attempts so
    /// a pathological collision cannot spin forever. <c>CreatedOn</c> is stamped in UTC here rather
    /// than trusted from the caller, and <c>IsDeleted</c> is forced false so a recycled object
    /// cannot be inserted pre-deleted. Validation failures are expected outcomes and come back as a
    /// failed <c>Result</c>; only an unexpected persistence error is caught, logged and converted.</para>
    /// <para><b>Flow:</b> validate → derive slug → resolve collisions → stamp timestamps →
    /// insert → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogPost</c> row and assigns its generated
    /// <c>PostID</c> back onto <paramref name="post"/>, which is mutated in place. Writes an
    /// information or error log entry. <b>Publication state is whatever the caller set</b> — this
    /// method does not force a draft, so passing a post with <c>Published = true</c> makes it
    /// public immediately.</para>
    /// <para><b>Slug collision race:</b> the uniqueness check and the insert are separate
    /// statements, so two simultaneous creations of the same title can both pass the check. The
    /// database constraint is the real guard; the loser surfaces as a failed <c>Result</c>.</para>
    /// </remarks>
    /// <param name="post">The post to create; mutated with its generated id and slug.</param>
    /// <returns>The created post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> CreatePost(BlogPost post)
    {
        // Validate required fields
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        // Handle duplicate slug by appending timestamp
        if (postRepo.SlugExists(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            // Keep checking until we find a unique slug
            int counter = 2;
            while (postRepo.SlugExists(post.Slug) && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        // Set timestamps
        post.CreatedOn = DateTime.UtcNow;
        post.IsDeleted = false;

        try
        {
            var postId = postRepo.InsertToGetId(post);
            post.PostID = postId;
            logger.LogInformation("Created post '{Title}' with ID {PostId}", post.Title, postId);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post: {Title}", post.Title);
            return Result<BlogPost>.Failure($"Failed to create post: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing blog post, keeping its slug unique.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is confirmed to exist before anything is written, so an
    /// edit of a post deleted in another tab reports "Post not found" instead of a success that
    /// updated nothing. Slug collision resolution excludes the row being edited, so re-saving a post
    /// without changing its title does not gratuitously suffix its slug — which matters because the
    /// slug is the public URL and changing it breaks every inbound link.</para>
    /// <para><b>Flow:</b> validate → confirm existence → derive slug → resolve collisions →
    /// stamp <c>UpdatedOn</c> → update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogPost</c> row and mutates
    /// <paramref name="post"/> in place with the resolved slug and timestamp. Writes an information
    /// or error log entry.</para>
    /// <para><b>Authorization:</b> none is applied — this method will happily update any post id it
    /// is given. The caller must have confirmed that the signed-in user is an Admin or Editor, or is
    /// the post's own author, before calling.</para>
    /// <para><b>Last write wins:</b> there is no concurrency token, so two editors saving the same
    /// post both succeed and the later save silently overwrites the earlier one.</para>
    /// </remarks>
    /// <param name="post">The post carrying the new values; mutated with its resolved slug.</param>
    /// <returns>The updated post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> UpdatePost(BlogPost post)
    {
        // Validate required fields
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (post.PostID <= 0)
            return Result<BlogPost>.Failure("Invalid post ID");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        // Check if post exists
        var existing = postRepo.GetSingle(post.PostID);
        if (existing == null)
            return Result<BlogPost>.Failure("Post not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        // Handle duplicate slug (exclude current post)
        if (postRepo.SlugExists(post.Slug, post.PostID))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            int counter = 2;
            while (postRepo.SlugExists(post.Slug, post.PostID) && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        // Set update timestamp
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            postRepo.Update(post);
            logger.LogInformation("Updated post '{Title}' with ID {PostId}", post.Title, post.PostID);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update post ID {PostId}: {Title}", post.PostID, post.Title);
            return Result<BlogPost>.Failure($"Failed to update post: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a post — inserting or updating according to whether it already has an identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A non-positive <c>PostID</c> means the post has never been
    /// persisted, so the editor can bind one method to its save button regardless of mode. Every
    /// state-changing helper below — <see cref="SaveDraft"/>, <see cref="PublishPost"/>,
    /// <see cref="SchedulePost"/> — sets its flags and then routes through here, which is why the
    /// validation and slug rules apply uniformly to all of them.</para>
    /// <para><b>Flow:</b> inspect the key → delegate to <see cref="CreatePost"/> or
    /// <see cref="UpdatePost"/>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated method — one insert or one update.</para>
    /// </remarks>
    /// <param name="post">The post to save.</param>
    /// <returns>The saved post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> SavePost(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (post.PostID <= 0)
        {
            return CreatePost(post);
        }
        else
        {
            return UpdatePost(post);
        }
    }

    /// <summary>
    /// Soft-deletes a blog post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is never removed — <c>IsDeleted</c> is set, which takes
    /// the post out of every published and admin query while preserving its comments, ratings and
    /// view history. Deleting an already-deleted post is refused rather than treated as a harmless
    /// repeat, so a double-submitted button reports honestly instead of implying it did something.</para>
    /// <para><b>Flow:</b> validate the id → confirm existence → reject if already deleted →
    /// soft-delete → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Sets <c>IsDeleted</c> on one row; writes an information or error
    /// log entry. Nothing is erased and no cascade runs, so the post can be restored by clearing the
    /// flag directly in the database.</para>
    /// <para><b>Authorization:</b> none is applied. Any caller that reaches this method can delete
    /// any post; the ownership and role check belongs to the page.</para>
    /// </remarks>
    /// <param name="postId">Identifier of the post to delete.</param>
    /// <returns>Success, or a failure describing why the delete was refused.</returns>
    public Result DeletePost(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        // Check if post exists
        var existing = postRepo.GetSingle(postId);
        if (existing == null)
            return Result.Failure("Post not found");

        if (existing.IsDeleted)
            return Result.Failure("Post is already deleted");

        try
        {
            postRepo.SoftDelete(postId);
            logger.LogInformation("Deleted post ID {PostId}", postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete post ID {PostId}", postId);
            return Result.Failure($"Failed to delete post: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a post as an unpublished draft.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Forces <c>Published</c> to false and routes through
    /// <see cref="SavePost"/>. Applied to a live post this <b>unpublishes it</b> — the draft button
    /// is a state change, not merely a save. <c>PublishedOn</c> is deliberately left alone so the
    /// original publication date survives a round trip through draft.</para>
    /// <para><b>Flow:</b> clear the published flag → stamp <c>UpdatedOn</c> → delegate to
    /// <see cref="SavePost"/>.</para>
    /// <para><b>Side Effects:</b> One insert or update; the post disappears from public listings if
    /// it was visible.</para>
    /// </remarks>
    /// <param name="post">The post to save as a draft.</param>
    /// <returns>The saved post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> SaveDraft(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePost(post);
    }

    /// <summary>
    /// Publishes a post, making it publicly visible.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>PublishedOn</c> is stamped only when it is not already set,
    /// so re-publishing a post that was temporarily unpublished restores it with its original
    /// publication date rather than jumping it to the top of the feed. That single conditional is
    /// what makes "unpublish, fix a typo, publish again" a non-event for readers.</para>
    /// <para><b>Flow:</b> set the published flag → stamp <c>UpdatedOn</c> → stamp
    /// <c>PublishedOn</c> if this is the first publication → delegate to <see cref="SavePost"/>.</para>
    /// <para><b>Side Effects:</b> One insert or update; <b>the post becomes visible to anonymous
    /// visitors immediately</b> and starts appearing in listings, the sitemap and the feed. Any
    /// pending schedule on the post is NOT cleared here — use <see cref="QuickPublish"/> for
    /// that.</para>
    /// </remarks>
    /// <param name="post">The post to publish.</param>
    /// <returns>The published post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> PublishPost(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;

        // Only set PublishedOn if not already set (first publish)
        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        return SavePost(post);
    }

    /// <summary>
    /// Withdraws a published post from public view.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Clears <c>Published</c> but deliberately keeps
    /// <c>PublishedOn</c>, so the original publication date is available when the post goes back up
    /// (see <see cref="PublishPost"/>). Unpublishing an already-unpublished post is refused rather
    /// than reported as a no-op success.</para>
    /// <para><b>Flow:</b> validate the id → load → reject if already unpublished → clear the flag →
    /// update.</para>
    /// <para><b>Side Effects:</b> Updates one row; the post vanishes from every public surface.
    /// Comments and ratings already attached to it are not touched, but become unreachable along
    /// with the page.</para>
    /// <para><b>Logging gap:</b> unlike its neighbours, the failure path here returns the exception
    /// message without logging it, so a persistence failure leaves no trace in the log.</para>
    /// </remarks>
    /// <param name="postId">Identifier of the post to unpublish.</param>
    /// <returns>Success, or a failure describing why the change was refused.</returns>
    public Result UnpublishPost(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = postRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (!post.Published)
            return Result.Failure("Post is already unpublished");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            postRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to unpublish post: {ex.Message}");
        }
    }

    /// <summary>
    /// Publishes a post straight from a list row, by identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The one-click counterpart to <see cref="PublishPost"/> for the
    /// admin grid, where no edited post object is in hand. It differs in one important way: it
    /// <b>clears any pending schedule</b>, because publishing now makes a future scheduled
    /// publication meaningless and leaving it set would have <c>ScheduledPostPublisher</c> act on
    /// the post a second time. As with <see cref="PublishPost"/>, <c>PublishedOn</c> is preserved
    /// when it already has a value.</para>
    /// <para><b>Flow:</b> validate the id → load → reject if already published → set the flag,
    /// clear the schedule, stamp the dates → update.</para>
    /// <para><b>Side Effects:</b> Updates one row; the post becomes publicly visible immediately and
    /// its <c>ScheduledPublishOn</c> is discarded.</para>
    /// <para><b>Logging gap:</b> the failure path returns the exception message without logging it.</para>
    /// </remarks>
    /// <param name="postId">Identifier of the post to publish.</param>
    /// <returns>Success, or a failure describing why the publication was refused.</returns>
    public Result QuickPublish(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = postRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (post.Published)
            return Result.Failure("Post is already published");

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;
        post.ScheduledPublishOn = null; // Clear any schedule
        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        try
        {
            postRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to publish post: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets every post awaiting a scheduled publication, for the admin view.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lists posts that carry a <c>ScheduledPublishOn</c> and are not
    /// yet published, whether or not that time has passed — so a schedule the background publisher
    /// has failed to act on stays visible here rather than disappearing.</para>
    /// <para><b>Flow:</b> read → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>Posts scheduled for future publication, or an empty sequence on failure.</returns>
    public IEnumerable<BlogPost> GetScheduledPosts()
    {
        try
        {
            return postRepo.GetScheduledPosts();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the posts whose scheduled publication time has arrived.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The cut-off is <c>DateTime.UtcNow</c>, evaluated here rather
    /// than passed in, so every caller agrees on "now" and a caller cannot accidentally publish the
    /// future by supplying a wrong clock. This drives <c>ScheduledPostPublisher</c>.</para>
    /// <para><b>Flow:</b> take the current UTC instant → read → log and degrade to an empty sequence
    /// on failure.</para>
    /// <para><b>Side Effects:</b> None beyond an error log entry on failure — this method only
    /// identifies the due posts; publishing them is the caller's job.</para>
    /// </remarks>
    /// <returns>Posts ready to be published, or an empty sequence on failure.</returns>
    public IEnumerable<BlogPost> GetDueScheduledPosts()
    {
        try
        {
            return postRepo.GetDueScheduledPosts(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting due scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Schedules a post to publish itself at a future instant.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A time in the past or present is refused, because a schedule
    /// that is already due would be picked up on the publisher's very next sweep and is really a
    /// request to publish now — <see cref="QuickPublish"/> says that honestly. Scheduling also
    /// forces <c>Published</c> to false, so scheduling a live post takes it down until its time
    /// arrives.</para>
    /// <para><b>Flow:</b> validate the post and the instant → set the schedule and clear the
    /// published flag → delegate to <see cref="SavePost"/>.</para>
    /// <para><b>Side Effects:</b> One insert or update. The post is not published by this call;
    /// <c>ScheduledPostPublisher</c> acts on it later.</para>
    /// <para><b>UTC:</b> <paramref name="scheduledUtc"/> is compared against
    /// <c>DateTime.UtcNow</c> and stored as given. Passing a local time schedules the wrong
    /// instant — convert before calling.</para>
    /// </remarks>
    /// <param name="post">The post to schedule.</param>
    /// <param name="scheduledUtc">UTC instant at which the post should publish; must be future.</param>
    /// <returns>The scheduled post on success, or a failure carrying the reason.</returns>
    public Result<BlogPost> SchedulePost(BlogPost post, DateTime scheduledUtc)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (scheduledUtc <= DateTime.UtcNow)
            return Result<BlogPost>.Failure("Scheduled time must be in the future");

        post.ScheduledPublishOn = scheduledUtc;
        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePost(post);
    }

    /// <summary>
    /// Cancels a pending schedule, leaving the post as a draft.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Clears <c>ScheduledPublishOn</c> only; the post keeps whatever
    /// publication state it had, which for a scheduled post is unpublished. Cancelling a post that
    /// was never scheduled is refused rather than silently succeeding.</para>
    /// <para><b>Flow:</b> validate the id → load → reject when nothing is scheduled → clear the
    /// schedule → update.</para>
    /// <para><b>Side Effects:</b> Updates one row; <c>ScheduledPostPublisher</c> will no longer act
    /// on the post.</para>
    /// <para><b>Logging gap:</b> the failure path returns the exception message without logging it.</para>
    /// </remarks>
    /// <param name="postId">Identifier of the post whose schedule should be cancelled.</param>
    /// <returns>Success, or a failure describing why the cancellation was refused.</returns>
    public Result CancelSchedule(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = postRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (!post.ScheduledPublishOn.HasValue)
            return Result.Failure("Post is not scheduled");

        post.ScheduledPublishOn = null;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            postRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to cancel schedule: {ex.Message}");
        }
    }

    /// <summary>
    /// Searches published posts and returns one page of matches.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The match itself is the repository's business — the query text
    /// is bound as a parameter there, never concatenated, so a visitor's search box cannot reach the
    /// SQL. This method adds only the degrade-to-empty policy: a search that fails renders "no
    /// results" rather than taking the page down.</para>
    /// <para><b>Flow:</b> pass the query and paging through → log and degrade to an empty sequence
    /// on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure. The query text is included
    /// in that entry.</para>
    /// </remarks>
    /// <param name="query">The visitor's search text.</param>
    /// <param name="pageSize">Number of results per page.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <returns>The requested page of matching posts, or an empty sequence on failure.</returns>
    public IEnumerable<BlogPost> SearchPosts(string query, int pageSize = 10, int offset = 0)
    {
        try
        {
            return postRepo.SearchPosts(query, pageSize, offset);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching posts with query: {Query}", query);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Counts the posts matching a search, for the results pager.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies the same match and publication filters as
    /// <see cref="SearchPosts"/>, so the pager cannot promise a page that the search will not
    /// return.</para>
    /// <para><b>Flow:</b> read the count → log and return 0 on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="query">The visitor's search text.</param>
    /// <returns>The number of matching posts; 0 on failure.</returns>
    public int GetSearchResultCount(string query)
    {
        try
        {
            return postRepo.GetSearchResultCount(query);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting search result count for: {Query}", query);
            return 0;
        }
    }

    // =================================================================================================
    // Async surface — REQ-NFR-026. Preferred over every member above.
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

            return await postRepo.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
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
            return await postRepo.GetPublishedPostsAsync(pageSize, offset, cancellationToken).ConfigureAwait(false);
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
            return await postRepo.GetFeaturedPostAsync(cancellationToken).ConfigureAwait(false);
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
            return await postRepo.GetPublishedPostCountAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting published post count");
            return 0;
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
            return await postRepo.GetPostsByCategoryAsync(categoryId, pageSize, offset, cancellationToken).ConfigureAwait(false);
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
            return await postRepo.GetPostCountByCategoryAsync(categoryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post count for category {CategoryId}", categoryId);
            return 0;
        }
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

        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        if (await postRepo.SlugExistsAsync(post.Slug, 0, cancellationToken).ConfigureAwait(false))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            int counter = 2;
            while (await postRepo.SlugExistsAsync(post.Slug, 0, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        post.CreatedOn = DateTime.UtcNow;
        post.IsDeleted = false;

        try
        {
            var postId = await postRepo.InsertToGetIdAsync(post, cancellationToken).ConfigureAwait(false);
            post.PostID = postId;
            logger.LogInformation("Created post '{Title}' with ID {PostId}", post.Title, postId);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create post: {Title}", post.Title);
            return Result<BlogPost>.Failure($"Failed to create post: {ex.Message}");
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

        var existing = await postRepo.GetSingleAsync(post.PostID, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result<BlogPost>.Failure("Post not found");

        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        if (await postRepo.SlugExistsAsync(post.Slug, post.PostID, cancellationToken).ConfigureAwait(false))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            int counter = 2;
            while (await postRepo.SlugExistsAsync(post.Slug, post.PostID, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Updated post '{Title}' with ID {PostId}", post.Title, post.PostID);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update post ID {PostId}: {Title}", post.PostID, post.Title);
            return Result<BlogPost>.Failure($"Failed to update post: {ex.Message}");
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
            logger.LogInformation("Deleted post ID {PostId}", postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete post ID {PostId}", postId);
            return Result.Failure($"Failed to delete post: {ex.Message}");
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

        if (!post.Published)
            return Result.Failure("Post is already unpublished");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unpublish post ID {PostId}", postId);
            return Result.Failure($"Failed to unpublish post: {ex.Message}");
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
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish post ID {PostId}", postId);
            return Result.Failure($"Failed to publish post: {ex.Message}");
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

        if (!post.ScheduledPublishOn.HasValue)
            return Result.Failure("Post is not scheduled");

        post.ScheduledPublishOn = null;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            await postRepo.UpdateAsync(post, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to cancel schedule for post ID {PostId}", postId);
            return Result.Failure($"Failed to cancel schedule: {ex.Message}");
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
