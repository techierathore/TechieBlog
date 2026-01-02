using BlogEngine.Common;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using BlogModels.Interfaces;
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

        // Subscriber services
        services.AddTransient<ISubscriberRepo>(x => new SubscriberRepo(dbConnectionString));
        services.AddTransient<Services.SubscriberSvc>();

        // Comment services
        services.AddTransient<IBlogCommentRepo>(x => new BlogCommentRepo(dbConnectionString));
        services.AddTransient<Services.CommentSvc>();

        // Rating services (FIX-013: Star Ratings)
        services.AddTransient<IPostRatingRepo>(x => new PostRatingRepo(dbConnectionString));
        services.AddTransient<Services.RatingSvc>();

        // Favorite services (FIX-014: Favorites/Bookmarks)
        services.AddTransient<IUserFavoriteRepo>(x => new UserFavoriteRepo(dbConnectionString));
        services.AddTransient<Services.FavoriteSvc>();

        // User skills repository (Wave 1: Resume features)
        services.AddTransient<IUserSkillsRepo>(x => new UserSkillsRepo(dbConnectionString));

        // User awards repository (Resume features)
        services.AddTransient<IUserAwardsRepo>(x => new UserAwardsRepo(dbConnectionString));

        // User events repository (Experience timeline)
        services.AddTransient<IUserEventRepo>(x => new UserEventRepo(dbConnectionString));

        // User stats repository (Profile statistics)
        services.AddTransient<IUserStatsRepo>(x => new UserStatsRepo(dbConnectionString));

        // Image repository and service
        services.AddTransient<IBlogImageRepo>(x => new BlogImageRepo(dbConnectionString));
        services.AddScoped<IBlogImageService, BlogImageService>();

        // Sitemap service
        services.AddTransient<Services.SitemapSvc>();

        // Markdown rendering (singleton for performance)
        services.AddSingleton<MarkdownRenderer>();

        // Scheduled post publisher background service
        services.AddHostedService<Services.ScheduledPostPublisher>();
    }
}
