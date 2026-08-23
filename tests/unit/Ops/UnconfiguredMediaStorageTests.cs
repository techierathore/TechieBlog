using BlogApp.Services;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Covers the guard that stops the desktop head silently writing uploads to the operator's own
/// machine (UAT-022, REQ-FN-062).
/// </summary>
/// <remarks>
/// The owner set a site logo from BlogApp on 2026-08-23. The setting saved correctly and the public
/// HTML emitted <c>/uploads/logos/…</c>, but the website answered 404 forever, because the file had
/// been written to <c>%LOCALAPPDATA%\TechieBlog\BlogApp\wwwroot\uploads\logos\</c> — the desktop
/// app's own web root. BlogApp always writes its rows to the SITE's database, so a local write can
/// never produce a usable reference; refusing is the only correct answer.
/// </remarks>
public class UnconfiguredMediaStorageTests
{
    /// <summary>
    /// An upload attempted with no media location configured is refused, rather than written
    /// somewhere the website cannot serve.
    /// </summary>
    [Fact]
    public async Task SaveRefusesInsteadOfWritingLocally()
    {
        var storage = new UnconfiguredMediaStorage();
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAsync(content, "uploads/logos/logo.png", "image/png", CancellationToken.None));

        Assert.Equal(UnconfiguredMediaStorage.NotConfiguredMessage, failure.Message);
    }

    /// <summary>
    /// The refusal message tells the operator where to fix it and leaks no path, host name or
    /// exception text, so it is safe to render straight into the upload dialog (REQ-NFR-033).
    /// </summary>
    [Fact]
    public async Task RefusalMessageIsActionableAndDisclosesNothing()
    {
        var storage = new UnconfiguredMediaStorage();
        using var content = new MemoryStream(new byte[] { 1 });

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.SaveAsync(content, "uploads/logos/logo.png", "image/png", CancellationToken.None));

        Assert.Contains("Change connection", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Media storage", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads answer "nothing here" rather than throwing, so the media library, cleanup paths and
    /// migration can enumerate on an unconfigured head without special-casing it.
    /// </summary>
    [Fact]
    public async Task ReadsReportAbsenceRatherThanFailing()
    {
        var storage = new UnconfiguredMediaStorage();

        Assert.False(await storage.ExistsAsync("uploads/logos/logo.png", CancellationToken.None));
        Assert.False(await storage.DeleteAsync("uploads/logos/logo.png", CancellationToken.None));
        Assert.Null(await storage.OpenReadAsync("uploads/logos/logo.png", CancellationToken.None));
    }

    /// <summary>
    /// The public URL is still the site-relative one, so rows written earlier against a server that
    /// does hold the file keep rendering; only NEW unusable rows are prevented.
    /// </summary>
    [Fact]
    public void PublicUrlStaysSiteRelative()
    {
        var storage = new UnconfiguredMediaStorage();

        Assert.Equal("/uploads/logos/logo.png", storage.GetPublicUrl("uploads/logos/logo.png"));
        Assert.Equal("/uploads/logos/logo.png", storage.GetPublicUrl("/uploads/logos/logo.png"));
        Assert.Equal(string.Empty, storage.GetPublicUrl("   "));
    }
}
