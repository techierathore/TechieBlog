using System.Text;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Gates REQ-UI-059: a public listing must sort by the same expression it dates its rows by, and a
/// paged one must break ties on a unique key so its pages stay disjoint.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-057 moved every public renderer onto <c>PublishedOn ?? CreatedOn</c>
/// and widened the four public read projections to carry <c>PublishedOn</c>. What it did not touch was
/// the <c>ORDER BY p.CreatedOn DESC</c> sitting on those same statements, so from that moment a card's
/// printed date and its position in the list came from two DIFFERENT columns. The visible result is
/// recorded in <c>tests/.artifacts/e-tag-archive-date-desktop.png</c>: a post dated "Aug 09" sitting
/// third, behind cards dated Jul 08 and Jul 01. Nothing threw, nothing logged, and no existing test
/// could see it — the projection gates assert on columns, and the column list was already correct.</para>
///
/// <para><b>Why the tiebreaker is gated as hard as the sort key:</b> <c>COALESCE</c> ties are the
/// normal case, not the corner case — a post published the moment it was written has
/// <c>PublishedOn = CreatedOn</c>, and seeded batches land several rows on the same instant.
/// PostgreSQL may return tied rows in any order, and it is free to choose a DIFFERENT order for the
/// <c>OFFSET 0</c> execution than for the <c>OFFSET 9</c> one, because they are separate statements
/// with separate plans. A visitor then sees the same post twice across two pages while another post is
/// never shown at all. Appending the primary key makes the ordering total, which makes it repeatable,
/// which is what LIMIT/OFFSET paging silently assumes.</para>
///
/// <para><b>Dependencies:</b> reflection over the built <c>BlogEngine</c> assembly through
/// <see cref="SqlStatementInventory"/>. No database, no host, no container — this reads SQL text, it
/// does not execute it.</para>
///
/// <para><b>Usage:</b> when a new public listing is added, add its statement to
/// <see cref="PublicPostListings"/>. <see cref="NoPublishedPostReadSortsByCreatedOn"/> is self-wiring
/// and will catch it even if that is forgotten, so long as it filters on <c>Published = TRUE</c>.</para>
/// </remarks>
public class PublicListingSortTests
{
    /// <summary>
    /// The expression every public listing must sort by, normalised the way
    /// <see cref="SqlStatementInventory.Normalise"/> renders it.
    /// </summary>
    private const string RequiredSortKey = "COALESCE(P.PUBLISHEDON, P.CREATEDON) DESC";

    /// <summary>
    /// The unique tiebreaker that makes the ordering total, and therefore repeatable across pages.
    /// </summary>
    private const string RequiredTiebreaker = "P.POSTID DESC";

    /// <summary>
    /// Every statement that lists posts to an anonymous visitor: the home listing and the RSS/sitemap
    /// feed behind it, the home hero card, the category archive, search, and the tag archive.
    /// </summary>
    /// <remarks>
    /// The series listings are deliberately absent. <c>/series/{slug}</c> orders by
    /// <c>SeriesPartNumber</c> because a serialised article is read part 1 first whatever its dates
    /// say, and the page prints the part number next to each row rather than a date. The ADMIN reads
    /// (<c>SelectAllSql</c>, <c>SelectAllByUserSql</c>, <c>SelectPagedSql</c>) are absent for the
    /// opposite reason: they include drafts, whose <c>PublishedOn</c> is <c>NULL</c>, so authoring
    /// order is the only order that carries meaning there.
    /// </remarks>
    /// <returns>Repository name and statement name for each public listing.</returns>
    public static TheoryData<string, string> PublicPostListings()
    {
        return new TheoryData<string, string>
        {
            { "BlogPostRepo", "SelectPublishedSql" },
            { "BlogPostRepo", "SelectFeaturedSql" },
            { "BlogPostRepo", "SelectByCategorySql" },
            { "BlogPostRepo", "SearchSql" },
            { "BlogTagRepo", "SelectPostsByTagSql" },
        };
    }

    /// <summary>
    /// Every public listing orders by <c>COALESCE(PublishedOn, CreatedOn) DESC</c> — the expression
    /// its own renderer prints on each card — so a back-dated or long-drafted post takes the list
    /// position its visible date claims.
    /// </summary>
    /// <param name="repositoryName">The repository declaring the statement.</param>
    /// <param name="statementName">The SQL constant under test.</param>
    [Theory]
    [MemberData(nameof(PublicPostListings))]
    public void PublicListingSortsByTheColumnItDatesBy(string repositoryName, string statementName)
    {
        // Arrange
        var orderBy = OrderByClause(repositoryName, statementName);

        // Act
        var sortsByTheRenderedDate = orderBy.Contains(RequiredSortKey, StringComparison.Ordinal);

        // Assert
        Assert.True(
            sortsByTheRenderedDate,
            $"{repositoryName}.{statementName} orders by '{orderBy}' but its renderer dates every row as "
            + "PublishedOn ?? CreatedOn. Dating by one column and sorting by another is not a cosmetic "
            + $"mismatch — it puts a post's card in a position its own printed date contradicts. Order by '{RequiredSortKey}'.");
    }

    /// <summary>
    /// Every public listing breaks ties on the primary key, so two rows sharing a publication instant
    /// cannot swap places between one page's query and the next.
    /// </summary>
    /// <remarks>
    /// The single-row featured read is held to the same rule for the same reason: with ties in the
    /// sort key and no tiebreaker, which post becomes the home page hero is decided by the plan rather
    /// than by the data, and can change between two renders of the same page.
    /// </remarks>
    /// <param name="repositoryName">The repository declaring the statement.</param>
    /// <param name="statementName">The SQL constant under test.</param>
    [Theory]
    [MemberData(nameof(PublicPostListings))]
    public void PublicListingBreaksTiesOnAUniqueKey(string repositoryName, string statementName)
    {
        // Arrange
        var orderBy = OrderByClause(repositoryName, statementName);

        // Act
        var isTotalOrder = orderBy.Contains(RequiredTiebreaker, StringComparison.Ordinal);

        // Assert
        Assert.True(
            isTotalOrder,
            $"{repositoryName}.{statementName} orders by '{orderBy}' with no unique tiebreaker. Posts published "
            + "in the same instant tie, PostgreSQL may return tied rows in any order, and it decides that order "
            + "separately for each OFFSET — so the same post can appear on page 1 and page 2 while another is "
            + $"never shown at all. Append '{RequiredTiebreaker}'.");
    }

    /// <summary>
    /// No read anywhere in <c>BlogEngine.DbAccess</c> that restricts itself to published posts sorts
    /// by <c>CreatedOn</c>, so a public listing added tomorrow inherits REQ-UI-059 without an edit here.
    /// </summary>
    /// <remarks>
    /// The <c>Published = TRUE</c> filter is what makes a statement public-facing, and a public-facing
    /// statement has no business ranking rows by when somebody started drafting them. The check looks
    /// at the ORDER BY only after every <c>COALESCE(…)</c> has been removed from it, so the required
    /// sort key — which legitimately names <c>CreatedOn</c> as its fallback — reads as compliant while
    /// a bare <c>p.CreatedOn DESC</c> does not.
    /// </remarks>
    [Fact]
    public void NoPublishedPostReadSortsByCreatedOn()
    {
        // Arrange
        var failures = new StringBuilder();

        // Act
        foreach (var repository in SqlStatementInventory.RepositoryTypes())
        {
            foreach (var (name, sql) in SqlStatementInventory.Statements(repository))
            {
                if (!SqlStatementInventory.FiltersOnPublished(sql))
                    continue;

                var orderBy = WithoutCoalesce(OrderByClause(sql));

                if (orderBy.Contains("CREATEDON", StringComparison.Ordinal))
                    failures.AppendLine($"  {repository.Name}.{name} orders by '{OrderByClause(sql)}'.");
            }
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"These published-post reads still rank rows by their creation date:{Environment.NewLine}{failures}"
            + "Every public renderer dates a post as PublishedOn ?? CreatedOn (REQ-FN-057), so sorting by CreatedOn "
            + "puts cards in an order their own printed dates contradict — the REQ-UI-059 defect. Sort by "
            + $"'{RequiredSortKey}, {RequiredTiebreaker}'. If a listing genuinely must rank by authoring order, it is "
            + "an admin read and should not carry a Published filter.");
    }

    /// <summary>
    /// The ORDER BY clause of a named statement, normalised and stripped of any trailing LIMIT.
    /// </summary>
    /// <param name="repositoryName">The repository declaring the statement.</param>
    /// <param name="statementName">The SQL constant to read.</param>
    /// <returns>The clause text, or an empty string when the statement has no ORDER BY.</returns>
    private static string OrderByClause(string repositoryName, string statementName)
    {
        var repository = SqlStatementInventory.RepositoryTypes()
            .SingleOrDefault(candidate => candidate.Name == repositoryName)
            ?? throw new InvalidOperationException(
                $"{repositoryName} was not found in {SqlStatementInventory.RepositoryNamespace}.");

        var statements = SqlStatementInventory.Statements(repository);

        if (!statements.TryGetValue(statementName, out var sql))
            throw new InvalidOperationException(
                $"{repositoryName} no longer declares {statementName}. If the statement was renamed, rename it in "
                + $"{nameof(PublicPostListings)} too — a public listing that falls out of this gate is a public "
                + "listing whose sort order nothing checks.");

        return OrderByClause(sql);
    }

    /// <summary>
    /// The ORDER BY clause of a statement, normalised and stripped of any trailing LIMIT.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>The clause text, or an empty string when the statement has no ORDER BY.</returns>
    private static string OrderByClause(string sql)
    {
        var normalised = SqlStatementInventory.Normalise(sql);
        var start = normalised.IndexOf("ORDER BY ", StringComparison.Ordinal);

        if (start < 0)
            return string.Empty;

        var clause = normalised[(start + "ORDER BY ".Length)..];
        var limit = clause.IndexOf(" LIMIT ", StringComparison.Ordinal);

        return (limit < 0 ? clause : clause[..limit]).Trim();
    }

    /// <summary>
    /// The clause with every balanced <c>COALESCE(…)</c> call removed, so the columns a fallback
    /// expression names cannot be mistaken for a bare sort on those columns.
    /// </summary>
    /// <param name="clause">The normalised ORDER BY clause.</param>
    /// <returns>The clause without its COALESCE calls.</returns>
    private static string WithoutCoalesce(string clause)
    {
        var stripped = new StringBuilder();
        var depth = 0;
        var index = 0;

        while (index < clause.Length)
        {
            if (depth == 0 && string.CompareOrdinal(clause, index, "COALESCE(", 0, 9) == 0)
            {
                depth = 1;
                index += 9;
                continue;
            }

            if (depth > 0)
            {
                if (clause[index] == '(')
                    depth++;
                else if (clause[index] == ')')
                    depth--;

                index++;
                continue;
            }

            stripped.Append(clause[index]);
            index++;
        }

        return stripped.ToString();
    }
}
