using System.Text.RegularExpressions;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Build-time gate asserting that the configuration keys and routes quoted in the setup
/// documentation actually exist in the source (UAT-014, UAT-015).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> On 2026-08-22 the two onboarding documents were found to describe a
/// plausible but imaginary version of this project. <c>README.md</c> told the reader to theme the
/// site by editing an SCSS tree nothing compiles. <c>GETTING_STARTED.md</c> was worse: it named
/// <c>ConnectionStrings:DefaultConnection</c> (the host reads a top-level <c>AppDbConString</c> and
/// throws without it), told the reader to put settings in <c>appsettings.Local.json</c> (loaded by
/// nothing), handed out a seeded administrator that does not exist, and pointed at
/// <c>/register</c>, a route retired months earlier.</para>
///
/// <para><b>Why every one of those survived so long:</b> they are the CONVENTIONAL ASP.NET answers.
/// A reviewer skims <c>ConnectionStrings:DefaultConnection</c> and sees nothing wrong, because in
/// almost every other project it would be right. Documentation drift of this kind cannot be caught
/// by reading; it has to be executed.</para>
///
/// <para><b>Code Flow:</b> each test extracts a class of claim from the documents with a regex, then
/// asserts the claim resolves against the tree that owns it — configuration keys against
/// <c>source/</c>, routes against the <c>@page</c> directives, and file paths against the file
/// system.</para>
///
/// <para><b>Dependencies:</b> the file system only. No database, no host, no network.</para>
///
/// <para><b>This class follows the REQ-NFR-041 discipline: every pattern carries a positive
/// control.</b> The coding-standards greps in this repository were silently unmatchable three
/// separate times, and each time the zero read as a pass. A documentation gate has exactly the same
/// failure mode — a regex that matches no claims reports a clean document — so
/// <see cref="EveryPatternMatchesItsPositiveControl"/> proves each pattern can still find the very
/// strings that were wrong, and the extraction tests assert they found a non-trivial number of
/// claims before judging any of them.</para>
/// </remarks>
public class DocumentationClaimTests
{
    /// <summary>A double-quote, so the positive controls can embed C# string literals readably.</summary>
    private const string Quote = "\"";

    /// <summary>The documents this gate governs. Both are read by someone setting the project up.</summary>
    private static readonly string[] GovernedDocuments = ["README.md", "GETTING_STARTED.md"];

    /// <summary>
    /// A configuration key quoted in prose, e.g. <c>`AppDbConString`</c> or
    /// <c>`SiteSettings:BaseUrl`</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: PascalCase, optionally colon-separated, inside backticks. That shape is
    /// what a configuration key looks like in these documents, and it keeps prose words and shell
    /// snippets out of the result set.
    /// </remarks>
    private static readonly Regex ConfigKeyInDocs = new(
        @"`([A-Z][A-Za-z0-9]*(?::[A-Z][A-Za-z0-9]*)*)`",
        RegexOptions.Compiled);

    /// <summary>An application route quoted in prose, e.g. <c>`/admin/speaking`</c>.</summary>
    private static readonly Regex RouteInDocs = new(
        @"`(/[a-zA-Z][a-zA-Z0-9/_-]*)`",
        RegexOptions.Compiled);

    /// <summary>A markdown link to a path inside the repository.</summary>
    private static readonly Regex RepoLinkInDocs = new(
        @"\]\((?!https?:|mailto:)([^)#]+)(?:#[^)]*)?\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Configuration keys whose absence from <c>source/</c> would be a documentation bug. Anything
    /// not listed here is prose the extraction happened to catch, and is ignored.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a deny-list, on purpose. A deny-list has to predict which wrong key
    /// somebody will invent next; this asserts that the keys we DO document are real, which is the
    /// property that was actually violated.
    /// </remarks>
    private static readonly string[] ConfigKeysThatMustExist =
    [
        "AppDbConString",
        "JwtSigningKey",
        "AppEncryptionKey",
        "SiteSettings:BaseUrl"
    ];

    /// <summary>
    /// Keys that must NOT appear as guidance, because they look right and are not.
    /// </summary>
    /// <remarks>
    /// The one place a deny-list earns its keep: these are the conventional ASP.NET spellings that
    /// this project deliberately does not use, so their reappearance in a document is a regression
    /// rather than a new idea. They are permitted in a sentence that explicitly warns against them,
    /// which is why the check ignores lines containing "not" or "instead of".
    /// </remarks>
    private static readonly string[] KeysThatMustNotBeRecommended =
    [
        "ConnectionStrings:DefaultConnection",
        "appsettings.Local.json"
    ];

    /// <summary>
    /// Words that mark a mention of a forbidden key as a WARNING against it rather than advice to
    /// use it.
    /// </summary>
    /// <remarks>
    /// Matched with word boundaries, so "no" does not fire on "know" or "note". Evaluated over a
    /// small window of lines because markdown wraps a sentence across them.
    /// </remarks>
    private static readonly Regex WarningVocabulary = new(
        @"\b(no|not|never|nothing|ignored|wrong|instead\s+of|does\s+not)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>An endpoint actually mapped by the host, e.g. <c>MapHealthChecks("/healthz")</c>.</summary>
    private const string MapEndpointPattern =
        @"\.Map[A-Za-z]*\(\s*""(/[^""]*)""";

    /// <summary>A route held in a string constant, e.g. <c>const string Path = "/healthz";</c>.</summary>
    private const string RouteConstantPattern =
        @"=\s*""(/[a-zA-Z][^""]*)""\s*;";

    /// <summary>A fenced JSON block in the documentation.</summary>
    private const string JsonBlockPattern = @"```json\s*(.*?)```";

    /// <summary>A property name inside a JSON block.</summary>
    private const string JsonPropertyPattern =
        @"""([A-Za-z][A-Za-z0-9_]*)""\s*:";

    /// <summary>
    /// Every configuration key the setup documents tell the reader to set must exist in the source.
    /// </summary>
    [Fact]
    public void DocumentedConfigurationKeysExistInSource()
    {
        var root = RequireRepositoryRoot();
        var documented = ExtractFromDocuments(root, ConfigKeyInDocs)
            .Where(candidate => ConfigKeysThatMustExist.Contains(candidate))
            .ToHashSet(StringComparer.Ordinal);

        // Guard the guard: if the extraction stops finding these, the test would pass vacuously.
        Assert.True(
            documented.Count == ConfigKeysThatMustExist.Length,
            $"Expected the setup docs to name all {ConfigKeysThatMustExist.Length} required " +
            $"configuration keys; found {documented.Count} ({string.Join(", ", documented)}). " +
            "Either a key stopped being documented, or the extraction pattern no longer matches.");

        var sourceText = ReadTree(Path.Combine(root, "source"), IsCSharpFile);
        var missing = documented
            .Where(key => !sourceText.Contains(LastSegment(key), StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These configuration keys are documented but appear nowhere in source/: " +
            string.Join(", ", missing) +
            ". A key the application never reads is a setting the reader will set and watch do nothing.");
    }

    /// <summary>
    /// The setup documents must not recommend the conventional-but-wrong configuration spellings.
    /// </summary>
    [Fact]
    public void DocumentsDoNotRecommendKeysThisProjectDoesNotUse()
    {
        var root = RequireRepositoryRoot();
        var offences = new List<string>();

        foreach (var document in GovernedDocuments)
        {
            var path = Path.Combine(root, document);
            if (!File.Exists(path))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];

                // A line that WARNS about the wrong key necessarily contains it, so the mention
                // alone cannot be the signal — the surrounding sentence decides.
                //
                // The window is deliberately +/- one line rather than the line itself: markdown
                // wraps prose at the column, so "…`appsettings.Local.json` is read by nothing"
                // routinely puts the key on one line and the negation on the next. Judging a single
                // line failed on exactly that, flagging three correct warnings in GETTING_STARTED.md
                // the first time this gate ran.
                var context = string.Join(
                    ' ',
                    lines[Math.Max(0, index - 1)..Math.Min(lines.Length, index + 2)]);

                if (WarningVocabulary.IsMatch(context))
                {
                    continue;
                }

                foreach (var forbidden in KeysThatMustNotBeRecommended)
                {
                    if (line.Contains(forbidden, StringComparison.Ordinal))
                    {
                        offences.Add($"{document}:{index + 1}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            "The setup docs name a configuration spelling this project does not use, outside a " +
            "sentence warning against it. These read as correct because they are the ASP.NET " +
            "default, which is exactly why they went unnoticed for months (UAT-015). Offending lines:" +
            Environment.NewLine + string.Join(Environment.NewLine, offences));
    }

    /// <summary>
    /// Every key inside a JSON configuration example in the docs must exist in the source.
    /// </summary>
    /// <remarks>
    /// <para>The allow-list in <see cref="DocumentedConfigurationKeysExistInSource"/> proves the
    /// keys we MEANT to document are real; it cannot catch a key somebody invents. A JSON block in
    /// these documents is always a "put this in your settings file" instruction, so every property
    /// name in one is a claim about a key the application reads -- which makes the block the right
    /// place to look for invented keys without drowning in prose false-positives.</para>
    /// <para>This is what would have caught <c>ConnectionStrings</c> / <c>DefaultConnection</c> and
    /// the <c>AllowRegistration</c> setting, neither of which appears anywhere in the codebase
    /// (UAT-015).</para>
    /// </remarks>
    [Fact]
    public void KeysInJsonExamplesExistInSource()
    {
        var root = RequireRepositoryRoot();
        var sourceText = ReadTree(Path.Combine(root, "source"), IsCSharpFile)
            + ReadTree(Path.Combine(root, "source", "TechieBlog"), IsJsonFile);

        var invented = new List<string>();
        var inspected = 0;

        foreach (var document in GovernedDocuments)
        {
            var path = Path.Combine(root, document);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (Match block in Regex.Matches(
                File.ReadAllText(path), JsonBlockPattern, RegexOptions.Singleline))
            {
                foreach (Match property in Regex.Matches(block.Groups[1].Value, JsonPropertyPattern))
                {
                    var key = property.Groups[1].Value;
                    inspected++;

                    if (!sourceText.Contains(key, StringComparison.Ordinal))
                    {
                        invented.Add(document + ": " + key);
                    }
                }
            }
        }

        Assert.True(inspected >= 1,
            "Expected at least one JSON configuration example across the setup docs; inspected "
            + inspected + " keys. The extraction pattern has probably stopped matching.");

        Assert.True(
            invented.Count == 0,
            "These keys appear in a JSON settings example but nowhere in source/: "
            + string.Join(", ", invented.Distinct())
            + ". A reader will copy the block, set the value, and watch it do nothing.");
    }

    /// <summary>
    /// Every application route quoted in the setup documents must be served by a real page.
    /// </summary>
    /// <remarks>
    /// Routes are matched against the <c>@page</c> directives in <c>source/BlogUI</c> plus the
    /// endpoints mapped in the host. A parameterised route (<c>/post/{Slug}</c>) is matched on its
    /// literal prefix, because the documents quote concrete examples rather than templates.
    /// </remarks>
    [Fact]
    public void DocumentedRoutesExistInTheApplication()
    {
        var root = RequireRepositoryRoot();

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFiles(Path.Combine(root, "source"), IsRazorFile))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"@page\s+""(/[^""]*)"""))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        // Endpoints mapped in the host rather than declared by a page (health checks, feeds).
        //
        // Matched on an actual Map* CALL, not on the mere presence of the string. The first
        // version asked whether the host source contained "/register" anywhere -- and it does,
        // as a stale entry in a rate-limiting path list for a route retired months ago. That
        // made the gate certify a dead route as live, which is the exact bug it exists to
        // catch. Found by mutation-testing the gate, not by reading it.
        var hostText = ReadTree(Path.Combine(root, "source", "TechieBlog"), IsCSharpFile);
        foreach (Match match in Regex.Matches(hostText, MapEndpointPattern))
        {
            declared.Add(match.Groups[1].Value);
        }

        // Routes declared as a constant and mapped through it, e.g.
        //   public const string Path = "/healthz";
        //   app.MapHealthChecks(DeploymentHealthProbe.Path, ...)
        // The Map* call carries no literal, so following the indirection is the only way to
        // see the route. /healthz is real (Program.cs) and was reported missing until this
        // existed -- the gate was about to demand the removal of correct documentation.
        foreach (Match match in Regex.Matches(hostText, RouteConstantPattern))
        {
            declared.Add(match.Groups[1].Value);
        }

        Assert.SkipWhen(declared.Count == 0, "no @page directives found — cannot judge routes");

        // Excludes routes named only to say they DO NOT exist. GETTING_STARTED.md ends the
        // registration section with "`/register` does not exist." -- a sentence whose whole
        // job is to correct the very claim UAT-015 removed. Judging that as a claim would
        // force the guide to stop warning about it, which is the opposite of the point.
        var quoted = ExtractClaimsExcludingWarnings(root, RouteInDocs)
            // Shell paths and folder references share the leading-slash shape; only judge strings
            // that look like application routes, not file-system paths.
            .Where(candidate => !candidate.Contains('.') && candidate.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(quoted.Count >= 3,
            $"Expected the docs to quote several routes; found {quoted.Count}. " +
            "The extraction pattern has probably stopped matching.");

        var missing = quoted
            .Where(route => !declared.Contains(route)
                && !declared.Any(d => d.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These routes are quoted in the setup docs but no page declares them: " +
            string.Join(", ", missing) +
            ". /register survived in GETTING_STARTED.md for months after the route was retired (UAT-015).");
    }

    /// <summary>
    /// Every in-repository link in the setup documents must resolve to a file or folder that exists.
    /// </summary>
    [Fact]
    public void DocumentedRepositoryLinksResolve()
    {
        var root = RequireRepositoryRoot();
        var broken = new List<string>();
        var checkedLinks = 0;

        foreach (var document in GovernedDocuments)
        {
            var path = Path.Combine(root, document);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (Match match in RepoLinkInDocs.Matches(File.ReadAllText(path)))
            {
                var target = match.Groups[1].Value.Trim();
                if (target.Length == 0)
                {
                    continue;
                }

                checkedLinks++;
                var resolved = Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar))
                    .TrimEnd(Path.DirectorySeparatorChar);

                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    broken.Add($"{document} → {target}");
                }
            }
        }

        Assert.True(checkedLinks >= 5,
            $"Expected several in-repo links across the setup docs; found {checkedLinks}. " +
            "The extraction pattern has probably stopped matching.");

        Assert.True(
            broken.Count == 0,
            "These documentation links point at paths that do not exist: " +
            string.Join(", ", broken) +
            ". README.md linked docs/architecture.md — a file this repository has never had (UAT-014).");
    }

    /// <summary>
    /// Each pattern still matches the exact strings that were wrong, so a clean run means the
    /// documents are clean rather than that the patterns stopped working.
    /// </summary>
    /// <remarks>
    /// This is the REQ-NFR-041 lesson applied to documentation: an unmatchable regex reports zero
    /// violations and reads identically to a passing gate. Every literal below is a real string from
    /// the pre-2026-08-22 documents.
    /// </remarks>
    [Fact]
    public void EveryPatternMatchesItsPositiveControl()
    {
        Assert.True(
            ConfigKeyInDocs.IsMatch("The key is a top-level `AppDbConString` — not the usual one."),
            "ConfigKeyInDocs no longer matches a backticked configuration key.");

        Assert.True(
            ConfigKeyInDocs.Match("use `SiteSettings:BaseUrl` here").Groups[1].Value == "SiteSettings:BaseUrl",
            "ConfigKeyInDocs no longer captures a colon-separated key.");

        Assert.True(
            RouteInDocs.IsMatch("1. Go to `/register`"),
            "RouteInDocs no longer matches a backticked route — the exact claim UAT-015 removed.");

        Assert.True(
            RepoLinkInDocs.IsMatch("Read [Architecture](docs/architecture.md) for details."),
            "RepoLinkInDocs no longer matches a repo-relative markdown link — the exact broken link UAT-014 removed.");

        Assert.False(
            RepoLinkInDocs.IsMatch("See [dotnet](https://dotnet.microsoft.com/download)."),
            "RepoLinkInDocs must ignore external links, or every http link would be reported broken.");

        Assert.True(
            KeysThatMustNotBeRecommended.Any(k =>
                "  \"ConnectionStrings\": { \"DefaultConnection\": \"...\" }".Contains(k.Split(':')[0], StringComparison.Ordinal)),
            "The forbidden-key list no longer covers the ConnectionStrings spelling.");

        Assert.True(
            WarningVocabulary.IsMatch("a file called `appsettings.Local.json` is read by nothing"),
            "WarningVocabulary must recognise a warning, or every correct caveat is reported as a violation.");

        Assert.False(
            WarningVocabulary.IsMatch("Edit the file and set the value."),
            "WarningVocabulary must NOT fire on ordinary prose, or the gate exempts everything and passes vacuously.");

        Assert.True(
            Regex.IsMatch("app.MapHealthChecks(" + Quote + "/healthz" + Quote + ");", MapEndpointPattern),
            "MapEndpointPattern no longer matches a mapped endpoint.");

        Assert.False(
            Regex.IsMatch("public static readonly string[] Paths = { " + Quote + "/register" + Quote + " };", MapEndpointPattern),
            "MapEndpointPattern must NOT match a bare string literal -- a stale rate-limit entry for "
            + "/register once made this gate certify a retired route as live.");

        Assert.True(
            Regex.IsMatch("    public const string Path = " + Quote + "/healthz" + Quote + ";", RouteConstantPattern),
            "RouteConstantPattern no longer matches a route held in a constant -- /healthz is "
            + "mapped through one, and without this the gate reports correct docs as wrong.");

        Assert.True(
            Regex.IsMatch("```json\n{ " + Quote + "AllowRegistration" + Quote + ": true }\n```",
                JsonBlockPattern, RegexOptions.Singleline),
            "JsonBlockPattern no longer matches a fenced JSON example.");

        Assert.False(
            WarningVocabulary.IsMatch("Note that you should know the port."),
            "WarningVocabulary must use word boundaries — 'note' and 'know' contain 'no'.");
    }

    /// <summary>
    /// Pulls every capture of <paramref name="pattern"/> out of the governed documents, EXCEPT
    /// those appearing in a sentence that warns the reader off them.
    /// </summary>
    /// <remarks>
    /// Shares <see cref="WarningVocabulary"/> and the same +/- one line window as the
    /// forbidden-key check, for the same reason: markdown wraps a sentence across lines, so a
    /// single-line test misreads a caveat as a recommendation.
    /// </remarks>
    /// <param name="root">Repository root.</param>
    /// <param name="pattern">Extraction pattern whose first group is the claim.</param>
    /// <returns>The distinct claims the documents genuinely assert.</returns>
    private static HashSet<string> ExtractClaimsExcludingWarnings(string root, Regex pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in GovernedDocuments)
        {
            var path = Path.Combine(root, document);
            if (!File.Exists(path))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var context = string.Join(
                    ' ',
                    lines[Math.Max(0, index - 1)..Math.Min(lines.Length, index + 2)]);

                if (WarningVocabulary.IsMatch(context))
                {
                    continue;
                }

                foreach (Match match in pattern.Matches(lines[index]))
                {
                    found.Add(match.Groups[1].Value);
                }
            }
        }

        return found;
    }

    /// <summary>Pulls every capture of <paramref name="pattern"/> out of the governed documents.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="pattern">Extraction pattern whose first group is the claim.</param>
    /// <returns>The distinct claims found.</returns>
    private static HashSet<string> ExtractFromDocuments(string root, Regex pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var document in GovernedDocuments)
        {
            var path = Path.Combine(root, document);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                found.Add(match.Groups[1].Value);
            }
        }

        return found;
    }

    /// <summary>
    /// The part of a configuration key the code actually names.
    /// </summary>
    /// <remarks>
    /// <c>SiteSettings:BaseUrl</c> is read as the whole path in one place and bound section-wise in
    /// another, so the leaf is the portion guaranteed to appear verbatim in source.
    /// </remarks>
    /// <param name="key">The documented key.</param>
    /// <returns>Its last colon-separated segment.</returns>
    private static string LastSegment(string key) => key.Split(':')[^1];

    /// <summary>Concatenates every matching file under a tree.</summary>
    /// <param name="tree">Folder to read.</param>
    /// <param name="filter">Extension predicate.</param>
    /// <returns>The combined text, or an empty string when the tree is absent.</returns>
    private static string ReadTree(string tree, Func<string, bool> filter) =>
        !Directory.Exists(tree)
            ? string.Empty
            : string.Join('\n', EnumerateFiles(tree, filter).Select(File.ReadAllText));

    /// <summary>Lists the files under a tree, skipping build output and scratch areas.</summary>
    /// <param name="tree">Folder to walk.</param>
    /// <param name="filter">Extension predicate.</param>
    /// <returns>The files to read.</returns>
    private static IEnumerable<string> EnumerateFiles(string tree, Func<string, bool> filter)
    {
        if (!Directory.Exists(tree))
        {
            return [];
        }

        var separator = Path.DirectorySeparatorChar;
        string[] excluded =
        [
            $"{separator}bin{separator}",
            $"{separator}obj{separator}",
            $"{separator}.smokeout{separator}",
            $"{separator}.artifacts{separator}"
        ];

        return Directory
            .EnumerateFiles(tree, "*", SearchOption.AllDirectories)
            .Where(path => filter(path) && !excluded.Any(path.Contains));
    }

    private static bool IsCSharpFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static bool IsRazorFile(string path) =>
        path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);

    private static bool IsJsonFile(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves the repository root, skipping the test when it cannot be found.</summary>
    /// <returns>The repository root path.</returns>
    private static string RequireRepositoryRoot()
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

        Assert.Skip("repository root not found next to the test assembly");
        return string.Empty;
    }
}
