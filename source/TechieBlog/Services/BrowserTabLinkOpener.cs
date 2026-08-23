using BlogUI;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace TechieBlog.Services;

/// <summary>
/// Website-side <see cref="IExternalLinkOpener"/>: opens a URL in a new browser tab.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> UAT-024. The website's own answer to "open this public post somewhere
/// other than the admin window" is the browser's own new-tab feature — the same behaviour
/// <c>AdminLayout</c>'s existing "View site" link already gets from <c>Target="_blank"</c>. This
/// class exists only so pages that decide the target PROGRAMMATICALLY (a published-vs-unpublished
/// branch, not a static attribute) can reach the same behaviour through the shared
/// <see cref="IExternalLinkOpener"/> seam.</para>
/// <para><b>Code Flow:</b> <c>OpenAsync</c> → <c>IJSRuntime.InvokeVoidAsync("open", …)</c>, which
/// calls the browser global <c>window.open(url, "_blank")</c> — no custom JavaScript file is
/// needed because Blazor's JS interop resolves an unqualified function name against
/// <c>window</c>.</para>
/// <para><b>Dependencies:</b> <see cref="IJSRuntime"/>.</para>
/// <para><b>Usage:</b> Registered as a scoped <see cref="IExternalLinkOpener"/> in
/// <c>Program.cs</c>, alongside the other BlogUI-facing service registrations.</para>
/// </remarks>
public class BrowserTabLinkOpener : IExternalLinkOpener
{
    private readonly IJSRuntime jsRuntime;
    private readonly ILogger<BrowserTabLinkOpener> logger;

    /// <summary>
    /// Creates the browser-tab link opener.
    /// </summary>
    /// <param name="jsRuntime">JavaScript interop for the current circuit.</param>
    /// <param name="logger">Logger used when the interop call itself fails.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="jsRuntime"/> or <paramref name="logger"/> is <c>null</c>.
    /// </exception>
    public BrowserTabLinkOpener(IJSRuntime jsRuntime, ILogger<BrowserTabLinkOpener> logger)
    {
        this.jsRuntime = jsRuntime ?? throw new ArgumentNullException(nameof(jsRuntime));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank URL is ignored — see the interface contract. A prerender
    /// (no JavaScript interop available yet) or a popup-blocked browser both fail the interop call;
    /// both are logged and swallowed rather than surfaced, because a broken preview link must not
    /// take down the admin page that requested it.</para>
    /// <para><b>Flow:</b> blank guard → <c>window.open(url, "_blank")</c> → swallow interop failures.</para>
    /// <para><b>Side Effects:</b> Opens a new browser tab when successful.</para>
    /// </remarks>
    public async Task OpenAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await jsRuntime.InvokeVoidAsync("open", url, "_blank");
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not open {Url} in a new tab", url);
        }
    }
}
