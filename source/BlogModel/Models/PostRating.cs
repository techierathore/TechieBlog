namespace BlogModels;

/// <summary>
/// A single star rating for a blog post, keyed by the rater's email address.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores the post-email-rating relationship behind the star widget.
/// Since [REQ-FN-023] the uniqueness key is <see cref="PostId"/> + <see cref="Email"/>
/// (case-insensitive), not a signed-in user id, so anonymous visitors can rate.</para>
///
/// <para><b>Code Flow:</b> <c>RatingSvc</c> upserts through
/// <c>UpsertPostRatingByEmail</c>; the rating is changeable, so a second submission from the
/// same address updates the existing row rather than adding one.</para>
///
/// <para><b>Dependencies:</b> Persisted by <c>PostRatingRepo</c>; aggregated by
/// <see cref="PostRatingStats"/>.</para>
///
/// <para><b>Usage:</b> Only rows with <see cref="IsEmailVerified"/> set contribute to the
/// public average and count.</para>
///
/// <para><b>Exposure:</b> this is a server-side row, not a view model. It carries a rater's email
/// address, so it must never be serialised to the browser — the public star widget is fed by
/// <see cref="PostRatingStats"/>, which holds aggregates and the caller's own score and no address
/// at all. Anything that returned <see cref="PostRating"/> instances for a post would publish the
/// list of everyone who had rated it.</para>
/// </remarks>
public class PostRating
{
    /// <summary>
    /// Gets or sets the primary key of the rating.
    /// </summary>
    public long RatingId { get; set; }

    /// <summary>
    /// Gets or sets the post being rated.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Gets or sets the optional signed-in user who rated. Null for anonymous raters.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Gets or sets the rater's email address - the identity key for this rating, matched
    /// case-insensitively together with <see cref="PostId"/> by the upsert.
    /// </summary>
    /// <remarks>
    /// Personal data, and the reason this type stays server-side: see the exposure note on the
    /// class. It is also the join key to <see cref="VerifiedEmail"/>, which is what
    /// <see cref="IsEmailVerified"/> reflects.
    /// </remarks>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the score: a whole number of stars, 1 to 5 inclusive. Nothing in the schema
    /// constrains the range, so the bound is enforced by <c>RatingSvc</c> on the way in — a row
    /// written by any other path could hold a value outside it and would skew
    /// <see cref="PostRatingStats.AverageRating"/> without failing anything.
    /// </summary>
    public int Rating { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rater's address has been confirmed
    /// through double opt-in. Unverified ratings are excluded from the aggregates.
    /// </summary>
    public bool IsEmailVerified { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant at which the rating was first submitted.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant of the last change; null if never changed.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }
}

/// <summary>
/// Aggregate rating figures for one post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Returns the average, the count and (optionally) the current
/// visitor's own score in a single round trip so the star widget can render in one pass.</para>
/// <para><b>Usage:</b> <see cref="UserRating"/> is populated only when the caller passes an
/// email address; it is null for an anonymous first-time visitor.</para>
///
/// <para><b>Exposure:</b> this is the type that is safe to send to the browser. It deliberately
/// carries no email address and no per-rater rows — only the two public aggregates and the one
/// score belonging to the visitor who asked. Keep it that way: adding a rater list or an address
/// here would publish it on every post page.</para>
/// </remarks>
public class PostRatingStats
{
    /// <summary>
    /// Gets or sets the average of all verified ratings for the post; 0 when there are none.
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// Gets or sets the number of verified ratings for the post.
    /// </summary>
    public int RatingCount { get; set; }

    /// <summary>
    /// Gets or sets the current visitor's own score; null when they have not rated.
    /// </summary>
    public int? UserRating { get; set; }
}
