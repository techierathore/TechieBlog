// =============================================================================
// TechieBlog Application Entry Point
// Purpose: Configures and starts the Blazor Server application
//
// Requirements wired here:
//   REQ-NFR-013 - Serilog console + daily rolling file, unhandled-exception handlers,
//                 CloseAndFlush on exit
//   REQ-NFR-015 - Correlation ID per request, echoed to the client and pushed into logs
//   REQ-NFR-014 - /health (liveness) and /health/ready (database + critical services)
//   REQ-NFR-039 - /healthz additionally asserts DbUp's journal matches the migration scripts,
//                 so a failed migration turns the deploy red instead of shipping an empty site
//   REQ-NFR-005 - Rate limiting on the authentication endpoints
//   REQ-NFR-012 - Polly retry + circuit-breaker pipelines for database, email and storage
//   REQ-NFR-018 - In-memory caching plus output caching for public listings and feeds
//   REQ-FN-009  - Five authorization policies built from AppPolicies.PolicyRoleMap
// =============================================================================
using Blazored.LocalStorage;
using BlogDb;
using BlogEngine;
using BlogEngine.Common;
using BlogEngine.Services;
using BlogEngine.Storage;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using BlogUI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Polly;
using Serilog;
using Serilog.Events;
using System.Text.Json;
using System.Threading.RateLimiting;
using TechieBlog.Authentication;
using TechieBlog.Configuration;
using TechieBlog.HealthChecks;
using TechieBlog.Middleware;
using TechieBlog.Observability;
using TechieBlog.Services;
using TrBlazeUI.Components.Toast;
using TrBlazeUI.Primitives.Extensions;

// Configure Serilog early for startup logging (REQ-NFR-013).
// {CorrelationId} is supplied by CorrelationIdMiddleware through the LogContext enricher
// and renders empty for events raised outside a request (REQ-NFR-015).
// The logger is built BEFORE the host, so it reads a minimal bootstrap configuration of its own
// rather than the app's IConfiguration (which does not exist yet). Verbose Blazor/SignalR logging
// is DEVELOPMENT-ONLY: at Debug those categories emit roughly 61 KB per request and produced a
// 341 MB log file in a single day of local testing. Before this gate the levels were hard-coded,
// so that volume shipped to production unchanged (REQ-NFR-013 / REQ-NFR-001).
// SetBasePath is AppContext.BaseDirectory, not the working directory: appsettings.json is copied
// to the output folder, so anchoring here means the bootstrap logger reads the same settings
// whether the host was started with `dotnet run`, from the published folder, or from a container -
// which is the same reasoning that fixes the log path itself (see LogFileSettings).
var bootstrapConfiguration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    // PascalCase deployment variables (LogFileEnabled, SeqUrl, ...) - the provider the coding
    // standards require. Added last so it outranks both the JSON files and the framework's
    // double-underscore form of the same setting.
    .AddAppEnvironmentVariables()
    .Build();

var environmentName = bootstrapConfiguration["ASPNETCORE_ENVIRONMENT"]
    ?? bootstrapConfiguration["DOTNET_ENVIRONMENT"]
    ?? "Production";
var isDevelopmentHost = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
var componentLogLevel = isDevelopmentHost ? LogEventLevel.Debug : LogEventLevel.Warning;

// REQ-NFR-029: where the file goes and how much disk it may ever use are both settings, resolved
// in ONE place. The path is anchored on AppContext.BaseDirectory rather than left relative, which
// is what ends the two-log-folder mess documented on LogFileSettings; the size cap multiplied by
// the retained count is the worst-case total, announced below so nobody has to work it out.
var logFileSettings = LogFileSettings.Resolve(bootstrapConfiguration, AppContext.BaseDirectory);
var seqSettings = SeqSettings.Resolve(bootstrapConfiguration);

var loggerConfiguration = new LoggerConfiguration()
    // Code defaults first; the Serilog section of appsettings.json is read afterwards and wins,
    // which is the override order the coding standards require (REQ-NFR-001).
    .MinimumLevel.Is(isDevelopmentHost ? LogEventLevel.Debug : LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.SignalR", componentLogLevel)
    .MinimumLevel.Override("Microsoft.AspNetCore.Components", componentLogLevel)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .ReadFrom.Configuration(bootstrapConfiguration)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    // One shared Seq server receives events from every application on the VPS, so an event that
    // does not name its application cannot be filtered. Enriched on the LOGGER, not on the Seq
    // sink, so the console and the file carry it too and a local trace is comparable with a
    // production one.
    .Enrich.WithProperty(SeqSettings.ApplicationPropertyName, SeqSettings.ApplicationName)
    // Console is unconditional and is the PRIMARY sink in a container: Docker captures stdout, and
    // `docker logs` is the first thing anyone reaches for when a deployment will not start.
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {CorrelationId}{NewLine}  {Message:lj}{NewLine}{Exception}");

// The file sink is CONDITIONAL (REQ-NFR-029). Inside a container it writes into an ephemeral layer
// that the next redeploy discards, so it earns nothing and costs disk; the deployment sets
// LogFileEnabled=false and keeps stdout plus Seq. On a developer machine it stays on, because that
// is where the verbose Blazor render-tree logging is actually read.
if (logFileSettings.Enabled)
{
    loggerConfiguration = loggerConfiguration.WriteTo.File(
        path: logFileSettings.FilePathTemplate,
        rollingInterval: RollingInterval.Day,
        // A daily roll alone does not bound a single file, and a per-file cap alone does not bound
        // the DISK - it just rolls, which is how 305 MB accumulated. The pair below is the bound:
        // SizeLimitBytes * RetainedFileCountLimit, and nothing else, is what this host can occupy.
        retainedFileCountLimit: logFileSettings.RetainedFileCountLimit,
        fileSizeLimitBytes: logFileSettings.SizeLimitBytes,
        rollOnFileSizeLimit: true,
        // Second instance of the host appends to the same file instead of silently starting its own
        // _NNN sequence. Serilog.Sinks.File 7.x routes a shared sink through SharedFileSink, which
        // takes the size limit too, so sharing and size-rolling are NOT mutually exclusive here -
        // the pair that IS refused is shared + buffered, and shared + lifecycle hooks, neither of
        // which this sink uses.
        shared: logFileSettings.Shared,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}");
}

// Seq only when a URL is configured, so a clone of this repository runs with no Seq anywhere and
// no connection errors (REQ-NFR-013). The API key is a live credential and is never logged.
if (seqSettings.IsEnabled)
{
    loggerConfiguration = loggerConfiguration.WriteTo.Seq(seqSettings.Url, apiKey: seqSettings.ApiKey);
}

Log.Logger = loggerConfiguration.CreateLogger();

// Last-resort handlers so nothing dies silently outside the request pipeline (REQ-NFR-013).
// Registered before the host is built, because a failure during composition must still be logged.
// The bodies live in GlobalExceptionLogging so they are unit-testable and so the flush-on-crash
// rule (close the sink only when the runtime is actually terminating) has one definition.
GlobalExceptionLogging.Wire();

// -----------------------------------------------------------------------------
// Thread-pool floor (REQ-NFR-001 / REQ-NFR-026) - DEFAULTS TO OFF, AND THE MEASUREMENT SAYS WHY.
//
// The diagnosis behind this knob is solid. Under 100 concurrent requests the public pages queue
// while PostgreSQL sits idle: pg_stat_activity showed 22-24 connections with only 1-3 ACTIVE, so
// the database is not the constraint. The cause is the synchronous half of the data-access layer -
// REQ-NFR-026 stages 3-4 are unfinished, ~109 Blazor call sites still reach the repositories
// through blocking Dapper calls, and each one parks a thread-pool thread for a whole round trip.
// Raising the pool's floor is the textbook mitigation for that shape, and it is what this knob does.
//
// IT WAS TRIED HERE AND IT MADE THINGS WORSE. Measured 2026-08-09 on this 8-core box, same binary,
// arms alternated A/B/A/B/A/B so machine drift landed on both arms equally, concurrency 100, 500
// requests per arm per round:
//     floor = runtime default (8):  p50 1.379 / 1.736 / 1.816 s, 65.6 / 55.8 / 59.4 % under 2 s
//     floor = 256:                  p50 2.439 / 2.298 / 2.167 s, 32.0 / 35.6 / 41.8 % under 2 s
// Every round agreed and the ranges do not overlap. 256 runnable threads oversubscribe 8 cores, and
// the context-switch and cache cost exceeds what is saved by not waiting on thread injection. The
// pages that breach the budget are partly CPU-bound (74-79 % total CPU during the /post run), and
// more threads cannot buy CPU that is already spent.
//
// So the default is 0 - OFF, runtime behaviour unchanged. The knob is kept, and this note with it,
// for two reasons: a production host with many more cores may well sit on the other side of that
// trade, and the next person to reach for SetMinThreads should see the numbers before spending the
// afternoon this cost. The real fix remains finishing REQ-NFR-026 so no request blocks a thread at
// all. Set Performance:MinWorkerThreads to a positive value to enable, and re-measure before
// keeping it.
// -----------------------------------------------------------------------------
var minimumWorkerThreads = bootstrapConfiguration.GetValue("Performance:MinWorkerThreads", 0);

if (minimumWorkerThreads > 0)
{
    ThreadPool.GetMinThreads(out var currentWorkerThreads, out var currentCompletionPortThreads);

    if (minimumWorkerThreads > currentWorkerThreads
        && ThreadPool.SetMinThreads(minimumWorkerThreads, currentCompletionPortThreads))
    {
        Log.Information(
            "Thread-pool minimum worker threads raised from {Previous} to {Current} (REQ-NFR-001)",
            currentWorkerThreads,
            minimumWorkerThreads);
    }
}

try
{
    Log.Information("Starting TechieBlog application");

    // Say where the logs went and how big they can get, in the logs themselves. The two-folder
    // incident (REQ-NFR-029) was hard to diagnose precisely because nothing ever named the
    // destination, so an operator reading one folder had no way to know a bigger one existed.
    if (logFileSettings.Enabled)
    {
        Log.Information(
            "Log file sink writing to {LogDirectory} - at most {RetainedFileCountLimit} files of "
            + "{SizeLimitBytes} bytes, worst case {WorstCaseTotalBytes} bytes total",
            logFileSettings.DirectoryPath,
            logFileSettings.RetainedFileCountLimit,
            logFileSettings.SizeLimitBytes,
            logFileSettings.WorstCaseTotalBytes);
    }
    else
    {
        Log.Information("Log file sink is disabled; console{SeqSuffix} only",
            seqSettings.IsEnabled ? " and Seq" : string.Empty);
    }

    if (seqSettings.IsEnabled)
    {
        Log.Information("Seq sink enabled for {SeqUrl} as application {ApplicationName}",
            seqSettings.Url, SeqSettings.ApplicationName);
    }

    var builder = WebApplication.CreateBuilder(args);

    // The same PascalCase provider the bootstrap configuration uses, now on the host's own
    // configuration so every IConfiguration consumer sees the deployment variables.
    builder.Configuration.AddAppEnvironmentVariables();

    // -------------------------------------------------------------------------
    // Cryptographic secrets (REQ-NFR-027)
    // The JWT signing key and the AES key used to be literals in AppConstants, readable by anyone
    // with the repository. They now come from configuration and this call is what loads them, so it
    // runs before any service is registered - AuthSvc signs tokens with the first and AppEncrypt
    // protects the session envelope with the second. A missing or unusable value throws here and
    // the host does not start; there is deliberately no fallback default.
    // -------------------------------------------------------------------------
    AppSecrets.Initialise(builder.Configuration);

    // -------------------------------------------------------------------------
    // Deployment settings that fail SILENTLY (REQ-NFR-030)
    // SiteSettings:BaseUrl is read once at construction by every service that mails a link, and
    // Analytics:VisitorSalt is the only thing making a stored visitor digest non-reversible. Both
    // are wrong-by-default rather than absent-by-default, so nothing downstream ever notices. This
    // gate runs before any service is registered: outside Development it THROWS and the host does
    // not start; in Development it logs one loud warning and carries on, which is what keeps this
    // repository runnable and the smoke harness green. See DeploymentConfiguration for the full
    // reasoning and the exact commands that supply both values.
    // -------------------------------------------------------------------------
    DeploymentConfiguration.Enforce(
        builder.Configuration,
        builder.Environment.EnvironmentName,
        warning => Log.Warning("{DeploymentConfigurationWarning}", warning));

    // Use Serilog for all logging
    builder.Host.UseSerilog();

    // -------------------------------------------------------------------------
    // Forwarded headers (REQ-NFR-028)
    // Registered here and invoked as the first middleware below, so that behind a reverse proxy
    // Connection.RemoteIpAddress is the real client and both rate limiters partition per caller
    // rather than collapsing into one site-wide bucket. The allow-list is explicit: with no
    // ForwardedHeaders section configured nothing is trusted and the transport address is used
    // unchanged, which is the correct behaviour for a direct-to-Kestrel deployment.
    // -------------------------------------------------------------------------
    builder.Services.Configure<ForwardedHeadersOptions>(
        options => ForwardedHeadersSetup.Configure(options, builder.Configuration));

    // Enable static web assets from referenced RCL projects
    builder.WebHost.UseStaticWebAssets();

    // Register TrBlazeUI (REQ-UI-048 — replaces Microsoft Fluent UI Blazor).
    // AddTrBlazeUIPrimitives supplies PortalService, FocusManager and PositioningService,
    // which the overlay components (Dialog, Sheet, Popover, DropdownMenu, Tooltip) require.
    builder.Services.AddTrBlazeUIPrimitives();
    builder.Services.AddScoped<ToastService>();

    // -------------------------------------------------------------------------
    // Database connection string - TWO ACCEPTED KEYS, and the deviation is deliberate.
    //
    // This project's canonical key is the flat PascalCase name `AppDbConString`, which is what the
    // coding standards' environment-variable rule produces and what every existing settings file,
    // user-secret and document in this repository uses. The portfolio deployment spec assumes the
    // framework's `ConnectionStrings:Default` instead. Rather than silently follow one and break the
    // other, BOTH are read: `AppDbConString` wins, and `ConnectionStrings:Default` is accepted as a
    // fallback so a compose file written to the generic spec still starts the host.
    //
    // The pipeline should set `AppDbConString`.
    // -------------------------------------------------------------------------
    var dbConnectionString = builder.Configuration["AppDbConString"]
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "AppDbConString is not configured. Set it in appsettings.json or the environment before "
        + "starting the host (the environment variable is AppDbConString; ConnectionStrings__Default "
        + "is accepted as a fallback).");

    // Initialize BlogEngine Services
    BlogSvcInitializer.Initialize(builder.Services, dbConnectionString);

    // -------------------------------------------------------------------------
    // Uploaded media location (REQ-FN-025)
    // Resolved ONCE, here, and registered so the static-file mapping below and the storage factory
    // that writes the bytes cannot disagree. Unset `Uploads:Path` keeps the historical behaviour -
    // uploads under wwwroot - so a fresh clone runs with nothing configured.
    // -------------------------------------------------------------------------
    var uploadsLocation = UploadsLocation.Resolve(
        builder.Configuration, builder.Environment.WebRootPath, builder.Environment.ContentRootPath);
    builder.Services.AddSingleton(uploadsLocation);

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DetailedErrors = true;
        });

    // -------------------------------------------------------------------------
    // Caching (REQ-NFR-018, BRD-78)
    // In-memory caching backs ICacheService for settings, taxonomy and listings; output
    // caching serves anonymous listing and feed responses without re-rendering them.
    // Both layers use the CacheTags names, so one invalidation event clears them together.
    // -------------------------------------------------------------------------
    builder.Services.AddMemoryCache();
    builder.Services.AddOutputCache(options =>
    {
        // NO BASE POLICY - opt-in only. There used to be
        //     options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromMinutes(1)));
        // which cached EVERY endpoint that did not opt out, for one minute, with NO TAG. That one
        // line was the real cause of "I changed the site title / logo / a post's abstract and the
        // site still shows the old one" (UAT-021, UAT-022, UAT-023), and it defeated every
        // invalidation this codebase has: the page HTML embeds the site title, logo and theme, so a
        // Save correctly evicted ICacheService AND correctly re-read the database - and then the
        // host served the previous minute's HTML anyway. Because the policy carried no tag, neither
        // ServiceCache's EvictTag nor /api/admin/cache/refresh's EvictByTagAsync(Content) could
        // reach it; nothing could, short of waiting it out. MEASURED: with the base policy, a
        // settings save took ~56s to become visible whenever the page had been requested in the
        // preceding minute, against ~1.3s with it removed - and the delay was flat at ~56s whether
        // 40 or 120 requests had populated the cache, which is what identified a fixed expiry
        // rather than a load-dependent effect.
        //
        // It was also caching authenticated pages. Output caching does not vary by the
        // BlazorServerAuth session cookie, so a signed-in user's rendered HTML was eligible to be
        // served to the next caller of the same URL. The `/healthz` opt-out below was written after
        // this exact hazard was observed once (a stale `200 Healthy` with `Age: 54`); the endpoint
        // was special-cased while the blanket policy that caused it was left in place.
        //
        // Caching is therefore explicit from here on: `.CacheOutput(...)` on the endpoints that
        // genuinely serve identical bytes to everyone - the feeds and robots.txt below - each
        // tagged CacheTags.Content so an invalidation event can actually clear them.
        options.AddPolicy(OutputCachePolicies.PublicListing, policy => policy
            .Expire(TimeSpan.FromMinutes(5))
            .Tag(CacheTags.Content));
        options.AddPolicy(OutputCachePolicies.Feed, policy => policy
            .Expire(TimeSpan.FromMinutes(15))
            .Tag(CacheTags.Content));
    });

    // -------------------------------------------------------------------------
    // Resilience (REQ-NFR-012, BRD-89)
    // Named Polly v8 pipelines - retry with jittered exponential backoff wrapped in a
    // circuit breaker - for the three outbound dependencies. Call sites resolve
    // ResiliencePipelineProvider<string> and execute through the pipeline they need;
    // an open breaker fails fast so callers can degrade to cached or queued behaviour.
    // -------------------------------------------------------------------------
    builder.Services.AddResiliencePipeline(ResiliencePipelines.Database, ResiliencePipelines.ConfigureDatabase);
    builder.Services.AddResiliencePipeline(ResiliencePipelines.Email, ResiliencePipelines.ConfigureEmail);
    builder.Services.AddResiliencePipeline(ResiliencePipelines.Storage, ResiliencePipelines.ConfigureStorage);

    // -------------------------------------------------------------------------
    // Migration scripts folder (REQ-NFR-039)
    //
    // Resolved ONCE, here, because two things must agree about it: DbUp, which applies the
    // scripts after builder.Build(), and SchemaMigrationProbe, which asserts they were applied.
    // Pointing the probe at a different folder than the migrator would make the gate assert
    // something nobody ran - so the value is computed before either is wired and shared.
    // -------------------------------------------------------------------------
    var migrationScriptsPath = MigrationScripts.ResolvePath();

    // -------------------------------------------------------------------------
    // Health checks (REQ-NFR-014, BRD-74, REQ-NFR-039)
    // /health answers while the process is alive; /health/ready additionally verifies the
    // database and the critical singletons before the instance accepts traffic.
    //
    // The "schema" check (REQ-NFR-039) is the third, and it closes a hole the other two could
    // not see: DbUp runs at startup and can FAIL - a role without DDL rights is the usual cause -
    // while the process still comes up. "database" only proves PostgreSQL answered SELECT 1, so
    // /healthz returned 200 over an unmigrated database and the pipeline's verify job went green
    // on a deploy that had shipped an empty site. The schema check reads DbUp's own journal and
    // compares it against the very folder DbUp was pointed at, so a migration failure is now a
    // 503 on /healthz rather than a warning nobody read.
    // -------------------------------------------------------------------------
    builder.Services.AddSingleton(serviceProvider => new SchemaMigrationProbe(
        dbConnectionString,
        migrationScriptsPath,
        serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SchemaMigrationProbe>>()));

    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: new[] { HealthCheckTags.Ready })
        .AddCheck<CriticalServicesHealthCheck>("criticalservices", tags: new[] { HealthCheckTags.Ready })
        .AddCheck<SchemaMigrationHealthCheck>("schema", tags: new[] { HealthCheckTags.Ready });

    // -------------------------------------------------------------------------
    // Rate limiting on the authentication endpoints (REQ-NFR-005, BRD-82)
    // Partitioned by client IP and scoped to the credential-handling paths, so the Blazor
    // circuit, static assets and health probes are untouched. The per-account lockout that
    // catches sign-ins arriving over an existing circuit lives in
    // BlogEngine.Common.LoginThrottle - the two together satisfy the requirement.
    // -------------------------------------------------------------------------
    var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", AuthRateLimit.DefaultPermitLimit);
    var authWindowSeconds = builder.Configuration.GetValue("RateLimiting:AuthWindowSeconds", AuthRateLimit.DefaultWindowSeconds);
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var partitionKey = AuthRateLimit.BuildPartitionKey(context);
            if (partitionKey == AuthRateLimit.UnlimitedPartitionKey)
                return RateLimitPartition.GetNoLimiter(partitionKey);

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            });
        });

        options.OnRejected = (context, cancellationToken) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = authWindowSeconds.ToString();

            // A body, however short, is what keeps this a rate-limit response. The status-code
            // page middleware added below re-executes ANY 4xx that carries no body, so a silent
            // 429 would be answered with the "page not found" screen - the status would survive
            // but the explanation would be a lie. Writing the reason here settles it.
            context.HttpContext.Response.ContentType = "text/plain";
            Log.Warning(
                "Rate limit rejected {RequestMethod} {RequestPath} from {RemoteIp}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);
            return new ValueTask(context.HttpContext.Response.WriteAsync(
                "Too many requests. Try again shortly.", cancellationToken));
        };
    });

    // Authentication and Authorization Services
    builder.Services.AddBlazoredLocalStorage();

    // -------------------------------------------------------------------------
    // Authentication (REQ-FN-058)
    //
    // THE DEFECT THIS REPLACES. This registration used to be AddCookie("BlazorServerAuth"), a
    // perfectly ordinary cookie scheme that NOTHING EVER ISSUED A COOKIE FOR - the product's session
    // is a JWT held in browser local storage, which no HTTP request carries. HttpContext.User was
    // therefore anonymous on every request the host has ever served. That was invisible until you
    // deep-linked: MapRazorComponents promotes a routable component's [Authorize] attribute to
    // ENDPOINT METADATA, so UseAuthorization challenged every full document load of an admin route
    // and answered 302 -> /login?ReturnUrl=..., no matter how valid the visitor's session was. The
    // login page then hydrated, saw an authenticated principal from local storage, and forwarded to
    // the public home page. Measured on 2026-08-10 before the fix:
    //     GET /admin/analytics -> 302 http://.../login?ReturnUrl=%2Fadmin%2Fanalytics
    // Bookmarks, shared admin links and F5 on any admin page all landed on "/". Only client-side
    // navigation worked, because it issues no document request at all.
    //
    // THE FIX. SessionCookieAuthenticationHandler reads the access token from the cookie that
    // CustomAuthStateProvider now mirrors alongside local storage, and resolves it through the SAME
    // IAuthService lookup the circuit uses - so a revoked or expired session is refused identically
    // whichever way it arrives, and the endpoint authorization that was rejecting everybody becomes
    // a real, working layer instead of a permanent 302. It also gives the STATIC PRERENDER PASS a
    // principal to read, so an admin route now prerenders its actual page rather than nothing.
    // -------------------------------------------------------------------------
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = SessionCookieAuthenticationHandler.SchemeName;
        options.DefaultChallengeScheme = SessionCookieAuthenticationHandler.SchemeName;
    })
    .AddScheme<AuthenticationSchemeOptions, SessionCookieAuthenticationHandler>(
        SessionCookieAuthenticationHandler.SchemeName, configureOptions: null);

    // -------------------------------------------------------------------------
    // Authorization policies (REQ-FN-009, BRD-7/8)
    // The four role-based policies - AdminOnly, EditorOrAbove, AuthorOrAbove and
    // ContributorOrAbove - are generated from AppPolicies.PolicyRoleMap so the hierarchy
    // has exactly one definition, which the unit tests assert directly. The fifth policy,
    // Authenticated, is not role-based and is added explicitly.
    // -------------------------------------------------------------------------
    builder.Services.AddAuthorization(options =>
    {
        foreach (var policy in AppPolicies.PolicyRoleMap)
        {
            options.AddPolicy(policy.Key, configure => configure.RequireRole(policy.Value));
        }

        options.AddPolicy(AppPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
    });
    builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddTransient<IAuthService, AuthService>();
    builder.Services.AddScoped<ThemeService>();

    // UAT-024: the website's answer to "open this public post outside the admin window" is a
    // new browser tab. Registered here (not BlogSvcInitializer, which defines only the shared
    // engine graph) because "the current window" is a head-specific notion.
    builder.Services.AddScoped<IExternalLinkOpener, BrowserTabLinkOpener>();

    // UAT-023 mechanism B: the website's own answer to "notify the site to refresh its cache" is
    // a no-op, because BlogSvc already invalidated the cache IN THIS PROCESS the moment the post
    // was saved. BlogApp's registration of the same interface (MauiProgram.cs) is the one that
    // actually reaches across a process boundary.
    builder.Services.AddScoped<ISiteCacheNotifier, NullSiteCacheNotifier>();

    var app = builder.Build();

    // -------------------------------------------------------------------------
    // Run database migrations automatically at startup
    //
    // A failure here is deliberately NOT fatal - the host keeps starting so an operator can reach
    // the site and diagnose it. That tolerance is precisely what REQ-NFR-039 had to compensate
    // for: it means a broken migration leaves a running process, so the readiness endpoint - not
    // this block - is what has to make the deploy red. The "schema" health check registered above
    // reads DbUp's journal against `migrationScriptsPath`, the same folder passed here.
    // -------------------------------------------------------------------------
    Log.Information("Running database migrations...");

    if (Directory.Exists(migrationScriptsPath))
    {
        var dbSvc = new BlogDbSvc();
        var migrationSuccess = dbSvc.UpgradeDatabase(dbConnectionString, migrationScriptsPath);
        if (!migrationSuccess)
        {
            Log.Error(
                "Database migration FAILED - check the DbUp output above. The host will still " +
                "start, but /healthz reports Unhealthy until the schema is complete (REQ-NFR-039)");
        }
        else
        {
            Log.Information("Database migrations completed successfully");
        }
    }
    else
    {
        Log.Warning("PostgresScripts folder not found at {ScriptsPath} - skipping migrations", migrationScriptsPath);
    }

    // Configure the HTTP request pipeline.

    // First in the pipeline (REQ-NFR-028): everything downstream - the correlation id, the request
    // log line, the rate limiter partitions - must see the rewritten client address, not the
    // proxy's. A no-op when the ForwardedHeaders allow-list is empty.
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // Correlation ID first, so every later log line - the request summary, the health
    // probes, repository warnings and any error page - carries the same id (REQ-NFR-015).
    app.UseMiddleware<CorrelationIdMiddleware>();

    // -------------------------------------------------------------------------
    // Not-found handling (REQ-UI-012)
    //
    // This is a Blazor Web App: MapRazorComponents registers ONE endpoint per discovered @page,
    // so a URL matching no directive matches no endpoint, routing ends the request with a bare
    // 404 and an EMPTY body, and the Blazor router never runs - which is why the <NotFound>
    // fragment in Routes.razor could never fire and a mistyped URL rendered a blank white page.
    //
    // The first fix for that was a catch-all `@page "/{*NotFoundPath}"` on 404Page.razor. It did
    // render the page, but it also made the defect unfixable and broke something else:
    //   * nothing 404s any more, so the response was HTTP 200 - a soft 404, which search engines
    //     index and monitoring cannot see; and the page cannot set the status itself, because the
    //     router is mounted @rendermode="InteractiveServer" and never receives HttpContext.
    //   * the catch-all matched EVERY unrouted path, including /uploads/<file>. StaticFileMiddleware
    //     skips a request that has already matched an endpoint, so a file uploaded at runtime -
    //     absent from the build-time static-asset manifest, therefore without an endpoint of its
    //     own - was answered with the 404 page as text/html instead of the image (REQ-FN-025).
    //
    // So the catch-all is gone and the status code is restored where it belongs, in the pipeline:
    // an unmatched URL 404s for real, and this middleware re-executes the request against /404 to
    // produce the body while PRESERVING the original status. Re-execution restarts the pipeline
    // from here, which is why UseRouting is now called EXPLICITLY below rather than being
    // auto-inserted by WebApplication at position zero - routing has to run again on the rewritten
    // path, otherwise the re-executed request matches no endpoint and the body stays empty.
    //
    // Scope: infrastructure paths are excluded. An HTML page is the wrong answer for a health
    // probe, a SignalR negotiate, a framework asset or a missing upload, all of which have callers
    // that parse the body.
    // -------------------------------------------------------------------------
    app.UseWhen(
        context => !NotFoundPage.IsInfrastructurePath(context.Request.Path),
        branch => branch.UseStatusCodePagesWithReExecute(NotFoundPage.Path));

    // -------------------------------------------------------------------------
    // Uploaded media (REQ-FN-025)
    //
    // Uploads are written under <web root>/uploads at RUNTIME, so they exist in no build-time
    // manifest and MapStaticAssets knows nothing about them. Serving them therefore has to happen
    // BEFORE routing: once an endpoint is selected the static-file middleware stands aside, and
    // any future catch-all or fallback route would silently swallow every newly uploaded image
    // again - the exact defect above, which stayed invisible because a rebuild refreshed the
    // manifest and made it look fixed.
    //
    // This handler is scoped to /uploads only; everything else still resolves through
    // MapStaticAssets and the wwwroot handler below, keeping their fingerprinting and caching.
    // Unknown extensions are NOT served (ServeUnknownFileTypes stays false), so this cannot become
    // a way to hand an arbitrary file type to a browser from a user-writable directory.
    //
    // THE DIRECTORY IS NO LONGER wwwroot/uploads BY DEFINITION (REQ-FN-025). It is whatever
    // UploadsLocation resolved above, which in a container is the mounted host volume - because a
    // file written under wwwroot lives in the image layer and the next redeploy deletes every image
    // an editor has ever uploaded. There is still exactly ONE /uploads route: this one. Nothing else
    // maps that prefix, and the storage factory writes beneath the same resolved root, so the URL
    // recorded against an image resolves to the file that was actually written.
    // -------------------------------------------------------------------------
    Directory.CreateDirectory(uploadsLocation.UploadsRootPath);
    Log.Information(
        "Uploaded media served from {UploadsRootPath} at {RequestPath} (configured: {IsConfigured})",
        uploadsLocation.UploadsRootPath, UploadsLocation.RequestPath, uploadsLocation.IsConfigured);
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsLocation.UploadsRootPath),
        RequestPath = UploadsLocation.RequestPath,
        ServeUnknownFileTypes = false
    });

    // Endpoint matching happens HERE, not at position zero. See the not-found block above: the
    // re-executed /404 request restarts the pipeline at that middleware, so routing must sit after
    // it. Everything endpoint-aware - the rate limiter's DisableRateLimiting metadata,
    // authorization, output caching, antiforgery - still runs after this call.
    app.UseRouting();

    // Serve static files from wwwroot
    app.UseStaticFiles();

    // Serilog request logging - shows HTTP requests in console
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            diagnosticContext.Set(
                CorrelationIdMiddleware.LogPropertyName,
                httpContext.Items[CorrelationIdMiddleware.ContextItemKey]);
    });

    app.MapStaticAssets();

    // Throttle the authentication endpoints (REQ-NFR-005)
    app.UseRateLimiter();

    // Authentication and Authorization middleware (order matters!)
    app.UseAuthentication();
    app.UseAuthorization();

    // Output caching for public listings and feeds (REQ-NFR-018)
    app.UseOutputCache();

    app.UseAntiforgery();

    app.MapRazorComponents<TechieBlog.Components.App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(BlogUI._Imports).Assembly);

    // -------------------------------------------------------------------------
    // A health probe must NEVER be served from the output cache (REQ-NFR-039).
    //
    // AddOutputCache above installs a BASE policy - one minute, applied to every endpoint that
    // does not opt out - and a health endpoint is the one place a cached 200 is actively
    // dangerous: it reports a state that has since changed. Observed directly while smoke-testing
    // this requirement's negative control: after the schema was broken, `/healthz` kept answering
    // `200 Healthy` with `Age: 54` for the remainder of the cached minute. A deployment pipeline
    // that curls the probe inside that window is told the deploy succeeded.
    //
    // Only 200s are cached, so the danger is entirely one-directional - a STALE GREEN, never a
    // stale red, which is exactly the wrong direction for a gate. NoCache() on all three health
    // endpoints makes every probe evaluate live.
    // -------------------------------------------------------------------------

    // Liveness: the process is up and the pipeline responds (REQ-NFR-014)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = WriteHealthResponse
    }).DisableRateLimiting().CacheOutput(policy => policy.NoCache());

    // Readiness: the database and the critical singletons are usable (REQ-NFR-014)
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
        ResponseWriter = WriteHealthResponse
    }).DisableRateLimiting().CacheOutput(policy => policy.NoCache());

    // -------------------------------------------------------------------------
    // Deployment probe (REQ-NFR-014)
    //
    // /healthz is the name the deployment pipeline curls after every push, and the URL an uptime
    // monitor watches. It carries the SAME checks as /health/ready - PostgreSQL included - because a
    // probe that goes green while the database is unreachable would let a broken deploy be reported
    // as successful, which is the one thing this endpoint exists to prevent.
    //
    // /health and /health/ready are KEPT: existing monitoring may already point at them, and this is
    // an alias rather than a rename.
    //
    // Three things could quietly break it, so each is handled explicitly:
    //   * authorization - AllowAnonymous, so the endpoint can never inherit a fallback policy;
    //   * rate limiting - DisableRateLimiting, so a monitor polling every minute is never 429'd;
    //   * the status-code re-execute - "/healthz" starts with the "/health" prefix already listed in
    //     NotFoundPage.InfrastructurePrefixes, so a failing probe returns its JSON body and not the
    //     HTML not-found page. That is load-bearing: the pipeline parses this response;
    //   * output caching - NoCache, so the probe is never answered from a cached 200 (REQ-NFR-039).
    // -------------------------------------------------------------------------
    app.MapHealthChecks(DeploymentHealthProbe.Path, new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
        ResponseWriter = WriteHealthResponse
    }).DisableRateLimiting().AllowAnonymous().CacheOutput(policy => policy.NoCache());

    // Sitemap.xml endpoint for SEO - output cached as a feed (REQ-NFR-018)
    app.MapGet("/sitemap.xml", (SitemapSvc sitemapSvc) =>
    {
        var xml = sitemapSvc.GenerateSitemap();
        return Results.Content(xml, "application/xml");
    }).CacheOutput(OutputCachePolicies.Feed);

    // -------------------------------------------------------------------------
    // RSS 2.0 feed (REQ-FN-037, REQ-UI-046, BRD-63)
    //
    // The feed is an ENDPOINT, not a page. /rss remains a human-facing subscription page rendered
    // by RssFeed.razor; this is the machine-readable document a reader actually subscribes to, and
    // the two are not interchangeable - a reader handed text/html has nothing to parse.
    //
    // The content type is the contract: application/rss+xml is what browser auto-discovery and
    // every reader dispatch on. /rss.xml is mapped as an alias because it is the other URL readers
    // and humans guess; both run the same handler, and the <atom:link rel="self"> inside the
    // document names the canonical one so an aggregator records a single subscription.
    //
    // Output cached under the same fifteen-minute Feed policy as the sitemap, tagged CacheTags.Content
    // so publishing a post evicts it (REQ-NFR-018) - this is the "output caching for RSS" half of
    // that requirement, which previously had nothing to cache.
    // -------------------------------------------------------------------------
    var rssFeedHandler = async (RssFeedSvc rssFeedSvc) =>
    {
        var xml = await rssFeedSvc.GenerateFeedAsync();
        return Results.Content(xml, RssFeedSvc.ContentType);
    };

    app.MapGet(RssFeedSvc.FeedPath, rssFeedHandler).CacheOutput(OutputCachePolicies.Feed);
    app.MapGet("/rss.xml", rssFeedHandler).CacheOutput(OutputCachePolicies.Feed);

    // Robots.txt endpoint for SEO - output cached as a public listing (REQ-NFR-018)
    app.MapGet("/robots.txt", (IConfiguration config) =>
    {
        var baseUrl = config["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? "https://localhost";
        var robotsTxt = $"""
            User-agent: *
            Allow: /

            Sitemap: {baseUrl}/sitemap.xml
            """;
        return Results.Content(robotsTxt, "text/plain");
    }).CacheOutput(OutputCachePolicies.PublicListing);

    // -------------------------------------------------------------------------
    // Remote cache refresh for BlogApp (UAT-023 mechanism B, REQ-FN)
    //
    // Public pages read through MemoryCacheService's ten-minute cache. An edit made through the
    // WEBSITE evicts it correctly (BlogSvc.UpdatePostAsync calls ServiceCache.InvalidateContent).
    // An edit made through BlogApp writes straight to the database from a SEPARATE process and
    // never runs a line of this host, so nothing evicts the entry and the public page keeps
    // serving the stale row for up to ten minutes. Settings.razor already has a "Clear cached
    // content" button (UAT-001) for exactly this class of problem, but it lives behind the
    // website's own admin session, which the BlogApp operator is not signed into here - they are
    // signed into BlogApp, against the database directly. This endpoint is the same remedy,
    // reachable from outside the process.
    //
    // AUTHENTICATION IS DELIBERATELY MANUAL, NOT [Authorize(Policy = ...)]. The caller is not a
    // browser carrying the BlazorServerAuth session cookie SessionCookieAuthenticationHandler
    // reads - it is BlogApp, presenting the SAME access token (BlogModels.AppConstants.AccessKey)
    // its already-signed-in operator holds, as a Bearer header. IAuthService.GetUserByAccessTokenAsync
    // is the IDENTICAL lookup that handler performs for a website request (resolve against the
    // UserLogin table), so a revoked, expired or tampered BlogApp session is refused exactly as a
    // revoked website session would be. Nothing new is trusted: no shared secret, no API key, no
    // widened surface - an anonymous caller gets 401, and a signed-in Reader (or any role below
    // AuthorOrAbove, the same floor ManagePost.razor itself requires to save a post) gets 403.
    // This is the least-surface option that works: it reuses a validation path that already exists
    // and already fails safely, rather than inventing a second one.
    // -------------------------------------------------------------------------
    // DisableAntiforgery is REQUIRED, not a relaxation, and was found by smoking the endpoint:
    // app.UseAntiforgery() sits in front of every POST, so this one answered 400 to BlogApp's call
    // before the Bearer token was ever read - the remedy would have shipped permanently broken and
    // the 400 would have read as "refused" rather than "never reached the handler".
    // Antiforgery defends COOKIE-authenticated form posts, where the browser attaches the caller's
    // identity automatically. This endpoint authenticates ONLY by an Authorization: Bearer header,
    // which a cross-site form post cannot set and a cross-origin fetch cannot add without a CORS
    // preflight this host never approves - so there is no ambient authority for CSRF to abuse.
    // The 401/403 checks in HandleCacheRefreshAsync remain the whole access control.
    app.MapPost("/api/admin/cache/refresh", HandleCacheRefreshAsync)
        .DisableAntiforgery()
        .CacheOutput(policy => policy.NoCache());

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // REQ-NFR-027: a missing secret must fail LOUDLY. Serilog's console sink can be suppressed by
    // a MinimumLevel override, and a zero exit code makes a container orchestrator believe the run
    // succeeded, so the reason is also written straight to stderr and the exit code is set.
    Console.Error.WriteLine($"FATAL: TechieBlog failed to start. {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("TechieBlog application shutting down");
    Log.CloseAndFlush();
}

/// <summary>
/// Writes a health-check report as JSON so an operator can see which dependency failed.
/// </summary>
/// <remarks>
/// <para><b>Business Logic:</b> Reports the overall status plus one entry per check, and echoes
/// the request's correlation id so a failing probe can be traced into the log file.</para>
/// <para><b>Flow:</b> set content type → project the report → serialise.</para>
/// <para><b>Side Effects:</b> Writes the response body.</para>
/// </remarks>
/// <param name="context">The request being answered.</param>
/// <param name="report">The aggregated health report.</param>
/// <returns>A task that completes when the body has been written.</returns>
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        correlationId = context.Items[CorrelationIdMiddleware.ContextItemKey]?.ToString(),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            durationMs = entry.Value.Duration.TotalMilliseconds
        })
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

/// <summary>
/// Handles <c>POST /api/admin/cache/refresh</c> — UAT-023 mechanism B's authenticated remedy for a
/// BlogApp write that never ran through this process's own cache invalidation.
/// </summary>
/// <remarks>
/// <para><b>Business Logic:</b> Authenticates the presented Bearer token through the exact same
/// <see cref="IAuthService.GetUserByAccessTokenAsync"/> lookup <c>SessionCookieAuthenticationHandler</c>
/// performs for a website request (resolve against the <c>UserLogin</c> table), then requires
/// <c>AuthorOrAbove</c> — the same floor <c>ManagePost.razor</c> itself requires to save a post in
/// the first place. On success, evicts the same three <c>ICacheService</c> tags
/// <c>Settings.razor</c>'s "Clear cached content" button evicts (UAT-001) plus the ASP.NET Core
/// output cache, which is a distinct store from <see cref="ICacheService"/>.</para>
/// <para><b>Flow:</b> parse the Bearer header → resolve the token to a user → require
/// <c>AuthorOrAbove</c> → evict every cache layer → log and confirm.</para>
/// <para><b>Side Effects:</b> Evicts <see cref="CacheTags.Content"/>, <see cref="CacheTags.Taxonomy"/>
/// and <see cref="CacheTags.Settings"/> from <see cref="ICacheService"/>, and
/// <see cref="CacheTags.Content"/> from the output cache. Writes one information or warning log
/// entry.</para>
/// <para><b>Extracted as a named local function, not an inline lambda</b>, because the branches
/// below return different concrete <see cref="IResult"/> implementations
/// (<c>UnauthorizedHttpResult</c>, <c>StatusCodeHttpResult</c>, <c>Ok&lt;T&gt;</c>) and an explicit
/// <c>Task&lt;IResult&gt;</c> return type sidesteps any ambiguity in the delegate's inferred natural
/// type — the same reason <see cref="WriteHealthResponse"/> above is a named function rather than
/// an inline lambda.</para>
/// </remarks>
/// <param name="context">The request being answered.</param>
/// <param name="authService">Resolves the presented access token to its owner.</param>
/// <param name="cacheService">The in-memory content/taxonomy/settings cache.</param>
/// <param name="outputCacheStore">The ASP.NET Core output cache, evicted separately.</param>
/// <param name="logger">Logger for the outcome and for a token-resolution failure.</param>
/// <returns>
/// <c>401</c> when no usable token is presented, <c>403</c> when the token's role is below
/// <c>AuthorOrAbove</c>, otherwise <c>200</c> once every cache layer has been evicted.
/// </returns>
static async Task<IResult> HandleCacheRefreshAsync(
    HttpContext context,
    IAuthService authService,
    ICacheService cacheService,
    IOutputCacheStore outputCacheStore,
    ILogger<Program> logger)
{
    const string bearerPrefix = "Bearer ";
    var authHeader = context.Request.Headers.Authorization.ToString();
    if (!authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Unauthorized();
    }

    var accessToken = authHeader[bearerPrefix.Length..].Trim();
    if (string.IsNullOrWhiteSpace(accessToken))
    {
        return Results.Unauthorized();
    }

    AppUser? callingUser;
    try
    {
        callingUser = await authService.GetUserByAccessTokenAsync(accessToken);
    }
    catch (Exception ex)
    {
        // Never disclose ex.Message to the caller (Coding Standards, exception-text disclosure) -
        // log with context and answer as if the token simply did not resolve.
        logger.LogWarning(ex, "Cache refresh endpoint could not resolve the presented access token");
        return Results.Unauthorized();
    }

    if (callingUser?.EmailId == null)
    {
        return Results.Unauthorized();
    }

    if (!AppPolicies.IsSatisfiedBy(AppPolicies.AuthorOrAbove, callingUser.UserRole))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    // Same three tags Settings.razor's ClearContentCache evicts for the identical UAT-001
    // reason: the caller changed the database from outside this process and cannot know which
    // of content/taxonomy/settings the change landed under, so over-evicting is the honest
    // choice. The output cache is evicted separately - it is a distinct store from
    // ICacheService, and AddOutputCache's PublicListing/Feed policies are tagged CacheTags.Content.
    cacheService.EvictTag(CacheTags.Content);
    cacheService.EvictTag(CacheTags.Taxonomy);
    cacheService.EvictTag(CacheTags.Settings);
    await outputCacheStore.EvictByTagAsync(CacheTags.Content, context.RequestAborted);

    logger.LogInformation(
        "Cache refreshed via /api/admin/cache/refresh by {Email}", callingUser.EmailId);
    return Results.Ok(new { refreshed = true });
}

/// <summary>
/// Partitioning rules for the authentication rate limiter (REQ-NFR-005, BRD-82).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the throttled path list and the partition-key shape in one place
/// so the limiter, its tests and any future endpoint agree on what "an authentication endpoint"
/// means.</para>
/// <para><b>Code Flow:</b> the global limiter calls <see cref="BuildPartitionKey"/> for every
/// request; anything that is not a credential-handling path lands in
/// <see cref="UnlimitedPartitionKey"/> and is passed straight through.</para>
/// <para><b>Usage:</b> Configured in <c>Program.cs</c>; the defaults are overridable through the
/// <c>RateLimiting:AuthPermitLimit</c> and <c>RateLimiting:AuthWindowSeconds</c> settings.</para>
/// </remarks>
public static class AuthRateLimit
{
    /// <summary>
    /// Requests permitted per window per client IP before a 429 is returned.
    /// </summary>
    public const int DefaultPermitLimit = 10;

    /// <summary>
    /// Length of the fixed window in seconds.
    /// </summary>
    public const int DefaultWindowSeconds = 60;

    /// <summary>
    /// Partition key used by every request that is not rate limited.
    /// </summary>
    public const string UnlimitedPartitionKey = "unlimited";

    /// <summary>
    /// Request paths whose HTTP surface is rate limited.
    /// </summary>
    /// <remarks>
    /// <para><b>Every entry must be a route the application actually serves.</b> A path listed here
    /// that no longer exists is not merely inert — it is a claim that the route is live, and
    /// something eventually reads it as one. <c>"/register"</c> sat here after public registration
    /// was retired (~~BRD-1~~), and on 2026-08-22 the documentation guard
    /// (<c>DocumentationClaimTests</c>) used its presence as evidence that <c>/register</c> was a
    /// real route — which made the guard certify the very stale documentation it exists to catch
    /// (UAT-016).</para>
    ///
    /// <para><b>Two entries were removed on 2026-08-22</b> (UAT-017): <c>"/register"</c>, and
    /// <c>"/logout"</c> — which never existed as an HTTP endpoint at all. Signing out is a component
    /// action, not a request: <c>AdminLayout.LogoutAsync</c> calls
    /// <c>CustomAuthStateProvider.MarkUserAsLoggedOut</c> and then navigates to <c>/</c>. Nothing is
    /// ever posted to <c>/logout</c>, so rate-limiting it protected nothing.</para>
    ///
    /// <para><b>This list cannot rate-limit a Blazor component action.</b> That is the constraint to
    /// understand before adding to it: these paths are matched against the REQUEST path, so they
    /// cover form posts and full page loads only. An interaction that happens over the SignalR
    /// circuit — which is most of this application, sign-out included — never presents a matching
    /// path and is throttled by <c>ILoginThrottle</c> per account instead (REQ-NFR-005). Adding a
    /// component route here buys nothing and implies protection that is not there.</para>
    ///
    /// <para>When a route is retired, delete it from this list in the same change.</para>
    /// </remarks>
    public static readonly string[] Paths =
    {
        "/login",
        "/forgot-password",
        "/reset-password",
        "/change-password"
    };

    /// <summary>
    /// Classifies a request into a rate-limit partition.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Credential-handling paths are partitioned per client IP so
    /// one attacker cannot exhaust the budget for everyone; every other path - the Blazor
    /// circuit, static assets, health probes - gets the shared unlimited partition and is passed
    /// straight through.</para>
    /// <para><b>Flow:</b> read path → prefix match against <see cref="Paths"/> → build key.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="context">The request being classified.</param>
    /// <returns>The partition key for the request.</returns>
    public static string BuildPartitionKey(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isAuthPath = Paths.Any(candidate => path.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (!isAuthPath)
            return UnlimitedPartitionKey;

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"auth:{clientIp}";
    }
}

/// <summary>
/// Named output-cache policies applied to public, anonymous responses (REQ-NFR-018, BRD-78).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the policies so endpoints and invalidation code agree. Both are
/// tagged <c>CacheTags.Content</c>, so evicting that tag clears the output cache and the
/// in-memory cache in one step when a post is published, unpublished or deleted.</para>
/// <para><b>Usage:</b> <c>app.MapGet(...).CacheOutput(OutputCachePolicies.Feed)</c>.</para>
/// </remarks>
public static class OutputCachePolicies
{
    /// <summary>Five-minute policy for public listing responses.</summary>
    public const string PublicListing = "PublicListing";

    /// <summary>Fifteen-minute policy for RSS, the sitemap and other feeds.</summary>
    public const string Feed = "Feed";
}

/// <summary>
/// Where the "page not found" screen lives and which requests are allowed to receive it
/// (REQ-UI-012).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The re-execution path and the exclusion list are policy, not plumbing, so
/// they get a name and a test rather than being buried in a lambda in the pipeline.</para>
/// <para><b>Code Flow:</b> <c>UseWhen(!IsInfrastructurePath, UseStatusCodePagesWithReExecute(Path))</c>
/// - a 4xx on an ordinary page path is answered by re-rendering <see cref="Path"/> with the
/// original status preserved; a 4xx on an infrastructure path is left exactly as the framework
/// produced it.</para>
/// <para><b>Usage:</b> <see cref="Path"/> must stay in step with the <c>@page</c> directive on
/// <c>404Page.razor</c>; if that route is renamed, a 404 silently goes back to an empty body.</para>
/// </remarks>
public static class NotFoundPage
{
    /// <summary>
    /// Route of the component that renders the not-found screen.
    /// </summary>
    public const string Path = "/404";

    /// <summary>
    /// Request-path prefixes whose callers parse the response body and must never be handed HTML.
    /// </summary>
    /// <remarks>
    /// Health probes read JSON, the Blazor circuit and framework assets are consumed by the
    /// runtime, and a missing upload must stay a plain 404 so an image element fails as a broken
    /// image rather than "succeeding" with a web page.
    /// </remarks>
    public static readonly string[] InfrastructurePrefixes =
    {
        "/_blazor",
        "/_framework",
        "/_content",
        // Prefix, so it covers /health, /health/ready AND /healthz - the deployment probe's JSON
        // body is parsed by the pipeline and must never be replaced by the not-found page.
        "/health",
        // UAT-023: BlogApp calls /api/admin/cache/refresh and PARSES the outcome, so it must get the
        // status the handler actually returned. Without this prefix the re-execution replaced the
        // handler's 401 with the Blazor not-found page, which itself demands an antiforgery token and
        // answered 400 + HTML — so an expired desktop session reported "bad request" instead of
        // "sign in again", and the endpoint looked broken while being correct. Observed in the host
        // log: `responded 401` immediately followed by `POST /404 responded 400`.
        "/api",
        UploadsLocation.RequestPath
    };

    /// <summary>
    /// Decides whether a request is infrastructure and must be excluded from the not-found page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Matches on prefix, case-insensitively, because these paths are
    /// framework-owned and are compared the same way by the framework itself.</para>
    /// <para><b>Flow:</b> read path → prefix match against <see cref="InfrastructurePrefixes"/>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="path">The request path being classified.</param>
    /// <returns><c>true</c> when the request must keep the framework's own response.</returns>
    public static bool IsInfrastructurePath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return InfrastructurePrefixes.Any(
            prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Route the deployment pipeline and the uptime monitor probe (REQ-NFR-014).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The path is named rather than repeated as a literal because three things
/// depend on it agreeing: the endpoint mapped in the pipeline, the <c>/health</c> prefix in
/// <see cref="NotFoundPage.InfrastructurePrefixes"/> that keeps its body as JSON, and the workflow
/// that curls it after every deploy. A rename that misses any one of them fails a deploy for a
/// reason that looks nothing like a routing change.</para>
/// <para><b>Usage:</b> <c>https://{DOMAIN}/healthz</c>. It runs the readiness checks, so a green
/// response means PostgreSQL answered, not merely that the process is alive.</para>
/// </remarks>
public static class DeploymentHealthProbe
{
    /// <summary>Route the pipeline and uptime monitoring probe.</summary>
    public const string Path = "/healthz";
}

/// <summary>
/// Locates the DbUp migration scripts folder (REQ-NFR-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The path is resolved by a named helper rather than inline because two
/// components must agree about it and they run at different points in startup: <c>BlogDbSvc</c>
/// applies the scripts after <c>builder.Build()</c>, while <c>SchemaMigrationProbe</c> is
/// registered before it and asserts those same scripts were applied. If the probe were given a
/// different folder the gate would assert an expectation nobody ran — passing on a database that
/// was never migrated, or failing on one that was, both of which are worse than no gate.</para>
/// <para><b>Usage:</b> Called once in <c>Program.cs</c>; the resulting value is passed to the probe
/// registration and to <c>UpgradeDatabase</c>. Never call it twice.</para>
/// </remarks>
public static class MigrationScripts
{
    /// <summary>Folder name holding the numbered PostgreSQL migration scripts.</summary>
    private const string FolderName = "PostgresScripts";

    /// <summary>
    /// Resolves the folder DbUp reads its scripts from.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A development checkout runs from
    /// <c>source/TechieBlog/bin/{Configuration}/{Tfm}</c>, four levels below <c>source/</c>, so the
    /// scripts are reached by walking back up to the sibling <c>BlogDb</c> project. A published
    /// deployment has them copied next to the executable instead. The development path is tried
    /// first because both can exist at once in a checkout, and the project folder is the one that
    /// holds the newest scripts.</para>
    /// <para><b>Flow:</b> try the in-repo path → fall back to the published path → return the
    /// published path even when it is absent, so the caller reports a missing folder against a
    /// stable location rather than against a speculative one.</para>
    /// <para><b>Side Effects:</b> Reads the file system.</para>
    /// </remarks>
    /// <returns>The scripts folder path; may not exist, which callers must handle.</returns>
    public static string ResolvePath()
    {
        var inRepoPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "BlogDb", FolderName);

        return Directory.Exists(inRepoPath)
            ? inRepoPath
            : System.IO.Path.Combine(AppContext.BaseDirectory, FolderName);
    }
}

/// <summary>
/// Tags used to group health-check registrations (REQ-NFR-014, BRD-74).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>/health</c> filters every check out to answer liveness, while
/// <c>/health/ready</c> selects the checks carrying <see cref="Ready"/>.</para>
/// <para><b>Usage:</b> <c>AddCheck&lt;T&gt;("name", tags: new[] { HealthCheckTags.Ready })</c>.</para>
/// </remarks>
public static class HealthCheckTags
{
    /// <summary>Marks a check that must pass before the instance receives traffic.</summary>
    public const string Ready = "ready";
}
