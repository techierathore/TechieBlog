using BlogEngine.Services;
using BlogUI.Pages.AdminPages;
using NSubstitute;
using System.Reflection;

namespace TechieBlog.Tests.Caching;

/// <summary>
/// UAT-023 mechanism B regression guard: the <c>ISiteCacheNotifier</c> seam and
/// <c>ManagePost</c>'s honest reporting of what it did with the result.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BlogApp writes straight to the site database and never runs a line of the
/// website's code, so nothing evicted the website's ten-minute content cache — the owner's edited
/// abstract kept showing the old value. The fix adds <see cref="ISiteCacheNotifier"/>, called from
/// <c>ManagePost.razor.cs</c> after every publish-affecting save. Two things need locking down: the
/// website's own registration is a genuine no-op (it must not attempt a redundant self-call), and
/// <c>ManagePost</c> never claims a refresh succeeded when it did not — an earlier round of this
/// class of feature shipped a probe that answered "OK" for something it had not actually verified,
/// which is exactly the failure mode <see cref="CacheRefreshResult"/>'s required <c>Outcome</c>
/// exists to make impossible to skip.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="ISiteCacheNotifier"/>.</para>
/// </remarks>
public class SiteCacheNotifierTests
{
    /// <summary>
    /// The website's own notifier always reports <c>NotApplicable</c> — its save already
    /// invalidated the cache in-process, so there is nothing for this call to do.
    /// </summary>
    [Fact]
    public async Task NullSiteCacheNotifierReportsNotApplicable()
    {
        var notifier = new NullSiteCacheNotifier();

        var result = await notifier.RefreshAsync();

        Assert.Equal(CacheRefreshOutcome.NotApplicable, result.Outcome);
    }

    /// <summary>
    /// A successful remote refresh is confirmed in the status message rather than left implicit.
    /// </summary>
    [Fact]
    public async Task ManagePostAppendsConfirmationWhenRefreshSucceeds()
    {
        // Arrange
        var page = new ManagePost { StatusMessage = "Post updated successfully!" };
        page.CacheNotifier = FakeNotifier(new CacheRefreshResult { Outcome = CacheRefreshOutcome.Succeeded });

        // Act
        await InvokeNotifySiteCacheRefreshAsync(page);

        // Assert
        Assert.Equal("Post updated successfully! Site cache refreshed.", page.StatusMessage);
    }

    /// <summary>
    /// A failed remote refresh is surfaced verbatim — the save already succeeded, so the failure
    /// must read as a note about the SITE, not be mistaken for the save itself having failed.
    /// </summary>
    [Fact]
    public async Task ManagePostAppendsDetailWhenRefreshFails()
    {
        // Arrange
        var page = new ManagePost { StatusMessage = "Post updated successfully!" };
        page.CacheNotifier = FakeNotifier(new CacheRefreshResult
        {
            Outcome = CacheRefreshOutcome.Failed,
            Detail = "Could not reach the site. The change is saved, but the public page may still show the old version for a while."
        });

        // Act
        await InvokeNotifySiteCacheRefreshAsync(page);

        // Assert
        Assert.Equal(
            "Post updated successfully! Could not reach the site. The change is saved, but the public page may still show the old version for a while.",
            page.StatusMessage);
    }

    /// <summary>
    /// A <c>NotApplicable</c> outcome (the website's own path, or BlogApp with no site address
    /// configured) leaves the status message exactly as the save already set it — no cache-refresh
    /// chatter is added where there is nothing to report.
    /// </summary>
    [Fact]
    public async Task ManagePostLeavesMessageUnchangedWhenRefreshIsNotApplicable()
    {
        // Arrange
        var page = new ManagePost { StatusMessage = "Post updated successfully!" };
        page.CacheNotifier = FakeNotifier(CacheRefreshResult.NotApplicable);

        // Act
        await InvokeNotifySiteCacheRefreshAsync(page);

        // Assert
        Assert.Equal("Post updated successfully!", page.StatusMessage);
    }

    /// <summary>Builds an <see cref="ISiteCacheNotifier"/> that always answers with a fixed result.</summary>
    private static ISiteCacheNotifier FakeNotifier(CacheRefreshResult result)
    {
        var notifier = Substitute.For<ISiteCacheNotifier>();
        notifier.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(result));
        return notifier;
    }

    /// <summary>Invokes the deliberately <c>private</c> <c>NotifySiteCacheRefreshAsync</c> helper.</summary>
    private static async Task InvokeNotifySiteCacheRefreshAsync(ManagePost page)
    {
        var method = typeof(ManagePost).GetMethod(
            "NotifySiteCacheRefreshAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "ManagePost.NotifySiteCacheRefreshAsync was not found — check for a rename (UAT-023).");

        var task = (Task)method.Invoke(page, null)!;
        await task;
    }
}
