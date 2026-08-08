// =============================================================================
// TechieBlog Application Entry Point
// Purpose: Configures and starts the Blazor Server application
//
// Requirements wired here:
//   REQ-NFR-013 - Serilog console + daily rolling file, unhandled-exception handlers,
//                 CloseAndFlush on exit
//   REQ-NFR-015 - Correlation ID per request, echoed to the client and pushed into logs
//   REQ-NFR-014 - /health (liveness) and /health/ready (database + critical services)
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
using BlogModels;
using BlogModels.Interfaces;
using BlogUI;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Serilog;
using Serilog.Events;
using System.Text.Json;
using System.Threading.RateLimiting;
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
var bootstrapConfiguration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile(
        $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
        optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var environmentName = bootstrapConfiguration["ASPNETCORE_ENVIRONMENT"]
    ?? bootstrapConfiguration["DOTNET_ENVIRONMENT"]
    ?? "Production";
var isDevelopmentHost = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
var componentLogLevel = isDevelopmentHost ? LogEventLevel.Debug : LogEventLevel.Warning;

// REQ-NFR-029: the size cap and the shared-write flag are both settings, so the smoke test can
// drive the cap down far enough to observe an actual roll instead of asserting on the config.
var logFileSizeLimitBytes = bootstrapConfiguration.GetValue(
    "LogFile:SizeLimitBytes", 50L * 1024 * 1024);
var logFileRetainedCount = bootstrapConfiguration.GetValue("LogFile:RetainedFileCountLimit", 7);
var logFileShared = bootstrapConfiguration.GetValue("LogFile:Shared", true);

Log.Logger = new LoggerConfiguration()
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
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {CorrelationId}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/techieblog-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: logFileRetainedCount,
        // A daily roll alone does not bound a single file, which is how one day reached 341 MB.
        // Cap each file and roll within the day so disk use stays predictable (REQ-NFR-029).
        fileSizeLimitBytes: logFileSizeLimitBytes,
        rollOnFileSizeLimit: true,
        // Second instance of the host appends to the same file instead of silently starting its own
        // _NNN sequence. Serilog.Sinks.File 7.x routes a shared sink through SharedFileSink, which
        // takes the size limit too, so sharing and size-rolling are NOT mutually exclusive here -
        // the pair that IS refused is shared + buffered, and shared + lifecycle hooks, neither of
        // which this sink uses.
        shared: logFileShared,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

// Last-resort handlers so nothing dies silently outside the request pipeline (REQ-NFR-013).
// Registered before the host is built, because a failure during composition must still be logged.
// The bodies live in GlobalExceptionLogging so they are unit-testable and so the flush-on-crash
// rule (close the sink only when the runtime is actually terminating) has one definition.
GlobalExceptionLogging.Wire();

try
{
    Log.Information("Starting TechieBlog application");

    var builder = WebApplication.CreateBuilder(args);

    // -------------------------------------------------------------------------
    // Cryptographic secrets (REQ-NFR-027)
    // The JWT signing key and the AES key used to be literals in AppConstants, readable by anyone
    // with the repository. They now come from configuration and this call is what loads them, so it
    // runs before any service is registered - AuthSvc signs tokens with the first and AppEncrypt
    // protects the session envelope with the second. A missing or unusable value throws here and
    // the host does not start; there is deliberately no fallback default.
    // -------------------------------------------------------------------------
    AppSecrets.Initialise(builder.Configuration);

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

    var dbConnectionString = builder.Configuration["AppDbConString"]
    ?? throw new InvalidOperationException(
        "AppDbConString is not configured. Set it in appsettings.json or the environment before starting the host.");

    // Initialize BlogEngine Services
    BlogSvcInitializer.Initialize(builder.Services, dbConnectionString);

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
        options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromMinutes(1)));
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
    // Health checks (REQ-NFR-014, BRD-74)
    // /health answers while the process is alive; /health/ready additionally verifies the
    // database and the critical singletons before the instance accepts traffic.
    // -------------------------------------------------------------------------
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: new[] { HealthCheckTags.Ready })
        .AddCheck<CriticalServicesHealthCheck>("criticalservices", tags: new[] { HealthCheckTags.Ready });

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
            Log.Warning(
                "Rate limit rejected {RequestMethod} {RequestPath} from {RemoteIp}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress);
            return ValueTask.CompletedTask;
        };
    });

    // Authentication and Authorization Services
    builder.Services.AddBlazoredLocalStorage();

    // Add Authentication services (required for authorization middleware)
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = "BlazorServerAuth";
        options.DefaultChallengeScheme = "BlazorServerAuth";
    })
    .AddCookie("BlazorServerAuth", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/access-denied";
    });

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

    var app = builder.Build();

    // Run database migrations automatically at startup
    Log.Information("Running database migrations...");
    var scriptsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BlogDb", "PostgresScripts");
    if (!Directory.Exists(scriptsPath))
    {
        // Fallback for published deployment - scripts should be in a known location
        scriptsPath = Path.Combine(AppContext.BaseDirectory, "PostgresScripts");
    }

    if (Directory.Exists(scriptsPath))
    {
        var dbSvc = new BlogDbSvc();
        var migrationSuccess = dbSvc.UpgradeDatabase(dbConnectionString, scriptsPath);
        if (!migrationSuccess)
        {
            Log.Warning("Database migration completed with warnings - check console output");
        }
        else
        {
            Log.Information("Database migrations completed successfully");
        }
    }
    else
    {
        Log.Warning("PostgresScripts folder not found at {ScriptsPath} - skipping migrations", scriptsPath);
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

    // Serve static files from wwwroot (including uploaded images)
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

    // Liveness: the process is up and the pipeline responds (REQ-NFR-014)
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        Predicate = _ => false,
        ResponseWriter = WriteHealthResponse
    }).DisableRateLimiting();

    // Readiness: the database and the critical singletons are usable (REQ-NFR-014)
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
        ResponseWriter = WriteHealthResponse
    }).DisableRateLimiting();

    // Sitemap.xml endpoint for SEO - output cached as a feed (REQ-NFR-018)
    app.MapGet("/sitemap.xml", (SitemapSvc sitemapSvc) =>
    {
        var xml = sitemapSvc.GenerateSitemap();
        return Results.Content(xml, "application/xml");
    }).CacheOutput(OutputCachePolicies.Feed);

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
    public static readonly string[] Paths =
    {
        "/login",
        "/logout",
        "/register",
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
