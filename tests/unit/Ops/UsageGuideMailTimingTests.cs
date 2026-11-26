using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// The usage guide's "Set up mail" section says when a mail change takes effect (REQ-NFR-046, owner
/// UAT 2026-11-26).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the section used to say only "a change takes effect on the next mail", which is
/// true for a save in the website's admin but not for a save from BlogApp, which reaches the live site
/// within ten minutes. The owner asked for both, plus a link to the Brevo set-up guide.</para>
/// <para><b>Dependencies:</b> the repository layout (<see cref="RepositoryLocator"/>); skipped outside a
/// checkout.</para>
/// </remarks>
public class UsageGuideMailTimingTests
{
    /// <summary>
    /// The section names both save places with their timing and links docs/Brevo-Mail-Setup.md.
    /// </summary>
    /// <remarks>
    /// Scenario: the owner reads "Set up mail" in docs/TechieBlog-UsageGuide.md; it says a save in the
    /// website's admin is used by the next mail, a save in BlogApp reaches the live site within ten
    /// minutes, and it links the Brevo guide, which exists.
    /// </remarks>
    [Fact(DisplayName = "REQ-NFR-046 Set up mail says when a change takes effect and links the Brevo guide")]
    public void SetUpMailStatesBothTimingsAndLinksBrevo()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");

        var guide = File.ReadAllText(Path.Combine(root!, "docs", "TechieBlog-UsageGuide.md"));
        var start = guide.IndexOf("### Set up mail", StringComparison.Ordinal);
        Assert.True(start >= 0, "the usage guide has no \"Set up mail\" section");
        var end = guide.IndexOf("\n#", start + 5, StringComparison.Ordinal);
        var section = end < 0 ? guide[start..] : guide[start..end];

        Assert.Contains("Saved in the website's admin:** the next mail uses it", section, StringComparison.Ordinal);
        Assert.Contains("Saved in BlogApp:** the live site picks it up within ten minutes", section, StringComparison.Ordinal);
        Assert.Contains("(Brevo-Mail-Setup.md)", section, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root!, "docs", "Brevo-Mail-Setup.md")), "docs/Brevo-Mail-Setup.md is missing");
    }
}
