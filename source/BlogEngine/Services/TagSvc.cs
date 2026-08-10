using BlogEngine.Common;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Tag taxonomy: CRUD, slug allocation, post association and tag-filtered listings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the free-form half of the taxonomy. A post has one category but many
/// tags, and tags are created on demand as authors type them — so this class carries the rules that
/// keep an author-driven vocabulary from producing duplicate or unreachable URLs.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Reads (<see cref="GetAllTags"/>, <see cref="GetAllWithCounts"/>,
///     <see cref="GetTagBySlug"/>, <see cref="SearchTags"/>) back the tag cloud, the admin list, the
///     public <c>/tag/{slug}</c> page and the editor's autocomplete.</item>
///   <item>Writes (<see cref="CreateTag"/>, <see cref="UpdateTag"/>, <see cref="SaveTag"/>,
///     <see cref="DeleteTag"/>) validate, allocate a unique slug and persist.</item>
///   <item><see cref="GetOrCreateTag"/> is the editor's path: it turns a typed name into a tag row,
///     reusing an existing one when the name slugs to the same value.</item>
///   <item><see cref="SetTagsForPost"/> replaces a post's whole tag set in one call.</item>
/// </list>
///
/// <para><b>The slug is the identity.</b> Uniqueness is enforced on the slug, not the name, and
/// <see cref="GetOrCreateTag"/> matches on it — so <c>.NET</c>, <c>dotnet</c> and <c>Dot Net</c>
/// collapse or diverge exactly as <c>SlugGenerator</c> decides. That is the rule to know before
/// changing slug generation: altering it retroactively changes which existing tags are considered
/// the same tag, and breaks every published <c>/tag/{slug}</c> URL.</para>
///
/// <para><b>Collision handling:</b> a taken slug gets a numeric suffix, retried up to 99 times. The
/// bound is a safety valve against an unbounded loop, not a business rule; exhausting it leaves the
/// last candidate in place and the insert fails on the database's unique constraint, which is
/// reported as a <c>Result</c> failure rather than crashing the editor.</para>
///
/// <para><b>Error contract:</b> reads swallow and log, returning an empty sequence or null, because
/// a taxonomy failure must degrade a sidebar rather than break a post page. Mutations return
/// <c>Result</c>; note that they surface <c>ex.Message</c> in the failure text, which is acceptable
/// for an admin-only screen but should not be echoed to an anonymous visitor.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogTagRepo"/> for data access, <c>SlugGenerator</c> for
/// URL slugs, <see cref="ILogger{TCategoryName}"/> for diagnostics.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Reads are used by
/// anonymous public pages; every mutation is reached from an admin screen behind
/// <c>AppPolicies.EditorOrAbove</c> (or <c>AuthorOrAbove</c> via the post editor, for
/// <see cref="GetOrCreateTag"/> and <see cref="SetTagsForPost"/>). This class enforces <b>no</b>
/// policy itself — the calling page owns that check.</para>
///
/// <para><b>Caching note:</b> the taxonomy is a declared cache group
/// (<c>CacheTags.Taxonomy</c>), but no mutation here evicts it, because nothing currently caches
/// through <c>ICacheService</c> on this path. If a caller starts caching tag reads, the eviction
/// call belongs in <see cref="CreateTag"/>, <see cref="UpdateTag"/> and
/// <see cref="DeleteTag"/>.</para>
///
/// <para><b>Async conversion (REQ-NFR-026, stage 3):</b> every public member now exists twice — the
/// original blocking member and an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>,
/// placed immediately after it. The async twin routes all of its I/O through
/// <see cref="IBlogTagRepo"/>'s async surface and is the member to call. The <c>Result</c> pattern is
/// unchanged by the conversion: <c>Result&lt;T&gt;</c> simply travels inside a task, so a method that
/// returned <c>Result&lt;BlogTag&gt;</c> returns <c>Task&lt;Result&lt;BlogTag&gt;&gt;</c> and the
/// <c>try/catch</c> that converts an unexpected exception into a failed <c>Result</c> keeps working
/// verbatim — an awaited call throws at the <c>await</c> exactly as a blocking call throws at the
/// call. <b>The synchronous surface is retained only while the remaining call sites migrate and is
/// deleted in stage 4</b>; do not add new callers of it.</para>
/// </remarks>
public class TagSvc
{
    private readonly IBlogTagRepo tagRepo;
    private readonly ILogger<TagSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagSvc"/> class.
    /// </summary>
    /// <param name="tagRepo">Tag data access.</param>
    /// <param name="logger">Logger for taxonomy changes and read failures.</param>
    public TagSvc(IBlogTagRepo tagRepo, ILogger<TagSvc> logger)
    {
        this.tagRepo = tagRepo;
        this.logger = logger;
    }

    /// <summary>
    /// Lists every tag, ordered by name.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unfiltered — a tag with no posts is still returned, because the
    /// admin list must be able to show and delete an orphan. Use
    /// <see cref="GetAllWithCounts"/> when the caller needs to hide empty tags.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <returns>Every tag; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogTag> GetAllTags()
    {
        try
        {
            return tagRepo.GetAll();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all tags");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Lists every tag, ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllTags"/> and behaviourally
    /// identical. Unfiltered — a tag with no posts is still returned, because the admin list must be
    /// able to show and delete an orphan. Use <see cref="GetAllWithCountsAsync"/> when the caller
    /// needs post counts.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives an empty sequence.</param>
    /// <returns>Every tag; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogTag>> GetAllTagsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all tags");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Lists every tag with the number of posts carrying it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One aggregate query rather than a count per tag, which is what
    /// makes a tag cloud affordable to render. The count reflects the repository's own definition of
    /// a countable post — check <c>IBlogTagRepo.GetAllWithCounts</c> before assuming it excludes
    /// drafts.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <returns>Tags with <c>PostCount</c> populated; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogTag> GetAllWithCounts()
    {
        try
        {
            return tagRepo.GetAllWithCounts();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags with counts");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Lists every tag with the number of posts carrying it, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllWithCounts"/> and behaviourally
    /// identical. One aggregate query rather than a count per tag, which is what makes a tag cloud
    /// affordable to render. The count reflects the repository's own definition of a countable post —
    /// check <c>IBlogTagRepo.GetAllWithCountsAsync</c> before assuming it excludes drafts.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives an empty sequence.</param>
    /// <returns>Tags with <c>PostCount</c> populated; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogTag>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetAllWithCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags with counts");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Loads one tag by its identifier, for the edit form.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Id-keyed, so it is the admin lookup; public pages resolve tags
    /// by slug instead.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="tagId">The tag's identifier.</param>
    /// <returns>The tag, or null when it does not exist or the read failed.</returns>
    public BlogTag? GetSingleTag(long tagId)
    {
        try
        {
            return tagRepo.GetSingle(tagId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tag by ID: {TagId}", tagId);
            return null;
        }
    }

    /// <summary>
    /// Loads one tag by its identifier, for the edit form, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSingleTag"/> and behaviourally
    /// identical. Id-keyed, so it is the admin lookup; public pages resolve tags by slug instead.
    /// "No such tag" and "the read failed" both surface as <c>null</c>, distinguished only in the
    /// log.</para>
    /// <para><b>Flow:</b> await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="tagId">The tag's identifier.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives <c>null</c>.</param>
    /// <returns>The tag, or null when it does not exist or the read failed.</returns>
    public async Task<BlogTag?> GetSingleTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetSingleAsync(tagId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tag by ID: {TagId}", tagId);
            return null;
        }
    }

    /// <summary>
    /// Resolves the tag behind a public <c>/tag/{slug}</c> URL.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank slug returns null before any query runs, so a truncated
    /// URL becomes a clean 404 rather than a database round trip.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Note that a read failure is indistinguishable
    /// from "no such tag" to the caller — both are null — so the page renders its not-found state
    /// either way and the real cause is only in the log.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <returns>The tag, or null when the slug is blank, unknown, or the read failed.</returns>
    public BlogTag? GetTagBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            return tagRepo.GetBySlug(slug);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tag by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Resolves the tag behind a public <c>/tag/{slug}</c> URL, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetTagBySlug"/> and behaviourally
    /// identical. A blank slug returns null before any query runs, so a truncated URL becomes a clean
    /// 404 rather than a database round trip.</para>
    /// <para><b>Flow:</b> guard the slug → await the repository → log and return <c>null</c> on
    /// failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Note that a read failure is indistinguishable
    /// from "no such tag" to the caller — both are null — so the page renders its not-found state
    /// either way and the real cause is only in the log.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug taken from the route.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives <c>null</c>.</param>
    /// <returns>The tag, or null when the slug is blank, unknown, or the read failed.</returns>
    public async Task<BlogTag?> GetTagBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return await tagRepo.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tag by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Finds tags whose name matches a fragment, for the editor's autocomplete.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty query returns the first ten tags rather than nothing,
    /// so the autocomplete has something to offer before the author has typed. Showing existing tags
    /// early is how the vocabulary stays small: an author who sees <c>blazor</c> in the list is far
    /// less likely to create <c>Blazor Server</c> beside it.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="query">Fragment to match against the tag name; empty returns a short sample.</param>
    /// <returns>The matching tags; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogTag> SearchTags(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return tagRepo.GetAll().Take(10);
            return tagRepo.SearchTags(query);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching tags with query: {Query}", query);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Finds tags whose name matches a fragment, for the editor's autocomplete, without blocking the
    /// calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SearchTags"/> and behaviourally
    /// identical, including the empty-query branch: an empty query returns the first ten tags rather
    /// than nothing, so the autocomplete has something to offer before the author has typed. Showing
    /// existing tags early is how the vocabulary stays small: an author who sees <c>blazor</c> in the
    /// list is far less likely to create <c>Blazor Server</c> beside it. The <c>Take(10)</c> on that
    /// branch is applied in memory over the full tag set, exactly as the synchronous twin does — the
    /// ten-row cap on the matching branch is applied in SQL by the repository.</para>
    /// <para><b>Flow:</b> blank query → await the full list and take ten; otherwise await the
    /// repository's search → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="query">Fragment to match against the tag name; empty returns a short sample.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives an empty sequence.</param>
    /// <returns>The matching tags; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogTag>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return (await tagRepo.GetAllAsync(cancellationToken).ConfigureAwait(false)).Take(10);

            return await tagRepo.SearchTagsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching tags with query: {Query}", query);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Returns the tag for a typed name, creating it when it is genuinely new.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The editor lets an author type a tag that may or may not exist.
    /// Matching is by <i>slug</i>, not by name, so <c>ASP.NET Core</c> and <c>asp-net-core</c>
    /// resolve to the same row and the vocabulary does not fragment on capitalisation or
    /// punctuation. When a match is found the <b>existing</b> tag is returned unchanged — the
    /// author's spelling does not rename the tag for every other post already carrying it.</para>
    /// <para><b>Flow:</b> reject blank → slug the name → look up by slug → return the match, or
    /// insert a new tag and return it with its generated id.</para>
    /// <para><b>Side Effects:</b> May insert a <c>BlogTag</c> row. <b>Unlike the rest of this
    /// class it has no try/catch</b> — a repository failure propagates to the caller. That is
    /// deliberate: this runs inside the post-save path, and silently returning null would attach the
    /// post to no tag while reporting success. The caller must be prepared for the throw.</para>
    /// <para><b>Race:</b> two authors saving the same new tag simultaneously both miss the lookup
    /// and both insert; the loser gets a unique-constraint violation from the database, which
    /// surfaces as the exception described above. Rare enough to accept, but do not assume this
    /// method is atomic.</para>
    /// </remarks>
    /// <param name="tagName">The tag name as the author typed it.</param>
    /// <returns>The existing or newly created tag; null only when the name is blank.</returns>
    public BlogTag? GetOrCreateTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var slug = SlugGenerator.GenerateSlug(tagName.Trim());
        var existing = tagRepo.GetBySlug(slug);
        if (existing != null)
            return existing;

        var tag = new BlogTag
        {
            TagName = tagName.Trim(),
            Slug = slug
        };
        tag.TagId = tagRepo.InsertToGetId(tag);
        return tag;
    }

    /// <summary>
    /// Returns the tag for a typed name, creating it when it is genuinely new, without blocking the
    /// calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetOrCreateTag"/> and behaviourally
    /// identical. Matching is by <i>slug</i>, not by name, so <c>ASP.NET Core</c> and
    /// <c>asp-net-core</c> resolve to the same row and the vocabulary does not fragment on
    /// capitalisation or punctuation. When a match is found the <b>existing</b> tag is returned
    /// unchanged — the author's spelling does not rename the tag for every other post already
    /// carrying it.</para>
    /// <para><b>Flow:</b> reject blank → slug the name → await the lookup by slug → return the match,
    /// or await the insert and return the new tag with its generated id.</para>
    /// <para><b>Side Effects:</b> May insert a <c>BlogTag</c> row. <b>Unlike the rest of this class it
    /// has no try/catch</b> — a repository failure faults the returned task and reaches the caller at
    /// its <c>await</c>. That is deliberate and is preserved from the synchronous twin: this runs
    /// inside the post-save path, and silently returning null would attach the post to no tag while
    /// reporting success. The caller must be prepared for the throw.</para>
    /// <para><b>Race:</b> two authors saving the same new tag simultaneously both miss the lookup and
    /// both insert; the loser gets a unique-constraint violation from the database, which surfaces as
    /// the faulted task described above. Rare enough to accept, but do not assume this method is
    /// atomic.</para>
    /// </remarks>
    /// <param name="tagName">The tag name as the author typed it.</param>
    /// <param name="cancellationToken">Cancels the lookup and the insert. Unlike the read members of
    /// this class, cancellation is <b>not</b> swallowed here — it faults the task like any other
    /// failure on this path.</param>
    /// <returns>The existing or newly created tag; null only when the name is blank.</returns>
    public async Task<BlogTag?> GetOrCreateTagAsync(string tagName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var slug = SlugGenerator.GenerateSlug(tagName.Trim());
        var existing = await tagRepo.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (existing != null)
            return existing;

        var tag = new BlogTag
        {
            TagName = tagName.Trim(),
            Slug = slug
        };
        tag.TagId = await tagRepo.InsertToGetIdAsync(tag, cancellationToken).ConfigureAwait(false);
        return tag;
    }

    /// <summary>
    /// Creates a tag from the admin form, allocating a free slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A name is mandatory; a slug is not, and is derived from the name
    /// when the administrator leaves it blank. If the slug is already taken it gains a numeric
    /// suffix and is retried until free (bounded at 99 attempts) — the tag is never silently merged
    /// into the existing one, because two tags may legitimately share a name-derived slug while
    /// meaning different things.</para>
    /// <para><b>Flow:</b> null and name guards → derive slug if absent → resolve collisions →
    /// insert → stamp the generated id onto the supplied instance.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogTag</c> row, <b>mutates the caller's
    /// object</b> (both <c>Slug</c> and <c>TagId</c> are written back), and logs the creation.</para>
    /// </remarks>
    /// <param name="tag">The tag to create; its <c>Slug</c> and <c>TagId</c> are assigned in place.</param>
    /// <returns>Success carrying the persisted tag, or a failure naming the problem.</returns>
    public Result<BlogTag> CreateTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug
        if (tagRepo.SlugExists(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (tagRepo.SlugExists(tag.Slug) && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            var tagId = tagRepo.InsertToGetId(tag);
            tag.TagId = tagId;
            logger.LogInformation("Created tag '{TagName}' with ID {TagId}", tag.TagName, tagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create tag: {TagName}", tag.TagName);
            return Result<BlogTag>.Failure($"Failed to create tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a tag from the admin form, allocating a free slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="CreateTag"/> and behaviourally identical.
    /// A name is mandatory; a slug is not, and is derived from the name when the administrator leaves
    /// it blank. If the slug is already taken it gains a numeric suffix and is retried until free
    /// (bounded at 99 attempts) — the tag is never silently merged into the existing one, because two
    /// tags may legitimately share a name-derived slug while meaning different things. The
    /// collision check runs with no exclusion (<c>excludeTagId</c> of zero), because nothing exists to
    /// exclude yet.</para>
    /// <para><b>Flow:</b> null and name guards → derive slug if absent → await the collision loop →
    /// await the insert → stamp the generated id onto the supplied instance.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogTag</c> row, <b>mutates the caller's object</b>
    /// (both <c>Slug</c> and <c>TagId</c> are written back), and logs the creation. The guard failures
    /// are returned before any I/O runs.</para>
    /// <para><b>Slug collision race:</b> the uniqueness check and the insert are separate statements,
    /// so two simultaneous creations of the same name can both pass the check; the database's unique
    /// constraint is the real guard, and its violation is reported as a failed <c>Result</c>.</para>
    /// </remarks>
    /// <param name="tag">The tag to create; its <c>Slug</c> and <c>TagId</c> are assigned in place.</param>
    /// <param name="cancellationToken">Cancels the slug checks and the insert. Cancellation during
    /// the insert faults the task rather than returning a failed <c>Result</c>; only exceptions raised
    /// by the insert itself are converted, exactly as in the synchronous twin.</param>
    /// <returns>Success carrying the persisted tag, or a failure naming the problem.</returns>
    public async Task<Result<BlogTag>> CreateTagAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug
        if (await tagRepo.SlugExistsAsync(tag.Slug, 0, cancellationToken).ConfigureAwait(false))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (await tagRepo.SlugExistsAsync(tag.Slug, 0, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            var tagId = await tagRepo.InsertToGetIdAsync(tag, cancellationToken).ConfigureAwait(false);
            tag.TagId = tagId;
            logger.LogInformation("Created tag '{TagName}' with ID {TagId}", tag.TagName, tagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create tag: {TagName}", tag.TagName);
            return Result<BlogTag>.Failure($"Failed to create tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves changes to an existing tag.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same rules as <see cref="CreateTag"/>, with two additions: the
    /// row must already exist, and the uniqueness check excludes the tag being edited — otherwise
    /// saving a tag without changing its name would collide with itself and pointlessly renumber
    /// its slug.</para>
    /// <para><b>Flow:</b> null, id and name guards → confirm existence → derive slug if absent →
    /// resolve collisions against every <i>other</i> tag → update.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogTag</c> row and mutates the caller's object.
    /// <b>Changing a tag's slug breaks its published URL</b> — every link and search-engine entry
    /// pointing at <c>/tag/{old-slug}</c> becomes a 404, and no redirect is written. Treat a slug
    /// edit on an established tag as a breaking change.</para>
    /// </remarks>
    /// <param name="tag">The tag carrying updated values; its <c>Slug</c> may be rewritten.</param>
    /// <returns>Success carrying the saved tag, or a failure naming the problem.</returns>
    public Result<BlogTag> UpdateTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (tag.TagId <= 0)
            return Result<BlogTag>.Failure("Invalid tag ID");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Check if tag exists
        var existing = tagRepo.GetSingle(tag.TagId);
        if (existing == null)
            return Result<BlogTag>.Failure("Tag not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug (exclude current tag)
        if (tagRepo.SlugExists(tag.Slug, tag.TagId))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (tagRepo.SlugExists(tag.Slug, tag.TagId) && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            tagRepo.Update(tag);
            logger.LogInformation("Updated tag '{TagName}' with ID {TagId}", tag.TagName, tag.TagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update tag ID {TagId}: {TagName}", tag.TagId, tag.TagName);
            return Result<BlogTag>.Failure($"Failed to update tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves changes to an existing tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="UpdateTag"/> and behaviourally identical.
    /// Same rules as <see cref="CreateTagAsync"/>, with two additions: the row must already exist, and
    /// the uniqueness check excludes the tag being edited — otherwise saving a tag without changing
    /// its name would collide with itself and pointlessly renumber its slug.</para>
    /// <para><b>Flow:</b> null, id and name guards → await the existence check → derive slug if absent
    /// → await the collision loop against every <i>other</i> tag → await the update.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogTag</c> row and mutates the caller's object.
    /// <b>Changing a tag's slug breaks its published URL</b> — every link and search-engine entry
    /// pointing at <c>/tag/{old-slug}</c> becomes a 404, and no redirect is written. Treat a slug edit
    /// on an established tag as a breaking change.</para>
    /// </remarks>
    /// <param name="tag">The tag carrying updated values; its <c>Slug</c> may be rewritten.</param>
    /// <param name="cancellationToken">Cancels the existence check, the slug checks and the update.
    /// Cancellation before the <c>try</c> faults the task rather than returning a failed
    /// <c>Result</c>; only exceptions raised by the update itself are converted, exactly as in the
    /// synchronous twin.</param>
    /// <returns>Success carrying the saved tag, or a failure naming the problem.</returns>
    public async Task<Result<BlogTag>> UpdateTagAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (tag.TagId <= 0)
            return Result<BlogTag>.Failure("Invalid tag ID");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Check if tag exists
        var existing = await tagRepo.GetSingleAsync(tag.TagId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result<BlogTag>.Failure("Tag not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug (exclude current tag)
        if (await tagRepo.SlugExistsAsync(tag.Slug, tag.TagId, cancellationToken).ConfigureAwait(false))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (await tagRepo.SlugExistsAsync(tag.Slug, tag.TagId, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            await tagRepo.UpdateAsync(tag, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Updated tag '{TagName}' with ID {TagId}", tag.TagName, tag.TagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update tag ID {TagId}: {TagName}", tag.TagId, tag.TagName);
            return Result<BlogTag>.Failure($"Failed to update tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates or updates a tag depending on whether it already has an identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets one admin form serve both add and edit; a non-positive
    /// <c>TagId</c> means "new".</para>
    /// <para><b>Flow:</b> null guard → delegate to <see cref="CreateTag"/> or
    /// <see cref="UpdateTag"/>.</para>
    /// <para><b>Side Effects:</b> Whatever the delegated method does — one row inserted or
    /// updated.</para>
    /// </remarks>
    /// <param name="tag">The tag to persist.</param>
    /// <returns>Success carrying the saved tag, or a failure naming the problem.</returns>
    public Result<BlogTag> SaveTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (tag.TagId <= 0)
        {
            return CreateTag(tag);
        }
        else
        {
            return UpdateTag(tag);
        }
    }

    /// <summary>
    /// Creates or updates a tag depending on whether it already has an identifier, without blocking
    /// the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SaveTag"/> and behaviourally identical.
    /// Lets one admin form serve both add and edit; a non-positive <c>TagId</c> means "new".</para>
    /// <para><b>Flow:</b> null guard → delegate to <see cref="CreateTagAsync"/> or
    /// <see cref="UpdateTagAsync"/>. This member is a pure delegation, so it returns the delegate's
    /// task directly rather than awaiting it, and the null guard — which needs no I/O — is returned as
    /// an already-completed task.</para>
    /// <para><b>Side Effects:</b> Whatever the delegated method does — one row inserted or
    /// updated.</para>
    /// </remarks>
    /// <param name="tag">The tag to persist.</param>
    /// <param name="cancellationToken">Cancels the delegated operation; it is flowed through
    /// unchanged and this member observes it only via the method it delegates to.</param>
    /// <returns>Success carrying the saved tag, or a failure naming the problem.</returns>
    public Task<Result<BlogTag>> SaveTagAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        if (tag == null)
            return Task.FromResult(Result<BlogTag>.Failure("Tag cannot be null"));

        return tag.TagId <= 0
            ? CreateTagAsync(tag, cancellationToken)
            : UpdateTagAsync(tag, cancellationToken);
    }

    /// <summary>
    /// Removes a tag from the taxonomy.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A hard delete, and it is <b>not</b> blocked when posts still
    /// carry the tag — unlike a category, a tag is a label rather than a home, so a post simply
    /// loses one label. Existence is confirmed first so deleting an already-deleted tag reports
    /// "not found" rather than succeeding silently.</para>
    /// <para><b>Flow:</b> id guard → confirm existence → delete.</para>
    /// <para><b>Side Effects:</b> Deletes one <c>BlogTag</c> row; the post-to-tag link rows go with
    /// it by cascade at the database level. Published <c>/tag/{slug}</c> URLs for the removed tag
    /// become 404s. Logs the deletion — this is the only record that the tag ever existed.</para>
    /// </remarks>
    /// <param name="tagId">Identifier of the tag to remove.</param>
    /// <returns>Success, or a failure when the tag is unknown or the delete failed.</returns>
    public Result DeleteTag(long tagId)
    {
        if (tagId <= 0)
            return Result.Failure("Invalid tag ID");

        var existing = tagRepo.GetSingle(tagId);
        if (existing == null)
            return Result.Failure("Tag not found");

        try
        {
            tagRepo.Delete(tagId);
            logger.LogInformation("Deleted tag ID {TagId}", tagId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tag ID {TagId}", tagId);
            return Result.Failure($"Failed to delete tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes a tag from the taxonomy, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="DeleteTag"/> and behaviourally identical.
    /// A hard delete, and it is <b>not</b> blocked when posts still carry the tag — unlike a category,
    /// a tag is a label rather than a home, so a post simply loses one label. Existence is confirmed
    /// first so deleting an already-deleted tag reports "not found" rather than succeeding
    /// silently.</para>
    /// <para><b>Flow:</b> id guard → await the existence check → await the delete.</para>
    /// <para><b>Side Effects:</b> Deletes one <c>BlogTag</c> row; the post-to-tag link rows go with it
    /// by cascade at the database level. Published <c>/tag/{slug}</c> URLs for the removed tag become
    /// 404s. Logs the deletion — this is the only record that the tag ever existed.</para>
    /// </remarks>
    /// <param name="tagId">Identifier of the tag to remove.</param>
    /// <param name="cancellationToken">Cancels the existence check and the delete. Cancellation
    /// during the existence check faults the task rather than returning a failed <c>Result</c>; only
    /// exceptions raised by the delete itself are converted, exactly as in the synchronous twin.</param>
    /// <returns>Success, or a failure when the tag is unknown or the delete failed.</returns>
    public async Task<Result> DeleteTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        if (tagId <= 0)
            return Result.Failure("Invalid tag ID");

        var existing = await tagRepo.GetSingleAsync(tagId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result.Failure("Tag not found");

        try
        {
            await tagRepo.DeleteAsync(tagId, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Deleted tag ID {TagId}", tagId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete tag ID {TagId}", tagId);
            return Result.Failure($"Failed to delete tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists the tags attached to one post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs both the tag chips under a published post and the
    /// pre-selected set when an author reopens the editor.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post's identifier.</param>
    /// <returns>The post's tags; an empty sequence when it has none or the read failed.</returns>
    public IEnumerable<BlogTag> GetTagsForPost(long postId)
    {
        try
        {
            return tagRepo.GetTagsForPost(postId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags for post ID {PostId}", postId);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Lists the tags attached to one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetTagsForPost"/> and behaviourally
    /// identical. Backs both the tag chips under a published post and the pre-selected set when an
    /// author reopens the editor.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post's identifier.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives an empty sequence.</param>
    /// <returns>The post's tags; an empty sequence when it has none or the read failed.</returns>
    public async Task<IEnumerable<BlogTag>> GetTagsForPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetTagsForPostAsync(postId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting tags for post ID {PostId}", postId);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Replaces a post's entire tag set.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Replace, not merge. The supplied ids become the post's complete
    /// set, so a tag the author removed in the editor is genuinely detached; passing an empty
    /// sequence clears every tag from the post. The tags themselves are untouched — only the link
    /// rows change.</para>
    /// <para><b>Flow:</b> delegate to the repository, which deletes the post's existing links and
    /// inserts the new ones.</para>
    /// <para><b>Side Effects:</b> Rewrites the post's rows in the post-tag link table.</para>
    /// <para><b>Failure is silent, and that is a trap.</b> This method returns <c>void</c>: a
    /// repository failure is logged and swallowed, so the post save reports success while the
    /// author's tag changes are lost. Prefer checking the log — or converting this to
    /// <c>Result</c> — before relying on it in a new flow.</para>
    /// </remarks>
    /// <param name="postId">The post whose tags are being set.</param>
    /// <param name="tagIds">The complete set of tag identifiers the post should carry.</param>
    public void SetTagsForPost(long postId, IEnumerable<long> tagIds)
    {
        try
        {
            tagRepo.SetTagsForPost(postId, tagIds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting tags for post ID {PostId}", postId);
        }
    }

    /// <summary>
    /// Replaces a post's entire tag set, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SetTagsForPost"/> and behaviourally
    /// identical. Replace, not merge. The supplied ids become the post's complete set, so a tag the
    /// author removed in the editor is genuinely detached; passing an empty sequence clears every tag
    /// from the post. The tags themselves are untouched — only the link rows change.</para>
    /// <para><b>Flow:</b> await the repository, which deletes the post's existing links and inserts the
    /// new ones inside one transaction.</para>
    /// <para><b>Side Effects:</b> Rewrites the post's rows in the post-tag link table.</para>
    /// <para><b>Failure is silent, and that is a trap.</b> This method returns a bare <c>Task</c>
    /// carrying no outcome: a repository failure is logged and swallowed, so the post save reports
    /// success while the author's tag changes are lost. The behaviour is preserved deliberately from
    /// the synchronous twin — cancellation is swallowed the same way. Prefer checking the log — or
    /// converting this to <c>Result</c> — before relying on it in a new flow.</para>
    /// </remarks>
    /// <param name="postId">The post whose tags are being set.</param>
    /// <param name="tagIds">The complete set of tag identifiers the post should carry.</param>
    /// <param name="cancellationToken">Cancels the transaction. Like every other failure on this
    /// path, cancellation is logged and swallowed rather than surfaced to the caller.</param>
    /// <returns>A task that completes when the repository has finished, whether or not it
    /// succeeded.</returns>
    public async Task SetTagsForPostAsync(long postId, IEnumerable<long> tagIds, CancellationToken cancellationToken = default)
    {
        try
        {
            await tagRepo.SetTagsForPostAsync(postId, tagIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting tags for post ID {PostId}", postId);
        }
    }

    /// <summary>
    /// Gets one page of published posts carrying a tag.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Published only — the repository's SQL applies that filter, so
    /// the anonymous <c>/tag/{slug}</c> page cannot expose a draft. Paging is done in the database
    /// rather than in memory, so a popular tag does not load its whole post set per request.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Caller contract:</b> the arguments are passed through unclamped. Pair this with
    /// <see cref="GetPostCountByTag"/> and derive the offset from a validated page number; never
    /// bind <paramref name="pageSize"/> straight from a query string.</para>
    /// </remarks>
    /// <param name="tagId">The tag to filter by.</param>
    /// <param name="pageSize">Number of posts to return.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>The page of published posts; an empty sequence if the read failed.</returns>
    public IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset)
    {
        try
        {
            return tagRepo.GetPostsByTag(tagId, pageSize, offset);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for tag ID {TagId}", tagId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets one page of published posts carrying a tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostsByTag"/> and behaviourally
    /// identical. Published only — the repository's SQL applies that filter, so the anonymous
    /// <c>/tag/{slug}</c> page cannot expose a draft or a soft-deleted post. Paging is done in the
    /// database rather than in memory, so a popular tag does not load its whole post set per
    /// request.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Caller contract:</b> the arguments are passed through unclamped. Pair this with
    /// <see cref="GetPostCountByTagAsync"/> and derive the offset from a validated page number; never
    /// bind <paramref name="pageSize"/> straight from a query string.</para>
    /// </remarks>
    /// <param name="tagId">The tag to filter by.</param>
    /// <param name="pageSize">Number of posts to return.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives an empty sequence.</param>
    /// <returns>The page of published posts; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsByTagAsync(long tagId, int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetPostsByTagAsync(tagId, pageSize, offset, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting posts for tag ID {TagId}", tagId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Counts the posts carrying a tag, for the pager.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Must agree with <see cref="GetPostsByTag"/>'s filter or the
    /// pager will advertise a last page that renders empty.</para>
    /// <para><b>Side Effects:</b> None beyond logging. A read failure returns 0, which collapses the
    /// pager rather than throwing.</para>
    /// </remarks>
    /// <param name="tagId">The tag to count against.</param>
    /// <returns>The number of matching posts; 0 if the read failed.</returns>
    public int GetPostCountByTag(long tagId)
    {
        try
        {
            return tagRepo.GetPostCountByTag(tagId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post count for tag ID {TagId}", tagId);
            return 0;
        }
    }

    /// <summary>
    /// Counts the posts carrying a tag, for the pager, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostCountByTag"/> and behaviourally
    /// identical. Must agree with <see cref="GetPostsByTagAsync"/>'s published, non-deleted filter or
    /// the pager will advertise a last page that renders empty; the repository applies the same filter
    /// to both.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to zero on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging. A read failure returns 0, which collapses the
    /// pager rather than throwing.</para>
    /// </remarks>
    /// <param name="tagId">The tag to count against.</param>
    /// <param name="cancellationToken">Cancels the query; cancellation surfaces as the same logged
    /// failure any other read error would, so the caller receives 0.</param>
    /// <returns>The number of matching posts; 0 if the read failed.</returns>
    public async Task<int> GetPostCountByTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await tagRepo.GetPostCountByTagAsync(tagId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting post count for tag ID {TagId}", tagId);
            return 0;
        }
    }
}
