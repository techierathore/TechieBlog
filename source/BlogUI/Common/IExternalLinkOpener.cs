namespace BlogUI;

/// <summary>
/// Opens a URL somewhere OTHER than the current admin window or app instance.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> UAT-024. A published post's preview used to navigate the CURRENT window
/// straight to the public page (<c>NavigationManager.NavigateTo($"/post/{slug}")</c>). On the
/// website that merely costs a Back-button click; inside BlogApp's <c>BlazorWebView</c> there is
/// no browser chrome at all, so the admin had no way back short of restarting the app. This
/// abstraction is what lets the shared <c>BlogUI</c> pages (<c>BlogsList</c>, <c>PreviewPost</c>)
/// say "open this public URL outside of here" without knowing whether "here" is a browser tab or
/// a desktop window — <c>BlogUI</c> must not reference MAUI (REQ-UI-052, one RCL, two heads).</para>
///
/// <para><b>Code Flow:</b> A page resolves this via DI and calls <see cref="OpenAsync"/> with the
/// absolute or site-relative URL of a PUBLISHED post. The website registers a JavaScript
/// <c>window.open(url, "_blank")</c> implementation (a new browser tab); BlogApp registers one
/// backed by <c>Microsoft.Maui.ApplicationModel.Launcher.OpenAsync</c>, which hands the URL to the
/// operating system's default browser and leaves the admin window exactly where it was.</para>
///
/// <para><b>Dependencies:</b> None on this interface itself — each implementation supplies its own
/// (<c>IJSRuntime</c> for the web head, MAUI's <c>Launcher</c> for the desktop head).</para>
///
/// <para><b>Usage:</b> Register exactly one implementation per host in its own composition root
/// (<c>TechieBlog/Program.cs</c>, <c>BlogApp/MauiProgram.cs</c>) — never in
/// <c>BlogSvcInitializer</c>, which defines only the shared engine graph and has no notion of
/// "the current window". An unpublished post has no public URL and must never be passed here; the
/// caller keeps that case in the current window by not calling this abstraction at all.</para>
/// </remarks>
public interface IExternalLinkOpener
{
    /// <summary>
    /// Opens <paramref name="url"/> outside the caller's own window or app instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Must never navigate the CALLING window/app — that is precisely
    /// the defect this abstraction exists to avoid repeating. A blank or whitespace-only URL is
    /// ignored rather than handed to the platform launcher, which would otherwise surface a
    /// confusing platform-level error for what is a caller bug.</para>
    /// <para><b>Side Effects:</b> Platform-dependent: opens a new browser tab (web) or hands the URL
    /// to the OS default browser (desktop). Implementations must not throw for an unreachable URL or
    /// a launcher failure — log and return, so a broken preview link degrades to "nothing happened"
    /// rather than crashing the admin page that requested it.</para>
    /// </remarks>
    /// <param name="url">
    /// The SITE-RELATIVE path of a PUBLISHED post, e.g. <c>/post/my-post-slug</c>. A relative path
    /// is deliberate: the web implementation lets the browser resolve it against the page it is
    /// already on (the real site), while the desktop implementation resolves it against the
    /// configured <c>SiteBaseUrl</c> — neither can share a single absolute URL, because BlogApp's
    /// own <c>NavigationManager.BaseUri</c> points at its packaged local content, not the site.
    /// </param>
    /// <returns>A task that completes once the open has been attempted.</returns>
    Task OpenAsync(string url);
}
