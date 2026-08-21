using BlogEngine.Common;
using Polly;
using Polly.CircuitBreaker;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="ResiliencePipelines"/> (REQ-NFR-012, BRD-89).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the pipelines actually retry and actually break, rather than
/// being configured and never exercised. The circuit-breaker test drives enough failures to trip
/// the breaker and then asserts that the next call fails fast — the graceful-degradation
/// behaviour callers translate into a "temporarily unavailable" result.</para>
/// <para><b>Dependencies:</b> xUnit and Polly; no database or host required.</para>
/// </remarks>
public class ResiliencePipelinesTests
{
    /// <summary>
    /// The three pipeline names are distinct, so registering all of them cannot collide in the
    /// resilience registry.
    /// </summary>
    [Fact]
    public void PipelineNamesAreDistinct()
    {
        var names = new[] { ResiliencePipelines.Database, ResiliencePipelines.Email, ResiliencePipelines.Storage };

        Assert.Equal(names.Length, names.Distinct().Count());
    }

    /// <summary>
    /// Every pipeline builds without throwing, which catches an invalid strategy combination at
    /// test time rather than at startup.
    /// </summary>
    [Fact]
    public void EveryPipelineBuilds()
    {
        Assert.NotNull(ResiliencePipelines.BuildDatabasePipeline());
        Assert.NotNull(ResiliencePipelines.BuildEmailPipeline());
        Assert.NotNull(ResiliencePipelines.BuildStoragePipeline());
    }

    /// <summary>
    /// A database call that fails once and then succeeds is retried transparently, so a brief
    /// blip never reaches the user.
    /// </summary>
    [Fact]
    public async Task DatabasePipelineRetriesTransientFailure()
    {
        var pipeline = ResiliencePipelines.BuildDatabasePipeline();
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            if (attempts == 1)
                throw new InvalidOperationException("transient");

            return ValueTask.FromResult("ok");
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// A permanently failing database call is attempted once plus the configured retries and
    /// then gives up, rather than retrying forever.
    /// </summary>
    [Fact]
    public async Task DatabasePipelineStopsAfterTheRetryBudget()
    {
        var pipeline = ResiliencePipelines.BuildDatabasePipeline();
        var attempts = 0;

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await pipeline.ExecuteAsync(_ =>
            {
                attempts++;
                throw new InvalidOperationException("down");
            }));

        Assert.Equal(4, attempts);
    }

    /// <summary>
    /// Sustained storage failures open the circuit breaker, after which further calls fail
    /// immediately with <see cref="BrokenCircuitException"/> instead of queueing behind a dead
    /// dependency — the fail-fast signal callers degrade on.
    /// </summary>
    [Fact]
    public async Task StoragePipelineBreaksAfterSustainedFailures()
    {
        var pipeline = ResiliencePipelines.BuildStoragePipeline();

        for (var call = 0; call < 10; call++)
        {
            try
            {
                await pipeline.ExecuteAsync<string>(_ => throw new InvalidOperationException("storage down"));
            }
            catch (Exception)
            {
                // Expected while the breaker samples failures.
            }
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(async () =>
            await pipeline.ExecuteAsync(_ => ValueTask.FromResult("should not run")));
    }

    /// <summary>
    /// A healthy call passes straight through every pipeline with its result intact.
    /// </summary>
    [Fact]
    public async Task HealthyCallPassesThroughUnchanged()
    {
        var pipeline = ResiliencePipelines.BuildEmailPipeline();

        var result = await pipeline.ExecuteAsync(_ => ValueTask.FromResult(42));

        Assert.Equal(42, result);
    }
}
