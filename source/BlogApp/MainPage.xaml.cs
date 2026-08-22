using BlogApp.Components.Pages;
using BlogApp.Services;

namespace BlogApp;

/// <summary>
/// The BlazorWebView host page — BlogApp's entire user interface.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Chooses the route the desktop head opens on. A configured install goes to
/// BlogApp's own entry point, which forwards to the route the signed-in role belongs on; a first
/// run — or a run after the connection was cleared — opens the connection-setup screen instead
/// (REQ-FN-047, REQ-UI-051, REQ-UI-063).</para>
/// <para><b>Code Flow:</b> <c>App.CreateWindow</c> resolves this page → the constructor reads
/// <see cref="ConnectionContext"/> → <c>BlazorWebView.StartPath</c> is set before the view
/// initialises → Blazor boots on that route.</para>
/// <para><b>Dependencies:</b> <see cref="ConnectionContext"/>.</para>
/// <para><b>Usage:</b> Registered as a singleton; there is exactly one window.</para>
/// </remarks>
public partial class MainPage : ContentPage
{

    /// <summary>
    /// Creates the page and points the WebView at the right start route.
    /// </summary>
    /// <param name="connectionContext">The connection the process booted with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionContext"/> is <c>null</c>.</exception>
    public MainPage(ConnectionContext connectionContext)
    {
        if (connectionContext == null)
        {
            throw new ArgumentNullException(nameof(connectionContext));
        }

        InitializeComponent();

        // REQ-UI-063: a configured head opens on BlogApp's OWN entry point, not on the shared
        // /login screen. Starting on /login meant that a remembered session hit LoginPage's
        // "already signed in" branch, which - having no returnUrl to honour - forwards to
        // RoleLandingRoutes.PublicHome. That is correct for the website, where /login is a page a
        // reader wandered onto; on an admin desktop tool it opened the public blog on every warm
        // start. DesktopStart makes the decision this head actually wants, from the role.
        blazorWebView.StartPath = connectionContext.IsConfigured
            ? DesktopStart.Route
            : ConnectionSetup.SetupRoute;
    }
}
