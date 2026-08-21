using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Hosted background service that publishes posts once their scheduled time has passed.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A post can be authored ahead of time with a <c>ScheduledPublishOn</c>
/// stamp. Nothing in a request pipeline will ever fire at that moment — a blog can sit with no
/// traffic all night — so publication needs a clock of its own. This is that clock.</para>
///
/// <para><b>Schedule:</b> one tick per minute, after a ten-second delay at startup. The delay lets
/// the host finish wiring up (and lets DbUp finish its migrations) before the first database read;
/// without it the very first tick can race application start. A minute of granularity means a post
/// goes live up to 59 seconds after its scheduled time, never before it — late is a rounding
/// artefact, early would be an embargo breach.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="ExecuteAsync"/> loops until the host's stopping token is cancelled.</item>
///   <item>Each tick opens its <b>own DI scope</b> and resolves <c>BlogSvc</c> from it. A hosted
///     service is a singleton and must never capture a scoped service in a field; taking a fresh
///     scope per tick is what keeps the scoped database connection from being shared across
///     ticks.</item>
///   <item>Every post that is due is flipped to published and saved individually.</item>
/// </list>
///
/// <para><b>Overlapping runs — why they cannot happen in one process.</b> The loop is strictly
/// sequential: the next <c>Task.Delay</c> is only reached after <c>PublishDuePosts</c> has
/// completed, so a tick that takes longer than a minute delays the following tick rather than
/// running beside it. There is <b>no cross-process lock</b>, however: run two instances of the host
/// and both will tick. That is survivable rather than correct — the second instance re-reads the
/// due list, finds the post already published (and its <c>ScheduledPublishOn</c> cleared) and has
/// nothing to do — but the window between the two reads is real, so a scaled-out deployment can
/// briefly double-write the same row. Add a leader election or an advisory lock before scaling
/// out.</para>
///
/// <para><b>Idempotency:</b> publication clears <c>ScheduledPublishOn</c> and sets
/// <c>Published</c>, so the post drops out of the due query permanently. Re-running a tick over an
/// already-published post is therefore a no-op, which is what makes a crashed or duplicated tick
/// safe to repeat.</para>
///
/// <para><b>Failure contract — a failed tick is never fatal.</b> Three nested catches, deliberately:
/// a single post's failure is logged and the loop moves to the next post; a failure reading the due
/// list is logged and that whole tick is abandoned; and the loop body's own catch guarantees the
/// timer survives anything the inner layers missed. An unhandled exception escaping
/// <c>ExecuteAsync</c> would stop the background service for the lifetime of the process — the blog
/// would keep serving pages while silently never publishing again — so the outer catch is the
/// difference between a bad minute and a dead scheduler. The cost is that a persistently failing
/// post is retried every minute forever; watch the warning log rather than assuming silence means
/// success.</para>
///
/// <para><b>Dependencies:</b> <see cref="IServiceProvider"/> for per-tick scopes, <c>BlogSvc</c>
/// resolved from each scope, and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered by <c>BlogSvcInitializer</c> with <c>AddHostedService</c>, so the
/// host starts and stops it. It runs with no user context and no authorization policy — it is the
/// system acting on an author's earlier, already-authorized decision to schedule the post, so the
/// permission check belongs on the screen that set <c>ScheduledPublishOn</c>, not here.</para>
/// </remarks>
public class ScheduledPostPublisher : BackgroundService
{
    private readonly IServiceProvider services;
    private readonly ILogger<ScheduledPostPublisher> logger;
    private readonly TimeSpan checkInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ScheduledPostPublisher"/> class.
    /// </summary>
    /// <remarks>
    /// The root provider is injected rather than <c>BlogSvc</c> itself: this class is a singleton
    /// and <c>BlogSvc</c> is not, so the dependency has to be resolved per tick from a scope.
    /// </remarks>
    /// <param name="services">Root service provider used to create a scope per tick.</param>
    /// <param name="logger">Logger for lifecycle, publication and failure events.</param>
    public ScheduledPostPublisher(
        IServiceProvider services,
        ILogger<ScheduledPostPublisher> logger)
    {
        this.services = services;
        this.logger = logger;
    }

    /// <summary>
    /// Runs the publication loop until the host shuts down.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ticks once a minute after a ten-second settling delay. Each
    /// tick is independent — a failure never breaks the loop, because a stopped scheduler is a
    /// silent outage.</para>
    /// <para><b>Flow:</b> log start → settle → while not cancelled: publish due posts, catch and
    /// log anything thrown, wait one interval → log stop.</para>
    /// <para><b>Side Effects:</b> Publishes posts (database writes) and writes log entries. Holds
    /// no state between ticks.</para>
    /// <para><b>Cancellation:</b> shutdown cancels the token, which makes the pending
    /// <c>Task.Delay</c> throw <see cref="OperationCanceledException"/>. That is the host's normal
    /// stop path and is handled by the framework, so the final "stopped" line is only reached when
    /// the loop exits on the condition rather than the token.</para>
    /// </remarks>
    /// <param name="stoppingToken">Signalled when the host is shutting down.</param>
    /// <returns>A task that completes when the service stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Scheduled Post Publisher started");

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
                logger.LogError(ex, "Error in scheduled post publishing cycle");
            }

            await Task.Delay(checkInterval, stoppingToken);
        }

        logger.LogInformation("Scheduled Post Publisher stopped");
    }

    /// <summary>
    /// Publishes every post whose scheduled time has arrived.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>PublishedOn</c> is stamped with the post's <i>scheduled</i>
    /// time, not with "now". That matters beyond tidiness: <c>PublishedOn</c> drives the ordering of
    /// listings, the RSS feed and the sitemap's <c>lastmod</c>, so using the tick time would let a
    /// minute of scheduler lag reorder a carefully sequenced set of posts.
    /// <c>ScheduledPublishOn</c> is then cleared, which is what removes the post from the due query
    /// and makes the operation idempotent.</para>
    /// <para><b>Flow:</b> open a scope → read the due list → return early when empty (the common
    /// case, and it logs nothing so the log stays readable) → per post: set the publication fields,
    /// save, log the outcome.</para>
    /// <para><b>Side Effects:</b> Updates one post row per published post; emits an information
    /// line per success and a warning per rejected save. Each post is saved in its own call, so
    /// there is no transaction spanning the batch — a failure part-way through leaves the earlier
    /// posts published, which is the desired behaviour here.</para>
    /// <para><b>Error handling:</b> a per-post exception is caught so one malformed post cannot
    /// block the rest of the batch, and the outer catch covers a failure to read the list at all.
    /// Nothing propagates to the caller.</para>
    /// </remarks>
    /// <returns>A task that completes when the batch has been attempted.</returns>
    private async Task PublishDuePosts()
    {
        try
        {
            using var scope = services.CreateScope();
            var blogSvc = scope.ServiceProvider.GetRequiredService<BlogSvc>();

            var duePosts = blogSvc.GetDueScheduledPosts().ToList();

            if (duePosts.Count == 0)
            {
                return;
            }

            logger.LogInformation("Found {Count} scheduled post(s) due for publishing", duePosts.Count);

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
                        logger.LogInformation(
                            "Published scheduled post: \"{Title}\" (ID: {PostId})",
                            post.Title, post.PostID);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Failed to publish scheduled post: \"{Title}\" (ID: {PostId}) - {Error}",
                            post.Title, post.PostID, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Error publishing scheduled post: \"{Title}\" (ID: {PostId})",
                        post.Title, post.PostID);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving scheduled posts");
        }
    }
}
