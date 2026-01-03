// =============================================================================
// TechieBlog Application Entry Point
// Purpose: Configures and starts the Blazor Server application
// =============================================================================
using Blazored.LocalStorage;
using BlogDb;
using BlogEngine;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogUI;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.FluentUI.AspNetCore.Components;
using Serilog;
using Serilog.Events;
using TechieBlog.Services;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()  // Changed to Debug for development
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)  // Show more Microsoft logs
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.SignalR", LogEventLevel.Debug)  // Debug SignalR issues
    .MinimumLevel.Override("Microsoft.AspNetCore.Components", LogEventLevel.Debug)  // Debug Blazor issues
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentName()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/techieblog-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting TechieBlog application");

    var builder = WebApplication.CreateBuilder(args);

    // Use Serilog for all logging
    builder.Host.UseSerilog();

    // Enable static web assets from referenced RCL projects
    builder.WebHost.UseStaticWebAssets();

    // Register Microsoft Fluent UI Blazor components
    // Provides modern, accessible UI components following Microsoft's Fluent design system
    builder.Services.AddFluentUIComponents();

    string sDbConnectionString = builder.Configuration["AppDbConString"];

    // Initialize BlogEngine Services
    BlogSvcInitializer.Initialize(builder.Services, sDbConnectionString);

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents(options =>
        {
            options.DetailedErrors = true;
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

    builder.Services.AddAuthorization(options =>
    {
        // AdminOnly: Full system access - users, settings, all content
        options.AddPolicy(AppPolicies.AdminOnly, policy =>
            policy.RequireRole(AppRoles.Admin));

        // EditorOrAbove: Content management - manage all posts, comments
        options.AddPolicy(AppPolicies.EditorOrAbove, policy =>
            policy.RequireRole(AppRoles.Admin, AppRoles.Editor));

        // AuthorOrAbove: Content creation - create/edit posts
        options.AddPolicy(AppPolicies.AuthorOrAbove, policy =>
            policy.RequireRole(AppRoles.Admin, AppRoles.Editor, AppRoles.Author));

        // ContributorOrAbove: Submit content for review
        options.AddPolicy(AppPolicies.ContributorOrAbove, policy =>
            policy.RequireRole(AppRoles.Admin, AppRoles.Editor, AppRoles.Author, AppRoles.Contributor));

        // Authenticated: Any logged-in user
        options.AddPolicy(AppPolicies.Authenticated, policy =>
            policy.RequireAuthenticatedUser());
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
        var migrationSuccess = dbSvc.UpgradeDatabase(sDbConnectionString, scriptsPath);
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
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();

    // Serve static files from wwwroot (including uploaded images)
    app.UseStaticFiles();

    // Serilog request logging - shows HTTP requests in console
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    app.MapStaticAssets();

    // Authentication and Authorization middleware (order matters!)
    app.UseAuthentication();
    app.UseAuthorization();

    app.UseAntiforgery();

    app.MapRazorComponents<TechieBlog.Components.App>()
        .AddInteractiveServerRenderMode()
        .AddAdditionalAssemblies(typeof(BlogUI._Imports).Assembly);

    // Sitemap.xml endpoint for SEO
    app.MapGet("/sitemap.xml", (SitemapSvc sitemapSvc) =>
    {
        var xml = sitemapSvc.GenerateSitemap();
        return Results.Content(xml, "application/xml");
    });

    // Robots.txt endpoint for SEO
    app.MapGet("/robots.txt", (IConfiguration config) =>
    {
        var baseUrl = config["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? "https://localhost";
        var robotsTxt = $"""
            User-agent: *
            Allow: /

            Sitemap: {baseUrl}/sitemap.xml
            """;
        return Results.Content(robotsTxt, "text/plain");
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.Information("TechieBlog application shutting down");
    Log.CloseAndFlush();
}
