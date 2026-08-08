using BlogApp.Components.Pages;
using BlogApp.Services;

namespace BlogApp;

/// <summary>
/// The BlazorWebView host page — BlogApp's entire user interface.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Chooses the route the desktop head opens on. A configured install goes
/// straight to the shared BlogUI sign-in screen; a first run — or a run after the connection was
/// cleared — opens the connection-setup screen instead (REQ-FN-047, REQ-UI-051).</para>
/// <para><b>Code Flow:</b> <c>App.CreateWindow</c> resolves this page → the constructor reads
/// <see cref="ConnectionContext"/> → <c>BlazorWebView.StartPath</c> is set before the view
/// initialises → Blazor boots on that route.</para>
/// <para><b>Dependencies:</b> <see cref="ConnectionContext"/>.</para>
/// <para><b>Usage:</b> Registered as a singleton; there is exactly one window.</para>
/// </remarks>
public partial class MainPage : ContentPage
{
    /// <summary>Route the shared BlogUI sign-in screen is published at.</summary>
    private const string LoginRoute = "/login";

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

        blazorWebView.StartPath = connectionContext.IsConfigured
            ? LoginRoute
            : ConnectionSetup.SetupRoute;
    }
}
