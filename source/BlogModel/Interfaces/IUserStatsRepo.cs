using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Repository interface for UserStat data access operations.
/// </summary>
public interface IUserStatsRepo : IGenericRepository<UserStat>
{
    /// <summary>
    /// Gets all stats for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>List of user stats ordered by display order.</returns>
    IEnumerable<UserStat> GetByUserId(long userId);

    /// <summary>
    /// Gets stats for a user filtered by category.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter.</param>
    /// <returns>List of user stats in the category ordered by display order.</returns>
    IEnumerable<UserStat> GetByUserIdAndCategory(long userId, string category);

    /// <summary>
    /// Gets a stat by its ID.
    /// </summary>
    /// <param name="statId">Stat ID.</param>
    /// <returns>UserStat if found, null otherwise.</returns>
    UserStat GetById(long statId);

    /// <summary>
    /// Creates a new stat.
    /// </summary>
    /// <param name="stat">Stat to create.</param>
    /// <returns>The ID of the created stat.</returns>
    long Create(UserStat stat);

    /// <summary>
    /// Deletes a stat by ID.
    /// </summary>
    /// <param name="statId">Stat ID to delete.</param>
    void Delete(long statId);
}
