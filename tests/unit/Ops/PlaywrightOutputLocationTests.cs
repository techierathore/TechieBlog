using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Keeps every Playwright spec's screenshots and notes under <c>tests/.artifacts/</c>, never in a
/// <c>test-results*</c> folder at the repository root.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> on 2026-10-29 the owner found the repository root crowded with
/// <c>test-results-cluster-*</c> folders. Nineteen specs under <c>tests/verify/</c> hard-coded
/// root-relative output paths, bypassing the <c>outputDir</c> in <c>playwright.config.ts</c>. The
/// session-start sweep deleted the folders, but nothing stopped the next run writing them again.
/// This test makes that a build failure.</para>
/// <para><b>Code Flow:</b> locate the repository root → read every <c>*.ts</c>, <c>*.js</c> and
/// <c>*.mjs</c> file under <c>tests/verify/</c> → flag any non-comment line holding a string literal
/// that starts with <c>test-results</c> or <c>./test-results</c>.</para>
/// <para><b>Dependencies:</b> xUnit and the repository layout; skipped when the root cannot be found.</para>
/// </remarks>
public class PlaywrightOutputLocationTests
{
    /// <summary>
    /// A quoted path (single, double or backtick) that begins at a root-level <c>test-results</c> folder.
    /// </summary>
    private static readonly Regex RootOutputPath = new(
        @"['""`](\./)?test-results",
        RegexOptions.Compiled);

    /// <summary>
    /// No spec under <c>tests/verify/</c> writes to a <c>test-results*</c> folder at the repository root.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-041 no Playwright spec writes output to a test-results folder at the repository root")]
    public void NoSpecWritesToRootTestResults()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");

        var specs = Path.Combine(root!, "tests", "verify");
        Assert.SkipWhen(!Directory.Exists(specs), "tests/verify/ not found next to the test assembly");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(specs, "*.*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                                 || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                                 || path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//"))
                {
                    continue;
                }

                if (RootOutputPath.IsMatch(line))
                {
                    violations.Add($"{Path.GetRelativePath(root!, file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Playwright output belongs under tests/.artifacts/, not in a test-results folder at the " +
            "repository root. Offending lines:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The pattern matches each root-relative form it must catch and leaves the correct form alone,
    /// so a zero from <see cref="NoSpecWritesToRootTestResults"/> is evidence.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-041 the root-output pattern matches a synthetic violation so a zero is evidence")]
    public void RootOutputPatternMatchesItsPositiveControl()
    {
        Assert.Matches(RootOutputPath, "const SHOTS = 'test-results-cluster-c';");
        Assert.Matches(RootOutputPath, "await page.screenshot({ path: `test-results-cluster-h/${name}.png` });");
        Assert.Matches(RootOutputPath, "const OUT = path.join(process.cwd(), \"./test-results/cluster-l\");");
        Assert.DoesNotMatch(RootOutputPath, "const OUT = 'tests/.artifacts/test-results-cluster-c';");
    }
}
