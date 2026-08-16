using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Executable form of enforcement patterns 1-4 in <c>docs/TechieBlog-Coding-Standards.md</c>
/// §Enforcement — identifier-naming conventions — run on every build (REQ-NFR-041).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the §Enforcement greps have been blind three separate times
/// (2026-08-07, 2026-08-11, 2026-08-14), and every time the zero they returned read as a pass.
/// Patterns 5-6 stopped depending on that when <see cref="ExceptionDisclosureTests"/> turned them
/// into build-time scans; patterns 1-4 had no equivalent, which after three failures is the real
/// exposure. This test makes the naming conventions fail the build rather than waiting for somebody
/// to run a shell command correctly, in the right dialect, from the right directory.</para>
///
/// <para><b>Code Flow:</b> walk up from the test assembly to the repository root → enumerate the
/// files each pattern owns (<c>*.cs</c> + <c>*.razor</c> under <c>source/</c>, <c>*.cs</c> under
/// <c>tests/</c>), skipping <c>bin</c>, <c>obj</c> and the gitignored <c>tests/.artifacts</c> scratch
/// area → match each non-comment line against the pattern → fail naming every offending file, line
/// number and line text.</para>
///
/// <para><b>The self-test is the point.</b> A regex that can never match anything is
/// indistinguishable from a clean tree, and that is precisely how the 2026-08-07 and 2026-08-14
/// patterns survived: <c>[\w.&lt;&gt;,\[\]?]+</c> closed its character class early and could not match
/// a single line of C#. <see cref="EveryPatternMatchesItsPositiveControl"/> asserts each
/// <see cref="Regex"/> matches a hand-written violation, so an unmatchable pattern fails the build
/// immediately instead of reading as compliance.</para>
///
/// <para><b>Dependencies:</b> xUnit, and the repository layout. Skipped rather than failed when the
/// repository root cannot be located, so a package-restored copy of the test assembly does not
/// report a false violation.</para>
/// </remarks>
public class SourceConventionTests
{
    /// <summary>
    /// Pattern 1 — a private field whose name carries the banned <c>_</c> prefix.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>[^;=(]*</c> spans the whole type expression, so generic,
    /// array, nullable and qualified type names are all covered without enumerating their
    /// characters — the omission that hid seven of the fourteen fields found under REQ-NFR-021.</para>
    /// </remarks>
    private static readonly Regex UnderscoreFieldPrefix = new(
        @"private[^;=(]*\s_[A-Za-z]",
        RegexOptions.Compiled);

    /// <summary>
    /// Pattern 2 — a test method named in the banned <c>Method_Scenario_Expected</c> form.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>(_[A-Za-z0-9]+)+</c> accepts ANY number of underscores. The
    /// superseded pattern used <c>[A-Za-z0-9]+_[A-Za-z0-9]+</c>, which permits exactly one and so
    /// could not see <c>Login_WithBadPassword_Fails</c> — the shape the standard actually bans. The
    /// optional <c>&lt;...&gt;</c> group covers generic test methods.</para>
    /// </remarks>
    private static readonly Regex TestMethodUnderscore = new(
        @"public\s+(async\s+)?(Task|void)\s+[A-Za-z0-9]+(_[A-Za-z0-9]+)+\s*(<[A-Za-z0-9,\s]*>)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Pattern 3 — a private field carrying a Hungarian type prefix, which this project bans outright.
    /// </summary>
    private static readonly Regex HungarianFieldPrefix = new(
        @"private[^;=(]*\s(obj|str|int|bln)[A-Z]",
        RegexOptions.Compiled);

    /// <summary>
    /// Pattern 4 — an <c>a</c>- or <c>v</c>-prefixed parameter or local, e.g. <c>aLoggedUser</c>.
    /// </summary>
    private static readonly Regex AmbiguousLocalPrefix = new(
        @"\b(a|v)[A-Z][A-Za-z]*\s*[,)=;]",
        RegexOptions.Compiled);

    /// <summary>
    /// Hand-written violation for <see cref="TestMethodUnderscore"/>, assembled from fragments so
    /// that no single line of this file is itself a pattern-2 violation — this file lives under
    /// <c>tests/</c> and is scanned by the very gate it defines.
    /// </summary>
    private const string TestMethodUnderscoreControl =
        "    public async Task Login"
        + "_WithBadPassword"
        + "_Fails()";

    /// <summary>
    /// No private field under <c>source/</c> carries the banned <c>_</c> prefix — the drift
    /// remediated under REQ-NFR-021, kept closed here.
    /// </summary>
    [Fact]
    public void NoUnderscorePrefixedFields()
    {
        AssertNoMatches(UnderscoreFieldPrefix, "source", ScanCSharpAndRazor);
    }

    /// <summary>
    /// No test method under <c>tests/</c> uses the underscore-separated
    /// <c>Method_Scenario_Expected</c> name; the standard requires short PascalCase with the full
    /// scenario in the XML <c>summary</c>.
    /// </summary>
    [Fact]
    public void NoUnderscoresInTestMethodNames()
    {
        AssertNoMatches(TestMethodUnderscore, "tests", ScanCSharpOnly);
    }

    /// <summary>
    /// No private field under <c>source/</c> carries an <c>obj</c>, <c>str</c>, <c>int</c> or
    /// <c>bln</c> Hungarian type prefix.
    /// </summary>
    [Fact]
    public void NoHungarianFieldPrefixes()
    {
        AssertNoMatches(HungarianFieldPrefix, "source", ScanCSharpAndRazor);
    }

    /// <summary>
    /// No parameter or local under <c>source/</c> carries an <c>a</c> or <c>v</c> prefix.
    /// </summary>
    [Fact]
    public void NoAmbiguousLocalPrefixes()
    {
        AssertNoMatches(AmbiguousLocalPrefix, "source", ScanCSharpOnly);
    }

    /// <summary>
    /// Every convention pattern matches a hand-written violation of the rule it enforces, so an
    /// unmatchable regex fails the build instead of masquerading as a clean tree.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> this is the guard REQ-NFR-041 exists for. Three enforcement
    /// patterns in this repository have shipped in a state where they could not match their own
    /// target, and each one's zero was recorded as compliance. Asserting the positive direction
    /// makes that failure mode loud.</para>
    /// <para><b>Side Effects:</b> none — it matches against in-memory strings only.</para>
    /// </remarks>
    [Fact]
    public void EveryPatternMatchesItsPositiveControl()
    {
        Assert.True(
            UnderscoreFieldPrefix.IsMatch("    private readonly ILogger<Foo> _logger;"),
            "Pattern 1 (underscore field prefix) matched nothing — it cannot enforce anything.");
        Assert.True(
            UnderscoreFieldPrefix.IsMatch("    private IRepo _repo;"),
            "Pattern 1 (underscore field prefix) missed a non-generic field type.");

        Assert.True(
            TestMethodUnderscore.IsMatch(TestMethodUnderscoreControl),
            "Pattern 2 (test-method underscores) missed the multi-underscore " +
            "Method_Scenario_Expected form — the exact 2026-08-14 blind spot.");
        Assert.True(
            TestMethodUnderscore.IsMatch("    public void Foo" + "_Bar()"),
            "Pattern 2 (test-method underscores) missed a single-underscore test method.");
        Assert.True(
            TestMethodUnderscore.IsMatch("    public void Foo" + "_Bar<T>()"),
            "Pattern 2 (test-method underscores) missed a generic test method.");
        Assert.False(
            TestMethodUnderscore.IsMatch("    public void LoginWithBadPasswordFails()"),
            "Pattern 2 (test-method underscores) flagged a correctly named test method.");

        Assert.True(
            HungarianFieldPrefix.IsMatch("    private string strName;"),
            "Pattern 3 (Hungarian prefixes) matched nothing — it cannot enforce anything.");

        Assert.True(
            AmbiguousLocalPrefix.IsMatch("        var aLoggedUser = GetUser();"),
            "Pattern 4 (a/v prefixes) matched nothing — it cannot enforce anything.");
        Assert.True(
            AmbiguousLocalPrefix.IsMatch("    public void Save(ClaimsPrincipal vIdentity)"),
            "Pattern 4 (a/v prefixes) missed a v-prefixed parameter.");
    }

    /// <summary>
    /// Scans one tree for the supplied pattern and fails naming every violation.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> reports every offending line at once rather than the first,
    /// because whoever runs into this gate is usually fixing a class of sites, not one. Comment
    /// lines are skipped so that documentation of the rule is not mistaken for a breach of it.</para>
    /// <para><b>Side Effects:</b> reads the repository's source tree.</para>
    /// </remarks>
    /// <param name="pattern">The convention violation to search for.</param>
    /// <param name="treeName">Repository-relative folder to scan — <c>source</c> or <c>tests</c>.</param>
    /// <param name="extensionFilter">Predicate selecting the file extensions this pattern owns.</param>
    private static void AssertNoMatches(Regex pattern, string treeName, Func<string, bool> extensionFilter)
    {
        var repositoryRoot = FindRepositoryRoot();
        Assert.SkipWhen(repositoryRoot == null, "repository root not found next to the test assembly");

        var tree = Path.Combine(repositoryRoot!, treeName);
        Assert.SkipWhen(!Directory.Exists(tree), $"{treeName}/ not found next to the test assembly");

        var violations = new List<string>();

        foreach (var file in EnumerateFiles(tree, extensionFilter))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//"))
                {
                    continue;
                }

                if (pattern.IsMatch(line))
                {
                    violations.Add($"{Path.GetRelativePath(repositoryRoot!, file)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Identifier naming must follow docs/TechieBlog-Coding-Standards.md §Fields, Parameters, " +
            "Locals (REQ-NFR-041): no underscore or Hungarian prefixes, no a-/v- prefixes, and no " +
            "underscores in test method names. Offending lines:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Lists every file under <paramref name="tree"/> the pattern owns, skipping build output and
    /// the gitignored <c>.artifacts</c> scratch area.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>tests/.artifacts/</c> holds the synthetic positive controls
    /// for these same patterns, so scanning it would fail the build on files whose entire job is to
    /// be violations. It is excluded from the test project's compile glob for the same reason.</para>
    /// </remarks>
    /// <param name="tree">Absolute path of the folder to walk.</param>
    /// <param name="extensionFilter">Predicate selecting the file extensions to include.</param>
    /// <returns>The files to scan.</returns>
    private static IEnumerable<string> EnumerateFiles(string tree, Func<string, bool> extensionFilter)
    {
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var artifactsSegment = $"{Path.DirectorySeparatorChar}.artifacts{Path.DirectorySeparatorChar}";

        return Directory
            .EnumerateFiles(tree, "*.*", SearchOption.AllDirectories)
            .Where(path => extensionFilter(path))
            .Where(path => !path.Contains(binSegment)
                        && !path.Contains(objSegment)
                        && !path.Contains(artifactsSegment));
    }

    /// <summary>
    /// Selects C# files only — the extension set for the test-naming and a-/v-prefix patterns.
    /// </summary>
    /// <param name="path">Candidate file path.</param>
    /// <returns><c>true</c> when the file is a C# file.</returns>
    private static bool ScanCSharpOnly(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Selects C# and Razor files — the field-prefix patterns must reach <c>.razor</c> markup, which
    /// declares fields in <c>@code</c> blocks that an earlier <c>*.cs</c>-only sweep never saw.
    /// </summary>
    /// <param name="path">Candidate file path.</param>
    /// <returns><c>true</c> when the file is a C# or Razor file.</returns>
    private static bool ScanCSharpAndRazor(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks up from the test assembly until the repository root is found.
    /// </summary>
    /// <returns>The absolute path of the repository root, or <c>null</c> when it is not present.</returns>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "source"))
                && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
