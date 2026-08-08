using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Supplies the admin dashboard's aggregate counts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Completes REQ-FN-036 (BRD-62). The dashboard previously rendered hardcoded
/// constants for users, comments and subscribers because no service produced them all; this is the
/// single call that does.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The dashboard calls <see cref="GetAdminCountsAsync"/> on first render.</item>
///   <item><c>IAdminCountsRepo</c> runs one aggregate statement covering posts, taxonomy, users,
///         images, comments, subscribers, newsletters and total post views.</item>
///   <item>A failure is logged and a zeroed value returned, so a database hiccup blanks the tiles
///         rather than breaking the whole admin screen.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>IAdminCountsRepo</c> and <c>ILogger</c>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IDashboardService</c>.
/// It supersedes <c>CommentSvc.GetAdminCounts</c>, which is left in place and still returns the
/// subset of counts it always did.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> The token added here is what makes the repository's
/// token useful: a token that stops at the service boundary cancels nothing. It is threaded onto the
/// existing signature rather than added as a second overload, so the dashboard's existing
/// parameterless call keeps compiling.</para>
/// </remarks>
public class DashboardSvc : IDashboardService
{
    private readonly IAdminCountsRepo adminCountsRepo;
    private readonly ILogger<DashboardSvc> logger;

    /// <summary>
    /// Initializes the dashboard service.
    /// </summary>
    /// <param name="adminCountsRepo">Aggregate-count data access.</param>
    /// <param name="logger">Logger for query failures.</param>
    public DashboardSvc(IAdminCountsRepo adminCountsRepo, ILogger<DashboardSvc> logger)
    {
        this.adminCountsRepo = adminCountsRepo;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await adminCountsRepo.GetAdminCountsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read admin dashboard counts");
            return new AdminCounts();
        }
    }
}
