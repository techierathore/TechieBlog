namespace BlogModels.Interfaces;

/// <summary>
/// Supplies the admin dashboard's aggregate counts (BRD-62).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces the constants the dashboard used to render with a single real
/// service call covering posts, users, comments and subscribers.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The dashboard calls <see cref="GetAdminCountsAsync"/> on first render.</item>
///   <item><c>IAdminCountsRepo</c> runs one aggregate query.</item>
///   <item>On failure the error is logged and a zeroed value is returned, so the dashboard renders
///         rather than erroring.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>IAdminCountsRepo</c>.</para>
///
/// <para><b>Usage:</b> Implemented by <c>BlogEngine.Services.DashboardSvc</c>, registered transient.</para>
/// </remarks>
public interface IDashboardService
{
    /// <summary>
    /// Reads every aggregate count shown on the admin dashboard.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One query fills posts (total, published, draft), taxonomy,
    /// users, images, comments (total and pending), subscribers (total and active), newsletters and
    /// total post views.</para>
    /// <para><b>Flow:</b> delegate to the repository → return the populated value.</para>
    /// <para><b>Side Effects:</b> None; read-only. Errors are logged, never thrown at the UI.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A fully populated <c>AdminCounts</c>, or a zeroed one if the query failed.</returns>
    Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default);
}
