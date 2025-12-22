using BlogEngine.Common;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine;
/// <summary>
/// Main Initializer of the nuget package.
/// </summary>
public static class BlogSvcInitializer
{
    public static void Initialize(IServiceCollection services, string dbConnectionString)
    {
        services.AddTransient<IUserLoginRepository>(x => new UserLoginRepo(dbConnectionString));
        services.AddTransient<IBlogUserRepo>(x => new BlogUserRepo(dbConnectionString));

        // Password reset token repository (in-memory for MVP)
        services.AddSingleton<IPasswordResetTokenRepo, PasswordResetTokenRepo>();

        // Email service (console logging for dev)
        services.AddTransient<Services.IEmailService, Services.ConsoleEmailService>();

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

        // Markdown rendering (singleton for performance)
        services.AddSingleton<MarkdownRenderer>();

        // Scheduled post publisher background service
        services.AddHostedService<Services.ScheduledPostPublisher>();
    }
}
