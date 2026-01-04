using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Repository interface for UserSkill data access operations.
/// </summary>
public interface IUserSkillsRepo : IGenericRepository<UserSkill>
{
    /// <summary>
    /// Gets all skills for a specific user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>List of user skills ordered by display order.</returns>
    IEnumerable<UserSkill> GetByUserId(long userId);

    /// <summary>
    /// Gets skills for a user filtered by category.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="category">Category name to filter.</param>
    /// <returns>List of user skills in the category ordered by display order.</returns>
    IEnumerable<UserSkill> GetByUserIdAndCategory(long userId, string category);

    /// <summary>
    /// Gets a skill by its ID.
    /// </summary>
    /// <param name="skillId">Skill ID.</param>
    /// <returns>UserSkill if found, null otherwise.</returns>
    UserSkill GetById(long skillId);

    /// <summary>
    /// Creates a new skill.
    /// </summary>
    /// <param name="skill">Skill to create.</param>
    /// <returns>The ID of the created skill.</returns>
    long Create(UserSkill skill);

    /// <summary>
    /// Deletes a skill by ID.
    /// </summary>
    /// <param name="skillId">Skill ID to delete.</param>
    void Delete(long skillId);

    /// <summary>
    /// Gets distinct categories for a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>List of distinct category names.</returns>
    IEnumerable<string> GetCategories(long userId);
}
