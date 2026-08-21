namespace BlogApp;

/// <summary>
/// The MAUI application object for the BlogApp desktop head.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Creates the single desktop window that hosts the shared BlogUI screens
/// (REQ-UI-051). BlogApp is an admin tool, not a browser: there is one window, it is sized for the
/// admin shell's sidebar plus content, and it has no public-site chrome.</para>
/// <para><b>Code Flow:</b> platform entry point → <c>MauiProgram.CreateMauiApp</c> → this type is
/// resolved from DI → <see cref="CreateWindow"/> returns the window wrapping
/// <see cref="MainPage"/>.</para>
/// <para><b>Dependencies:</b> <see cref="IServiceProvider"/> so the page can be resolved with its
/// own dependencies rather than newed up.</para>
/// <para><b>Usage:</b> Registered implicitly by <c>UseMauiApp&lt;App&gt;()</c>.</para>
/// </remarks>
public partial class App : Application
{
    /// <summary>Initial window width, wide enough for the admin sidebar plus a data table.</summary>
    private const double InitialWindowWidth = 1440;

    /// <summary>Initial window height.</summary>
    private const double InitialWindowHeight = 900;

    /// <summary>Smallest width at which the admin shell still lays out correctly.</summary>
    private const double MinimumWindowWidth = 1024;

    /// <summary>Smallest usable window height.</summary>
    private const double MinimumWindowHeight = 700;

    private readonly IServiceProvider services;

    /// <summary>
    /// Creates the application.
    /// </summary>
    /// <param name="services">The container built by <c>MauiProgram</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <c>null</c>.</exception>
    public App(IServiceProvider services)
    {
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        InitializeComponent();
    }

    /// <summary>
    /// Creates the single BlogApp window.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Sizes the window for the admin shell rather than leaving it at
    /// the phone-shaped MAUI default, and sets a floor below which the sidebar shell would collapse
    /// awkwardly.</para>
    /// <para><b>Known limitation (measured 2026-08-07, REQ-UI-052):</b> these values do NOT survive
    /// a scaled display. The unpackaged WinUI process runs DPI-UNAWARE — <c>GetDpiForWindow</c>
    /// returns 96 even though <c>Platforms\Windows\app.manifest</c> declares <c>PerMonitorV2</c>,
    /// and adding <c>&lt;ApplicationManifest&gt;</c> to the csproj did not change it. On a 150%
    /// display the window is 1440x900 by <c>GetWindowRect</c> while the hosted WebView reports
    /// <c>devicePixelRatio</c> 1.5 and only 950x574 CSS pixels of layout — under the 1024 floor
    /// above. The shared BlogUI admin tables then push their action column past the right edge with
    /// no horizontal scroll container to reach it. Scaling these constants by the display density
    /// was tried and does not work while the process reports 96 DPI; the fix belongs with the WinUI
    /// DPI-awareness setup (or an <c>overflow-x: auto</c> wrapper on the shared admin tables), not
    /// with a larger constant here.</para>
    /// <para><b>Flow:</b> resolve the page → wrap it in a window → apply the size constraints.</para>
    /// <para><b>Side Effects:</b> Instantiates the BlazorWebView host page.</para>
    /// </remarks>
    /// <param name="activationState">Platform activation state; not used.</param>
    /// <returns>The application's only window.</returns>
    protected override Window CreateWindow(IActivationState activationState)
    {
        var page = services.GetRequiredService<MainPage>();

        return new Window(page)
        {
            Title = "TechieBlog - BlogApp",
            Width = InitialWindowWidth,
            Height = InitialWindowHeight,
            MinimumWidth = MinimumWindowWidth,
            MinimumHeight = MinimumWindowHeight
        };
    }
}
