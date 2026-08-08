using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for star ratings, their double opt-in state and the public aggregates.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns <c>PostRating</c>. Ratings are anonymous — the identity is an email
/// address, not an account — so this contract carries the whole "one rating per address per post,
/// changeable, and counted only once the address is confirmed" rule. Splitting the identity members
/// from the aggregate members is deliberate: the aggregates must never see an unconfirmed score.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Rate — <c>RatingSvc</c> calls <see cref="UpsertByEmailAsync"/>, which inserts or replaces
///         this address's score in one round trip.</item>
///   <item>Confirm — <c>EmailVerificationSvc</c> calls <see cref="MarkEmailVerifiedAsync"/> when the
///         opt-in link is followed; only then does the score join the aggregates.</item>
///   <item>Display — the rating widget reads <see cref="GetStatsByPostAsync"/> (or the separate
///         average/count members), and popular-content lists read
///         <see cref="GetTopRatedPostIdsAsync"/>.</item>
///   <item>Withdraw — <see cref="DeleteByPostAndEmailAsync"/> removes one address's score;
///         <see cref="DeleteAsync"/> removes a row by key for administration.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.PostRatingRepo</c> over Dapper and
/// PostgreSQL, delegating the upsert and the verification flip to the <c>UpsertPostRatingByEmail</c> and
/// <c>MarkRatingEmailVerified</c> stored functions so those rules cannot drift into C#. Consumed by
/// <c>RatingSvc</c> and <c>EmailVerificationSvc</c>.</para>
///
/// <para><b>Usage — the verified/unverified split is the contract.</b> Every aggregate member
/// (<see cref="GetAverageByPostAsync"/>, <see cref="GetCountByPostAsync"/>,
/// <see cref="GetStatsByPostAsync"/>, <see cref="GetTopRatedPostIdsAsync"/>) counts confirmed rows
/// only, so a score whose address never completed double opt-in cannot move a number a reader sees.
/// The identity members (<see cref="GetByPostAndEmailAsync"/>, the upsert, the deletes) ignore that
/// distinction, because a rater must be able to see and change their own pending score. Email matching
/// is case-insensitive throughout. <c>RatingSvc</c> is the layer that converts expected failures into a
/// <c>Result</c>; this contract has none and throws on any data-access failure.</para>
///
/// <para><b>Story:</b> FIX-013 - Star Ratings Implementation (Epic 4, FR15-16)</para>
///
/// <para><b>Async surface (REQ-NFR-026):</b> the members that were already asynchronous now carry a
/// <see cref="CancellationToken"/>; the token was added to the existing signatures rather than
/// introduced on new overloads, because a <c>FooAsync(id, ct = default)</c> sitting beside a
/// <c>FooAsync(id)</c> makes every existing <c>FooAsync(id)</c> call ambiguous at the call site. The
/// remaining blocking members gained <c>…Async</c> twins with default implementations, so an
/// unconverted implementer — including the in-memory test doubles — keeps compiling untouched.</para>
///
/// <para>Those defaults go through <c>RepoSyncBridge</c>, which preserves task semantics faithfully — a
/// pre-cancelled token yields a cancelled task and a thrown exception yields a faulted task.
/// <b>They are still not asynchronous</b>: the operation runs inline on the calling thread, parks it for
/// the whole round trip, and a token cancelled <i>after</i> the call starts has no effect. The five
/// bridged members are <see cref="GetAverageByPostAsync"/>, <see cref="GetCountByPostAsync"/>,
/// <see cref="GetStatsByPostAsync"/>, <see cref="GetTopRatedPostIdsAsync"/> and
/// <see cref="DeleteAsync"/>; the four email-keyed members above them are abstract and genuinely
/// asynchronous in every implementer. <c>PostRatingRepo</c> overrides the bridged five, so a caller that
/// resolves this contract from the container gets real asynchrony — but any implementer that still
/// inherits them is unconverted, however green the build is.</para>
/// </remarks>
public interface IPostRatingRepo : IGenericRepository<PostRating>
{
    /// <summary>
    /// Gets the rating an email address left on a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-023] re-keyed ratings from user id to email, so
    /// this is the identity lookup behind "one rating per email per post, changeable".
    /// Matching is case-insensitive.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="email">The rater's email address; matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The rating row — verified or not — or <c>null</c> when the address has not rated this
    /// post. "Not found" is a normal answer and is never an exception.</returns>
    Task<PostRating?> GetByPostAndEmailAsync(long postId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates the rating an email address has for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>UpsertPostRatingByEmail</c> stored
    /// function so the "one per email" rule is enforced in a single round trip. Verification
    /// is sticky: an address already verified on this row stays verified.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one row in <c>PostRating</c>.</para>
    /// </remarks>
    /// <param name="postId">Post being rated.</param>
    /// <param name="email">The rater's email address.</param>
    /// <param name="rating">Score, 1 to 5.</param>
    /// <param name="userId">Optional signed-in user id.</param>
    /// <param name="isEmailVerified">Whether the address is already confirmed.</param>
    /// <param name="cancellationToken">Cancels the upsert.</param>
    /// <returns>The id of the inserted or updated rating.</returns>
    Task<long> UpsertByEmailAsync(
        long postId,
        string email,
        int rating,
        long? userId,
        bool isEmailVerified,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a pending rating count towards the public aggregates.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to <c>MarkRatingEmailVerified</c>, which only
    /// touches a row that is still unverified.</para>
    /// <para><b>Side Effects:</b> Sets IsEmailVerified and UpdatedOn.</para>
    /// </remarks>
    /// <param name="ratingId">The rating to confirm.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when this call flipped the row, <c>false</c> when it was already verified
    /// <i>or</i> the identifier is unknown — the two are not distinguishable through this member, which
    /// is what makes replaying a confirmation link harmless.</returns>
    Task<bool> MarkEmailVerifiedAsync(long ratingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the rating an email address left on a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Case-insensitive match on the address.</para>
    /// <para><b>Side Effects:</b> Deletes at most one row.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="email">The rater's email address; matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns><c>true</c> when a row was deleted, <c>false</c> when the address had not rated the
    /// post. Deleting nothing is a normal outcome, not an error.</returns>
    Task<bool> DeleteByPostAndEmailAsync(long postId, string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the average rating for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>The mean of the <i>verified</i> scores, or 0 when the post has no verified ratings.
    /// Zero therefore means "unrated", not "rated badly" — the scale starts at 1.</returns>
    double GetAverageByPost(long postId);

    /// <summary>
    /// Gets the total number of ratings for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>The number of <i>verified</i> ratings; zero for an unrated or unknown post. Matches the
    /// population <see cref="GetAverageByPost"/> averages, so the two figures shown side by side always
    /// describe the same set.</returns>
    int GetCountByPost(long postId);

    /// <summary>
    /// Gets rating statistics for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Average and count over the verified ratings, from one statement so the two can never
    /// disagree. A zeroed instance — never <c>null</c> — for an unrated or unknown post.</returns>
    PostRatingStats GetStatsByPost(long postId);

    /// <summary>
    /// Gets top-rated posts for popular content lists.
    /// </summary>
    /// <param name="count">Number of posts to return.</param>
    /// <param name="minRatings">Minimum number of verified ratings a post must have before it can
    /// appear, so one enthusiastic five-star vote cannot top the chart.</param>
    /// <returns>Post IDs ordered by average score descending, then by rating count descending as a
    /// tie-break; an empty sequence — never <c>null</c> — when no post clears the threshold. Ids only:
    /// the caller loads the posts, and a soft-deleted post is not filtered out here.</returns>
    IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1);

    /// <summary>
    /// Deletes a rating by ID.
    /// </summary>
    /// <param name="ratingId">Rating ID to delete. An unknown identifier affects no rows and is a
    /// no-op, not an error.</param>
    void Delete(long ratingId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every blocking member above.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the average of the verified ratings for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unverified rows are excluded, so a score whose address never
    /// completed double opt-in cannot move the number a reader sees.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → AVG over verified rows → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited default observes it only before
    /// the call starts — see the type remarks.</param>
    /// <returns>The average score, or 0 when the post has no verified ratings. Zero means "unrated",
    /// not "rated badly".</returns>
    Task<double> GetAverageByPostAsync(long postId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetAverageByPost(postId), cancellationToken);

    /// <summary>
    /// Gets the number of verified ratings for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts verified rows only, matching the average, so the two
    /// figures shown beside each other always describe the same population.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered COUNT → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited default observes it only before
    /// the call starts.</param>
    /// <returns>The verified rating count; zero for an unrated or unknown post.</returns>
    Task<int> GetCountByPostAsync(long postId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetCountByPost(postId), cancellationToken);

    /// <summary>
    /// Gets the aggregate figures for a post in one round trip, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Average and count come from one statement so the rating widget
    /// costs a single round trip and the two numbers can never disagree.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → aggregate query → first row.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited default observes it only before
    /// the call starts.</param>
    /// <returns>Average and count over the verified ratings; a zeroed instance — never <c>null</c> —
    /// when there are none.</returns>
    Task<PostRatingStats> GetStatsByPostAsync(long postId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetStatsByPost(postId), cancellationToken);

    /// <summary>
    /// Gets the ids of the best-rated posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only verified ratings count, and a post must clear
    /// <paramref name="minRatings"/> of them before it can appear — one enthusiastic five-star vote
    /// should not top the chart.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → grouped HAVING query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="count">Number of posts to return.</param>
    /// <param name="minRatings">Minimum number of verified ratings a post must have before it can
    /// appear.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited default observes it only before
    /// the call starts.</param>
    /// <returns>Post IDs ordered by average score descending, then by rating count descending; an empty
    /// sequence when no post clears the threshold. Ids only — the caller loads the posts.</returns>
    Task<IEnumerable<long>> GetTopRatedPostIdsAsync(
        int count = 10, int minRatings = 1, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetTopRatedPostIds(count, minRatings), cancellationToken);

    /// <summary>
    /// Deletes a rating by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown id affects no rows and is treated as a no-op
    /// rather than an error, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="ratingId">Rating ID to delete. An unknown identifier is a no-op.</param>
    /// <param name="cancellationToken">Cancels the statement. The inherited default observes it only
    /// before the call starts.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op; use <see cref="DeleteByPostAndEmailAsync"/> when
    /// that distinction matters.</returns>
    Task DeleteAsync(long ratingId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => Delete(ratingId), cancellationToken);
}
