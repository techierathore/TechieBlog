namespace BlogEngine.Services;

/// <summary>
/// The website's own <see cref="ISiteCacheNotifier"/>: does nothing, because there is nothing to do.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> When a post is saved through the website, <c>BlogSvc</c> already calls
/// <c>ServiceCache.InvalidateContent</c> in the SAME process the reader is served from — that path
/// is verified working end to end. Asking the website to refresh its own cache over HTTP after it
/// has already done so in-process would be redundant work with no benefit, so the website's
/// registration of <see cref="ISiteCacheNotifier"/> is this no-op rather than a self-call.</para>
/// <para><b>Code Flow:</b> <c>ManagePost.razor</c> calls <see cref="ISiteCacheNotifier.RefreshAsync"/>
/// unconditionally after a successful save, on both heads. On the website this resolves to this
/// class and returns immediately; on BlogApp it resolves to the real HTTP-calling implementation.
/// The shared page therefore needs no <c>if (running on BlogApp)</c> branch at all.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Registered as the default <see cref="ISiteCacheNotifier"/> in
/// <c>TechieBlog/Program.cs</c>.</para>
/// </remarks>
public sealed class NullSiteCacheNotifier : ISiteCacheNotifier
{
    /// <inheritdoc />
    /// <remarks>
    /// Always returns <see cref="CacheRefreshResult.NotApplicable"/> — this is not a failure to
    /// report, so <c>ManagePost.razor</c> must not surface it as one.
    /// </remarks>
    public Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CacheRefreshResult.NotApplicable);
    }
}
