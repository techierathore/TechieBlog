using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Executable form of the <c>ex.Message</c> enforcement grep in
/// <c>docs/TechieBlog-Coding-Standards.md</c> §Enforcement (REQ-NFR-033).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-031 curated four named services and left seventeen disclosures
/// elsewhere, which is exactly what happens when a gate names files instead of a rule. This test
/// scans <b>all of <c>source/</c></b> for exception text reaching a surface a user reads, so the
/// rule travels with the codebase rather than with a list somebody has to remember to extend. It is
/// the same two patterns the Coding Standards document, run on every build.</para>
///
/// <para><b>Code Flow:</b> walk up from the test assembly to the repository root → enumerate
/// <c>*.cs</c> and <c>*.razor</c> under <c>source/</c>, skipping <c>bin</c> and <c>obj</c> → match
/// each line against the two sink patterns → fail with the offending file, line number and text.</para>
///
/// <para><b>What is deliberately NOT matched, and why.</b> Three uses of <c>ex.Message</c> in
/// <c>source/</c> are correct and stay: <c>MigrationRunner</c>'s and <c>Program.cs</c>'s
/// <c>Console</c> writes at the process boundary, where the audience is an operator reading a
/// terminal and no user surface is involved, and <c>ForwardedHeadersSetup</c>'s configuration
/// exception, which fails the host at startup before it ever serves a request. The patterns
/// therefore target the <i>sinks</i> — a <c>Result.Failure</c> the caller renders, and an assignment
/// to a message field a page binds — rather than the token itself, so the gate stays true instead of
/// accumulating exemptions.</para>
///
/// <para><b>Dependencies:</b> xUnit, and the repository layout. It is skipped rather than failed
/// when <c>source/</c> cannot be located, so a package-restored copy of the test assembly does not
/// report a false violation.</para>
/// </remarks>
public class ExceptionDisclosureTests
{
    /// <summary>
    /// Exception text interpolated into a <c>Result.Failure</c>, which every calling page renders
    /// verbatim.
    /// </summary>
    private static readonly Regex ResultFailureSink = new(
        @"Result(<[^>]*>)?\.Failure\s*\(.*\bex\.Message",
        RegexOptions.Compiled);

    /// <summary>
    /// Exception text assigned to a field or property a page binds into its markup.
    /// </summary>
    private static readonly Regex MessageAssignmentSink = new(
        @"\b(StatusMessage|UploadError|ErrorMessage|errorMessage|statusMessage|Message)\s*=\s*[^;]*\bex\.Message",
        RegexOptions.Compiled);

    /// <summary>
    /// No file under <c>source/</c> puts exception text into a failed <c>Result</c>, which is the
    /// pattern REQ-NFR-031 established and REQ-NFR-033 widened from four named services to the whole
    /// tree.
    /// </summary>
    [Fact]
    public void NoResultFailureCarriesExceptionText()
    {
        AssertNoMatches(ResultFailureSink);
    }

    /// <summary>
    /// No file under <c>source/</c> assigns exception text to a message a page renders — the page
    /// level of the same rule, which is where the residual disclosures lived after REQ-NFR-031.
    /// </summary>
    [Fact]
    public void NoRenderedMessageCarriesExceptionText()
    {
        AssertNoMatches(MessageAssignmentSink);
    }

    /// <summary>
    /// Scans every source file for the supplied pattern and fails naming each violation.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reports every offending line at once rather than the first,
    /// because whoever runs into this gate is usually fixing a class of sites, not one.</para>
    /// <para><b>Side Effects:</b> Reads the repository's source tree.</para>
    /// </remarks>
    /// <param name="sink">The disclosure pattern to search for.</param>
    private static void AssertNoMatches(Regex sink)
    {
        var sourceRoot = FindSourceRoot();
        Assert.SkipWhen(sourceRoot == null, "source/ not found next to the test assembly");

        var violations = new List<string>();

        foreach (var file in EnumerateSourceFiles(sourceRoot!))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("///") || line.TrimStart().StartsWith("//"))
                {
                    continue;
                }

                if (sink.IsMatch(line))
                {
                    violations.Add($"{Path.GetRelativePath(sourceRoot!, file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Exception text must never reach a user-facing message (REQ-NFR-031/033). Log it with " +
            "context and return a curated constant instead. Offending lines:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Lists every C# and Razor file under <c>source/</c>, skipping build output.
    /// </summary>
    /// <param name="sourceRoot">Absolute path of the <c>source/</c> folder.</param>
    /// <returns>The files to scan.</returns>
    private static IEnumerable<string> EnumerateSourceFiles(string sourceRoot)
    {
        return Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>
    /// Walks up from the test assembly until a folder containing <c>source/</c> is found.
    /// </summary>
    /// <returns>The absolute path of <c>source/</c>, or <c>null</c> when it is not present.</returns>
    private static string? FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
