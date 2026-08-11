using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Http;
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
///   <item>A post page calls <see cref="TrackCurrentVisitAsync"/> while its markup is being produced
///         for the HTTP response.</item>
///   <item>The address and user-agent are read from the ambient request and folded into the visitor
///         hash together with the salt.</item>
///   <item>The repository performs a conditional insert, so the de-duplication test and the write
///         are one atomic statement.</item>
/// </list>
///
/// <para><b>Where the visitor comes from, and the one case it does not cover.</b> The address and
/// user-agent only exist while an HTTP request is being served. In a Blazor Server application that
/// is the static/prerender pass of a page — the interactive pass runs over a SignalR circuit with no
/// <c>HttpContext</c> at all, which is precisely why calling this on every render cannot double-count:
/// the second pass has no visitor to attribute a view to and returns without writing. The de-duplication
/// window is the second, independent guard behind that. The case genuinely not covered is a reader who
/// reaches a post by client-side navigation inside an already-established circuit; counting those needs
/// the connection metadata captured at circuit start, which is a host-level concern
/// (<c>Program.cs</c>), not this service's.</para>
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
/// calls cannot both decide "not seen yet" and both insert.</para>
///
/// <para><b>[REQ-NFR-034] The write no longer happens on the render path.</b> "Cheap" was still a
/// database round trip that an article render waited for before it could return markup — a write
/// blocking a read. <see cref="TrackCurrentVisitAsync"/> now snapshots the visitor out of the live
/// request and hands it to <see cref="IPostViewQueue"/>, which accepts it without waiting;
/// <c>PostViewWriter</c> calls <see cref="TrackViewAsync"/> from its own background loop and its own
/// DI scope to do the insert. Two consequences a caller has to know about:</para>
/// <list type="bullet">
///   <item><b>The return value of <see cref="TrackCurrentVisitAsync"/> changed meaning.</b> It used
///     to report whether a row had been written; it now reports whether the view was ACCEPTED FOR
///     RECORDING. Nobody can answer the old question synchronously any more, because the answer is
///     not known until the queue is drained. <see cref="TrackViewAsync"/> is unchanged and still
///     reports the real outcome — the background writer is its caller.</item>
///   <item><b>The count the page reads is the count before this visit.</b> The render enqueues and
///     immediately reads <c>PostViewCount</c>, so a brand-new visitor sees the figure as it stood
///     when they arrived. This is the ordinary behaviour of every "N views" byline on the web and it
///     is the price of not making a reader wait on an analytics write.</item>
/// </list>
/// <para>The snapshot has to be taken here rather than in the writer: the address and user-agent
/// live on <c>HttpContext</c>, which is disposed the moment the response completes.</para>
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
/// <para><b>Dependencies:</b> <c>IPostViewRepo</c>, <c>IConfiguration</c>, <c>ILogger</c> and —
/// since REQ-NFR-034 — the singleton <see cref="IPostViewQueue"/>. The queue is optional at
/// construction so a unit test can exercise <see cref="TrackViewAsync"/> without one; when it is
/// absent <see cref="TrackCurrentVisitAsync"/> falls back to writing inline, which is the old
/// behaviour and is only ever reached in tests.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IPostViewTracker</c>.
/// Requires no authorization — a view by an anonymous reader is exactly what it exists to record.
/// Analytics must never break a page, so this method logs and returns a failed <c>Result</c> rather
/// than throwing.</para>
/// </remarks>
public class PostViewTracker : IPostViewTracker
{
    /// <summary>
    /// Fallback salt used when <c>Analytics:VisitorSalt</c> is not configured.
    /// </summary>
    /// <remarks>
    /// <para><b>Development only, and now enforced as such (REQ-NFR-030).</b> This value is in the
    /// repository, and a salt an attacker knows is no salt at all: an IP hash with a known salt is
    /// reversible by brute force across the whole IPv4 address space, which turns the pseudonymous
    /// digest described in the class remarks into a plain record of who read what. Since
    /// REQ-NFR-030, <c>TechieBlog.Configuration.DeploymentConfiguration</c> REFUSES TO START a host
    /// whose environment is anything other than <c>Development</c> while
    /// <c>Analytics:VisitorSalt</c> is missing or still set to this constant, so this fallback can
    /// now only ever be reached on a developer machine or in the smoke harness. It is public so
    /// that gate can name it rather than keep a second copy of the string.</para>
    /// <para>The constructor still logs a warning when it is used, because the Development branch of
    /// that gate is a warning rather than a failure.</para>
    /// </remarks>
    public const string DefaultVisitorSalt = "TechieBlogDefaultVisitorSalt";

    private const int DefaultDedupeWindowHours = 24;

    private readonly IPostViewRepo postViewRepo;
    private readonly ILogger<PostViewTracker> logger;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly IPostViewQueue? viewQueue;
    private readonly string visitorSalt;
    private readonly int dedupeWindowHours;

    /// <summary>
    /// Initializes the view tracker from configuration.
    /// </summary>
    /// <remarks>
    /// Reads <c>Analytics:VisitorSalt</c> and <c>Analytics:ViewDedupeWindowHours</c>. The salt is a
    /// deployment secret and belongs in user secrets or environment configuration, not in a shipped
    /// settings file. The HTTP context accessor is optional so a unit test can construct the tracker
    /// without a request pipeline; the container always supplies it. The queue is optional for the
    /// same reason — see the class remarks for what its absence changes.
    /// </remarks>
    /// <param name="postViewRepo">Post-view data access.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="logger">Logger for tracking failures.</param>
    /// <param name="httpContextAccessor">Access to the request being served, when there is one.</param>
    /// <param name="viewQueue">Queue that carries the write off the render path (REQ-NFR-034).</param>
    public PostViewTracker(
        IPostViewRepo postViewRepo,
        IConfiguration configuration,
        ILogger<PostViewTracker> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        IPostViewQueue? viewQueue = null)
    {
        this.postViewRepo = postViewRepo;
        this.logger = logger;
        this.httpContextAccessor = httpContextAccessor;
        this.viewQueue = viewQueue;

        var configuredSalt = configuration?["Analytics:VisitorSalt"];
        visitorSalt = string.IsNullOrWhiteSpace(configuredSalt) ? DefaultVisitorSalt : configuredSalt;
        if (string.IsNullOrWhiteSpace(configuredSalt))
            logger.LogWarning("Analytics:VisitorSalt is not configured — using the built-in default salt.");

        dedupeWindowHours = ResolveWindow(configuration);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> A view can only be attributed to a visitor while an HTTP request
    /// is in flight, so the absence of one is reported as "nothing recorded" rather than as an error —
    /// that is the normal answer on the interactive render pass of a Blazor Server page, and it is
    /// what stops a page that renders twice from counting twice.</para>
    /// <para><b>[REQ-NFR-034] This method no longer touches the database.</b> It copies the visitor
    /// out of the live request and hands it to <see cref="IPostViewQueue"/>, which accepts it without
    /// waiting, so the render continues immediately instead of blocking on an INSERT round trip. The
    /// copy must happen here — <c>HttpContext</c> and everything on it is disposed as soon as the
    /// response completes, so the background writer could never look the visitor up itself.</para>
    /// <para><b>Flow:</b> resolve the ambient request → return early when there is none → read the
    /// transport address and the user-agent header → enqueue → (no queue registered, i.e. a unit
    /// test) fall back to writing inline through <see cref="TrackViewAsync"/>.</para>
    /// <para><b>Side Effects:</b> Enqueues at most one view. Performs no I/O and never throws.</para>
    /// </remarks>
    /// <returns>
    /// Success carrying <c>true</c> when the view was accepted for recording, and <c>false</c> when
    /// there was no request to attribute it to or the queue was saturated and dropped it. This is
    /// deliberately NOT "a row was written" — that outcome is not knowable synchronously any more.
    /// </returns>
    public Task<Result<bool>> TrackCurrentVisitAsync(long postId)
    {
        var httpContext = httpContextAccessor?.HttpContext;
        if (httpContext == null)
            return Task.FromResult(Result<bool>.Success(false));

        var ipAddress = httpContext.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        var userAgent = httpContext.Request?.Headers.UserAgent.ToString() ?? string.Empty;

        if (viewQueue == null)
            return TrackViewAsync(postId, ipAddress, userAgent);

        if (postId <= 0)
            return Task.FromResult(Result<bool>.Failure("A post id is required to track a view."));

        var isQueued = viewQueue.TryEnqueue(new PostViewRequest(postId, ipAddress, userAgent));
        return Task.FromResult(Result<bool>.Success(isQueued));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Records at most one view per visitor per post per
    /// de-duplication window. The visitor is identified by the salted digest of the IP address and
    /// user agent, never by the address itself.</para>
    /// <para><b>Flow:</b> reject a non-positive post id → derive the visitor hash → conditional
    /// insert, which performs the window test and the write as one statement.</para>
    /// <para><b>Side Effects:</b> Inserts at most one <c>PostViews</c> row and, when it does, moves
    /// the post's <c>PostViewCount</c> rollup by the same amount in the same statement. Writes no raw
    /// IP address. Logs an error on failure and nothing on the happy path — this runs once per
    /// counted view, so a success line would flood the log.</para>
    /// <para><b>Safe to call more than once</b> for the same reader and post; the second call
    /// returns success with <c>false</c> rather than double-counting.</para>
    /// <para><b>[REQ-NFR-034]</b> Since the write moved off the render path, the normal caller of
    /// this method is <c>PostViewWriter</c> on its background loop, not a page. It remains public and
    /// unchanged for callers that already hold the visitor's details — imports, tests, a future API
    /// head — and it is still the only place a visitor identity is constructed.</para>
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
