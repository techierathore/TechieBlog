using System.Reflection;
using System.Text.RegularExpressions;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// REQ-FN-063 — only confirmed ratings count anywhere, and a rater's email reaches a staff screen
/// only already masked.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The owner saw a 5-star rating on Analytics that no other screen showed:
/// Analytics averaged unconfirmed ratings. These tests pin the fix (every rating aggregate in
/// <see cref="AnalyticsRepo"/> filters on the confirmed flag) and the staff ratings list (masked in
/// the service, no email property on the row model).</para>
/// <para><b>Code Flow:</b> the SQL check reads the repository's private SQL constants by reflection
/// and carries its own positive control; the service tests drive the real <see cref="RatingSvc"/>
/// over the in-memory rating repository.</para>
/// <para><b>Dependencies:</b> xUnit and the fakes in this folder.</para>
/// <para><b>Usage:</b> Pure unit tests — no database.</para>
/// </remarks>
public class StaffRatingsTests
{
    private static readonly Regex RatingSubSelect = new(
        @"FROM\s+PostRating\s+r\s+WHERE(?<where>[^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// REQ-FN-063: every PostRating sub-select in AnalyticsRepo's SQL restricts to confirmed
    /// ratings, so Analytics can never again count a rating the post page does not.
    /// </summary>
    [Fact]
    public void AnalyticsRatingAggregatesCountConfirmedOnly()
    {
        var sqlTexts = typeof(AnalyticsRepo)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        var subSelects = sqlTexts.SelectMany(sql => RatingSubSelect.Matches(sql)).ToList();

        Assert.NotEmpty(subSelects);
        Assert.All(subSelects, match => Assert.True(IsConfirmedOnly(match.Groups["where"].Value), match.Value));
    }

    /// <summary>
    /// REQ-FN-063 positive control: the same check rejects the sub-select shape that shipped
    /// before the fix, so a passing run above is evidence rather than a pattern that matches nothing.
    /// </summary>
    [Fact]
    public void ConfirmedOnlyCheckRejectsUnfilteredSubSelect()
    {
        const string before = "(SELECT COUNT(*)::int FROM PostRating r WHERE r.PostId = p.PostId) AS RatingCount";

        var match = RatingSubSelect.Match(before);

        Assert.True(match.Success);
        Assert.False(IsConfirmedOnly(match.Groups["where"].Value));
    }

    /// <summary>
    /// REQ-FN-063: the staff row model has no property that can hold an email address, so a full
    /// address cannot reach a Razor component through it.
    /// </summary>
    [Fact]
    public void StaffRatingRowCarriesNoEmailProperty()
    {
        var names = typeof(StaffRatingRow).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(names, name => name.Equals("Email", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nameof(StaffRatingRow.MaskedEmail), names);
    }

    /// <summary>
    /// REQ-FN-063 / REQ-UI-067: the staff list returns every rating, confirmed or not, newest first,
    /// with the rater reduced to first letter and domain.
    /// </summary>
    [Fact]
    public async Task StaffListMasksEveryRater()
    {
        var repo = new FakePostRatingRepo();
        repo.InsertToGetId(new PostRating { PostId = 7, Email = "ravi@gmail.com", Rating = 5, IsEmailVerified = true, CreatedOn = new DateTime(2026, 11, 1) });
        repo.InsertToGetId(new PostRating { PostId = 7, Email = "Asha.K@Outlook.com", Rating = 2, IsEmailVerified = false, CreatedOn = new DateTime(2026, 11, 9) });
        repo.InsertToGetId(new PostRating { PostId = 8, Email = "other@example.org", Rating = 4, IsEmailVerified = true, CreatedOn = new DateTime(2026, 11, 3) });

        var result = await BuildService(repo).GetRatingsForStaffAsync(7, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Data,
            newest => { Assert.Equal("a•••@outlook.com", newest.MaskedEmail); Assert.False(newest.IsConfirmed); },
            oldest => { Assert.Equal("r•••@gmail.com", oldest.MaskedEmail); Assert.True(oldest.IsConfirmed); });
    }

    /// <summary>
    /// REQ-UI-068 / REQ-FN-063: the Posts list summary counts confirmed ratings only.
    /// </summary>
    [Fact]
    public async Task PostsListStatsCountConfirmedOnly()
    {
        var repo = new FakePostRatingRepo();
        repo.InsertToGetId(new PostRating { PostId = 7, Email = "a@x.org", Rating = 4, IsEmailVerified = true });
        repo.InsertToGetId(new PostRating { PostId = 7, Email = "b@x.org", Rating = 1, IsEmailVerified = false });

        var stats = await BuildService(repo).GetStatsForPostsAsync(new long[] { 7, 9 }, TestContext.Current.CancellationToken);

        var post = Assert.Single(stats);
        Assert.Equal(7, post.Key);
        Assert.Equal(1, post.Value.RatingCount);
        Assert.Equal(4d, post.Value.AverageRating);
    }

    /// <summary>
    /// REQ-FN-063: masking keeps only the first letter and the domain, and never echoes a malformed
    /// address.
    /// </summary>
    [Theory]
    [InlineData("ravi@gmail.com", "r•••@gmail.com")]
    [InlineData("  Z@Example.ORG ", "z•••@example.org")]
    [InlineData("no-at-sign", "•••")]
    [InlineData("@domain.com", "•••")]
    [InlineData("name@", "•••")]
    [InlineData("", "•••")]
    public void EmailMaskerKeepsFirstLetterAndDomain(string input, string expected)
    {
        Assert.Equal(expected, EmailMasker.Mask(input));
    }

    private static bool IsConfirmedOnly(string whereClause) =>
        whereClause.Contains("IsEmailVerified = TRUE", StringComparison.OrdinalIgnoreCase);

    private static RatingSvc BuildService(FakePostRatingRepo repo)
    {
        var verification = new EmailVerificationSvc(
            new FakeEmailVerificationTokenRepo(),
            new FakeVerifiedEmailRepo(),
            new FakeBlogCommentRepo(),
            repo,
            new FakeSubscriberRepo(),
            new FakeVerificationEmailSender(),
            new FakeConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = "https://blog.example" }),
            new FakeSiteSettingsService(),
            NullLogger<EmailVerificationSvc>.Instance);
        return new RatingSvc(repo, new FakeCaptchaService(), verification, NullLogger<RatingSvc>.Instance);
    }
}
