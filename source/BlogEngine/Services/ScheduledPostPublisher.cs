using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Background service that automatically publishes scheduled posts when their scheduled time arrives.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Monitors scheduled posts and publishes them when due.</para>
/// <para><b>Interval:</b> Checks every minute for posts ready to publish.</para>
/// </remarks>
public class ScheduledPostPublisher : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ScheduledPostPublisher> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public ScheduledPostPublisher(
        IServiceProvider services,
        ILogger<ScheduledPostPublisher> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled Post Publisher started");

        // Wait a bit before first check to let app fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishDuePosts();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled post publishing cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Scheduled Post Publisher stopped");
    }

    private async Task PublishDuePosts()
    {
        try
        {
            using var scope = _services.CreateScope();
            var blogSvc = scope.ServiceProvider.GetRequiredService<BlogSvc>();

            var duePosts = blogSvc.GetDueScheduledPosts().ToList();

            if (duePosts.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Found {Count} scheduled post(s) due for publishing", duePosts.Count);

            foreach (var post in duePosts)
            {
                try
                {
                    // Set publish time to the scheduled time (not current time)
                    post.Published = true;
                    post.PublishedOn = post.ScheduledPublishOn ?? DateTime.UtcNow;
                    post.ScheduledPublishOn = null;
                    post.UpdatedOn = DateTime.UtcNow;

                    var result = blogSvc.UpdatePost(post);

                    if (result.IsSuccess)
                    {
                        _logger.LogInformation(
                            "Published scheduled post: \"{Title}\" (ID: {PostId})",
                            post.Title, post.PostID);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Failed to publish scheduled post: \"{Title}\" (ID: {PostId}) - {Error}",
                            post.Title, post.PostID, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error publishing scheduled post: \"{Title}\" (ID: {PostId})",
                        post.Title, post.PostID);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving scheduled posts");
        }
    }
}
