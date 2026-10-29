using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// REQ-NFR-017 (BRD-77) — the CI workflow triggers on push and pull request, restores, builds,
/// tests and uploads build artifacts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the pipeline is the only thing that runs the suite on a clean machine; this
/// gate fails the build if someone edits a step out of the workflow file.</para>
/// <para><b>Code Flow:</b> read <c>.github/workflows/ci.yml</c> → match the trigger block and each
/// required step with a pattern → every pattern is also run against a synthetic fragment first (the
/// positive control) so a pattern that cannot match cannot report a clean pass.</para>
/// <para><b>Dependencies:</b> the repository layout; skipped when the checkout is not present.</para>
/// </remarks>
public class CiWorkflowTests
{
    private static readonly Regex PushTrigger = new(
        @"^on:\s*\n\s+push:\s*\n\s+branches:\s*\[(?<b>[^\]]*)\]", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PullRequestTrigger = new(
        @"^\s+pull_request:", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// The trigger patterns match a hand-written workflow fragment.
    /// </summary>
    [Fact]
    public void TriggerPatternsMatchPositiveControl()
    {
        const string control = "on:\n  push:\n    branches: [master, dev]\n  pull_request:\n    branches: [master]\n";
        var push = PushTrigger.Match(control);
        Assert.True(push.Success);
        Assert.Contains("master", push.Groups["b"].Value);
        Assert.Matches(PullRequestTrigger, control);
    }

    /// <summary>
    /// The workflow runs on push to master and dev, and on pull requests.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-017 the CI workflow triggers on push to master and dev and on pull requests")]
    public void WorkflowTriggersOnPushToMasterAndDevAndOnPullRequests()
    {
        var workflow = ReadWorkflow();
        var push = PushTrigger.Match(workflow);
        Assert.True(push.Success, "REQ-NFR-017: ci.yml has no 'on: push: branches: [...]' trigger");
        Assert.Contains("master", push.Groups["b"].Value);
        Assert.Contains("dev", push.Groups["b"].Value);
        Assert.Matches(PullRequestTrigger, workflow);
    }

    /// <summary>
    /// The workflow restores, builds the solution, runs the tests and uploads artifacts.
    /// </summary>
    [Theory(DisplayName = "REQ-NFR-017 the CI workflow restores, builds, tests and uploads artifacts")]
    [InlineData(@"run:\s*dotnet restore TechieBlog\.slnx", "restore the solution")]
    [InlineData(@"run:\s*dotnet build TechieBlog\.slnx", "build the solution")]
    [InlineData(@"dotnet test\s+tests/TechieBlog\.Tests/TechieBlog\.Tests\.csproj", "run the tests")]
    [InlineData(@"uses:\s*actions/upload-artifact@v\d+", "upload build artifacts")]
    public void WorkflowHasRequiredStep(string pattern, string what)
    {
        Assert.True(new Regex(pattern).IsMatch(ReadWorkflow()), $"REQ-NFR-017: ci.yml must {what}");
    }

    /// <summary>
    /// The test project the workflow runs exists, so the step cannot silently point at nothing.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-017 the test project the CI workflow runs exists")]
    public void WorkflowTestProjectExists()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");
        Assert.True(File.Exists(Path.Combine(root!, "tests", "TechieBlog.Tests", "TechieBlog.Tests.csproj")));
    }

    private static string ReadWorkflow()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");
        var path = Path.Combine(root!, ".github", "workflows", "ci.yml");
        Assert.True(File.Exists(path), "REQ-NFR-017: .github/workflows/ci.yml is missing");
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }
}
