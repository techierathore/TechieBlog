using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Source-level guards for the UAT-023 cache-refresh endpoint, whose two failure modes were both
/// invisible to a compiler and to every existing test.
/// </summary>
/// <remarks>
/// <para><b>Why source and not a request test:</b> the endpoint and its exclusion list live in
/// <c>Program.cs</c>'s top-level statements, which cannot be linked into this assembly the way
/// <c>ForwardedHeadersSetup</c> and <c>DeploymentConfiguration</c> are. The behaviour itself is
/// proven by the live smoke (<c>tests/.artifacts/harness/uat-r7/</c>); these guards exist so the
/// two specific regressions cannot come back silently.</para>
///
/// <para><b>Both faults shipped from a reviewer-invisible place.</b> The endpoint was written
/// correctly and still could not work: <c>app.UseAntiforgery()</c> rejected BlogApp's POST at 400
/// before the Bearer token was read, and the not-found re-execution then rewrote the handler's own
/// 401 into a 400 HTML page. Reading the handler showed nothing wrong in either case — only running
/// it did.</para>
/// </remarks>
public class CacheRefreshEndpointTests
{
    private const string EndpointRoute = "/api/admin/cache/refresh";

    /// <summary>
    /// The cache-refresh endpoint opts out of antiforgery, because it authenticates by Bearer token
    /// rather than by the ambient cookie antiforgery exists to protect.
    /// </summary>
    /// <remarks>
    /// Without this the middleware answers 400 before the handler runs, so BlogApp's refresh — the
    /// whole remedy for an out-of-process write — fails permanently while looking like a refusal.
    /// </remarks>
    [Fact]
    public void CacheRefreshEndpointDisablesAntiforgery()
    {
        var source = ReadProgramSource();
        Assert.SkipWhen(source == null, "Program.cs not found next to the test assembly");

        var mapping = Regex.Match(
            source!,
            @"MapPost\(\s*""" + Regex.Escape(EndpointRoute) + @"""[\s\S]{0,400}?;",
            RegexOptions.None);

        Assert.True(mapping.Success, $"Program.cs no longer maps POST {EndpointRoute}.");
        Assert.True(
            mapping.Value.Contains("DisableAntiforgery", StringComparison.Ordinal),
            $"POST {EndpointRoute} must call .DisableAntiforgery(). It authenticates by Bearer " +
            "token, which a cross-site caller cannot set, so antiforgery adds no protection here — " +
            "it only rejects BlogApp's refresh with 400 before the token is ever read.");
    }

    /// <summary>
    /// API paths are excluded from the not-found re-execution, so a caller that parses the response
    /// receives the status the handler returned rather than an HTML error page.
    /// </summary>
    /// <remarks>
    /// Observed in the host log before the fix: <c>responded 401</c> immediately followed by
    /// <c>POST /404 responded 400</c>. BlogApp would have reported "bad request" for an expired
    /// session instead of asking the operator to sign in again.
    /// </remarks>
    [Fact]
    public void ApiPathsAreExcludedFromTheNotFoundReExecution()
    {
        var source = ReadProgramSource();
        Assert.SkipWhen(source == null, "Program.cs not found next to the test assembly");

        var list = Regex.Match(source!, @"InfrastructurePrefixes\s*=\s*\{(?<body>[\s\S]*?)\};");
        Assert.True(list.Success, "Program.cs no longer declares InfrastructurePrefixes.");

        var entries = Regex.Matches(list.Groups["body"].Value, @"""(?<prefix>[^""]+)""")
            .Select(match => match.Groups["prefix"].Value)
            .ToArray();

        Assert.True(
            entries.Contains("/api", StringComparer.Ordinal),
            "InfrastructurePrefixes must contain \"/api\". Without it a 4xx from an API endpoint is " +
            "re-executed as the Blazor not-found page, which demands an antiforgery token and turns " +
            "the handler's 401 into 400 + HTML. Present entries: " + string.Join(", ", entries));
    }

    /// <summary>
    /// The two patterns above match text that really is a violation, so neither zero can be a dead
    /// regex reading as a pass.
    /// </summary>
    /// <remarks>
    /// This repository has been bitten three times by a guard whose pattern silently stopped
    /// matching; every source-scanning gate here carries a self-test for that reason.
    /// </remarks>
    [Fact]
    public void GuardPatternsMatchSyntheticViolations()
    {
        const string mappedWithoutOptOut = @"
    app.MapPost(""/api/admin/cache/refresh"", HandleCacheRefreshAsync)
        .CacheOutput(policy => policy.NoCache());";
        var mapping = Regex.Match(
            mappedWithoutOptOut,
            @"MapPost\(\s*""" + Regex.Escape(EndpointRoute) + @"""[\s\S]{0,400}?;");
        Assert.True(mapping.Success, "the mapping pattern no longer finds the endpoint at all");
        Assert.DoesNotContain("DisableAntiforgery", mapping.Value, StringComparison.Ordinal);

        const string listWithoutApi = @"
    public static readonly string[] InfrastructurePrefixes =
    {
        ""/_blazor"",
        ""/health""
    };";
        var list = Regex.Match(listWithoutApi, @"InfrastructurePrefixes\s*=\s*\{(?<body>[\s\S]*?)\};");
        Assert.True(list.Success, "the prefix-list pattern no longer finds the declaration");
        var entries = Regex.Matches(list.Groups["body"].Value, @"""(?<prefix>[^""]+)""")
            .Select(match => match.Groups["prefix"].Value)
            .ToArray();
        Assert.DoesNotContain("/api", entries);
        Assert.Contains("/health", entries);
    }

    /// <summary>
    /// Reads the web host's Program.cs from the repository, or null when it cannot be located.
    /// </summary>
    /// <returns>The file's text, or null.</returns>
    private static string? ReadProgramSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source", "TechieBlog", "Program.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        return null;
    }
}
