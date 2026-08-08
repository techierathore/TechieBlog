using BlogEngine.Common;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using BlogModels.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine;

/// <summary>
/// The engine's composition root: registers every repository, service and background worker the
/// application needs, with the lifetime each one requires.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place where the whole object graph is declared, so the host
/// (<c>source/TechieBlog/Program.cs</c>) adds the engine with a single call and no part of
/// <c>BlogEngine</c> has to know how it is constructed. It doubles as the most complete map of the
/// application there is — reading it top to bottom names every capability the engine offers.</para>
///
/// <para><b>Code Flow:</b> the host resolves the <c>AppDbConString</c> configuration value and
/// calls <see cref="Initialize"/>; that method registers the graph in dependency order (repositories
/// before the services that consume them, settings before the storage factory that reads them) and
/// finishes by delegating the anonymous-engagement graph to
/// <see cref="EngagementSvcInitializer.AddEngagementServices"/>.</para>
///
/// <para><b>Dependencies:</b> <see cref="IServiceCollection"/>, and a PostgreSQL connection string.
/// Nothing here touches the database — registration is declaration only, so a bad connection string
/// fails at first use, not at start-up. DbUp migrations are run separately by the host.</para>
///
/// <para><b>Lifetimes, and why each one was chosen.</b> This is the part to get right: a lifetime
/// mistake here is not a style problem, it is a cross-request data-leak bug that no unit test will
/// catch.</para>
/// <list type="bullet">
///   <item><b>Transient — every repository and most services.</b> A repository holds nothing but a
///     connection string and opens/closes its own connection per call, so there is no state to
///     share and no benefit to caching the instance. Transient is also the safe default in Blazor
///     Server, where a "scope" is a whole circuit that may live for hours: a scoped repository
///     would quietly outlive any request-shaped assumption made about it.</item>
///   <item><b>Singleton — <see cref="ILoginThrottle"/>, <see cref="ICacheService"/>,
///     <see cref="MarkdownRenderer"/>, <see cref="ISiteSettingsService"/> and
///     <see cref="IFileStorageFactory"/>.</b> Each holds process-wide state that is <i>meant</i> to
///     be shared: failed-login counters that must survive across circuits or the throttle counts
///     nothing (REQ-NFR-005), the shared cache, an immutable and expensive-to-build Markdig
///     pipeline, the settings snapshot every request reads, and a stateless factory. None of them
///     stores per-user data, which is the test a singleton has to pass.</item>
///   <item><b>Scoped — <see cref="IBlogImageService"/>.</b> It coordinates an upload across the
///     storage provider and the image repository within one user interaction; a circuit is the
///     right boundary for that.</item>
///   <item><b>Hosted — <c>ScheduledPostPublisher</c>.</b> A background loop owned by the host's
///     lifetime, not by any request.</item>
/// </list>
///
/// <para><b>Known captive dependency (deliberate):</b> the singleton <c>SiteSettingsService</c>
/// captures a transient <see cref="ISiteSettingRepo"/>, which normally pins a short-lived object
/// for the life of the process. It is safe here precisely because the repository is stateless — it
/// holds a connection string and nothing else — and it is what lets the settings service keep its
/// in-process snapshot. Do not copy the pattern for a repository that ever gains per-request state.</para>
///
/// <para><b>Not registered here:</b> <c>SvcTokenRepo</c>. It was deleted from the codebase under
/// REQ-FN-052 and had never been registered in this container — nothing resolved it, which is how
/// it could be removed without a single call site changing.</para>
///
/// <para><b>Usage:</b> Call once from the host's service configuration, before
/// <c>builder.Build()</c>. Calling it twice would double every registration; the container tolerates
/// that (last registration wins for a single resolve) but it is never intended.</para>
/// </remarks>
public static class BlogSvcInitializer
{
    /// <summary>
    /// Registers the engine's complete object graph into the host's service collection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Repositories are registered with a factory lambda rather than
    /// by type because each needs the connection string as a constructor argument and the string is
    /// a method parameter, not a container-resolvable service. Two registrations choose an
    /// implementation at resolve time rather than at start-up —
    /// <see cref="Services.IEmailService"/> picks SMTP or console depending on whether
    /// <c>EmailSettings:SmtpHost</c> is set — so a configuration reload takes effect without a
    /// restart and a clone-and-run checkout still works with no mail server.</para>
    /// <para><b>Flow:</b> authentication and audit stores → cross-cutting singletons (throttle,
    /// cache, health probe, email) → content repositories and services → profile and media →
    /// newsletter, analytics and dashboard → site settings → storage providers → anonymous
    /// engagement. Ordering matters in exactly one place: <see cref="ISiteSettingsService"/> must
    /// be registered before <see cref="IFileStorageFactory"/>, which reads its provider selection
    /// from it.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="services"/>. No I/O, no database
    /// connection and no configuration read happens here.</para>
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    /// <param name="dbConnectionString">PostgreSQL connection string from the <c>AppDbConString</c>
    /// configuration key. Captured by the repository factory lambdas, so it must remain valid for
    /// the life of the container.</param>
    public static void Initialize(IServiceCollection services, string dbConnectionString)
    {
        services.AddTransient<IUserLoginRepository>(x => new UserLoginRepo(dbConnectionString));
        services.AddTransient<IBlogUserRepo>(x => new BlogUserRepo(dbConnectionString));

        // Credential repository - reads and rotates PBKDF2 password hashes (REQ-NFR-002)
        services.AddTransient<IUserCredentialRepo>(x => new UserCredentialRepo(dbConnectionString));

        // Password reset token repository - database backed so links survive restarts (REQ-NFR-019)
        services.AddTransient<IPasswordResetTokenRepo>(x => new PasswordResetTokenRepo(dbConnectionString));

        // Sign-in audit trail - one row per attempt, successful or refused (REQ-FN-051)
        services.AddTransient<ILoginLogRepo>(x => new LoginLogRepo(dbConnectionString));

        // Failed-login throttle - singleton so counters are shared across circuits (REQ-NFR-005)
        services.AddSingleton<ILoginThrottle, LoginThrottle>();

        // Tag-aware in-memory cache for settings, taxonomy and listings (REQ-NFR-018).
        // Singleton: the whole point is one cache shared by every circuit. It stores only public
        // content and settings - never per-user data, which is what makes a singleton safe here.
        services.AddSingleton<ICacheService, Services.MemoryCacheService>();

        // Database readiness probe backing /health/ready (REQ-NFR-014)
        services.AddTransient(x => new Services.DatabaseHealthProbe(
            dbConnectionString,
            x.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.DatabaseHealthProbe>>()));

        // Email service (REQ-FN-033): real SMTP whenever EmailSettings:SmtpHost is configured,
        // otherwise the console transport keeps a clone-and-run checkout working with no mail
        // server. The choice is made per resolution rather than at startup so a configuration
        // reload takes effect without a restart.
        services.AddTransient<Services.IEmailService>(x =>
        {
            var configuration = x.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var isSmtpConfigured = !string.IsNullOrWhiteSpace(configuration[Services.SmtpEmailService.SmtpHostKey]);
            return isSmtpConfigured
                ? ActivatorUtilities.CreateInstance<Services.SmtpEmailService>(x)
                : ActivatorUtilities.CreateInstance<Services.ConsoleEmailService>(x);
        });

        // Auth service
        services.AddTransient<Services.AuthSvc>();

        // Blog post services
        services.AddTransient<IBlogPostRepo>(x => new BlogPostRepo(dbConnectionString));
        services.AddTransient<Services.BlogSvc>();

        // Category services
        services.AddTransient<ICategoryRepo>(x => new CategoryRepo(dbConnectionString));
        services.AddTransient<Services.CategorySvc>();

        // Tag services
        services.AddTransient<IBlogTagRepo>(x => new BlogTagRepo(dbConnectionString));
        services.AddTransient<Services.TagSvc>();

        // Series services
        services.AddTransient<IBlogSeriesRepo>(x => new BlogSeriesRepo(dbConnectionString));
        services.AddTransient<Services.SeriesSvc>();

        // Subscriber services
        services.AddTransient<ISubscriberRepo>(x => new SubscriberRepo(dbConnectionString));
        services.AddTransient<Services.SubscriberSvc>();

        // Comment services
        services.AddTransient<IBlogCommentRepo>(x => new BlogCommentRepo(dbConnectionString));
        services.AddTransient<Services.CommentSvc>();

        // Rating services (FIX-013: Star Ratings)
        services.AddTransient<IPostRatingRepo>(x => new PostRatingRepo(dbConnectionString));
        services.AddTransient<Services.RatingSvc>();

        // User skills repository (Wave 1: Resume features)
        services.AddTransient<IUserSkillsRepo>(x => new UserSkillsRepo(dbConnectionString));

        // User awards repository (Resume features)
        services.AddTransient<IUserAwardsRepo>(x => new UserAwardsRepo(dbConnectionString));

        // User events repository (Experience timeline)
        services.AddTransient<IUserEventRepo>(x => new UserEventRepo(dbConnectionString));

        // User stats repository and service (Profile statistics)
        services.AddTransient<IUserStatsRepo>(x => new UserStatsRepo(dbConnectionString));
        services.AddTransient<Services.UserStatsSvc>();

        // Image repository and service. The service is SCOPED - it coordinates one upload across
        // the storage provider and the image repository within a single user interaction, so a
        // circuit is the right boundary; the repository behind it stays transient and stateless.
        services.AddTransient<IBlogImageRepo>(x => new BlogImageRepo(dbConnectionString));
        services.AddScoped<IBlogImageService, BlogImageService>();

        // Sitemap service
        services.AddTransient<Services.SitemapSvc>();

        // Markdown rendering (REQ-NFR-006). Singleton because the sanitising Markdig pipeline is
        // immutable once built and expensive to rebuild; the renderer holds no per-caller state.
        services.AddSingleton<MarkdownRenderer>();

        // Scheduled post publisher background service
        services.AddHostedService<Services.ScheduledPostPublisher>();

        // Newsletter compose/send/history/unsubscribe + public archive (REQ-FN-032, REQ-FN-050)
        services.AddTransient<INewsletterRepo>(x => new NewsletterRepo(dbConnectionString));
        services.AddTransient<INewsletterService, Services.NewsletterSvc>();

        // Post view tracking (REQ-FN-034) - writes the PostViews table
        services.AddTransient<IPostViewRepo>(x => new PostViewRepo(dbConnectionString));
        services.AddTransient<IPostViewTracker, Services.PostViewTracker>();

        // Popular posts and per-post engagement statistics (REQ-FN-035)
        services.AddTransient<IAnalyticsRepo>(x => new AnalyticsRepo(dbConnectionString));
        services.AddTransient<IAnalyticsService, Services.AnalyticsSvc>();

        // Admin dashboard aggregate counts (REQ-FN-036)
        services.AddTransient<IAdminCountsRepo>(x => new AdminCountsRepo(dbConnectionString));
        services.AddTransient<IDashboardService, Services.DashboardSvc>();

        // Site settings persistence (REQ-FN-040) - the settings service is cached in-process and
        // is the source the storage factory reads its provider selection from, so it registers
        // ahead of the storage graph below.
        services.AddTransient<ISiteSettingRepo>(x => new SiteSettingRepo(dbConnectionString));
        services.AddSingleton<ISiteSettingsService, Services.SiteSettingsService>();

        // Configurable storage provider (REQ-FN-042). The factory builds a provider per operation
        // from current settings; the cloud provider resolves its client from IHttpClientFactory,
        // so the HTTP client factory must be present in the container.
        services.AddHttpClient();
        services.AddSingleton<IFileStorageFactory, Storage.FileStorageFactory>();

        // Anonymous engagement: captcha, spam screening, persisted email verification
        // (REQ-FN-022 / REQ-FN-023 / REQ-FN-048 / REQ-FN-049).
        EngagementSvcInitializer.AddEngagementServices(services, dbConnectionString);
    }
}
