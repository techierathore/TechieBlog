using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Records post views for analytics, de-duplicated per visit and free of raw IP storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-FN-034 by giving the <c>PostViews</c> table — present since
/// migration 001 and never written to — its first writer.</para>
///
/// <para><b>Definition of a view (the choice this service makes explicit):</b></para>
/// <list type="bullet">
///   <item>A <b>view</b> is one row in <c>PostViews</c>. At most one row is written per visitor per
///         post per de-duplication window (<c>Analytics:ViewDedupeWindowHours</c>, 24 by default),
///         so re-reading a post inside one visit is counted once and a refresh loop cannot inflate
///         the number. <b>Total views</b> is the row count.</item>
///   <item>A <b>unique view</b> is one distinct visitor hash for the post, over all time.</item>
///   <item>A <b>visitor</b> is <c>SHA-256(siteSalt | ipAddress | userAgent)</c>. Only that digest is
///         stored; the legacy <c>ViewerIp</c> column is written as NULL. The salt makes the digest
///         non-reversible, so the site keeps a pseudonymous readership signal rather than a log of
///         who read what.</item>
/// </list>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A post page calls <see cref="TrackViewAsync"/> on first render.</item>
///   <item>The visitor hash is derived from the salt and the request metadata.</item>
///   <item>The repository performs a conditional insert, so the de-duplication test and the write
///         are one atomic statement.</item>
/// </list>
///
/// <para><b>Privacy — what the site can and cannot learn.</b> The raw IP address is never
/// persisted; only the salted digest is, and the legacy <c>ViewerIp</c> column is explicitly
/// written as NULL. Because the salt is a deployment secret, the digest cannot be reversed by
/// anyone holding only the database, and the same visitor produces a <i>different</i> digest on a
/// different deployment. What the site retains is therefore a pseudonymous "same reader as before"
/// signal rather than a record of who read what. Two consequences follow. First, the digest is
/// still a stable per-visitor identifier within one deployment, so it is personal data and its
/// retention should be bounded like any other. Second, <b>rotating <c>Analytics:VisitorSalt</c>
/// makes every stored digest stop matching</b> — no error, but unique-view counts jump as returning
/// readers are counted as new, and de-duplication restarts. Treat the salt as write-once. The
/// hashing itself lives in <c>VisitorHasher</c>; see it for the exact construction.</para>
///
/// <para><b>Hot path.</b> This runs on the first render of every post view, so it is deliberately
/// cheap: one SHA-256 and one conditional insert, no read-then-write, no caching, no lock. The
/// de-duplication test and the write are a <b>single atomic statement in the repository</b>, which
/// is what makes concurrent renders of the same post by the same visitor safe — two simultaneous
/// calls cannot both decide "not seen yet" and both insert. The return value reports whether a row
/// was actually written, so a caller can distinguish a counted view from a de-duplicated
/// one.</para>
///
/// <para><b>Idempotency:</b> repeated calls inside the de-duplication window are no-ops by
/// construction, so a page that renders twice, a reconnecting Blazor circuit, or a reader
/// refreshing in a loop all count once. Outside the window the same visitor counts again — that is
/// the definition of a <i>total</i> view, and it is why unique views are counted by distinct hash
/// over all time instead.</para>
///
/// <para><b>Result contract:</b> nothing throws. An invalid post id is returned as a failure, and
/// an unexpected failure is logged with the post id and converted into a failed <c>Result</c> — a
/// missing analytics row must never cost a reader the article. The corollary is that a caller
/// ignoring the <c>Result</c> will never learn that view tracking has stopped working; the log is
/// the only signal.</para>
///
/// <para><b>Dependencies:</b> <c>IPostViewRepo</c>, <c>IConfiguration</c> and <c>ILogger</c>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IPostViewTracker</c>.
/// Requires no authorization — a view by an anonymous reader is exactly what it exists to record.
/// Analytics must never break a page, so this method logs and returns a failed <c>Result</c> rather
/// than throwing.</para>
/// </remarks>
public class PostViewTracker : IPostViewTracker
{
    /// <summary>
    /// Fallback salt used when <c>Analytics:VisitorSalt</c> is not configured. A deployment should
    /// always set its own; the constructor logs a warning when this is used.
    /// </summary>
    private const string DefaultVisitorSalt = "TechieBlogDefaultVisitorSalt";

    private const int DefaultDedupeWindowHours = 24;

    private readonly IPostViewRepo postViewRepo;
    private readonly ILogger<PostViewTracker> logger;
    private readonly string visitorSalt;
    private readonly int dedupeWindowHours;

    /// <summary>
    /// Initializes the view tracker from configuration.
    /// </summary>
    /// <remarks>
    /// Reads <c>Analytics:VisitorSalt</c> and <c>Analytics:ViewDedupeWindowHours</c>. The salt is a
    /// deployment secret and belongs in user secrets or environment configuration, not in a shipped
    /// settings file.
    /// </remarks>
    /// <param name="postViewRepo">Post-view data access.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Logger for tracking failures.</param>
    public PostViewTracker(
        IPostViewRepo postViewRepo,
        IConfiguration configuration,
        ILogger<PostViewTracker> logger)
    {
        this.postViewRepo = postViewRepo;
        this.logger = logger;

        var configuredSalt = configuration?["Analytics:VisitorSalt"];
        visitorSalt = string.IsNullOrWhiteSpace(configuredSalt) ? DefaultVisitorSalt : configuredSalt;
        if (string.IsNullOrWhiteSpace(configuredSalt))
            logger.LogWarning("Analytics:VisitorSalt is not configured — using the built-in default salt.");

        dedupeWindowHours = ResolveWindow(configuration);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Records at most one view per visitor per post per
    /// de-duplication window. The visitor is identified by the salted digest of the IP address and
    /// user agent, never by the address itself.</para>
    /// <para><b>Flow:</b> reject a non-positive post id → derive the visitor hash → conditional
    /// insert, which performs the window test and the write as one statement.</para>
    /// <para><b>Side Effects:</b> Inserts at most one <c>PostViews</c> row. Writes no raw IP
    /// address. Logs an error on failure and nothing on the happy path — this runs on every post
    /// render, so a success line would flood the log.</para>
    /// <para><b>Safe to call more than once</b> for the same reader and post; the second call
    /// returns success with <c>false</c> rather than double-counting.</para>
    /// </remarks>
    /// <returns>
    /// Success carrying <c>true</c> when a row was written and <c>false</c> when the view was
    /// de-duplicated; a failure when the post id is invalid or the write could not be attempted.
    /// </returns>
    public async Task<Result<bool>> TrackViewAsync(long postId, string ipAddress, string userAgent)
    {
        if (postId <= 0)
            return Result<bool>.Failure("A post id is required to track a view.");

        try
        {
            var visitorHash = VisitorHasher.ComputeHash(visitorSalt, ipAddress, userAgent);
            var isRecorded = await postViewRepo
                .RecordViewAsync(postId, visitorHash, DateTime.UtcNow, dedupeWindowHours)
                .ConfigureAwait(false);
            return Result<bool>.Success(isRecorded);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record a view for post {PostId}", postId);
            return Result<bool>.Failure("The view could not be recorded.");
        }
    }

    /// <summary>
    /// Resolves the de-duplication window from configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A window of zero or less would count every refresh, so an
    /// invalid value falls back to the 24-hour default.</para>
    /// <para><b>Flow:</b> parse the setting → validate → return the value or the default.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The de-duplication window in hours.</returns>
    private static int ResolveWindow(IConfiguration? configuration)
    {
        var raw = configuration?["Analytics:ViewDedupeWindowHours"];
        if (!int.TryParse(raw, out var hours) || hours <= 0)
            return DefaultDedupeWindowHours;

        return hours;
    }
}
