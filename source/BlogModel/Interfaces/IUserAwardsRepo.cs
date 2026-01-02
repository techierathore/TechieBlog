using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Repository interface for UserAward data access operations.
/// </summary>
public interface IUserAwardsRepo : IGenericRepository<UserAward>
{
    /// <summary>
    /// Gets all awards for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>List of user awards ordered by display order.</returns>
    IEnumerable<UserAward> GetByUserId(long userId);

    /// <summary>
    /// Gets an award by its ID.
    /// </summary>
    /// <param name="awardId">Award ID.</param>
    /// <returns>UserAward if found, null otherwise.</returns>
    UserAward GetById(long awardId);

    /// <summary>
    /// Creates a new award.
    /// </summary>
    /// <param name="award">Award to create.</param>
    /// <returns>The ID of the created award.</returns>
    long Create(UserAward award);

    /// <summary>
    /// Deletes an award by ID.
    /// </summary>
    /// <param name="awardId">Award ID to delete.</param>
    void Delete(long awardId);
}
