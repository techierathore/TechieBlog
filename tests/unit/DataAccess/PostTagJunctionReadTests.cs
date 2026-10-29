using BlogEngine.DbAccess;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Gates REQ-FN-021: a post's tags live in the <c>PostTag</c> junction, so search must read the
/// junction, and a save must keep the legacy <c>BlogPost.Tags</c> mirror in step with it.
/// </summary>
/// <remarks>
/// Reads SQL text through <see cref="SqlStatementInventory"/>; no database is touched. The
/// behavioural proof is the Playwright test in <c>tests/verify/fxv1-public.spec.ts</c>.
/// </remarks>
public class PostTagJunctionReadTests
{
    private static string Sql(Type repository, string name)
        => SqlStatementInventory.Normalise(SqlStatementInventory.Statements(repository)[name]);

    /// <summary>
    /// [REQ-FN-021] Both the search listing and its COUNT match a tag name through the junction.
    /// </summary>
    /// <param name="statementName">The search statement under test.</param>
    [Theory(DisplayName = "REQ-FN-021 search matches tags through the PostTag junction")]
    [InlineData("SearchSql")]
    [InlineData("SearchCountSql")]
    public void SearchMatchesJunctionTags(string statementName)
    {
        // Arrange
        var sql = Sql(typeof(BlogPostRepo), statementName);

        // Act
        var readsJunction = sql.Contains("FROM PostTag", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("t.TagName ILIKE @Query", StringComparison.OrdinalIgnoreCase);

        // Assert
        Assert.True(readsJunction, $"{statementName} must match @Query against Tag.TagName via PostTag, not only BlogPost.Tags.");
    }

    /// <summary>
    /// [REQ-FN-021] Replacing a post's tag set rewrites the legacy mirror in the same transaction.
    /// </summary>
    [Fact(DisplayName = "REQ-FN-021 saving tags keeps the legacy Tags column in sync")]
    public void SettingTagsSyncsTheLegacyColumn()
    {
        // Arrange
        var sql = Sql(typeof(BlogTagRepo), "SyncLegacyTagsSql");

        // Act + Assert
        Assert.Contains("UPDATE BlogPost SET Tags", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string_agg", sql, StringComparison.OrdinalIgnoreCase);
    }
}
