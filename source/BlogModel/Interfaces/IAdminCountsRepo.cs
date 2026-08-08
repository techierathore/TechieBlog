namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for the admin dashboard's aggregate counts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides every dashboard tile number in one round trip (BRD-62), replacing
/// the hardcoded constants the dashboard used to render.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IDashboardService.GetAdminCountsAsync</c> calls <see cref="GetAdminCountsAsync"/>.</item>
///   <item>A single statement of scalar sub-selects fills every property of <c>AdminCounts</c>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.AdminCountsRepo</c> over Dapper
/// and PostgreSQL.</para>
///
/// <para><b>Usage:</b> This supersedes the partial <c>IBlogCommentRepo.GetAdminCounts</c>, which is
/// left in place for backward compatibility.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> The cancellation token was added to the existing
/// signature rather than as a second overload; an <c>Async()</c> / <c>Async(ct)</c> pair makes the
/// existing parameterless calls ambiguous at every call site.</para>
/// </remarks>
public interface IAdminCountsRepo
{
    /// <summary>
    /// Reads every aggregate count the admin dashboard displays.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A fully populated <c>AdminCounts</c>; zeroes on an empty database.</returns>
    Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default);
}
