namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for post ratings, keyed by the rater's email address.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every SQL statement that touches <c>PostRating</c>.
/// [REQ-FN-023] re-keyed the table from a signed-in user id to an email address, so an
/// anonymous visitor can rate a post once and change that rating later.</para>
///
/// <para><b>Code Flow:</b> Writes go through the <c>UpsertPostRatingByEmail</c> stored
/// function, which enforces "one row per (post, lower(email))" in a single round trip.
/// Aggregate reads count only rows whose address has completed double opt-in, so an
/// unconfirmed rating cannot move the public average.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/>, Dapper, and the
/// stored functions <c>UpsertPostRatingByEmail</c> and <c>MarkRatingEmailVerified</c>
/// from migration script 014.</para>
///
/// <para><b>Usage:</b> Registered per request by <c>EngagementSvcInitializer</c>.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member has an <c>…Async</c> twin carrying a
/// <see cref="CancellationToken"/>, and every one of them opens its connection asynchronously through
/// the protected helpers on <see cref="GenericRepository{TEntity}"/>. The members that were already
/// async took their token on the existing signature rather than on a new overload, because a
/// <c>FooAsync(x, ct = default)</c> beside a <c>FooAsync(x)</c> makes every existing call ambiguous
/// and the error surfaces at the call site, not here.</para>
/// </remarks>
public class PostRatingRepo : GenericRepository<PostRating>, IPostRatingRepo
{
    /// <summary>
    /// The column list shared by every rating read.
    /// </summary>
    private const string RatingColumns =
        "RatingId, PostId, UserId, Email, Rating, IsEmailVerified, CreatedOn, UpdatedOn";

    private const string SelectAllSql =
        "SELECT " + RatingColumns + " FROM PostRating ORDER BY CreatedOn DESC";

    private const string SelectByPostSql =
        "SELECT " + RatingColumns + @" FROM PostRating
           WHERE PostId = @PostId
           ORDER BY CreatedOn DESC";

    private const string SelectByIdSql =
        "SELECT " + RatingColumns + " FROM PostRating WHERE RatingId = @RatingId";

    private const string SelectByPostAndEmailSql =
        "SELECT " + RatingColumns + @" FROM PostRating
           WHERE PostId = @PostId AND LOWER(Email) = LOWER(@Email)";

    private const string SelectPagedSql =
        "SELECT " + RatingColumns + @" FROM PostRating
           ORDER BY CreatedOn DESC
           LIMIT @PageSize OFFSET @OffSet";

    private const string UpsertByEmailSql =
        "SELECT UpsertPostRatingByEmail(@pPostId, @pEmail, @pRating, @pUserId, @pIsEmailVerified)";

    private const string MarkEmailVerifiedSql = "SELECT MarkRatingEmailVerified(@pRatingId)";

    private const string DeleteByPostAndEmailSql =
        "DELETE FROM PostRating WHERE PostId = @PostId AND LOWER(Email) = LOWER(@Email)";

    private const string AverageByPostSql = @"
            SELECT COALESCE(AVG(Rating::DECIMAL), 0)
              FROM PostRating
             WHERE PostId = @PostId AND IsEmailVerified = TRUE";

    private const string CountByPostSql =
        "SELECT COUNT(*) FROM PostRating WHERE PostId = @PostId AND IsEmailVerified = TRUE";

    private const string StatsByPostSql = @"
            SELECT COALESCE(AVG(Rating::DECIMAL), 0) AS AverageRating,
                   COUNT(*) AS RatingCount
              FROM PostRating
             WHERE PostId = @PostId AND IsEmailVerified = TRUE";

    private const string TopRatedPostIdsSql = @"
            SELECT PostId
              FROM PostRating
             WHERE IsEmailVerified = TRUE
             GROUP BY PostId
            HAVING COUNT(*) >= @MinRatings
             ORDER BY AVG(Rating) DESC, COUNT(*) DESC
             LIMIT @Count";

    private const string InsertReturningIdSql = @"
            INSERT INTO PostRating (PostId, UserId, Email, Rating, IsEmailVerified, CreatedOn)
            VALUES (@PostId, @UserId, @Email, @Rating, @IsEmailVerified, @CreatedOn)
            RETURNING RatingId";

    private const string UpdateSql = @"
            UPDATE PostRating
               SET Rating = @Rating,
                   UpdatedOn = @UpdatedOn
             WHERE RatingId = @RatingId";

    private const string DeleteSql = "DELETE FROM PostRating WHERE RatingId = @RatingId";

    /// <summary>
    /// Initializes a new instance of the <see cref="PostRatingRepo"/> class.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public PostRatingRepo(string connectionString) : base(connectionString)
    {
    }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every rating, newest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Administrative view — unverified rows are included, because the
    /// operator reviewing abuse needs to see the scores that are not counting.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All ratings, verified or not.</returns>
    public override async Task<IEnumerable<PostRating>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PostRating>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every rating for one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The raw rows, not the aggregate — the public average comes from
    /// <see cref="GetStatsByPostAsync"/>, which excludes unverified scores.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All ratings for the post, newest first.</returns>
    public override async Task<IEnumerable<PostRating>> GetAllByIdAsync(long postId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return await QueryAsync<PostRating>(SelectByPostSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single rating by id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="ratingId">The rating id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The rating, or <c>null</c>.</returns>
    public override async Task<PostRating?> GetSingleAsync(long ratingId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("RatingId", ratingId);
        return await QueryFirstOrDefaultAsync<PostRating>(
            SelectByIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single rating by its 32-bit id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="ratingId">The rating id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The rating, or <c>null</c>.</returns>
    public override Task<PostRating?> GetIntSingleAsync(int ratingId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(ratingId, cancellationToken);
    }

    /// <summary>
    /// Gets one address's rating of one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The lookup behind "you rated this 4 stars" on a return visit.
    /// Inline SQL over <c>PostRating</c> matching <c>PostId = @PostId AND LOWER(Email) = LOWER(@Email)</c>
    /// — the same case-insensitive comparison the <c>UpsertPostRatingByEmail</c> function uses for its
    /// uniqueness rule, so this read can never miss the row the upsert would have found. A blank
    /// address short circuits to <c>null</c> without a round trip.</para>
    /// <para><b>Projection:</b> the full <c>RatingColumns</c> set — <c>RatingId, PostId, UserId,
    /// Email, Rating, IsEmailVerified, CreatedOn, UpdatedOn</c> — so the caller can tell a parked
    /// unverified score from a counted one.</para>
    /// <para><b>Flow:</b> guard the blank address → trim → helper opens the connection asynchronously →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The rated post.</param>
    /// <param name="email">The rater's address; blank or whitespace yields <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The rating, or <c>null</c> when that address has not rated that post.</returns>
    public async Task<PostRating?> GetByPostAndEmailAsync(long postId, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("Email", email.Trim());
        return await QueryFirstOrDefaultAsync<PostRating>(
            SelectByPostAndEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or replaces one address's rating of a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The primary write path, and the only one that honours the
    /// one-row-per-(post, lower(email)) rule. It calls the stored function
    /// <c>SELECT UpsertPostRatingByEmail(@pPostId, @pEmail, @pRating, @pUserId, @pIsEmailVerified)</c>
    /// (migration script 014) rather than doing a read-then-write here: the check and the write happen
    /// inside one statement, so two concurrent submissions from the same address cannot both see "no
    /// existing row" and insert twice. The function returns the id of the row it created or updated.</para>
    /// <para><b>Parameter shapes:</b> the score is cast to <c>short</c> because the function declares
    /// <c>smallint</c> and PostgreSQL resolves overloads strictly — passing an <c>int</c> is the same
    /// class of mismatch as the <c>timestamptz</c> trap and would fail at runtime with <c>42883</c>,
    /// not at compile time. <c>pUserId</c> is a genuine <c>long?</c>: an anonymous rater binds SQL
    /// <c>NULL</c> and is still recorded.</para>
    /// <para><b>Flow:</b> trim the address → bind → helper opens the connection asynchronously →
    /// <c>QuerySingleAsync</c>, since the function always yields exactly one row.</para>
    /// <para><b>Side Effects:</b> Adds or updates exactly one row in <c>PostRating</c>. Passing
    /// <paramref name="isEmailVerified"/> as <c>true</c> makes the score count towards the public
    /// average immediately, so only the verification path should do that.</para>
    /// </remarks>
    /// <param name="postId">The post being rated.</param>
    /// <param name="email">The rater's address; trimmed before binding.</param>
    /// <param name="rating">The score, narrowed to <c>smallint</c> for the function signature.</param>
    /// <param name="userId">The signed-in user, or <c>null</c> for an anonymous rater.</param>
    /// <param name="isEmailVerified">Whether the score counts towards the public average at once.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The id of the inserted or updated rating row.</returns>
    public async Task<long> UpsertByEmailAsync(
        long postId,
        string email,
        int rating,
        long? userId,
        bool isEmailVerified,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pPostId", postId);
        parameters.Add("pEmail", email?.Trim());
        parameters.Add("pRating", (short)rating);
        parameters.Add("pUserId", userId);
        parameters.Add("pIsEmailVerified", isEmailVerified);
        return await QuerySingleAsync<long>(
            UpsertByEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Promotes a parked rating to a counted one, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Calls <c>SELECT MarkRatingEmailVerified(@pRatingId)</c> (migration
    /// script 014). This is the single moment a score starts moving the public average, because every
    /// aggregate here filters on <c>IsEmailVerified = TRUE</c>. The function owns the transition so
    /// the flag and its stamp are written together. It returns the number of rows it touched, so a
    /// stale or already-consumed verification link yields <c>0</c> and this returns <c>false</c>
    /// rather than throwing.</para>
    /// <para><b>Flow:</b> bind <c>pRatingId</c> → helper opens the connection asynchronously →
    /// <c>QuerySingleAsync</c> (the function always yields one row) → compare to zero.</para>
    /// <para><b>Side Effects:</b> Makes one rating count towards <see cref="GetAverageByPostAsync"/>,
    /// <see cref="GetCountByPostAsync"/> and <see cref="GetStatsByPostAsync"/>.</para>
    /// </remarks>
    /// <param name="ratingId">The rating whose address was confirmed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when a row was updated; <c>false</c> for an unknown or already-verified id.</returns>
    public async Task<bool> MarkEmailVerifiedAsync(long ratingId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pRatingId", ratingId);
        var affected = await QuerySingleAsync<long>(
            MarkEmailVerifiedSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Withdraws one address's rating of one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A hard delete from <c>PostRating</c> keyed on the same
    /// <c>PostId</c> + <c>LOWER(Email)</c> pair the upsert treats as unique, so it removes exactly the
    /// row a re-rating would have replaced — never more. The WHERE clause carries both predicates; a
    /// blank address short circuits to <c>false</c> before any statement is built, which is what stops
    /// an empty address from ever widening the match.</para>
    /// <para><b>Flow:</b> guard the blank address → trim → helper opens the connection asynchronously →
    /// execute DELETE → report whether a row went.</para>
    /// <para><b>Side Effects:</b> Permanently removes one rating; the post's average and count both
    /// move if the row was verified.</para>
    /// </remarks>
    /// <param name="postId">The rated post.</param>
    /// <param name="email">The rater's address; blank or whitespace yields <c>false</c>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns><c>true</c> when a row was deleted; <c>false</c> when there was nothing to delete.</returns>
    public async Task<bool> DeleteByPostAndEmailAsync(long postId, string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("Email", email.Trim());
        var affected = await ExecuteAsync(
            DeleteByPostAndEmailSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Gets a post's public average score, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>AVG(Rating::DECIMAL)</c> over <c>PostRating</c> filtered to
    /// <c>IsEmailVerified = TRUE</c> — an unconfirmed score can never move a number a reader acts on.
    /// The explicit <c>::DECIMAL</c> cast matters: averaging a <c>smallint</c> column without it gives
    /// PostgreSQL's exact-numeric result but the cast makes the intent unambiguous and keeps the
    /// value away from integer truncation. <c>COALESCE(…, 0)</c> turns "no verified ratings" into
    /// <c>0</c>, so an unrated post reads as zero stars instead of returning no row.</para>
    /// <para><b>Flow:</b> bind the post id → helper opens the connection asynchronously → scalar read.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post whose average is wanted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The mean verified score, or <c>0</c> when the post has no verified ratings.</returns>
    public async Task<double> GetAverageByPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return await ExecuteScalarAsync<double>(
            AverageByPostSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts a post's verified ratings, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts <c>PostRating</c> rows for the post with
    /// <c>IsEmailVerified = TRUE</c> — the same predicate as
    /// <see cref="GetAverageByPostAsync"/>, so "4.2 from 17 ratings" is always a self-consistent
    /// pair. Parked unverified scores are invisible here by design; use
    /// <see cref="GetAllByIdAsync"/> to see them.</para>
    /// <para><b>Flow:</b> bind the post id → helper opens the connection asynchronously → scalar count.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post whose ratings are being counted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of verified ratings; <c>0</c> when the post has none.</returns>
    public async Task<int> GetCountByPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return await ExecuteScalarAsync<int>(
            CountByPostSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a post's average and rating count in one round trip, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The member the rating widget should call. It is not a convenience
    /// wrapper over <see cref="GetAverageByPostAsync"/> and <see cref="GetCountByPostAsync"/>: taking
    /// both aggregates from one statement means the pair is read at one instant, so the widget can
    /// never render an average computed over a different set of rows than the count beside it. Same
    /// <c>IsEmailVerified = TRUE</c> filter as the two single-value members.</para>
    /// <para><b>Projection:</b> <c>COALESCE(AVG(Rating::DECIMAL), 0) AS AverageRating</c> and
    /// <c>COUNT(*) AS RatingCount</c>, mapped onto <c>PostRatingStats</c>.</para>
    /// <para><b>Flow:</b> bind the post id → helper opens the connection asynchronously → single-row
    /// aggregate → fall back to a zeroed <c>PostRatingStats</c> if no row comes back.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post whose statistics are wanted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics; never <c>null</c> — an unrated post yields zero and zero.</returns>
    public async Task<PostRatingStats> GetStatsByPostAsync(long postId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);

        // The aggregate always yields exactly one row, so the fallback is defensive only: it keeps
        // the rating widget rendering zeroes instead of throwing if the query ever returns none.
        var stats = await QueryFirstOrDefaultAsync<PostRatingStats>(
            StatsByPostSql, parameters, cancellationToken).ConfigureAwait(false);
        return stats ?? new PostRatingStats { AverageRating = 0, RatingCount = 0 };
    }

    /// <summary>
    /// Gets the ids of the best-rated posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Groups verified ratings by post and orders by
    /// <c>AVG(Rating) DESC, COUNT(*) DESC</c>. The <c>HAVING COUNT(*) &gt;= @MinRatings</c> floor is
    /// the point of the member: without it a post with a single five-star vote outranks a post with
    /// fifty four-star votes, and the "top rated" list becomes a list of flukes. The count tie-break
    /// then puts the better-evidenced post first when two averages are equal.</para>
    /// <para><b>Projection:</b> ids only. This deliberately returns no post data — the caller loads
    /// the posts it needs through <c>BlogPostRepo</c>, so the ranking cannot go stale against a
    /// projection that lives here.</para>
    /// <para><b>Flow:</b> bind the floor and the limit → helper opens the connection asynchronously →
    /// grouped query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="count">Maximum ids to return.</param>
    /// <param name="minRatings">Minimum verified ratings a post needs to qualify.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Post ids best-rated first; an empty sequence when no post clears the floor.</returns>
    public async Task<IEnumerable<long>> GetTopRatedPostIdsAsync(
        int count = 10, int minRatings = 1, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("MinRatings", minRatings);
        parameters.Add("Count", count);
        return await QueryAsync<long>(
            TopRatedPostIdsSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of ratings for the administration grid, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a popular post's history never
    /// crosses the wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A page of ratings, newest first.</returns>
    public override async Task<IEnumerable<PostRating>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return await QueryAsync<PostRating>(SelectPagedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a rating row directly, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Prefer <see cref="UpsertByEmailAsync"/>, which honours the
    /// one-per-address rule. This override exists to satisfy the generic repository contract, and is
    /// written in terms of the key-returning insert so the two cannot drift apart.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/> and discard the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PostRating</c>.</para>
    /// </remarks>
    /// <param name="rating">The rating to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(PostRating rating, CancellationToken cancellationToken = default)
    {
        await InsertToGetIdAsync(rating, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a rating row and returns its generated id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The double opt-in flow needs the key back so a verification token
    /// can be hung off the parked score.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PostRating</c>.</para>
    /// </remarks>
    /// <param name="rating">The rating to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated rating id.</returns>
    public override async Task<long> InsertToGetIdAsync(PostRating rating, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(rating), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the score of an existing rating, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the score and its stamp change — re-keying a rating to a
    /// different post or address would break the one-per-address rule the upsert enforces.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>RatingId</c>.</para>
    /// </remarks>
    /// <param name="rating">The rating carrying the new score.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(PostRating rating, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(rating), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a rating by its own id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The administrative delete, keyed on <c>RatingId</c> — used when a
    /// moderator removes an abusive score they are looking at. The reader-facing withdrawal is
    /// <see cref="DeleteByPostAndEmailAsync"/>, which identifies the row the way a reader can.</para>
    /// <para><b>Flow:</b> bind the key → helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Permanently removes one row; an unknown id is a silent no-op, so a
    /// double submit is harmless.</para>
    /// </remarks>
    /// <param name="ratingId">The rating to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    public async Task DeleteAsync(long ratingId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("RatingId", ratingId);
        await ExecuteAsync(DeleteSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every rating, newest first.
    /// </summary>
    /// <returns>All ratings, verified or not.</returns>
    public override IEnumerable<PostRating> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostRating>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every rating for one post.
    /// </summary>
    /// <param name="postId">The post id.</param>
    /// <returns>All ratings for the post, newest first.</returns>
    public override IEnumerable<PostRating> GetAllById(long postId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return connection.Query<PostRating>(SelectByPostSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a single rating by id.
    /// </summary>
    /// <param name="ratingId">The rating id.</param>
    /// <returns>The rating, or null.</returns>
    public override PostRating? GetSingle(long ratingId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("RatingId", ratingId);
        return connection.QueryFirstOrDefault<PostRating>(SelectByIdSql, parameters);
    }

    /// <summary>
    /// Gets a single rating by its 32-bit id.
    /// </summary>
    /// <param name="ratingId">The rating id.</param>
    /// <returns>The rating, or null.</returns>
    public override PostRating? GetIntSingle(int ratingId)
    {
        return GetSingle(ratingId);
    }

    /// <summary>
    /// Gets the average of the verified ratings for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unverified rows are excluded, so a bot cannot shift the
    /// average without completing double opt-in.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <returns>The average score, or 0 when there are no verified ratings.</returns>
    public double GetAverageByPost(long postId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return connection.ExecuteScalar<double>(AverageByPostSql, parameters);
    }

    /// <summary>
    /// Gets the number of verified ratings for a post.
    /// </summary>
    /// <param name="postId">The post id.</param>
    /// <returns>The verified rating count.</returns>
    public int GetCountByPost(long postId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return connection.ExecuteScalar<int>(CountByPostSql, parameters);
    }

    /// <summary>
    /// Gets the aggregate figures for a post in one round trip.
    /// </summary>
    /// <param name="postId">The post id.</param>
    /// <returns>Average and count over the verified ratings.</returns>
    public PostRatingStats GetStatsByPost(long postId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        return connection.QueryFirstOrDefault<PostRatingStats>(StatsByPostSql, parameters)
            ?? new PostRatingStats { AverageRating = 0, RatingCount = 0 };
    }

    /// <summary>
    /// Gets the ids of the best-rated posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only verified ratings count, and a post must clear
    /// <paramref name="minRatings"/> of them before it can appear - one enthusiastic
    /// five-star vote should not top the chart.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="count">Maximum number of posts to return.</param>
    /// <param name="minRatings">Minimum verified ratings required.</param>
    /// <returns>Post ids ordered by average score, then by popularity.</returns>
    public IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("MinRatings", minRatings);
        parameters.Add("Count", count);
        return connection.Query<long>(TopRatedPostIdsSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a page of ratings for the administration grid.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>A page of ratings, newest first.</returns>
    public override IEnumerable<PostRating> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return connection.Query<PostRating>(SelectPagedSql, parameters).ToList();
    }

    /// <summary>
    /// Inserts a rating row directly.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Prefer <see cref="UpsertByEmailAsync"/>, which honours the
    /// one-per-address rule. This override exists to satisfy the generic repository contract.</para>
    /// <para><b>Side Effects:</b> Inserts one row.</para>
    /// </remarks>
    /// <param name="rating">The rating to insert.</param>
    public override void Insert(PostRating rating)
    {
        InsertToGetId(rating);
    }

    /// <summary>
    /// Inserts a rating row and returns its generated id.
    /// </summary>
    /// <param name="rating">The rating to insert.</param>
    /// <returns>The generated rating id.</returns>
    public override long InsertToGetId(PostRating rating)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(rating));
    }

    /// <summary>
    /// Updates the score of an existing rating.
    /// </summary>
    /// <param name="rating">The rating carrying the new score.</param>
    public override void Update(PostRating rating)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(rating));
    }

    /// <summary>
    /// Deletes a rating by id.
    /// </summary>
    /// <param name="ratingId">The rating to delete.</param>
    public void Delete(long ratingId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("RatingId", ratingId);
        connection.Execute(DeleteSql, parameters);
    }

    /// <summary>
    /// Builds the parameter set shared by both insert paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unset <c>CreatedOn</c> defaults to now. The stamp is
    /// normalised to <see cref="DateTimeKind.Unspecified"/> because Npgsql picks the wire type from
    /// the value's Kind: a <c>Utc</c> value is sent as <c>timestamptz</c>, which does not match the
    /// <c>TIMESTAMP</c> column this schema declares. The score is narrowed to <c>short</c> to match
    /// the <c>SMALLINT</c> column.</para>
    /// <para><b>Flow:</b> default the stamp → normalise → bind.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="rating">The rating being inserted.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildInsertParameters(PostRating rating)
    {
        var createdOn = rating.CreatedOn == default ? DateTime.UtcNow : rating.CreatedOn;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", rating.PostId);
        parameters.Add("UserId", rating.UserId);
        parameters.Add("Email", rating.Email);
        parameters.Add("Rating", (short)rating.Rating);
        parameters.Add("IsEmailVerified", rating.IsEmailVerified);
        parameters.Add("CreatedOn", DbTimestamp.AsTimestamp(createdOn));
        return parameters;
    }

    /// <summary>
    /// Builds the parameter set shared by both update paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An absent <c>UpdatedOn</c> is stamped now, because an update that
    /// did not record when it happened is worse than no stamp at all. The value is normalised for the
    /// same reason as the insert stamp.</para>
    /// <para><b>Flow:</b> default the stamp → normalise → bind.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="rating">The rating being written.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildUpdateParameters(PostRating rating)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Rating", (short)rating.Rating);
        parameters.Add("UpdatedOn", DbTimestamp.AsTimestamp(rating.UpdatedOn ?? DateTime.UtcNow));
        parameters.Add("RatingId", rating.RatingId);
        return parameters;
    }
}
