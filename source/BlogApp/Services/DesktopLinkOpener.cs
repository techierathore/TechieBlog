using BlogModels.Common;
using BlogUI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace BlogApp.Services;

/// <summary>
/// Desktop-side <see cref="IExternalLinkOpener"/>: hands a URL to the OS default browser.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> UAT-024. BlogApp's <c>BlazorWebView</c> has no browser chrome — no
/// address bar, no Back button — so navigating the CURRENT window to a public post's URL (the
/// original defect) stranded the operator with only "restart the app" as a way back. MAUI's
/// <see cref="Launcher"/> opens the URL in whatever browser the operating system considers
/// default, entirely outside the app's own window, which leaves the admin window exactly where it
/// was — still on the posts list, still signed in.</para>
///
/// <para><b>Code Flow:</b> <c>OpenAsync</c> receives a SITE-RELATIVE path → resolves it against
/// <see cref="ConnectionContext.Settings"/>.<c>SiteBaseUrl</c> via <see cref="SiteUrlResolver"/>
/// (the same field <c>UploadsUrlRewriter</c> already reads to display server-hosted images) →
/// marshal to the UI thread (the same requirement <c>FilePickerService</c> documents: a Blazor
/// event handler runs on the WebView's callback thread, and MAUI's platform APIs are not safe to
/// call from there) → <c>Launcher.OpenAsync</c>.</para>
///
/// <para><b>Why resolution happens here, not in the caller.</b> BlogApp's own
/// <c>NavigationManager.BaseUri</c> points at the app's packaged local content (what the
/// BlazorWebView is actually showing), not at the live site — so a caller that resolved the path
/// itself via <c>NavigationManager</c> would build a URL to nowhere. <c>SiteBaseUrl</c> is the only
/// value in this process that names the real site.</para>
///
/// <para><b>Dependencies:</b> <see cref="ConnectionContext"/>, <c>Microsoft.Maui.ApplicationModel.Launcher</c>.</para>
///
/// <para><b>Usage:</b> Registered as a singleton <see cref="IExternalLinkOpener"/> in
/// <c>MauiProgram.cs</c>, replacing the website's <c>BrowserTabLinkOpener</c> for this head.</para>
/// </remarks>
public class DesktopLinkOpener : IExternalLinkOpener
{
    private readonly ConnectionContext connectionContext;
    private readonly ILogger<DesktopLinkOpener> logger;

    /// <summary>
    /// Creates the desktop link opener.
    /// </summary>
    /// <param name="connectionContext">The connection BlogApp booted with, for <c>SiteBaseUrl</c>.</param>
    /// <param name="logger">Logger used when the URL cannot be resolved or the OS cannot open it.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="connectionContext"/> or <paramref name="logger"/> is <c>null</c>.
    /// </exception>
    public DesktopLinkOpener(ConnectionContext connectionContext, ILogger<DesktopLinkOpener> logger)
    {
        this.connectionContext = connectionContext ?? throw new ArgumentNullException(nameof(connectionContext));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank <paramref name="url"/> is ignored — see the interface
    /// contract. An unconfigured <c>SiteBaseUrl</c> (it is an OPTIONAL field on the connection-setup
    /// screen) means this process has no way to know the site's public address at all, so the open
    /// is skipped and logged rather than attempted against a guess. No default browser, a malformed
    /// address, or the platform refusing the launch are all recoverable failures from the caller's
    /// point of view: the admin page stays exactly where it was, so every failure here logs and
    /// returns rather than throwing into the Blazor render pipeline — a MAUI hard crash there
    /// re-boots the WebView into "An unhandled error has occurred", the very trap this fix must not
    /// walk into.</para>
    /// <para><b>Flow:</b> blank guard → read <c>SiteBaseUrl</c> → combine with the relative path →
    /// marshal to the UI thread → <c>Launcher.OpenAsync</c> → swallow platform failures.</para>
    /// <para><b>Side Effects:</b> Starts the OS default browser when successful. Never touches the
    /// BlazorWebView's own navigation state.</para>
    /// </remarks>
    public async Task OpenAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var absoluteUrl = SiteUrlResolver.Combine(connectionContext.Settings?.SiteBaseUrl, url);
        if (absoluteUrl == null)
        {
            logger.LogWarning(
                "Cannot open {RelativeUrl} externally: no website address is configured for this connection",
                url);
            return;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => Launcher.OpenAsync(new Uri(absoluteUrl)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open {AbsoluteUrl} in the default browser", absoluteUrl);
        }
    }
}
