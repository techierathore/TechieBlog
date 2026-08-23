// =============================================================================
// BlogApp — .NET MAUI Blazor Hybrid desktop head for TechieBlog
//
// Requirements wired here:
//   REQ-FN-046 - the sixth solution project: references BlogUI, BlogEngine and BlogModels and
//                registers the SAME DI graph the web host builds in source/TechieBlog/Program.cs
//   REQ-FN-047 - the connection string is read from platform secure storage at startup and handed
//                to BlogSvcInitializer; a first run leaves it empty and opens the setup screen
//   REQ-UI-051 - the five authorization policies, the shared authentication state provider and the
//                shared BlogUI screens, unchanged
//   REQ-NFR-013 - Serilog rolling file sink, unhandled-exception handlers, CloseAndFlush on exit
//
// NOT wired here, deliberately:
//   - DbUp migrations. The website owns the schema; BlogApp connects to an already-migrated
//     database (BRD-96). BlogDb is not referenced.
//   - Hosted services. MAUI does not run IHostedService, so BlogEngine's ScheduledPostPublisher
//     stays dormant in the desktop head and the website remains the only publisher.
// =============================================================================
using BlogApp.Services;
using BlogEngine;
using BlogEngine.Common;
using BlogEngine.Services;
using BlogEngine.Storage;
using BlogModels;
using BlogModels.Interfaces;
using BlogUI;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Serilog;
using Serilog.Events;
using System.Reflection;
using TrBlazeUI.Components.Toast;
using TrBlazeUI.Primitives.Extensions;

namespace BlogApp;

/// <summary>
/// Composition root for the BlogApp desktop head.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Builds the MAUI application: logging first, then the connection string,
/// then the shared BlogEngine and BlogUI service graph (REQ-FN-046).</para>
/// <para><b>Code Flow:</b> <see cref="CreateMauiApp"/> → configure Serilog → load the stored
/// connection → register services → build. Every later screen resolves from the container built
/// here.</para>
/// <para><b>Dependencies:</b> <c>BlogSvcInitializer</c> (the single definition of the engine graph),
/// <c>ConnectionStore</c>, Serilog.</para>
/// <para><b>Usage:</b> Invoked by the platform entry points under <c>Platforms/</c>.</para>
/// </remarks>
public static class MauiProgram
{
    /// <summary>
    /// Configuration key the whole solution reads its PostgreSQL connection string from.
    /// </summary>
    public const string ConnectionStringKey = "AppDbConString";

    /// <summary>Per-file cap for the rolling log file: 10 MB (REQ-NFR-036).</summary>
    /// <remarks>
    /// Matches <c>TechieBlog.Configuration.LogFileSettings.DefaultSizeLimitBytes</c> so both heads
    /// state their disk budget in the same units. Without this the sink took Serilog's 1 GB default.
    /// </remarks>
    public const long LogFileSizeLimitBytes = 10L * 1024 * 1024;

    /// <summary>Rolled log files retained; older ones are deleted (REQ-NFR-036).</summary>
    /// <remarks>
    /// Fourteen days of history is the desktop head's long-standing choice and is kept: a user
    /// reporting a fault a week later still has the evidence. With
    /// <see cref="LogFileSizeLimitBytes"/> the count now bounds a VOLUME rather than a file count —
    /// <c>LogFileSizeLimitBytes * LogFileRetainedFileCountLimit</c> = <b>140 MB worst case</b>.
    /// </remarks>
    public const int LogFileRetainedFileCountLimit = 14;

    /// <summary>The most disk this head's logs can ever occupy: 140 MB.</summary>
    /// <remarks>
    /// The number an operator actually needs, stated once so nobody has to multiply it themselves —
    /// the same contract <c>LogFileSettings.WorstCaseTotalBytes</c> exposes for the web head.
    /// </remarks>
    public const long LogFileWorstCaseTotalBytes =
        LogFileSizeLimitBytes * LogFileRetainedFileCountLimit;

    /// <summary>
    /// Builds and returns the configured MAUI application.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Logging is wired before anything else so a failure while
    /// composing the container still reaches the log file. The connection string is read from
    /// secure storage on a thread-pool thread — blocking the UI thread on a WinRT credential call
    /// during startup can deadlock — and an absent value is passed through as an empty string,
    /// which is legal to register with and only fails if a screen actually queries. The
    /// connection-setup screen does not.</para>
    /// <para><b>Flow:</b> Serilog → exception handlers → builder → configuration → connection →
    /// services → build.</para>
    /// <para><b>Side Effects:</b> Creates the log directory, reads the OS credential store, and
    /// installs process-wide exception handlers.</para>
    /// </remarks>
    /// <returns>The built application.</returns>
    /// <exception cref="Exception">Rethrown after logging when composition fails.</exception>
    public static MauiApp CreateMauiApp()
    {
        ConfigureLogging();

        try
        {
            Log.Information("Starting BlogApp desktop head");

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"));

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(dispose: false);
#if DEBUG
            builder.Logging.AddDebug();
#endif

            AddEmbeddedConfiguration(builder.Configuration);

            var connectionStore = CreateConnectionStore();
            var storedSettings = LoadStoredSettings(connectionStore);
            var connectionString = storedSettings?.ToConnectionString() ?? string.Empty;

            // Parity with the web head: every consumer of IConfiguration sees the same key.
            builder.Configuration[ConnectionStringKey] = connectionString;

            ApplySiteSecrets(builder.Configuration, storedSettings);

            builder.Services.AddMauiBlazorWebView();
#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
#endif

            RegisterServices(builder.Services, connectionStore, storedSettings, connectionString);

            Log.Information(
                "BlogApp composed. Connection configured: {IsConfigured}; storage backend: {StorageBackend}",
                storedSettings != null,
                connectionStore.StorageDescription);

            return builder.Build();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "BlogApp failed to start");
            Log.CloseAndFlush();
            throw;
        }
    }

    /// <summary>
    /// Wires Serilog with a daily rolling file sink and the last-resort exception handlers.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mandated for every executable head by the coding standards. A
    /// desktop app has no console to write to and cannot rely on its installation folder being
    /// writable, so the sink targets the user's local application data alongside the connection
    /// store. The Debug sink keeps the same events visible in the IDE output window.</para>
    /// <para><b>Flow:</b> resolve the log folder → build the logger → attach the AppDomain,
    /// TaskScheduler and ProcessExit handlers.</para>
    /// <para><b>Side Effects:</b> Creates the log directory, assigns <see cref="Log.Logger"/> and
    /// installs process-wide handlers.</para>
    /// </remarks>
    private static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(ResolveAppDataRoot(), "logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .WriteTo.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "blogapp-.log"),
                rollingInterval: RollingInterval.Day,
                // REQ-NFR-036 - bound the VOLUME, not just the file count. A daily roll with a
                // retention count and no size cap is not a bound: Serilog defaults
                // fileSizeLimitBytes to 1 GB and, with rollOnFileSizeLimit left false, SILENTLY
                // STOPS WRITING at that ceiling - so the worst case was 14 GB of disk AND a log
                // that quietly goes deaf on the loudest day, which is the day it was needed.
                // Capping a file is not capping a disk; the PRODUCT of the next two lines is:
                // 10 MB * 14 = 140 MB, stated once as LogFileWorstCaseTotalBytes. Raise either
                // number with that product in mind.
                retainedFileCountLimit: LogFileRetainedFileCountLimit,
                fileSizeLimitBytes: LogFileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                // shared: a connection change relaunches BlogApp, so the outgoing and incoming
                // instances overlap briefly. Without it the second process cannot open the file
                // and drops every event it writes during startup.
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // REQ-NFR-036 - state the disk budget in the log it describes, so an operator reading the
        // file never has to find this source to learn what bounds it. Parity with the web head's
        // equivalent line in Program.cs.
        Log.Information(
            "Log file sink writing to {LogDirectory} - at most {RetainedFileCountLimit} files of "
            + "{SizeLimitBytes} bytes, {WorstCaseTotalBytes} bytes of disk worst case",
            logDirectory,
            LogFileRetainedFileCountLimit,
            LogFileSizeLimitBytes,
            LogFileWorstCaseTotalBytes);

        // Same contract as the web head's TechieBlog.Observability.GlobalExceptionLogging: the sink
        // is closed ONLY when the runtime says it is terminating. CloseAndFlush swaps in a silent
        // logger, so flushing on a non-terminating notification would blind a process that is still
        // running - and the unbuffered file sink has already written the event by then anyway.
        // The bodies are duplicated rather than shared because BlogApp deliberately takes no
        // dependency on the web head (see the header note), and a class library may not reference
        // Serilog (Coding Standards §Logging).
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            Log.Fatal(
                eventArgs.ExceptionObject as Exception,
                "Unhandled exception escaped to the AppDomain (terminating: {IsTerminating})",
                eventArgs.IsTerminating);

            if (eventArgs.IsTerminating)
                Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.Error(eventArgs.Exception, "Unobserved task exception was collected by the finalizer");
            eventArgs.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Information("BlogApp shutting down");
            Log.CloseAndFlush();
        };
    }

    /// <summary>
    /// Registers the same service graph the web host registers, minus its web-only pieces.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>BlogSvcInitializer.Initialize</c> is the single definition of
    /// the engine graph and is called unchanged, so a service added for the website is
    /// automatically present here. Around it sit the registrations the website makes in
    /// <c>Program.cs</c> that a desktop head still needs: the memory cache the cache service is
    /// built on, the resilience pipelines, TrBlazeUI's primitives and toasts, local storage, the
    /// five authorization policies, the shared authentication state provider and the theme service.
    /// <see cref="IWebHostEnvironment"/> is supplied by a desktop stand-in because
    /// <c>FileStorageFactory</c> requires it and MAUI has no web host.</para>
    /// <para><b>Flow:</b> connection services → engine graph → cache → resilience → UI services →
    /// auth.</para>
    /// <para><b>Side Effects:</b> Creates the desktop content-root folders.</para>
    /// </remarks>
    /// <param name="services">The MAUI service collection.</param>
    /// <param name="connectionStore">The store the startup settings were read from.</param>
    /// <param name="storedSettings">The settings in force, or <c>null</c> on a first run.</param>
    /// <param name="connectionString">The connection string the engine repositories are bound to.</param>
    private static void RegisterServices(
        IServiceCollection services,
        IConnectionStore connectionStore,
        ConnectionSettings storedSettings,
        string connectionString)
    {
        // ---------------------------------------------------------------------
        // Desktop-only services: connection capture, probing and relaunch (REQ-FN-047)
        // ---------------------------------------------------------------------
        services.AddSingleton(connectionStore);
        services.AddSingleton(new ConnectionContext(storedSettings));
        services.AddSingleton<AppRestarter>();
        services.AddTransient<ConnectionProbe>();
        services.AddTransient<MediaLocationProbe>();

        // REQ-FN-062 - OS pickers for the two settings that are paths on THIS machine, and the
        // one-click migration that sends already-stranded images up over the same SSH connection.
        services.AddSingleton<FilePickerService>();
        services.AddTransient<MediaMigrator>();

        // Hosting environment stand-in required by BlogEngine.Storage.FileStorageFactory.
        services.AddSingleton<IWebHostEnvironment>(new DesktopHostEnvironment(ResolveAppDataRoot()));

        // ---------------------------------------------------------------------
        // The shared engine graph - one definition, two heads (REQ-FN-046)
        // ---------------------------------------------------------------------
        BlogSvcInitializer.Initialize(services, connectionString);

        // REQ-FN-062 - the desktop head's MEDIA connection, the counterpart to its database one.
        // Registered AFTER the engine graph so it REPLACES the engine's IFileStorageFactory rather
        // than competing with it (last registration wins for a single resolve). Without it every
        // image uploaded here is written to DesktopHostEnvironment.WebRootPath - a folder under
        // this operator's %LOCALAPPDATA% - while the database row points at /uploads/... on the
        // web server, so the picture exists nowhere the site can serve it. The decorator falls
        // straight through to the engine factory when no media folder has been configured, which
        // is why adding it cannot change the behaviour of a head that has not opted in.
        services.AddSingleton<FileStorageFactory>();
        services.AddSingleton<IFileStorageFactory>(provider => new DesktopFileStorageFactory(
            provider.GetRequiredService<FileStorageFactory>(),
            provider.GetRequiredService<ConnectionContext>(),
            provider.GetRequiredService<ILoggerFactory>()));

        // Backs BlogEngine's ICacheService and CaptchaSvc, exactly as on the web head.
        services.AddMemoryCache();

        // REQ-NFR-012 parity: named retry / circuit-breaker pipelines.
        services.AddResiliencePipeline(ResiliencePipelines.Database, ResiliencePipelines.ConfigureDatabase);
        services.AddResiliencePipeline(ResiliencePipelines.Email, ResiliencePipelines.ConfigureEmail);
        services.AddResiliencePipeline(ResiliencePipelines.Storage, ResiliencePipelines.ConfigureStorage);

        // ---------------------------------------------------------------------
        // Shared BlogUI services (REQ-UI-051 / REQ-UI-052)
        // ---------------------------------------------------------------------
        services.AddTrBlazeUIPrimitives();
        services.AddScoped<ToastService>();
        services.AddBlazoredLocalStorage();
        services.AddScoped<ThemeService>();

        // ---------------------------------------------------------------------
        // Authorization: the same five policies, generated from the same map (REQ-FN-009)
        // ---------------------------------------------------------------------
        services.AddAuthorizationCore(options =>
        {
            foreach (var policy in AppPolicies.PolicyRoleMap)
            {
                options.AddPolicy(policy.Key, configure => configure.RequireRole(policy.Value));
            }

            options.AddPolicy(AppPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
        });

        services.AddCascadingAuthenticationState();

        // DesktopAuthStateProvider derives from BlogUI's CustomAuthStateProvider - the shared
        // LoginPage casts to that type - and adds the two guards a repointable desktop head needs.
        services.AddScoped<AuthenticationStateProvider, DesktopAuthStateProvider>();
        services.AddTransient<IAuthService, AuthService>();

        // UAT-024: opening a published post's preview must leave the admin window where it was
        // (there is no browser chrome to go "back" with in a BlazorWebView). Registered here, not
        // in BlogSvcInitializer, for the same reason the web head registers its own new-tab
        // implementation in Program.cs rather than there: "the current window" is head-specific.
        services.AddSingleton<IExternalLinkOpener, DesktopLinkOpener>();

        // UAT-023 mechanism B: a save made here never runs a line of the website's code, so
        // nothing evicts the website's in-process content cache. This calls the website's
        // authenticated refresh endpoint with the operator's own session token afterwards.
        // A single singleton HttpClient (rather than IHttpClientFactory, which needs the
        // Microsoft.Extensions.Http package this project does not otherwise reference) - the same
        // choice the standard MAUI Blazor Hybrid template makes, and this client makes exactly one
        // short-lived POST per save, not the high-churn traffic IHttpClientFactory exists for.
        services.AddSingleton(new HttpClient());
        services.AddScoped<ISiteCacheNotifier>(provider => new RemoteSiteCacheNotifier(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<ILocalStorageService>(),
            provider.GetRequiredService<ConnectionContext>(),
            provider.GetRequiredService<ILogger<RemoteSiteCacheNotifier>>()));

        // The single window, resolved from DI so it can read the connection state.
        services.AddSingleton<MainPage>();
    }

    /// <summary>
    /// Publishes the stored site secrets into configuration and loads them (REQ-NFR-027).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>AppEncrypt</c> and <c>AppConstants.AccessKey</c> throw on first
    /// use until <c>AppSecrets.Initialise</c> has run, so without this the desktop head compiles and
    /// launches but fails the moment anyone signs in. The values come from the same DPAPI-protected
    /// <see cref="ConnectionStore"/> that already holds the database password — never from the
    /// embedded <c>appsettings.json</c>, which ships inside the distributed binary and would put a
    /// live site key in every copy of the app.</para>
    /// <para>The call is <b>guarded rather than unconditional</b> on purpose. A first run has no
    /// stored settings at all, and <c>Initialise</c> throws on an absent secret by design — running
    /// it here would abort composition before the connection-setup screen could ever be shown, so
    /// the operator would have no way to supply the values that would fix it. Skipping instead
    /// leaves <c>ConnectionContext.IsConfigured</c> false, which is exactly the state that opens
    /// setup. <c>ConnectionSettings.IsComplete</c> now requires both secrets, so a settings blob
    /// saved before they existed reads as incomplete and reopens setup as well.</para>
    /// <para>No re-initialisation path is needed: <c>ConnectionSetup.SaveAndContinue</c> restarts the
    /// process through <c>AppRestarter</c>, so freshly saved secrets always arrive through a new
    /// composition rather than being swapped into a live one.</para>
    /// <para><b>Flow:</b> null/usability guard → copy both values onto the configuration under the
    /// paths <c>AppSecrets</c> reads → initialise.</para>
    /// <para><b>Side Effects:</b> Sets process-wide secret state via <c>AppSecrets.Initialise</c>.
    /// Nothing is logged — neither value may reach a log or a crash report.</para>
    /// </remarks>
    /// <param name="configuration">The configuration the host is being composed from.</param>
    /// <param name="storedSettings">The decrypted settings, or <c>null</c> on a first run.</param>
    /// <exception cref="InvalidOperationException">
    /// A stored secret passed the length gate but was rejected by <c>AppSecrets</c> — a retired
    /// literal. Deliberately fatal: the alternative is running on a key known to be compromised.
    /// </exception>
    private static void ApplySiteSecrets(IConfiguration configuration, ConnectionSettings storedSettings)
    {
        if (storedSettings?.HasUsableSecrets() != true)
        {
            return;
        }

        configuration[AppSecrets.JwtSigningKeyPath] = storedSettings.JwtSigningKey;
        configuration[AppSecrets.EncryptionKeyPath] = storedSettings.AppEncryptionKey;

        AppSecrets.Initialise(configuration);
    }

    /// <summary>
    /// Layers the embedded <c>appsettings.json</c> onto the configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A MAUI app has no content-root file system to read settings
    /// from, so the defaults ship as an embedded resource. It deliberately carries no secrets: the
    /// only secret BlogApp holds is the connection string, and that lives in secure storage.</para>
    /// <para><b>Flow:</b> open the manifest stream → add as a JSON source.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="configuration">The configuration builder to add the source to.</param>
    private static void AddEmbeddedConfiguration(IConfigurationBuilder configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("BlogApp.appsettings.json");
        if (stream == null)
        {
            Log.Warning("Embedded appsettings.json was not found; continuing with built-in defaults");
            return;
        }

        configuration.AddJsonStream(stream);
    }

    /// <summary>
    /// Creates the connection store used before the container exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The store is needed to decide what connection string to register,
    /// which happens before there is a service provider to resolve it from. The instance built here
    /// is the one registered afterwards, so the storage backend it actually used is the one the
    /// settings screen reports.</para>
    /// <para><b>Flow:</b> build a Serilog-backed logger factory → construct the store.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The connection store.</returns>
    private static IConnectionStore CreateConnectionStore()
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddSerilog(dispose: false));
        return new ConnectionStore(loggerFactory.CreateLogger<ConnectionStore>());
    }

    /// <summary>
    /// Reads the stored connection settings without blocking the UI thread's message pump.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Secure storage is asynchronous on every platform, but the DI
    /// graph must be registered synchronously. Running the read on a thread-pool thread avoids the
    /// classic deadlock of blocking a UI thread on a continuation that wants to come back to it. A
    /// failure is logged and treated as "not configured", which sends the operator to the setup
    /// screen instead of leaving them with a dead window.</para>
    /// <para><b>Flow:</b> offload → await → return, or log and return null.</para>
    /// <para><b>Side Effects:</b> Reads the OS credential store.</para>
    /// </remarks>
    /// <param name="connectionStore">The store to read from.</param>
    /// <returns>The stored settings, or <c>null</c> when none are available.</returns>
    private static ConnectionSettings LoadStoredSettings(IConnectionStore connectionStore)
    {
        try
        {
            return Task.Run(connectionStore.LoadAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Stored connection settings could not be read; opening the connection-setup screen");
            return null;
        }
    }

    /// <summary>
    /// Resolves the writable folder BlogApp keeps its logs, uploads and credentials in.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uses the OS's per-user local application data rather than
    /// <c>AppContext.BaseDirectory</c>, because an installed desktop app's program folder is not
    /// writable. The path is stable across restarts, which is what makes the persistence acceptance
    /// criterion checkable.</para>
    /// <para><b>Flow:</b> read the special folder → append the product path.</para>
    /// <para><b>Side Effects:</b> None; callers create the directories they need.</para>
    /// </remarks>
    /// <returns>The absolute path of BlogApp's per-user data folder.</returns>
    private static string ResolveAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TechieBlog", "BlogApp");
    }
}
