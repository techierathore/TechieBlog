using System.Data;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="IPostRatingRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the rating tests prove the email re-key and the
/// verified-only aggregate rules without a database.</para>
/// <para><b>Code Flow:</b> <see cref="UpsertByEmailAsync"/> reproduces the
/// <c>UpsertPostRatingByEmail</c> stored function, including its case-insensitive key and its
/// sticky verification flag.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Inspect <see cref="Ratings"/> to assert on what was stored.</para>
/// </remarks>
public class FakePostRatingRepo : IPostRatingRepo
{
    private readonly List<PostRating> ratings = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the ratings this fake currently holds.
    /// </summary>
    public IReadOnlyList<PostRating> Ratings => ratings;

    /// <inheritdoc />
    public Task<PostRating?> GetByPostAndEmailAsync(
        long postId,
        string email,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PostRating?>(Find(postId, email));
    }

    /// <inheritdoc />
    public Task<long> UpsertByEmailAsync(
        long postId,
        string email,
        int rating,
        long? userId,
        bool isEmailVerified,
        CancellationToken cancellationToken = default)
    {
        var existing = Find(postId, email);
        if (existing == null)
        {
            var created = new PostRating
            {
                RatingId = nextId++,
                PostId = postId,
                Email = email,
                Rating = rating,
                UserId = userId,
                IsEmailVerified = isEmailVerified,
                CreatedOn = DateTime.UtcNow
            };
            ratings.Add(created);
            return Task.FromResult(created.RatingId);
        }

        existing.Rating = rating;
        existing.UpdatedOn = DateTime.UtcNow;
        existing.IsEmailVerified = existing.IsEmailVerified || isEmailVerified;
        return Task.FromResult(existing.RatingId);
    }

    /// <inheritdoc />
    public Task<bool> MarkEmailVerifiedAsync(long ratingId, CancellationToken cancellationToken = default)
    {
        var existing = ratings.FirstOrDefault(r => r.RatingId == ratingId);
        if (existing == null || existing.IsEmailVerified)
            return Task.FromResult(false);

        existing.IsEmailVerified = true;
        existing.UpdatedOn = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteByPostAndEmailAsync(
        long postId,
        string email,
        CancellationToken cancellationToken = default)
    {
        var existing = Find(postId, email);
        if (existing == null)
            return Task.FromResult(false);

        ratings.Remove(existing);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public double GetAverageByPost(long postId)
    {
        var verified = Verified(postId);
        return verified.Count == 0 ? 0 : verified.Average(r => r.Rating);
    }

    /// <inheritdoc />
    public int GetCountByPost(long postId) => Verified(postId).Count;

    /// <inheritdoc />
    public PostRatingStats GetStatsByPost(long postId)
    {
        return new PostRatingStats
        {
            AverageRating = GetAverageByPost(postId),
            RatingCount = GetCountByPost(postId)
        };
    }

    /// <inheritdoc />
    public IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1)
    {
        return ratings
            .Where(r => r.IsEmailVerified)
            .GroupBy(r => r.PostId)
            .Where(g => g.Count() >= minRatings)
            .OrderByDescending(g => g.Average(r => r.Rating))
            .Take(count)
            .Select(g => g.Key)
            .ToList();
    }

    /// <inheritdoc />
    public void Delete(long ratingId) => ratings.RemoveAll(r => r.RatingId == ratingId);

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() => throw new NotSupportedException("The fake repository has no database.");

    /// <inheritdoc />
    public long InsertToGetId(PostRating rating)
    {
        rating.RatingId = nextId++;
        ratings.Add(rating);
        return rating.RatingId;
    }

    /// <inheritdoc />
    public void Insert(PostRating rating) => InsertToGetId(rating);

    /// <inheritdoc />
    public void Update(PostRating ratingToUpdate)
    {
        var existing = ratings.FirstOrDefault(r => r.RatingId == ratingToUpdate.RatingId);
        if (existing == null)
            return;

        existing.Rating = ratingToUpdate.Rating;
        existing.UpdatedOn = ratingToUpdate.UpdatedOn;
    }

    /// <inheritdoc />
    public PostRating? GetSingle(long ratingId) => ratings.FirstOrDefault(r => r.RatingId == ratingId);

    /// <inheritdoc />
    public PostRating? GetIntSingle(int ratingId) => GetSingle(ratingId);

    /// <inheritdoc />
    public IEnumerable<PostRating> GetAll() => ratings.ToList();

    /// <inheritdoc />
    public IEnumerable<PostRating> GetPagedData(int pageSize, int offSet) => ratings.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public IEnumerable<PostRating> GetAllById(long postId) => ratings.Where(r => r.PostId == postId).ToList();

    /// <summary>
    /// Finds a rating by post and address, matching the address case-insensitively.
    /// </summary>
    /// <param name="postId">The post id.</param>
    /// <param name="email">The rater's address.</param>
    /// <returns>The rating, or null.</returns>
    private PostRating? Find(long postId, string email)
    {
        return ratings.FirstOrDefault(r =>
            r.PostId == postId &&
            string.Equals(r.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the verified ratings for a post.
    /// </summary>
    /// <param name="postId">The post id.</param>
    /// <returns>The verified ratings.</returns>
    private List<PostRating> Verified(long postId)
    {
        return ratings.Where(r => r.PostId == postId && r.IsEmailVerified).ToList();
    }
}
