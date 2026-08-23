using BlogModels.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="SiteUrlResolver.Combine"/> — the seam UAT-024's
/// <c>DesktopLinkOpener</c> and UAT-023 mechanism B's <c>RemoteSiteCacheNotifier</c> both use to
/// turn the operator's configured website address into a real, callable URL.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Neither <c>DesktopLinkOpener</c> nor <c>RemoteSiteCacheNotifier</c> is
/// compiled into <c>TechieBlog.Tests</c> — BlogApp is a MAUI project with no <c>ProjectReference</c>
/// here and degrades to an empty library on this build machine (see the header comment in
/// <c>BlogApp.csproj</c>) — so their behaviour is proved indirectly, through the one piece of logic
/// they share and that DOES live in a referenced, platform-neutral project:
/// <see cref="SiteUrlResolver"/> in <c>BlogModels.Common</c>. A wrong join here (a double slash, or
/// a missing one) is exactly the class of bug that is invisible until BlogApp actually runs.</para>
/// <para><b>Dependencies:</b> xUnit v3. Pure function — no I/O, no platform dependency.</para>
/// </remarks>
public class SiteUrlResolverTests
{
    /// <summary>A bare base URL and a leading-slash path join with exactly one slash.</summary>
    [Fact]
    public void CombineJoinsBaseAndLeadingSlashPath()
    {
        var combined = SiteUrlResolver.Combine("https://techierathore.com", "/post/my-slug");

        Assert.Equal("https://techierathore.com/post/my-slug", combined);
    }

    /// <summary>
    /// A base URL the operator typed WITH a trailing slash does not produce a double slash — the
    /// connection-setup screen's field has no format enforcement, so both shapes must work.
    /// </summary>
    [Fact]
    public void CombineAvoidsDoubleSlashWhenBaseHasTrailingSlash()
    {
        var combined = SiteUrlResolver.Combine("https://techierathore.com/", "/post/my-slug");

        Assert.Equal("https://techierathore.com/post/my-slug", combined);
    }

    /// <summary>A relative path with no leading slash still joins correctly.</summary>
    [Fact]
    public void CombineAddsMissingSlashWhenRelativePathHasNone()
    {
        var combined = SiteUrlResolver.Combine("https://techierathore.com", "post/my-slug");

        Assert.Equal("https://techierathore.com/post/my-slug", combined);
    }

    /// <summary>
    /// No configured base URL resolves to <c>null</c> rather than a partial or guessed address —
    /// the caller uses this to report "not configured" instead of attempting a broken open.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CombineReturnsNullWhenBaseUrlIsNotConfigured(string? baseUrl)
    {
        var combined = SiteUrlResolver.Combine(baseUrl, "/post/my-slug");

        Assert.Null(combined);
    }

    /// <summary>
    /// The cache-refresh endpoint path joins exactly the same way a post's public path does — this
    /// is the arithmetic <c>RemoteSiteCacheNotifier</c> depends on to reach
    /// <c>/api/admin/cache/refresh</c>.
    /// </summary>
    [Fact]
    public void CombineJoinsBaseWithTheCacheRefreshPath()
    {
        var combined = SiteUrlResolver.Combine("https://techierathore.com", "/api/admin/cache/refresh");

        Assert.Equal("https://techierathore.com/api/admin/cache/refresh", combined);
    }
}
