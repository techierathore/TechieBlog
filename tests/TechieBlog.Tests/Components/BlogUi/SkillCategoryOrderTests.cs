using Bunit;
using BlogModels.Interfaces;
using BlogModels.Models;
using BlogUI.Components.Resume;
using BlogUI.Pages.AdminPages;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// Pins the CATEGORY ordering that <c>/admin/skills</c> and the public resume share (REQ-UI-064).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owner UAT (2026-08-22) reported "there is no way to change the order of
/// skills". The per-skill Move up / Move down buttons existed and worked, but both surfaces ordered
/// the CATEGORIES alphabetically — and no sequence of per-skill moves can carry a skill past its
/// category boundary. The author's intent was already in the <c>DisplayOrder</c> column; neither
/// surface read it at the category level.</para>
///
/// <para><b>What is pinned, and why each part earns its place:</b> that a category's position comes
/// from the LOWEST <c>DisplayOrder</c> it contains rather than from its name; that the name is still
/// the tie-break, so equal numbers give a stable order instead of the repository's row order; that a
/// null category groups under one visible heading rather than vanishing; and — the test that matters
/// most — that the PUBLIC resume renders the same sequence the admin screen computes. Those two
/// orderings live in different files and are the exact pair that drifted before, so this suite
/// renders the real component and compares it against the real admin helper rather than restating
/// the rule twice.</para>
///
/// <para><b>Dependencies:</b> BlogUI and therefore TrBlazeUI; this suite compiles only under
/// <c>-p:IncludeBlogUiTests=true</c>, which is the default.</para>
/// </remarks>
public class SkillCategoryOrderTests : BunitContext
{
    /// <summary>Owner whose skills every case in this suite belongs to.</summary>
    private const long OwnerId = 1;

    /// <summary>
    /// Builds a skill.
    /// </summary>
    /// <param name="skillId">Identity, used only to keep the rows distinguishable.</param>
    /// <param name="name">Skill name.</param>
    /// <param name="category">Owning category, or <c>null</c> to exercise the ungrouped case.</param>
    /// <param name="displayOrder">The author's position for this skill.</param>
    /// <returns>The skill.</returns>
    private static UserSkill Skill(long skillId, string name, string? category, int displayOrder) => new()
    {
        SkillId = skillId,
        UserId = OwnerId,
        SkillName = name,
        Category = category!,
        DisplayOrder = displayOrder
    };

    /// <summary>
    /// The arrangement owner UAT was looking at: an authored sequence whose categories are NOT in
    /// alphabetical order, so an alphabetical implementation cannot pass by coincidence.
    /// </summary>
    /// <returns>Skills in no particular order, as a repository would hand them over.</returns>
    private static List<UserSkill> AuthoredArrangement() =>
    [
        Skill(3, "TypeScript", "Languages", 3),
        Skill(9, "GitHub Actions", "Cloud and DevOps", 11),
        Skill(1, "C#", "Languages", 1),
        Skill(7, "Dapper", "Frameworks", 6),
        Skill(12, "Azure", "Cloud and DevOps", 9),
        Skill(2, "SQL", "Languages", 2),
        Skill(10, "ASP.NET Core", "Frameworks", 4),
        Skill(11, "Docker", "Cloud and DevOps", 10),
        Skill(5, "Blazor", "Frameworks", 5)
    ];

    /// <summary>
    /// A category's position follows the lowest display order it contains, not its name.
    /// </summary>
    /// <remarks>
    /// Alphabetically this data reads Cloud and DevOps, Frameworks, Languages — the order the screen
    /// actually rendered before this REQ, and the reason the owner could not put Languages first.
    /// </remarks>
    [Fact]
    public void CategoriesFollowTheLowestDisplayOrderTheyContain()
    {
        // Arrange, Act
        var ordered = ManageSkills.OrderCategories(AuthoredArrangement()).Select(g => g.Key).ToList();

        // Assert
        Assert.Equal(["Languages", "Frameworks", "Cloud and DevOps"], ordered);
        Assert.NotEqual(ordered.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), ordered);
    }

    /// <summary>
    /// Categories whose lowest display order is equal fall back to name order, case-insensitively.
    /// </summary>
    /// <remarks>
    /// Without a second key the order would be whatever the repository happened to return, which
    /// makes the screen appear to shuffle itself between visits.
    /// </remarks>
    [Fact]
    public void EqualDisplayOrdersFallBackToNameOrder()
    {
        // Arrange
        var tied = new List<UserSkill>
        {
            Skill(1, "Kubernetes", "platform", 4),
            Skill(2, "Blazor", "Frameworks", 4),
            Skill(3, "C#", "Languages", 4)
        };

        // Act
        var ordered = ManageSkills.OrderCategories(tied).Select(g => g.Key).ToList();

        // Assert
        Assert.Equal(["Frameworks", "Languages", "platform"], ordered);
    }

    /// <summary>
    /// A skill with no category of its own is grouped rather than dropped.
    /// </summary>
    [Fact]
    public void SkillsWithNoCategoryAreGroupedUnderOneHeading()
    {
        // Arrange
        var mixed = new List<UserSkill>
        {
            Skill(1, "C#", "Languages", 2),
            Skill(2, "Curiosity", null, 1),
            Skill(3, "Patience", null, 3)
        };

        // Act
        var ordered = ManageSkills.OrderCategories(mixed).ToList();

        // Assert
        Assert.Equal(["Uncategorized", "Languages"], ordered.Select(g => g.Key));
        Assert.Equal(2, ordered.First(g => g.Key == "Uncategorized").Count());
    }

    /// <summary>
    /// An empty or null skill set produces no groups rather than throwing.
    /// </summary>
    [Fact]
    public void NoSkillsProducesNoCategories()
    {
        // Arrange, Act, Assert
        Assert.Empty(ManageSkills.OrderCategories(null));
        Assert.Empty(ManageSkills.OrderCategories([]));
    }

    /// <summary>
    /// The PUBLIC resume renders categories in exactly the order the admin screen computes.
    /// </summary>
    /// <remarks>
    /// The regression this suite exists for. Both orderings are written out in full in their own
    /// files, so restating the rule a third time here would prove nothing; instead the real
    /// component is rendered and its headings are compared against the real admin helper. If either
    /// side is changed alone, an author arranges one order and a visitor sees another, and this
    /// fails.
    /// </remarks>
    [Fact]
    public void PublicResumeRendersTheSameCategoryOrderAsTheAdminScreen()
    {
        // Arrange
        var skills = AuthoredArrangement();
        var repo = Substitute.For<IUserSkillsRepo>();
        repo.GetByUserIdAsync(OwnerId).Returns(skills);
        Services.AddSingleton(repo);

        // Act
        var cut = Render<ResumeSkills>(parameters => parameters.Add(section => section.UserId, OwnerId));
        var rendered = cut.FindAll("[data-testid='skill-category-name']").Select(node => node.TextContent.Trim());

        // Assert
        Assert.Equal(ManageSkills.OrderCategories(skills).Select(g => g.Key), rendered);
    }

    /// <summary>
    /// Skills inside a category still render in their own display order.
    /// </summary>
    /// <remarks>
    /// Category ordering was added on top of the per-skill ordering, not in place of it — this is
    /// the guard that the new outer key did not disturb the inner one.
    /// </remarks>
    [Fact]
    public void SkillsWithinACategoryKeepTheirDisplayOrder()
    {
        // Arrange
        var repo = Substitute.For<IUserSkillsRepo>();
        repo.GetByUserIdAsync(OwnerId).Returns(AuthoredArrangement());
        Services.AddSingleton(repo);

        // Act
        var cut = Render<ResumeSkills>(parameters => parameters.Add(section => section.UserId, OwnerId));
        var firstCategory = cut.FindAll("[data-testid='skill-category']").First();
        var names = firstCategory.QuerySelectorAll("[data-testid='skill-badge']")
            .Select(node => node.TextContent.Trim());

        // Assert
        Assert.Equal(["C#", "SQL", "TypeScript"], names);
    }
}
