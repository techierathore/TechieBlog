using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// REQ-NFR-025 — no <c>nuget.config</c> in the repository holds a credential literal.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> a personal access token was once committed in <c>nuget.config</c> and
/// published to every clone. Revoking it on GitHub is an owner action; this gate keeps the repository
/// half true by failing the build if a credential ever returns.</para>
/// <para><b>Code Flow:</b> find every file named <c>nuget.config</c> (any case) outside build output
/// → parse it as XML and fail on a <c>packageSourceCredentials</c> element or a <c>Password</c> /
/// <c>ClearTextPassword</c> entry → also scan the raw text for GitHub token shapes (comments too).</para>
/// <para><b>The self-test is the point:</b> each detector runs on a synthetic credential first.</para>
/// </remarks>
public class NuGetConfigCredentialTests
{
    private static readonly Regex TokenShape = new(
        @"\b(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}", RegexOptions.Compiled);

    /// <summary>
    /// The detectors flag a synthetic credential block and a synthetic token.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-025 the credential detectors match a synthetic credential")]
    public void DetectorsMatchPositiveControl()
    {
        var control = "<configuration><packageSourceCredentials><TrBlazeUI>"
            + "<add key=\"ClearTextPassword\" value=\"x\" /></TrBlazeUI></packageSourceCredentials></configuration>";
        Assert.NotEmpty(FindCredentialEntries(XDocument.Parse(control)));
        Assert.Matches(TokenShape, "ghp_" + new string('a', 36));
        Assert.Matches(TokenShape, "github_pat_" + new string('B', 40));
    }

    /// <summary>
    /// No NuGet config file in the repository carries a credential.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-025 no NuGet config in the repository holds a credential literal")]
    public void NoNuGetConfigHoldsACredential()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");

        var files = EnumerateConfigs(root!).ToList();
        Assert.True(files.Count > 0, "expected at least the root NuGet.Config to be found");

        var problems = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root!, file);
            var text = File.ReadAllText(file);
            problems.AddRange(FindCredentialEntries(XDocument.Parse(text)).Select(e => $"{relative}: {e}"));
            if (TokenShape.IsMatch(text))
            {
                problems.Add($"{relative}: contains a GitHub token literal");
            }
        }

        Assert.True(problems.Count == 0, "REQ-NFR-025: credential found in a NuGet config:\n" + string.Join("\n", problems));
    }

    private static IEnumerable<string> FindCredentialEntries(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (element.Name.LocalName.Equals("packageSourceCredentials", StringComparison.OrdinalIgnoreCase))
            {
                yield return "<packageSourceCredentials> element";
            }

            var key = (string?)element.Attribute("key");
            if (key is not null
                && (key.Equals("ClearTextPassword", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("Password", StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"'{key}' entry";
            }
        }
    }

    private static IEnumerable<string> EnumerateConfigs(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*.config", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file).Equals("nuget.config", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(sub);
            if (name is "bin" or "obj" or "node_modules" or ".git" or ".artifacts" or ".nuget" or ".smokeout"
                || name.StartsWith('.'))
            {
                continue;
            }

            foreach (var file in EnumerateConfigs(sub))
            {
                yield return file;
            }
        }
    }
}
