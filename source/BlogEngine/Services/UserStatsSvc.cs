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
/// returns <c>Result</c>, so pages surface failures without exception handling. Call the
/// <c>…Async</c> members — the synchronous twins are retained only until the last caller migrates.</para>
///
/// <para><b>Async conversion (REQ-NFR-026 stage 3):</b> every public member that performs I/O now has
/// an <c>…Async</c> twin sitting directly beneath it, routing through the repository's genuinely
/// asynchronous members with the caller's <c>CancellationToken</c> flowed all the way into the Dapper
/// command. Each twin mirrors its synchronous counterpart exactly — the same guards, the same filters,
/// the same display order, the same swallow-and-log on reads and the same <c>Result</c> failure
/// messages on writes — so migrating a call site changes only which thread the round trip parks, never
/// what the caller observes. <c>Result&lt;T&gt;</c> is unaffected by the conversion: it models the
/// expected-failure axis and <c>Task</c> models the completion axis, so a member that returned
/// <c>Result&lt;UserStat&gt;</c> simply returns <c>Task&lt;Result&lt;UserStat&gt;&gt;</c>.
/// <b>The synchronous surface above is pending deletion in stage 4</b> — do not add new callers of it;
/// <see cref="ValidateStat"/> is exempt because it is a pure validator that performs no I/O and so
/// needs no asynchronous twin.</para>
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
    /// Lists every statistic belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetStatsForUser"/> and identical to
    /// it in every observable way. A non-positive identifier is not a database question and never
    /// reaches the repository. A read failure returns an empty list rather than throwing, so a database
    /// problem degrades the resume section instead of the whole page. Ordering is the repository's —
    /// rows come back ordered by <c>DisplayOrder</c> ascending and are never re-sorted here.</para>
    /// <para><b>Flow:</b> guard the identifier → await the repository → log and degrade to an empty
    /// sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation surfaces as the same empty
    /// list a failed read produces, because the catch swallows every exception.</param>
    /// <returns>The user's statistics, or an empty list.</returns>
    public async Task<IEnumerable<UserStat>> GetStatsForUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return Enumerable.Empty<UserStat>();
        }

        try
        {
            return await userStatsRepo.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
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
    /// Lists a user's statistics within one category, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetStatsForCategory"/>. Categories
    /// group the resume's About and Community blocks; a blank category is treated as "no filter
    /// applies" and returns nothing rather than everything. Matching is exact and the column is
    /// nullable, so an uncategorised statistic never matches. Ordering is the repository's —
    /// <c>DisplayOrder</c> ascending, never re-sorted here.</para>
    /// <para><b>Flow:</b> guard the identifier and the category → await the repository → log and
    /// degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics.</param>
    /// <param name="category">The category to filter by.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation surfaces as the same empty
    /// list a failed read produces, because the catch swallows every exception.</param>
    /// <returns>The matching statistics, or an empty list.</returns>
    public async Task<IEnumerable<UserStat>> GetStatsForCategoryAsync(long userId, string category, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(category))
        {
            return Enumerable.Empty<UserStat>();
        }

        try
        {
            return await userStatsRepo.GetByUserIdAndCategoryAsync(userId, category, cancellationToken).ConfigureAwait(false);
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
    /// Reads a single statistic by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetStat"/>. Used by the edit form
    /// to load the row being changed. Both "no such statistic" and "the lookup failed" surface as
    /// <c>null</c>; the failure case is distinguished in the log, not in the return value.</para>
    /// <para><b>Flow:</b> guard the identifier → await the repository → log and return <c>null</c> on
    /// failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <param name="statId">The statistic's identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancellation surfaces as the same
    /// <c>null</c> a failed read produces, because the catch swallows every exception.</param>
    /// <returns>The statistic, or null when it does not exist.</returns>
    public async Task<UserStat?> GetStatAsync(long statId, CancellationToken cancellationToken = default)
    {
        if (statId <= 0)
        {
            return null;
        }

        try
        {
            return await userStatsRepo.GetByIdAsync(statId, cancellationToken).ConfigureAwait(false);
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
    /// Creates a new statistic for a user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="CreateStat"/>. Label and value are
    /// both required and length-bounded to match the column widths; a statistic with no owner is
    /// meaningless and is rejected. Validation failures are expected outcomes and come back as a failed
    /// <c>Result</c> carrying the validator's own message; only an unexpected persistence error is
    /// caught, logged and converted.</para>
    /// <para><b>Flow:</b> validate → await the insert → echo back the generated identifier → wrap in
    /// <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>UserStats</c> row and assigns its generated
    /// <c>StatId</c> back onto <paramref name="stat"/>, which is mutated in place. Writes an
    /// information or error log entry.</para>
    /// </remarks>
    /// <param name="stat">The statistic to create; mutated with its generated identifier.</param>
    /// <param name="cancellationToken">Cancels the insert; a cancellation faults the awaited call and
    /// is caught by the same handler as any other failure, yielding a failed <c>Result</c>.</param>
    /// <returns>Success carrying the persisted statistic, or a failure describing the problem.</returns>
    public async Task<Result<UserStat>> CreateStatAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        var validation = ValidateStat(stat);
        if (validation.IsFailure)
        {
            return Result<UserStat>.Failure(validation.ErrorMessage);
        }

        try
        {
            stat.StatId = await userStatsRepo.CreateAsync(stat, cancellationToken).ConfigureAwait(false);
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
    /// Updates an existing statistic, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="UpdateStat"/>. The row must already
    /// exist — an update against a statistic deleted in another tab is reported as "Statistic not
    /// found" rather than silently creating one. Validation runs before the identifier check, so a
    /// statistic that is both invalid and unkeyed reports the validation message, exactly as the
    /// synchronous twin does.</para>
    /// <para><b>Flow:</b> validate → check the identifier → confirm existence → await the write → wrap
    /// in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one <c>UserStats</c> row. Writes an information or error log
    /// entry.</para>
    /// </remarks>
    /// <param name="stat">The statistic carrying updated values.</param>
    /// <param name="cancellationToken">Cancels the existence check and the update; a cancellation
    /// faults the awaited call and is caught by the same handler as any other failure, yielding a
    /// failed <c>Result</c>.</param>
    /// <returns>Success carrying the saved statistic, or a failure describing the problem.</returns>
    public async Task<Result<UserStat>> UpdateStatAsync(UserStat stat, CancellationToken cancellationToken = default)
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
            return await SaveExistingAsync(stat, cancellationToken).ConfigureAwait(false);
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
    /// Creates or updates a statistic depending on whether it already has an identifier, without
    /// blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="SaveStat"/>. A non-positive
    /// <c>StatId</c> means the statistic has never been persisted, so a single admin form can bind one
    /// method to its save button regardless of mode.</para>
    /// <para><b>Flow:</b> guard against <c>null</c> → inspect the key → delegate to
    /// <see cref="CreateStatAsync"/> or <see cref="UpdateStatAsync"/>. Pure delegation, so the
    /// delegate's task is returned directly rather than awaited, and the <c>null</c> guard — which
    /// performs no I/O — is wrapped with <c>Task.FromResult</c>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated member — one insert or one update.</para>
    /// </remarks>
    /// <param name="stat">The statistic to persist.</param>
    /// <param name="cancellationToken">Cancels the delegated operation.</param>
    /// <returns>Success carrying the saved statistic, or a failure describing the problem.</returns>
    public Task<Result<UserStat>> SaveStatAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        if (stat == null)
        {
            return Task.FromResult(Result<UserStat>.Failure("Statistic cannot be null"));
        }

        return stat.StatId <= 0
            ? CreateStatAsync(stat, cancellationToken)
            : UpdateStatAsync(stat, cancellationToken);
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
    /// Deletes a statistic, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="DeleteStat"/>. Deleting an absent
    /// statistic is reported as a failure so the admin page can tell the user the row had already gone
    /// — the repository's delete is a silent no-op on an unknown key, which is why existence is
    /// confirmed first. That confirmation sits inside the <c>try</c>, so a failed lookup is reported as
    /// a delete failure rather than as "not found".</para>
    /// <para><b>Flow:</b> validate the identifier → await the existence check → await the delete → wrap
    /// in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Removes one <c>UserStats</c> row. Writes an information or error log
    /// entry.</para>
    /// </remarks>
    /// <param name="statId">Identifier of the statistic to remove.</param>
    /// <param name="cancellationToken">Cancels the existence check and the delete; a cancellation
    /// faults the awaited call and is caught by the same handler as any other failure, yielding a
    /// failed <c>Result</c>.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    public async Task<Result> DeleteStatAsync(long statId, CancellationToken cancellationToken = default)
    {
        if (statId <= 0)
        {
            return Result.Failure("Invalid statistic id");
        }

        try
        {
            if (await userStatsRepo.GetByIdAsync(statId, cancellationToken).ConfigureAwait(false) == null)
            {
                return Result.Failure("Statistic not found");
            }

            await userStatsRepo.DeleteAsync(statId, cancellationToken).ConfigureAwait(false);
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
    /// Rewrites display order for a user's statistics, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="ReorderStats"/>. The supplied
    /// identifiers define the new order; position in the sequence becomes <c>DisplayOrder</c>, so the
    /// caller never computes order numbers itself. The success log reports the number of identifiers
    /// supplied, not the number of rows actually written — an identifier belonging to another user is
    /// skipped and still counted.</para>
    /// <para><b>Flow:</b> validate the user and the list → await each load, ownership check and stamp
    /// in turn → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row per accepted identifier. Writes an information or
    /// error log entry, plus a warning per skipped identifier.</para>
    /// <para><b>Authorization:</b> statistics that do not belong to <paramref name="userId"/> are
    /// skipped, so a forged identifier list cannot reorder someone else's resume.</para>
    /// <para><b>Atomicity:</b> the updates are separate statements with no enclosing transaction, so a
    /// failure part-way through leaves the earlier rows renumbered — the same behaviour the
    /// synchronous twin has always had.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics being reordered.</param>
    /// <param name="orderedStatIds">Statistic identifiers in their new display order.</param>
    /// <param name="cancellationToken">Cancels the loads and the updates; a cancellation faults the
    /// awaited call and is caught by the same handler as any other failure, yielding a failed
    /// <c>Result</c> with rows already written left in place.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    public async Task<Result> ReorderStatsAsync(long userId, IReadOnlyList<long> orderedStatIds, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || orderedStatIds == null || orderedStatIds.Count == 0)
        {
            return Result.Failure("A user and at least one statistic are required");
        }

        try
        {
            await ApplyOrderAsync(userId, orderedStatIds, cancellationToken).ConfigureAwait(false);
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
    /// Writes an update once the target row has been confirmed to exist, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="SaveExisting"/>. Exceptions are
    /// deliberately left to propagate — <see cref="UpdateStatAsync"/> owns the <c>try</c> that converts
    /// them into a failed <c>Result</c>.</para>
    /// <para><b>Flow:</b> await the existence check → await the update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one <c>UserStats</c> row; writes an information log entry.</para>
    /// </remarks>
    /// <param name="stat">The statistic carrying updated values.</param>
    /// <param name="cancellationToken">Cancels the existence check and the update.</param>
    /// <returns>Success carrying the saved statistic, or a not-found failure.</returns>
    private async Task<Result<UserStat>> SaveExistingAsync(UserStat stat, CancellationToken cancellationToken)
    {
        if (await userStatsRepo.GetByIdAsync(stat.StatId, cancellationToken).ConfigureAwait(false) == null)
        {
            return Result<UserStat>.Failure("Statistic not found");
        }

        await userStatsRepo.UpdateAsync(stat, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Stamps a new display order onto each statistic the caller listed, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="ApplyOrder"/>. Statistics belonging
    /// to another user are skipped with a warning, so a forged identifier list cannot reorder someone
    /// else's resume. Exceptions are deliberately left to propagate — <see cref="ReorderStatsAsync"/>
    /// owns the <c>try</c> that converts them into a failed <c>Result</c>.</para>
    /// <para><b>Flow:</b> for each position in turn, await the load, check ownership, stamp the new
    /// order and await the update. The rows are processed sequentially rather than concurrently
    /// because each iteration takes its own connection and the order of the writes is the point.</para>
    /// <para><b>Side Effects:</b> Updates one row per accepted identifier; writes a warning per skipped
    /// identifier.</para>
    /// </remarks>
    /// <param name="userId">Owner of the statistics being reordered.</param>
    /// <param name="orderedStatIds">Statistic identifiers in their new display order.</param>
    /// <param name="cancellationToken">Cancels the loads and the updates.</param>
    /// <returns>A task that completes when every listed identifier has been processed.</returns>
    private async Task ApplyOrderAsync(long userId, IReadOnlyList<long> orderedStatIds, CancellationToken cancellationToken)
    {
        for (var position = 0; position < orderedStatIds.Count; position++)
        {
            var stat = await userStatsRepo.GetByIdAsync(orderedStatIds[position], cancellationToken).ConfigureAwait(false);
            if (stat == null || stat.UserId != userId)
            {
                logger.LogWarning("Skipped statistic {StatId} not owned by user {UserId}",
                    orderedStatIds[position], userId);
                continue;
            }

            stat.DisplayOrder = position;
            await userStatsRepo.UpdateAsync(stat, cancellationToken).ConfigureAwait(false);
        }
    }
}
