using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for the resume's headline statistics (REQ-FN-027, BRD-50/51).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>IUserStatsRepo</c> has been registered since the resume epic, but no
/// service sat above it, so the About and Community figures on <c>/resume</c> could only be
/// populated with direct SQL. This class supplies the validated CRUD an admin maintenance page
/// needs, so the statistics become editable content rather than a database chore.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A maintenance page lists a user's stats, optionally filtered by category.</item>
///   <item>Create and update validate the label, value and owner before touching the repository.</item>
///   <item><see cref="ReorderStats"/> rewrites display order in one pass after a drag-and-drop.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IUserStatsRepo"/>, <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Authorization:</b> none is enforced here. The reads back the public <c>/resume</c>
/// page and are safe to call anonymously; every mutation is reached from an admin maintenance page
/// behind <c>AppPolicies.AdminOnly</c>, and that page owns the policy check. One ownership rule
/// <i>is</i> enforced in code: <see cref="ReorderStats"/> skips any statistic that does not belong
/// to the supplied user, so a forged identifier list cannot reorder someone else's resume. The
/// create, update and delete members have no such check — they trust the caller's id — so never
/// expose them from a surface where the user id is attacker-controlled.</para>
///
/// <para><b>Result contract:</b> expected failures (missing owner, over-length label, unknown id)
/// are returned; unexpected ones are caught, logged with the statistic or user id, and converted.
/// Reads never throw — a failure yields an empty sequence or null, so a database problem degrades
/// the resume section rather than the page. The mutation failure messages interpolate
/// <c>ex.Message</c>, acceptable only because every caller is admin-only.</para>
///
/// <para><b>Usage:</b> Registered transiently alongside the other engine services. Every mutation
/// returns <c>Result</c>, so pages surface failures without exception handling. Synchronous
/// throughout, following the repository it sits on.</para>
/// </remarks>
public class UserStatsSvc
{
    private const int MaxLabelLength = 100;
    private const int MaxValueLength = 50;

    private readonly IUserStatsRepo userStatsRepo;
    private readonly ILogger<UserStatsSvc> logger;

    /// <summary>
    /// Creates the service over the user statistics repository.
    /// </summary>
    /// <param name="userStatsRepo">Persistence for <c>UserStats</c> rows.</param>
    /// <param name="logger">Structured logger for read and write failures.</param>
    public UserStatsSvc(IUserStatsRepo userStatsRepo, ILogger<UserStatsSvc> logger)
    {
        this.userStatsRepo = userStatsRepo ?? throw new ArgumentNullException(nameof(userStatsRepo));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists every statistic belonging to a user, in display order.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A read failure returns an empty list rather than throwing, so a
    /// database problem degrades the resume section instead of the whole page.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics.</param>
    /// <returns>The user's statistics, or an empty list.</returns>
    public IEnumerable<UserStat> GetStatsForUser(long userId)
    {
        if (userId <= 0)
        {
            return Enumerable.Empty<UserStat>();
        }

        try
        {
            return userStatsRepo.GetByUserId(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading statistics for user {UserId}", userId);
            return Enumerable.Empty<UserStat>();
        }
    }

    /// <summary>
    /// Lists a user's statistics within one category, in display order.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Categories group the resume's About and Community blocks; an
    /// empty category is treated as "no filter applies" and returns nothing rather than everything.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics.</param>
    /// <param name="category">The category to filter by.</param>
    /// <returns>The matching statistics, or an empty list.</returns>
    public IEnumerable<UserStat> GetStatsForCategory(long userId, string category)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(category))
        {
            return Enumerable.Empty<UserStat>();
        }

        try
        {
            return userStatsRepo.GetByUserIdAndCategory(userId, category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading statistics for user {UserId} in {Category}", userId, category);
            return Enumerable.Empty<UserStat>();
        }
    }

    /// <summary>
    /// Reads a single statistic by its identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used by the edit form to load the row being changed.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="statId">The statistic's identifier.</param>
    /// <returns>The statistic, or null when it does not exist.</returns>
    public UserStat? GetStat(long statId)
    {
        if (statId <= 0)
        {
            return null;
        }

        try
        {
            return userStatsRepo.GetById(statId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading statistic {StatId}", statId);
            return null;
        }
    }

    /// <summary>
    /// Creates a new statistic for a user.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Label and value are both required and length-bounded to match
    /// the column widths; a statistic with no owner is meaningless and is rejected.</para>
    /// <para><b>Flow:</b> Validate, insert, echo back the generated identifier.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <param name="stat">The statistic to create.</param>
    /// <returns>Success carrying the persisted statistic, or a failure describing the problem.</returns>
    public Result<UserStat> CreateStat(UserStat stat)
    {
        var validation = ValidateStat(stat);
        if (validation.IsFailure)
        {
            return Result<UserStat>.Failure(validation.ErrorMessage);
        }

        try
        {
            stat.StatId = userStatsRepo.Create(stat);
            logger.LogInformation("Created statistic {StatId} for user {UserId}", stat.StatId, stat.UserId);
            return Result<UserStat>.Success(stat);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create statistic for user {UserId}", stat.UserId);
            return Result<UserStat>.Failure($"Failed to create statistic: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing statistic.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row must already exist — an update against a deleted
    /// statistic is reported rather than silently creating one.</para>
    /// <para><b>Flow:</b> Validate, confirm existence, write.</para>
    /// <para><b>Side Effects:</b> Updates one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <param name="stat">The statistic carrying updated values.</param>
    /// <returns>Success carrying the saved statistic, or a failure describing the problem.</returns>
    public Result<UserStat> UpdateStat(UserStat stat)
    {
        var validation = ValidateStat(stat);
        if (validation.IsFailure)
        {
            return Result<UserStat>.Failure(validation.ErrorMessage);
        }

        if (stat.StatId <= 0)
        {
            return Result<UserStat>.Failure("Invalid statistic id");
        }

        try
        {
            return SaveExisting(stat);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update statistic {StatId}", stat.StatId);
            return Result<UserStat>.Failure($"Failed to update statistic: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates or updates a statistic depending on whether it already has an identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets a single admin form handle both add and edit.</para>
    /// <para><b>Side Effects:</b> Writes one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <param name="stat">The statistic to persist.</param>
    /// <returns>Success carrying the saved statistic, or a failure describing the problem.</returns>
    public Result<UserStat> SaveStat(UserStat stat)
    {
        if (stat == null)
        {
            return Result<UserStat>.Failure("Statistic cannot be null");
        }

        return stat.StatId <= 0 ? CreateStat(stat) : UpdateStat(stat);
    }

    /// <summary>
    /// Deletes a statistic.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an absent statistic is reported as a failure so the
    /// admin page can tell the user the row had already gone.</para>
    /// <para><b>Side Effects:</b> Removes one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <param name="statId">Identifier of the statistic to remove.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    public Result DeleteStat(long statId)
    {
        if (statId <= 0)
        {
            return Result.Failure("Invalid statistic id");
        }

        try
        {
            if (userStatsRepo.GetById(statId) == null)
            {
                return Result.Failure("Statistic not found");
            }

            userStatsRepo.Delete(statId);
            logger.LogInformation("Deleted statistic {StatId}", statId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete statistic {StatId}", statId);
            return Result.Failure($"Failed to delete statistic: {ex.Message}");
        }
    }

    /// <summary>
    /// Rewrites display order for a user's statistics.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The supplied identifiers define the new order; position in the
    /// sequence becomes <c>DisplayOrder</c>, so the caller never computes order numbers itself.</para>
    /// <para><b>Flow:</b> Load each statistic, confirm it belongs to the user, stamp its new order.</para>
    /// <para><b>Side Effects:</b> Updates one row per supplied identifier.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics being reordered.</param>
    /// <param name="orderedStatIds">Statistic identifiers in their new display order.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    public Result ReorderStats(long userId, IReadOnlyList<long> orderedStatIds)
    {
        if (userId <= 0 || orderedStatIds == null || orderedStatIds.Count == 0)
        {
            return Result.Failure("A user and at least one statistic are required");
        }

        try
        {
            ApplyOrder(userId, orderedStatIds);
            logger.LogInformation("Reordered {Count} statistics for user {UserId}", orderedStatIds.Count, userId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reorder statistics for user {UserId}", userId);
            return Result.Failure($"Failed to reorder statistics: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the fields common to create and update.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the column constraints in migration 012 so a bad value
    /// is rejected with a readable message rather than a database error.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="stat">The candidate statistic.</param>
    /// <returns>Success, or a failure naming the offending field.</returns>
    public static Result ValidateStat(UserStat stat)
    {
        if (stat == null)
        {
            return Result.Failure("Statistic cannot be null");
        }

        if (stat.UserId <= 0)
        {
            return Result.Failure("A statistic must belong to a user");
        }

        if (string.IsNullOrWhiteSpace(stat.StatLabel) || stat.StatLabel.Length > MaxLabelLength)
        {
            return Result.Failure($"Statistic label is required and must be {MaxLabelLength} characters or fewer");
        }

        if (string.IsNullOrWhiteSpace(stat.StatValue) || stat.StatValue.Length > MaxValueLength)
        {
            return Result.Failure($"Statistic value is required and must be {MaxValueLength} characters or fewer");
        }

        return Result.Success();
    }

    /// <summary>
    /// Writes an update once the target row has been confirmed to exist.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Updates one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <param name="stat">The statistic carrying updated values.</param>
    /// <returns>Success carrying the saved statistic, or a not-found failure.</returns>
    private Result<UserStat> SaveExisting(UserStat stat)
    {
        if (userStatsRepo.GetById(stat.StatId) == null)
        {
            return Result<UserStat>.Failure("Statistic not found");
        }

        userStatsRepo.Update(stat);
        logger.LogInformation("Updated statistic {StatId} for user {UserId}", stat.StatId, stat.UserId);
        return Result<UserStat>.Success(stat);
    }

    /// <summary>
    /// Stamps a new display order onto each statistic the caller listed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Statistics belonging to another user are skipped, so a forged
    /// identifier list cannot reorder someone else's resume.</para>
    /// <para><b>Side Effects:</b> Updates one row per accepted identifier.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics being reordered.</param>
    /// <param name="orderedStatIds">Statistic identifiers in their new display order.</param>
    private void ApplyOrder(long userId, IReadOnlyList<long> orderedStatIds)
    {
        for (var position = 0; position < orderedStatIds.Count; position++)
        {
            var stat = userStatsRepo.GetById(orderedStatIds[position]);
            if (stat == null || stat.UserId != userId)
            {
                logger.LogWarning("Skipped statistic {StatId} not owned by user {UserId}",
                    orderedStatIds[position], userId);
                continue;
            }

            stat.DisplayOrder = position;
            userStatsRepo.Update(stat);
        }
    }
}
