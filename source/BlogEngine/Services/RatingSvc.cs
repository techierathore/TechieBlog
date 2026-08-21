using System.Net.Mail;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Business logic for the star rating system, keyed by email address.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-FN-023] re-keyed ratings from a signed-in user id to an email
/// address, so an anonymous reader can rate a post - once - and change their mind later.</para>
///
/// <para><b>Code Flow:</b> <see cref="SubmitRatingAsync"/> validates the score, makes an
/// anonymous submitter answer a captcha, upserts the rating on (post, email) and - unless the
/// address is already on the verified registry - issues a double opt-in link. Only verified
/// ratings feed the average, the count and the top-rated list, so an unconfirmed score cannot
/// move the public numbers.</para>
///
/// <para><b>The security-relevant rule: an unverified rating must never affect the public
/// average.</b> A rating is stored with a verified flag, and <i>every</i> aggregate read —
/// <see cref="GetAverageRating"/>, <see cref="GetRatingCount"/>,
/// <see cref="GetPostRatingStats"/> and <see cref="GetTopRatedPostIds"/> — goes through repository
/// SQL that filters on it. Without that filter the star rating would be an open ballot box: anyone
/// could type an address they do not own and move a post's score, repeatedly, with different
/// addresses. The parked row is deliberately kept and shown back to <i>its own submitter</i> via
/// <see cref="GetRatingByEmailAsync"/> so the stars they clicked stay selected, but it contributes
/// nothing to any public number until the confirmation link is opened. <b>Any new aggregate query
/// must carry the same filter</b> — that is the one invariant to preserve when changing this
/// class.</para>
///
/// <para><b>One rating per email per post, and it is changeable.</b> Identity is the email address,
/// not the user id, so an anonymous reader can rate. A second submission from the same address
/// <i>updates</i> the first through an upsert rather than adding a second vote, and
/// <see cref="RemoveRatingAsync"/> withdraws it. Case and whitespace: the address is trimmed before
/// the upsert, so uniqueness depends on the repository's own comparison — do not assume a
/// case-difference creates a second rating.</para>
///
/// <para><b>Authorization:</b> none is enforced here and none is required — rating is an anonymous
/// public action. The two gates that stand in for a policy are the <b>captcha</b>, demanded of any
/// submitter without a signed-in user id, and the <b>double opt-in</b>, demanded of any address not
/// already verified. Note the asymmetry: a signed-in user skips the captcha but <i>not</i> the
/// verification check, so a signed-in user whose address has never been confirmed still has their
/// rating parked. Read methods are safe to call from an anonymous page; only
/// <see cref="GetRatingByEmailAsync"/> reveals anything about an individual, and the caller must
/// pass an address the visitor has already proved is theirs rather than one from a query
/// string.</para>
///
/// <para><b>Result contract:</b> expected failures — a score outside 1..5, a malformed address, a
/// failed captcha, no rating to remove — are <i>returned</i> as <c>Result.Failure</c> with a
/// visitor-safe message. Unexpected failures are caught, logged with the post id, and converted
/// into a generic message; nothing throws out of this class.</para>
///
/// <para><b>Dependencies:</b> <see cref="IPostRatingRepo"/>, <see cref="ICaptchaService"/> —
/// which resolves to the rate-limited decorator, so a scripted submitter is capped —
/// <see cref="IEmailVerificationService"/> and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Read methods swallow
/// errors and return neutral values (0, null, empty), because a broken rating widget must never
/// take a blog post down with it — which also means a zero average cannot be distinguished from a
/// read failure without checking the log. Note the mixed sync/async surface: each of the three
/// aggregate reads exists twice, as a blocking member and as an <c>…Async</c> twin (REQ-NFR-026
/// stage 3), while the per-address reads and every mutation are asynchronous only. <b>Call the
/// twins.</b> The blocking members are retained until stage 4 deletes them; the home page's
/// latest-articles grid asks every card for an average and a count, so those two reads were among
/// the last blocking calls left on the most-hit route in the application.</para>
///
/// <para><b>Caching (REQ-NFR-018).</b> The three aggregate reads —
/// <see cref="GetPostRatingStats"/>, <see cref="GetAverageRating"/> and
/// <see cref="GetRatingCount"/> — go through <see cref="ICacheService"/> under
/// <c>CacheTags.Content</c>, keyed by post id. They are the N+1 behind the home page: the
/// latest-articles grid asks each of its cards for an average and a count, so three article cards
/// cost six round trips on every render of a figure that changes when somebody rates a post, which
/// on a personal blog is rarely.</para>
///
/// <para><b>Only the aggregates are cached, never the visitor's own score.</b>
/// <see cref="GetPostRatingStatsForEmailAsync"/> and <see cref="GetRatingByEmailAsync"/> vary by
/// email address and are read straight from the repository every time. Storing either under a
/// post-keyed entry would show one visitor the stars another visitor selected — the cross-user
/// disclosure <see cref="ICacheService"/>'s own remarks warn about.</para>
///
/// <para><b>Each aggregate is cached TWICE, under adjacent keys.</b> The synchronous read stores a
/// value and its <c>…Async</c> twin stores a task, and <see cref="ICacheService"/> reads a type
/// mismatch as a miss — so a shared key would make the two twins evict each other on every
/// alternating call, turning the cache into a permanent miss. Each twin therefore reads under
/// <see cref="ServiceCache.AsyncVariant"/> of its original's key, carrying the SAME tag.</para>
///
/// <para><b>Invalidation:</b> <see cref="SubmitRatingAsync"/> and <see cref="RemoveRatingAsync"/>
/// drop that post's keys by name through <see cref="ServiceCache.InvalidateRatings"/> —
/// precisely, so one reader rating one article does not throw away every cached listing on the
/// site. That helper evicts <b>both</b> members of each pair; a twin that is cached but not
/// invalidated would leave the home page and the post page showing different star counts for the
/// same article until the entry expired. Confirming a rating by email changes the aggregates too
/// (they count verified rows only) and is invalidated from <c>EmailVerificationSvc</c>, which is
/// where that transition happens.</para>
/// </remarks>
public class RatingSvc
{
    /// <summary>Lowest acceptable score.</summary>
    private const int MinimumRating = 1;

    /// <summary>Highest acceptable score.</summary>
    private const int MaximumRating = 5;

    private readonly IPostRatingRepo ratingRepo;
    private readonly ICaptchaService captchaService;
    private readonly IEmailVerificationService emailVerificationService;
    private readonly ILogger<RatingSvc> logger;
    private readonly ICacheService? cacheService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RatingSvc"/> class.
    /// </summary>
    /// <param name="ratingRepo">Rating data access.</param>
    /// <param name="captchaService">Proves an anonymous submitter is human.</param>
    /// <param name="emailVerificationService">Issues and tracks double opt-in confirmations.</param>
    /// <param name="logger">Logger for operational and security events.</param>
    /// <param name="cacheService">
    /// Cache holding the per-post rating aggregates (REQ-NFR-018). Optional: omitting it makes every
    /// read go to the database, which is what a unit test that is not exercising caching wants.
    /// </param>
    public RatingSvc(
        IPostRatingRepo ratingRepo,
        ICaptchaService captchaService,
        IEmailVerificationService emailVerificationService,
        ILogger<RatingSvc> logger,
        ICacheService? cacheService = null)
    {
        this.ratingRepo = ratingRepo;
        this.captchaService = captchaService;
        this.emailVerificationService = emailVerificationService;
        this.logger = logger;
        this.cacheService = cacheService;
    }

    /// <summary>
    /// Records or changes the rating an email address has given a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One rating per address per post; a second submission updates
    /// the first. An anonymous submitter must answer a captcha. An address that has never been
    /// confirmed gets a verification link and its score is parked, uncounted, until the link is
    /// clicked.</para>
    /// <para><b>Flow:</b> validate, captcha, upsert, issue token when unverified.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one rating row; <b>consumes the captcha
    /// challenge</b> (single-use — a retry needs a fresh one) and counts against the client's
    /// captcha rate limits; <b>may send a real confirmation email</b> when the address has never
    /// been verified. The row is written <i>before</i> the mail is attempted, and is deliberately
    /// left in place if the mail fails — a parked rating is invisible and harmless, and keeping it
    /// means the visitor's score survives to be confirmed by a later link.</para>
    /// <para><b>The score does not count yet unless the address was already verified.</b> Check
    /// <c>RatingSubmissionOutcome.IsEmailVerificationRequired</c> before telling the visitor their
    /// rating has been recorded, and do not re-read the public average expecting it to have
    /// moved.</para>
    /// </remarks>
    /// <param name="submission">The visitor's rating submission.</param>
    /// <returns>What the visitor should be told, or a failure carrying a safe message.</returns>
    public async Task<Result<RatingSubmissionOutcome>> SubmitRatingAsync(RatingSubmission submission)
    {
        var validation = ValidateSubmission(submission);
        if (validation.IsFailure)
            return Result<RatingSubmissionOutcome>.Failure(validation.ErrorMessage);

        if (submission.UserId is not > 0)
        {
            var captchaResult = captchaService.Validate(submission.CaptchaChallengeId, submission.CaptchaAnswer);
            if (captchaResult.IsFailure)
                return Result<RatingSubmissionOutcome>.Failure(captchaResult.ErrorMessage);
        }

        try
        {
            var isVerified = await emailVerificationService
                .IsAddressVerifiedAsync(submission.Email).ConfigureAwait(false);
            var ratingId = await ratingRepo.UpsertByEmailAsync(
                submission.PostId, submission.Email.Trim(), submission.Rating,
                submission.UserId, isVerified).ConfigureAwait(false);
            ServiceCache.InvalidateRatings(cacheService, submission.PostId);
            return await FinishSubmissionAsync(submission, ratingId, isVerified).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record a rating for post {PostId}", submission.PostId);
            return Result<RatingSubmissionOutcome>.Failure("Failed to submit rating. Please try again.");
        }
    }

    /// <summary>
    /// Issues the confirmation link, or reports that the score already counts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A verified address is done immediately. An unverified one
    /// keeps its parked row even when the mail cannot be sent - unlike a comment, a stray
    /// unverified rating is invisible and harmless, and keeping it means the visitor's score is
    /// not lost if they confirm through a later link.</para>
    /// <para><b>Side Effects:</b> May send an email.</para>
    /// </remarks>
    /// <param name="submission">The original submission.</param>
    /// <param name="ratingId">The upserted rating id.</param>
    /// <param name="isVerified">Whether the address was already verified.</param>
    /// <returns>The outcome to show the visitor.</returns>
    private async Task<Result<RatingSubmissionOutcome>> FinishSubmissionAsync(
        RatingSubmission submission, long ratingId, bool isVerified)
    {
        if (isVerified)
        {
            logger.LogInformation("Rating {RatingId} recorded for post {PostId}", ratingId, submission.PostId);
            return Result<RatingSubmissionOutcome>.Success(new RatingSubmissionOutcome
            {
                RatingId = ratingId,
                IsEmailVerificationRequired = false,
                Message = "Thank you for rating this post."
            });
        }

        var issued = await emailVerificationService.IssueAsync(
            submission.Email, submission.DisplayName, EmailVerificationPurpose.Rating,
            ratingId, submission.IpAddress).ConfigureAwait(false);

        if (issued.IsFailure)
            return Result<RatingSubmissionOutcome>.Failure(issued.ErrorMessage);

        return Result<RatingSubmissionOutcome>.Success(new RatingSubmissionOutcome
        {
            RatingId = ratingId,
            IsEmailVerificationRequired = true,
            Message = "Almost there - check your inbox and click the link to confirm your rating."
        });
    }

    /// <summary>
    /// Checks the submitted fields.
    /// </summary>
    /// <param name="submission">The submission under test.</param>
    /// <returns>Success when every field is acceptable.</returns>
    private static Result ValidateSubmission(RatingSubmission submission)
    {
        if (submission == null)
            return Result.Failure("No rating was supplied.");

        if (submission.PostId <= 0)
            return Result.Failure("Invalid post ID.");

        if (submission.Rating < MinimumRating || submission.Rating > MaximumRating)
            return Result.Failure("Rating must be between 1 and 5.");

        if (string.IsNullOrWhiteSpace(submission.Email) || !MailAddress.TryCreate(submission.Email.Trim(), out _))
            return Result.Failure("Please enter a valid email address.");

        return Result.Success();
    }

    /// <summary>
    /// Gets the score an address gave a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the parked score too, so a visitor who has not yet
    /// confirmed still sees their own stars selected.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="email">The rater's address.</param>
    /// <returns>The score, or null when the address has not rated this post.</returns>
    public async Task<int?> GetRatingByEmailAsync(long postId, string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var existing = await ratingRepo.GetByPostAndEmailAsync(postId, email).ConfigureAwait(false);
            return existing?.Rating;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting the rating for post {PostId}", postId);
            return null;
        }
    }

    /// <summary>
    /// Gets the aggregate figures for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Average and count over <b>verified ratings only</b> — the
    /// filter lives in the repository's SQL, and it is what stops an unconfirmed submission moving
    /// a public number.</para>
    /// <para><b>Side Effects:</b> None beyond logging. A read failure returns zeroes, which render
    /// as an unrated post rather than an error.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <returns>Average and count over the verified ratings; zeroed on error.</returns>
    public PostRatingStats GetPostRatingStats(long postId)
    {
        try
        {
            return ServiceCache.Read(
                cacheService,
                ServiceCache.RatingStatsKey(postId),
                CacheTags.Content,
                () => ratingRepo.GetStatsByPost(postId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting rating stats for post {PostId}", postId);
            return new PostRatingStats { AverageRating = 0, RatingCount = 0 };
        }
    }

    /// <summary>
    /// Gets the aggregate figures for a post together with one address's own score.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One call for the whole widget: the public average, the
    /// public count and the visitor's own selection. Note the two halves obey different rules —
    /// the aggregates count verified ratings only, while <c>UserRating</c> reflects this visitor's
    /// row whether or not it has been confirmed. That is why a visitor can see their own five
    /// stars selected while the public average has not moved.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Pass only an address the visitor has proved is theirs</b> — supplying an arbitrary
    /// address discloses whether that person rated the post, and what they gave it.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="email">The visitor's address; may be null for a first-time reader.</param>
    /// <returns>Populated statistics; zeroed on error.</returns>
    public async Task<PostRatingStats> GetPostRatingStatsForEmailAsync(long postId, string email)
    {
        try
        {
            var stats = await ratingRepo.GetStatsByPostAsync(postId).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(email))
            {
                var existing = await ratingRepo.GetByPostAndEmailAsync(postId, email).ConfigureAwait(false);
                stats.UserRating = existing?.Rating;
            }

            return stats;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting rating stats with the visitor's score for post {PostId}", postId);
            return new PostRatingStats { AverageRating = 0, RatingCount = 0 };
        }
    }

    /// <summary>
    /// Gets the average of the verified ratings for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Verified ratings only. A post with no verified ratings and a
    /// post whose read failed both return 0 — check <see cref="GetRatingCount"/> to tell "unrated"
    /// from "rated zero", which cannot otherwise be distinguished.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <returns>The average, 0 to 5; 0 on error.</returns>
    public double GetAverageRating(long postId)
    {
        try
        {
            return ServiceCache.Read(
                cacheService,
                ServiceCache.RatingAverageKey(postId),
                CacheTags.Content,
                () => ratingRepo.GetAverageByPost(postId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting average rating for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets the number of verified ratings for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Verified ratings only, so the "based on N ratings" caption
    /// matches the average beside it. Parked submissions are invisible to this count.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <returns>The count; 0 on error.</returns>
    public int GetRatingCount(long postId)
    {
        try
        {
            return ServiceCache.Read(
                cacheService,
                ServiceCache.RatingCountKey(postId),
                CacheTags.Content,
                () => ratingRepo.GetCountByPost(postId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting rating count for post {PostId}", postId);
            return 0;
        }
    }

    // =================================================================================================
    // Async surface — REQ-NFR-026 stage 3. Preferred over the three blocking aggregate reads above.
    //
    // Each twin is written line for line against its synchronous original: the same repository query,
    // the same verified-ratings-only filter (which lives in the repository SQL), the same
    // swallow-and-log read contract, the same neutral value on failure. The only differences are the
    // awaited repository member, the flowed token and the cache key.
    //
    // The cache key is the ONE place they must differ. A synchronous read stores a T and its twin
    // stores a Task<T>; ICacheService treats a type mismatch as a miss, so sharing a key would have
    // the two twins evict each other on every alternating call. ServiceCache.AsyncVariant gives the
    // twin an adjacent key under the SAME tag, and ServiceCache.InvalidateRatings drops both, so the
    // pair can never hold different numbers for one post.
    // =================================================================================================

    /// <summary>
    /// Gets the aggregate figures for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetPostRatingStats"/> and
    /// behaviourally identical to it. Average and count over <b>verified ratings only</b> — the
    /// filter lives in the repository's SQL, and it is what stops an unconfirmed submission moving
    /// a public number.</para>
    /// <para><b>Flow:</b> read through the cache under this twin's own key → await the repository →
    /// log and degrade to zeroes on failure.</para>
    /// <para><b>Side Effects:</b> Populates the cache on a miss; nothing else beyond logging. A read
    /// failure returns zeroes, which render as an unrated post rather than an error.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Average and count over the verified ratings; zeroed on error.</returns>
    public async Task<PostRatingStats> GetPostRatingStatsAsync(
        long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.RatingStatsKey(postId)),
                CacheTags.Content,
                () => ratingRepo.GetStatsByPostAsync(postId, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting rating stats for post {PostId}", postId);
            return new PostRatingStats { AverageRating = 0, RatingCount = 0 };
        }
    }

    /// <summary>
    /// Gets the average of the verified ratings for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetAverageRating"/>. Verified
    /// ratings only. A post with no verified ratings and a post whose read failed both return 0 —
    /// check <see cref="GetRatingCountAsync"/> to tell "unrated" from "rated zero", which cannot
    /// otherwise be distinguished.</para>
    /// <para><b>Flow:</b> read through the cache under this twin's own key → await the repository →
    /// log and degrade to 0 on failure.</para>
    /// <para><b>Side Effects:</b> Populates the cache on a miss; nothing else beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The average, 0 to 5; 0 on error.</returns>
    public async Task<double> GetAverageRatingAsync(long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.RatingAverageKey(postId)),
                CacheTags.Content,
                () => ratingRepo.GetAverageByPostAsync(postId, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting average rating for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets the number of verified ratings for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetRatingCount"/>. Verified ratings
    /// only, so the "based on N ratings" caption matches the average beside it. Parked submissions
    /// are invisible to this count.</para>
    /// <para><b>Flow:</b> read through the cache under this twin's own key → await the repository →
    /// log and degrade to 0 on failure.</para>
    /// <para><b>Side Effects:</b> Populates the cache on a miss; nothing else beyond logging.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count; 0 on error.</returns>
    public async Task<int> GetRatingCountAsync(long postId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.RatingCountKey(postId)),
                CacheTags.Content,
                () => ratingRepo.GetCountByPostAsync(postId, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting rating count for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets the ids of the best-rated posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ranks on verified ratings only, so a post cannot be pushed into
    /// the "top rated" list by unconfirmed submissions. <paramref name="minRatings"/> is the
    /// small-sample guard: a single five-star rating would otherwise outrank a post with fifty
    /// ratings averaging 4.8, so raise it above the default of 1 on a busy site.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Returns ids only — the caller resolves the
    /// posts, and must apply its own published filter before rendering them.</para>
    /// </remarks>
    /// <param name="count">Maximum number of posts to return.</param>
    /// <param name="minRatings">Minimum verified ratings a post needs to qualify.</param>
    /// <returns>Post ids ordered by average score; empty on error.</returns>
    public IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1)
    {
        try
        {
            return ratingRepo.GetTopRatedPostIds(count, minRatings);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting top rated posts");
            return Enumerable.Empty<long>();
        }
    }

    /// <summary>
    /// Removes the rating an address gave a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Removal is keyed by address like every other rating
    /// operation, so a visitor can withdraw a score they left anonymously. Removing a rating that
    /// does not exist is reported as a failure rather than treated as success, so the widget can
    /// tell the visitor there was nothing to withdraw.</para>
    /// <para><b>Flow:</b> require an address → delete by post and address → report whether a row
    /// went.</para>
    /// <para><b>Side Effects:</b> Deletes at most one row, which <b>changes the post's public
    /// average and count</b> if the removed rating had been verified. Logs the removal without the
    /// address. No captcha and no verification are required here — withdrawing is not an
    /// attack.</para>
    /// <para><b>The address is the only credential.</b> A caller must pass an address the visitor
    /// has proved is theirs; passing one from a query string would let anyone delete a stranger's
    /// rating.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <param name="email">The rater's address.</param>
    /// <returns>Success, or a failure message.</returns>
    public async Task<Result> RemoveRatingAsync(long postId, string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure("An email address is required to remove a rating.");

        try
        {
            var isRemoved = await ratingRepo.DeleteByPostAndEmailAsync(postId, email).ConfigureAwait(false);
            if (!isRemoved)
                return Result.Failure("No rating found for this post.");

            ServiceCache.InvalidateRatings(cacheService, postId);

            logger.LogInformation("Rating removed for post {PostId}", postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing the rating for post {PostId}", postId);
            return Result.Failure("Failed to remove rating. Please try again.");
        }
    }
}
