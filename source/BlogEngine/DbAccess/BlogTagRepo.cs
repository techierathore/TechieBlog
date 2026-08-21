using BlogModels;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing tag data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogTag entities, plus the post↔tag junction,
/// using Dapper.</para>
///
/// <para><b>Code Flow:</b> <c>TagSvc</c> and <c>SitemapSvc</c> inject this repository, call an
/// <c>…Async</c> member, and the member routes through the protected helpers on
/// <c>GenericRepository</c>, which open the connection asynchronously and flow the cancellation token
/// into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>Async conversion (REQ-NFR-026, Group C).</b> Every SQL statement is hoisted into a
/// <c>private const</c> shared by both twins, so the async version cannot drift from the synchronous
/// one it replaces. Two members need a connection of their own rather than a helper —
/// <see cref="DeleteAsync"/> issues two statements and <see cref="SetTagsForPostAsync"/> runs a
/// transaction — and both take it from <c>GetOpenConnectionAsync</c>, never from the blocking
/// <c>GetOpenConnection</c>.</para>
/// </remarks>
public class BlogTagRepo : GenericRepository<BlogTag>, IBlogTagRepo
{
    private const string SelectAllSql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            ORDER BY TagName";

    private const string SelectAllWithCountsSql = @"
            SELECT t.TagId, t.TagName, t.Slug,
                   COUNT(p.PostId) as PostCount
            FROM Tag t
            LEFT JOIN PostTag pt ON t.TagId = pt.TagId
            LEFT JOIN BlogPost p ON pt.PostId = p.PostId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY t.TagId, t.TagName, t.Slug
            ORDER BY t.TagName";

    private const string SelectByIdSql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE TagId = @TagId";

    private const string SelectBySlugSql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE Slug = @Slug";

    private const string CountBySlugSql = @"
            SELECT COUNT(1) FROM Tag
            WHERE Slug = @Slug AND TagId != @ExcludeTagId";

    private const string SearchSql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            WHERE TagName ILIKE @Query
            ORDER BY TagName
            LIMIT 10";

    private const string SelectPagedSql = @"
            SELECT TagId, TagName, Slug
            FROM Tag
            ORDER BY TagName
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO Tag (TagName, Slug)
            VALUES (@TagName, @Slug)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING TagId";

    private const string UpdateSql = @"
            UPDATE Tag SET
                TagName = @TagName,
                Slug = @Slug
            WHERE TagId = @TagId";

    private const string DeleteJunctionByTagSql = "DELETE FROM PostTag WHERE TagId = @TagId";

    private const string DeleteTagSql = "DELETE FROM Tag WHERE TagId = @TagId";

    private const string DeleteJunctionByPostSql = "DELETE FROM PostTag WHERE PostId = @PostId";

    private const string InsertJunctionSql = "INSERT INTO PostTag (PostId, TagId) VALUES (@PostId, @TagId)";

    private const string SelectTagsForPostSql = @"
            SELECT t.TagId, t.TagName, t.Slug
            FROM Tag t
            INNER JOIN PostTag pt ON t.TagId = pt.TagId
            WHERE pt.PostId = @PostId
            ORDER BY t.TagName";

    /// <summary>
    /// The public tag archive's listing read. [REQ-FN-057] <c>PublishedOn</c> is projected because
    /// every renderer dates a post as <c>PublishedOn ?? CreatedOn</c>: a column that is never
    /// selected does not make that fallback fire "when appropriate", it makes it fire on every row,
    /// so the tag archive silently dated its cards by when the post was drafted while the home page
    /// and the category archive — reading through <c>BlogPostRepo</c>, which does project it —
    /// dated the same posts by when they went live. Fixing the renderer alone would have changed
    /// nothing at all.
    /// <para>[REQ-UI-059] The <c>ORDER BY</c> is now that same expression rather than
    /// <c>p.CreatedOn DESC</c>. Dating a card by one column while sorting it by another is what put a
    /// post dated "Aug 09" third on this very page, behind cards dated Jul 08 and Jul 01
    /// (<c>tests/.artifacts/e-tag-archive-date-desktop.png</c>). <c>p.PostID DESC</c> is the unique
    /// tiebreaker: this listing is paged with <c>LIMIT</c>/<c>OFFSET</c>, and tied
    /// <c>COALESCE</c> values under a non-deterministic order let one post appear on two pages while
    /// another is never shown at all.</para>
    /// </summary>
    private const string SelectPostsByTagSql = @"
            SELECT p.PostID, p.Title, p.Slug, p.Abstract, p.PostContent, p.CreatedOn, p.UpdatedOn,
                   p.PublishedOn, p.UserID, p.Tags, p.CategoryId, p.FeaturedImage, p.Published,
                   CONCAT(u.FirstName, ' ', u.LastName) as BlogWriter
            FROM BlogPost p
            INNER JOIN PostTag pt ON p.PostID = pt.PostId
            LEFT JOIN BlogUser u ON p.UserID = u.UserId
            WHERE pt.TagId = @TagId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            ORDER BY COALESCE(p.PublishedOn, p.CreatedOn) DESC, p.PostID DESC
            LIMIT @PageSize OFFSET @Offset";

    private const string CountPostsByTagSql = @"
            SELECT COUNT(*) FROM BlogPost p
            INNER JOIN PostTag pt ON p.PostID = pt.PostId
            WHERE pt.TagId = @TagId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public BlogTagRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets all tags ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Alphabetical order is the browsing order the tag cloud and the
    /// admin grid both present, so it is applied in SQL rather than left to each caller.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All tags, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<BlogTag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogTag>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all tags with their post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The count covers published, non-deleted posts only — a draft or a
    /// soft-deleted post must not inflate a number a reader can act on. The joins are LEFT JOINs so an
    /// unused tag still appears, with a count of zero.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → grouped left join → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Tags with the computed PostCount field.</returns>
    public async Task<IEnumerable<BlogTag>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogTag>(SelectAllWithCountsSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all tags for a parent ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Tags are a flat list in this schema — there is no parent
    /// relationship — so the parent filter is ignored and the whole set is returned. The member exists
    /// only to satisfy the generic contract.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="parentId">Ignored; tags have no parent.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All tags.</returns>
    public override Task<IEnumerable<BlogTag>> GetAllByIdAsync(long parentId, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single tag by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tag, or <c>null</c> when no row carries that key.</returns>
    public override async Task<BlogTag?> GetSingleAsync(long tagId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogTag>(
            SelectByIdSql, new { TagId = tagId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single tag by INT ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tag, or <c>null</c> when no row carries that key.</returns>
    public override Task<BlogTag?> GetIntSingleAsync(int tagId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(tagId, cancellationToken);
    }

    /// <summary>
    /// Gets a tag by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The slug is the public identifier used by <c>/tag/{slug}</c>, so an
    /// unknown slug must return <c>null</c> for the page to render its 404 rather than throw. The same
    /// lookup is how <c>TagSvc.GetOrCreateTag</c> decides whether a tag already exists.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tag, or <c>null</c> when the slug is unknown.</returns>
    public async Task<BlogTag?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogTag>(
            SelectBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a tag slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Slugs must be unique because they address a page. The exclusion
    /// parameter lets an update ignore the row being edited, so re-saving a tag without renaming it
    /// does not collide with itself.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeTagId">Tag ID to exclude, for updates.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another tag already uses the slug.</returns>
    public async Task<bool> SlugExistsAsync(string slug, long excludeTagId = 0, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountBySlugSql, new { Slug = slug, ExcludeTagId = excludeTagId }, cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Searches tags by name for autocomplete, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Capped at ten rows in SQL because the result feeds a type-ahead
    /// list, where more suggestions than fit on screen cost a round trip and buy nothing.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → ILIKE query with LIMIT → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">The search text.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>At most ten matching tags, alphabetically.</returns>
    public async Task<IEnumerable<BlogTag>> SearchTagsAsync(string query, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogTag>(
            SearchSql, new { Query = $"%{query}%" }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of tags, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large taxonomy never crosses the wire
    /// in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<BlogTag>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogTag>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Tag</c>.</para>
    /// </remarks>
    /// <param name="tag">The tag to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, new { tag.TagName, tag.Slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a tag and returns the generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key. This is the path the post editor uses when an
    /// author types a tag that does not exist yet.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → INSERT … RETURNING → read the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Tag</c>.</para>
    /// </remarks>
    /// <param name="tag">The tag to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>TagId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, new { tag.TagName, tag.Slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both editable fields are written together; the key is never
    /// updated.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>TagId</c>.</para>
    /// </remarks>
    /// <param name="tag">The tag carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(BlogTag tag, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateSql, new { tag.TagId, tag.TagName, tag.Slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a tag and its post associations, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The junction rows go first: <c>PostTag</c> holds a foreign key to
    /// <c>Tag</c>, so deleting the tag while they exist would be rejected. Deleting an unknown
    /// identifier affects no rows and is treated as a no-op, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → delete junction rows → delete the tag.</para>
    /// <para><b>Side Effects:</b> Removes the tag and every post's association with it.</para>
    /// </remarks>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    /// <returns>A task that completes when both statements have run.</returns>
    public async Task DeleteAsync(long tagId, CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var deleteJunction = new CommandDefinition(
            DeleteJunctionByTagSql, new { TagId = tagId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteJunction).ConfigureAwait(false);

        var deleteTag = new CommandDefinition(
            DeleteTagSql, new { TagId = tagId }, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteTag).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the tags attached to one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An INNER JOIN, not a LEFT JOIN — a post with no tags yields an
    /// empty sequence rather than one null row, which is what the post page's tag strip expects.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → junction join → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post's tags, alphabetically.</returns>
    public async Task<IEnumerable<BlogTag>> GetTagsForPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogTag>(
            SelectTagsForPostSql, new { PostId = postId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the set of tags attached to a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Replace-not-merge: the post's existing associations are removed
    /// before the new ones are written, so unticking a tag in the editor actually detaches it. The
    /// whole replacement runs in one transaction, because a failure between the delete and the inserts
    /// would otherwise leave the post with no tags at all.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → begin transaction → delete existing rows
    /// → insert each new row → commit, or roll back and rethrow.</para>
    /// <para><b>Side Effects:</b> Rewrites this post's rows in <c>PostTag</c>.</para>
    /// </remarks>
    /// <param name="postId">The post identifier.</param>
    /// <param name="tagIds">The tag identifiers to associate; <c>null</c> or empty detaches every tag.</param>
    /// <param name="cancellationToken">Cancels the transaction.</param>
    /// <returns>A task that completes when the transaction has committed.</returns>
    public async Task SetTagsForPostAsync(long postId, IEnumerable<long> tagIds, CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var removeExisting = new CommandDefinition(
                DeleteJunctionByPostSql, new { PostId = postId }, transaction, cancellationToken: cancellationToken);
            await connection.ExecuteAsync(removeExisting).ConfigureAwait(false);

            if (tagIds != null)
            {
                foreach (var tagId in tagIds)
                {
                    var addAssociation = new CommandDefinition(
                        InsertJunctionSql,
                        new { PostId = postId, TagId = tagId },
                        transaction,
                        cancellationToken: cancellationToken);
                    await connection.ExecuteAsync(addAssociation).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Gets a page of published posts carrying one tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs <c>/tag/{slug}</c>; drafts and soft-deleted rows are excluded
    /// in SQL so no caller can leak an unpublished post by forgetting to filter.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → junction join with LIMIT/OFFSET → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts with this tag, most recently published first (REQ-UI-059).</returns>
    public async Task<IEnumerable<BlogPost>> GetPostsByTagAsync(long tagId, int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogPost>(
            SelectPostsByTagSql,
            new { TagId = tagId, PageSize = pageSize, Offset = offset },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the number of published posts carrying one tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Drives the tag page's pager, and matches
    /// <see cref="GetPostsByTagAsync"/> filter for filter so the last page is never empty.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting join.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts with this tag.</returns>
    public async Task<int> GetPostCountByTagAsync(long tagId, CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(
            CountPostsByTagSql, new { TagId = tagId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets all tags ordered by name.
    /// </summary>
    /// <returns>All tags.</returns>
    public override IEnumerable<BlogTag> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets all tags with their post counts.
    /// </summary>
    /// <returns>Tags with the computed PostCount field.</returns>
    public IEnumerable<BlogTag> GetAllWithCounts()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectAllWithCountsSql).ToList();
    }

    /// <summary>
    /// Gets all tags by parent ID (not used for flat tags).
    /// </summary>
    /// <param name="parentId">Ignored; tags have no parent.</param>
    /// <returns>All tags.</returns>
    public override IEnumerable<BlogTag> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single tag by ID.
    /// </summary>
    /// <param name="tagId">The tag identifier.</param>
    /// <returns>The tag, or <c>null</c> when not found.</returns>
    public override BlogTag? GetSingle(long tagId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectByIdSql, new { TagId = tagId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single tag by INT ID.
    /// </summary>
    /// <param name="tagId">The tag identifier.</param>
    /// <returns>The tag, or <c>null</c> when not found.</returns>
    public override BlogTag? GetIntSingle(int tagId)
    {
        return GetSingle(tagId);
    }

    /// <summary>
    /// Gets a tag by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>The tag, or <c>null</c> when the slug is unknown.</returns>
    public BlogTag? GetBySlug(string slug)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectBySlugSql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks whether a tag slug is already taken.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeTagId">Tag ID to exclude, for updates.</param>
    /// <returns><c>true</c> when another tag already uses the slug.</returns>
    public bool SlugExists(string slug, long excludeTagId = 0)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountBySlugSql, new { Slug = slug, ExcludeTagId = excludeTagId }) > 0;
    }

    /// <summary>
    /// Searches tags by name for autocomplete.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <returns>At most ten matching tags.</returns>
    public IEnumerable<BlogTag> SearchTags(string query)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SearchSql, new { Query = $"%{query}%" }).ToList();
    }

    /// <summary>
    /// Gets a page of tags.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<BlogTag> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new tag.
    /// </summary>
    /// <param name="tag">The tag to persist.</param>
    public override void Insert(BlogTag tag)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, new { tag.TagName, tag.Slug });
    }

    /// <summary>
    /// Inserts a tag and returns the generated ID.
    /// </summary>
    /// <param name="tag">The tag to persist.</param>
    /// <returns>The generated <c>TagId</c>.</returns>
    public override long InsertToGetId(BlogTag tag)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, new { tag.TagName, tag.Slug });
    }

    /// <summary>
    /// Updates an existing tag.
    /// </summary>
    /// <param name="tag">The tag carrying the new values.</param>
    public override void Update(BlogTag tag)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, new { tag.TagId, tag.TagName, tag.Slug });
    }

    /// <summary>
    /// Deletes a tag and its post associations.
    /// </summary>
    /// <param name="tagId">The tag identifier.</param>
    public void Delete(long tagId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteJunctionByTagSql, new { TagId = tagId });
        connection.Execute(DeleteTagSql, new { TagId = tagId });
    }

    /// <summary>
    /// Gets the tags attached to one post.
    /// </summary>
    /// <param name="postId">The post identifier.</param>
    /// <returns>The post's tags, alphabetically.</returns>
    public IEnumerable<BlogTag> GetTagsForPost(long postId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogTag>(SelectTagsForPostSql, new { PostId = postId }).ToList();
    }

    /// <summary>
    /// Replaces the set of tags attached to a post.
    /// </summary>
    /// <param name="postId">The post identifier.</param>
    /// <param name="tagIds">The tag identifiers to associate.</param>
    public void SetTagsForPost(long postId, IEnumerable<long> tagIds)
    {
        using var connection = GetOpenConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            connection.Execute(DeleteJunctionByPostSql, new { PostId = postId }, transaction);

            if (tagIds != null)
            {
                foreach (var tagId in tagIds)
                {
                    connection.Execute(InsertJunctionSql, new { PostId = postId, TagId = tagId }, transaction);
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Gets a page of published posts carrying one tag.
    /// </summary>
    /// <param name="tagId">The tag identifier.</param>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Published posts with this tag, most recently published first (REQ-UI-059).</returns>
    public IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogPost>(
            SelectPostsByTagSql, new { TagId = tagId, PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Gets the number of published posts carrying one tag.
    /// </summary>
    /// <param name="tagId">The tag identifier.</param>
    /// <returns>The count of published posts with this tag.</returns>
    public int GetPostCountByTag(long tagId)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountPostsByTagSql, new { TagId = tagId });
    }
}
