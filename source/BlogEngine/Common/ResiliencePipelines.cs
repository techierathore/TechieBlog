using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace BlogEngine.Common;

/// <summary>
/// Builds the retry / circuit-breaker pipelines that protect the application's outbound
/// dependencies (REQ-NFR-012, BRD-89).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Database, email and storage calls all fail in the same two ways — a
/// brief blip that a retry fixes, and a sustained outage where retrying only makes things worse.
/// This factory defines one pipeline per dependency so the retry budget, backoff curve and
/// breaker thresholds are stated once and can be asserted in tests.</para>
///
/// <para><b>Code Flow:</b> the host registers each pipeline by name with
/// <c>AddResiliencePipeline</c>; call sites resolve <c>ResiliencePipelineProvider&lt;string&gt;</c>
/// and execute through the named pipeline.</para>
///
/// <para><b>The three pipelines and their thresholds</b> (strategies apply retry innermost, then
/// the breaker, then the timeout):</para>
/// <list type="table">
///   <listheader>
///     <term>Pipeline</term>
///     <description>Retries / backoff — breaker — timeout, and the reasoning</description>
///   </listheader>
///   <item>
///     <term><see cref="Database"/></term>
///     <description>3 retries, jittered exponential from 200 ms; breaker opens for 30 s once 50% of
///     at least 8 calls in a 30 s window fail; 10 s timeout. Tuned for a PostgreSQL failover, which
///     is short — retrying past that only burns request threads on a database that is gone.</description>
///   </item>
///   <item>
///     <term><see cref="Email"/></term>
///     <description>3 retries, jittered exponential from 1 s; breaker opens for 60 s once 70% of at
///     least 5 calls in a 60 s window fail; 30 s timeout. Slower and more forgiving because relays
///     throttle rather than fail, and a temporary refusal is not an outage.</description>
///   </item>
///   <item>
///     <term><see cref="Storage"/></term>
///     <description>2 retries, jittered exponential from 500 ms; breaker opens for 30 s once 50% of
///     at least 5 calls in a 30 s window fail; 60 s timeout. Fewest retries because an upload is
///     large and someone is watching it; longest timeout for the same reason.</description>
///   </item>
/// </list>
/// <para>Jitter is enabled on all three deliberately: without it, every caller that failed at the
/// same moment retries at the same moment, and the retry burst becomes the second outage.</para>
///
/// <para><b>Graceful degradation — what a caller should expect when a breaker is open.</b> An open
/// breaker <b>fails fast</b>: the pipeline throws <see cref="BrokenCircuitException"/> immediately,
/// without attempting the call and without queueing. That is the point — during an outage the
/// application stops spending threads on work that cannot succeed, and stops adding load to a
/// dependency that is already struggling. Callers must therefore treat
/// <see cref="BrokenCircuitException"/> as a distinct, expected outcome rather than an unexpected
/// fault, and translate it into a <c>Result.Failure</c> carrying a "temporarily unavailable"
/// message. Per dependency: content reads fall back to the cache and serve slightly stale data
/// (REQ-NFR-018); email sends are dropped with a logged warning rather than blocking a request,
/// because mail is never on the critical path; storage writes surface a retryable error to the
/// user, since silently discarding an upload would be worse than asking for it again. The breaker
/// reopens on its own after the break duration, admitting a trial call — no operator action is
/// needed to recover, and no caller should implement its own retry-around-the-breaker, which would
/// undo the protection entirely.</para>
///
/// <para><b>Dependencies:</b> Polly v8 (<c>Polly.Core</c>).</para>
///
/// <para><b>Usage:</b> <c>ResiliencePipelines.BuildDatabasePipeline()</c> — or, in the host,
/// <c>services.AddResiliencePipeline(ResiliencePipelines.Database, ResiliencePipelines.ConfigureDatabase)</c>.</para>
/// </remarks>
public static class ResiliencePipelines
{
    /// <summary>
    /// Pipeline name for PostgreSQL calls.
    /// </summary>
    public const string Database = "Database";

    /// <summary>
    /// Pipeline name for outbound SMTP calls.
    /// </summary>
    public const string Email = "Email";

    /// <summary>
    /// Pipeline name for blob / file-storage calls.
    /// </summary>
    public const string Storage = "Storage";

    /// <summary>
    /// Configures the database pipeline: three exponential retries behind a circuit breaker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL blips are short, so three attempts with jittered
    /// exponential backoff from 200 ms cover a failover. The breaker opens for 30 seconds once
    /// half the calls in a 30-second window fail, which stops a dead database from consuming
    /// every request thread.</para>
    /// <para><b>Flow:</b> retry strategy added first (inner), breaker second (outer), timeout last.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied builder.</para>
    /// </remarks>
    /// <param name="builder">The pipeline builder to configure.</param>
    public static void ConfigureDatabase(ResiliencePipelineBuilder builder)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 8,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Configures the email pipeline: patient retries behind a slow-tripping breaker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> SMTP relays throttle rather than fail outright, so retries
    /// start at one second and the breaker tolerates a higher failure ratio before opening.
    /// Email is never on the critical path — a dropped send degrades gracefully to a logged
    /// warning.</para>
    /// <para><b>Flow:</b> retry → circuit breaker → timeout.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied builder.</para>
    /// </remarks>
    /// <param name="builder">The pipeline builder to configure.</param>
    public static void ConfigureEmail(ResiliencePipelineBuilder builder)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.7,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(60)
            })
            .AddTimeout(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Configures the storage pipeline for image and CV uploads.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uploads are large and user-visible, so only two retries are
    /// attempted before the caller is told to try again; the breaker protects the request thread
    /// pool when the storage backend is down.</para>
    /// <para><b>Flow:</b> retry → circuit breaker → timeout.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied builder.</para>
    /// </remarks>
    /// <param name="builder">The pipeline builder to configure.</param>
    public static void ConfigureStorage(ResiliencePipelineBuilder builder)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Builds a standalone database pipeline for callers without dependency injection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies <see cref="ConfigureDatabase"/> to a fresh builder.</para>
    /// <para><b>Flow:</b> new builder → configure → build.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A ready-to-use database pipeline.</returns>
    public static ResiliencePipeline BuildDatabasePipeline()
    {
        var builder = new ResiliencePipelineBuilder();
        ConfigureDatabase(builder);
        return builder.Build();
    }

    /// <summary>
    /// Builds a standalone email pipeline for callers without dependency injection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies <see cref="ConfigureEmail"/> to a fresh builder.</para>
    /// <para><b>Flow:</b> new builder → configure → build.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A ready-to-use email pipeline.</returns>
    public static ResiliencePipeline BuildEmailPipeline()
    {
        var builder = new ResiliencePipelineBuilder();
        ConfigureEmail(builder);
        return builder.Build();
    }

    /// <summary>
    /// Builds a standalone storage pipeline for callers without dependency injection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies <see cref="ConfigureStorage"/> to a fresh builder.</para>
    /// <para><b>Flow:</b> new builder → configure → build.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A ready-to-use storage pipeline.</returns>
    public static ResiliencePipeline BuildStoragePipeline()
    {
        var builder = new ResiliencePipelineBuilder();
        ConfigureStorage(builder);
        return builder.Build();
    }
}
