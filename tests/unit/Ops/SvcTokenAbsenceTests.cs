using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// REQ-FN-052 — no source file may reference the retired token table, because no migration ever
/// created it and any statement naming it would fail with a missing-relation error.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the data-access stack for that table was deleted as dead code; this gate
/// keeps it deleted. The acceptance line reads "no source file references it", so comments count too.</para>
///
/// <para><b>Code Flow:</b> walk up to the repository root → enumerate <c>*.cs</c>, <c>*.razor</c>,
/// <c>*.sql</c>, <c>*.json</c> and <c>*.cshtml</c> under <c>source/</c> (skipping <c>bin</c>,
/// <c>obj</c> and <c>wwwroot</c>) → fail naming every file and line that matches.</para>
///
/// <para><b>The self-test is the point:</b> <see cref="PatternMatchesPositiveControl"/> proves the regex
/// can match, so a zero from the scan is a real zero (see the history in
/// <see cref="SourceConventionTests"/>).</para>
/// </remarks>
public class SvcTokenAbsenceTests
{
    /// <summary>The retired relation's name, case-insensitive, with or without a type-name suffix.</summary>
    private static readonly Regex RetiredTable = new("svctoken", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The scan pattern must match a hand-written reference, assembled from fragments so this file
    /// does not itself contain the retired name.
    /// </summary>
    [Fact]
    public void PatternMatchesPositiveControl()
    {
        var control = "SELECT * FROM " + "svc" + "token";
        Assert.Matches(RetiredTable, control);
    }

    /// <summary>
    /// Scans every source file for the retired relation name.
    /// </summary>
    [Fact]
    public void NoSourceFileReferencesTheRetiredTokenTable()
    {
        var root = FindRepositoryRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");
        var tree = Path.Combine(root!, "source");
        Assert.SkipWhen(!Directory.Exists(tree), "source/ not found next to the test assembly");

        var hits = new List<string>();
        foreach (var file in Enumerate(tree))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (RetiredTable.IsMatch(lines[index]))
                {
                    hits.Add($"{Path.GetRelativePath(root!, file)}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        Assert.True(hits.Count == 0, "REQ-FN-052: the retired token table must not be referenced:\n" + string.Join("\n", hits));
    }

    /// <summary>Enumerates scannable files, pruning build output and static web assets.</summary>
    private static IEnumerable<string> Enumerate(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".cs" or ".razor" or ".sql" or ".json" or ".cshtml")
            {
                yield return file;
            }
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(sub);
            if (name is "bin" or "obj" or "wwwroot" or "node_modules")
            {
                continue;
            }

            foreach (var file in Enumerate(sub))
            {
                yield return file;
            }
        }
    }

    /// <summary>Walks up from the test assembly to the repository root.</summary>
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
