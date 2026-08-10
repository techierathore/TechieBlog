using System.Reflection;
using BlogEngine.DbAccess;

namespace TechieBlog.Tests.DbAccess;

/// <summary>
/// Guards the SQL projections in <see cref="BlogPostRepo"/> and <see cref="BlogSeriesRepo"/> against
/// projection drift.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-015. A public listing query and the COUNT query that pages or badges
/// it must agree about what a row is; when they drift, the page shows rows the count never counted.
/// That is exactly how <c>/series/{slug}</c> came to list every unpublished part of a series to
/// anonymous visitors — <c>SelectBySeriesSql</c> filtered on soft-deletion alone while its sibling
/// <c>CountBySeriesSql</c> also required <c>Published = TRUE</c>. Neither the compiler nor a
/// behavioural unit test can see that: both statements compile, both run, and the mismatch shows up
/// only as extra rows on a public page.</para>
///
/// <para><b>Approach:</b> The statements are <c>private const string</c> fields, so they are read by
/// reflection and asserted as text. Text assertions are deliberately coarse — they check that a
/// filter is PRESENT, not that the whole statement matches a golden copy — so ordinary edits to a
/// projection do not break the suite while removing a security filter does.</para>
///
/// <para><b>Dependencies:</b> Reflection over the two repository types. No database, no container.</para>
///
/// <para><b>When you add a public listing query</b>, add it to
/// <see cref="PublicListingStatementNames"/> so it is covered from the day it is written.</para>
/// </remarks>
public class BlogPostRepoProjectionTests
{
    /// <summary>
    /// Every statement in <see cref="BlogPostRepo"/> that feeds a page an anonymous visitor can
    /// reach. Each one must restrict itself to published posts.
    /// </summary>
    public static TheoryData<string> PublicListingStatementNames =>
    [
        "SelectPublishedSql",
        "SelectFeaturedSql",
        "SelectByCategorySql",
        "SelectPublishedBySeriesSql",
        "SearchSql"
    ];

    /// <summary>
    /// Reads a <c>private const string</c> SQL statement from a repository type.
    /// </summary>
    /// <param name="repoType">The repository declaring the constant.</param>
    /// <param name="fieldName">Name of the constant field.</param>
    /// <returns>The statement text.</returns>
    private static string ReadSql(Type repoType, string fieldName)
    {
        var field = repoType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.True(
            field is not null,
            $"{repoType.Name} no longer declares '{fieldName}'. If the statement was renamed, rename it here too — do not delete the guard.");

        var sql = field!.GetRawConstantValue() as string;

        Assert.False(string.IsNullOrWhiteSpace(sql), $"{repoType.Name}.{fieldName} is empty.");

        return sql!;
    }

    /// <summary>
    /// Normalises a statement for filter matching: upper-cased with runs of whitespace collapsed to
    /// a single space, so line breaks and indentation cannot hide a filter from the assertion.
    /// </summary>
    /// <param name="sql">The raw statement text.</param>
    /// <returns>The normalised text.</returns>
    private static string Normalise(string sql)
    {
        return string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }

    /// <summary>
    /// Reports whether a statement restricts its rows to published posts, accepting either the
    /// aliased or unaliased spelling of the filter.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns><c>true</c> when the published filter is present.</returns>
    private static bool FiltersOnPublished(string sql)
    {
        var normalised = Normalise(sql);
        return normalised.Contains("P.PUBLISHED = TRUE") || normalised.Contains(" PUBLISHED = TRUE");
    }

    /// <summary>
    /// Every statement behind a public listing restricts itself to published posts, so a draft
    /// cannot reach an anonymous visitor because a page forgot to filter. This is the assertion that
    /// would have failed on the <c>/series/{slug}</c> draft leak.
    /// </summary>
    /// <param name="statementName">Name of the SQL constant under test.</param>
    [Theory]
    [MemberData(nameof(PublicListingStatementNames))]
    public void PublicListingStatementsFilterOnPublished(string statementName)
    {
        // Arrange
        var sql = ReadSql(typeof(BlogPostRepo), statementName);

        // Act
        var filtersOnPublished = FiltersOnPublished(sql);

        // Assert
        Assert.True(
            filtersOnPublished,
            $"BlogPostRepo.{statementName} feeds a public page but does not filter on 'Published = TRUE'. Every unpublished row it returns is a leak.");
    }

    /// <summary>
    /// The public series listing and the count that renders the "N Parts" badge above it apply the
    /// same published filter, so the page can never show more parts than it claims to have. The
    /// defect this pins had the count filtering and the listing not.
    /// </summary>
    [Fact]
    public void PublishedSeriesListingAndItsCountAgreeOnPublished()
    {
        // Arrange
        var listing = ReadSql(typeof(BlogPostRepo), "SelectPublishedBySeriesSql");
        var count = ReadSql(typeof(BlogPostRepo), "CountBySeriesSql");

        // Act
        var listingFilters = FiltersOnPublished(listing);
        var countFilters = FiltersOnPublished(count);

        // Assert
        Assert.True(
            listingFilters == countFilters,
            "SelectPublishedBySeriesSql and CountBySeriesSql disagree about the Published filter, so the parts listed on /series/{slug} and the parts counted in its badge describe different sets.");
    }

    /// <summary>
    /// The authoring series read stays unfiltered on purpose — the admin series editor lists draft
    /// parts — so this test documents the split rather than the filter, and fails if someone
    /// "fixes" the authoring statement and silently empties the editor's part list.
    /// </summary>
    [Fact]
    public void AuthoringSeriesListingStillReturnsDrafts()
    {
        // Arrange
        var authoring = ReadSql(typeof(BlogPostRepo), "SelectBySeriesSql");

        // Act
        var filtersOnPublished = FiltersOnPublished(authoring);

        // Assert
        Assert.False(
            filtersOnPublished,
            "SelectBySeriesSql is the ADMIN read and must keep returning drafts; public callers use SelectPublishedBySeriesSql.");
    }

    /// <summary>
    /// Both series statements exclude soft-deleted rows, so a deleted part cannot resurface on
    /// either the public page or the editor.
    /// </summary>
    /// <param name="statementName">Name of the SQL constant under test.</param>
    [Theory]
    [InlineData("SelectBySeriesSql")]
    [InlineData("SelectPublishedBySeriesSql")]
    [InlineData("CountBySeriesSql")]
    public void SeriesStatementsExcludeSoftDeletedRows(string statementName)
    {
        // Arrange
        var sql = Normalise(ReadSql(typeof(BlogPostRepo), statementName));

        // Act
        var excludesDeleted = sql.Contains("ISDELETED = FALSE");

        // Assert
        Assert.True(excludesDeleted, $"BlogPostRepo.{statementName} does not exclude soft-deleted rows.");
    }

    /// <summary>
    /// The public series listing projects the columns the series page renders. A projection that
    /// drops one of these does not fail to compile — the property simply arrives as its default, so
    /// the page silently shows part number 0, a blank author or "Draft" for a published post.
    /// </summary>
    /// <param name="columnName">Column the page depends on.</param>
    [Theory]
    [InlineData("SeriesPartNumber")]
    [InlineData("Published")]
    [InlineData("PublishedOn")]
    [InlineData("Title")]
    [InlineData("Slug")]
    [InlineData("Abstract")]
    [InlineData("PostContent")]
    public void PublishedSeriesListingProjectsTheColumnsThePageRenders(string columnName)
    {
        // Arrange
        var sql = Normalise(ReadSql(typeof(BlogPostRepo), "SelectPublishedBySeriesSql"));

        // Act
        var projectsColumn = sql.Contains(columnName.ToUpperInvariant());

        // Assert
        Assert.True(
            projectsColumn,
            $"SelectPublishedBySeriesSql no longer projects {columnName}; SeriesView.razor renders it and would show a default value instead.");
    }

    /// <summary>
    /// The two series reads project the same column list. They differ only by the published filter,
    /// and letting their projections drift is how one of a matched pair of statements quietly
    /// becomes a different shape from the other.
    /// </summary>
    [Fact]
    public void BothSeriesListingsProjectTheSameColumns()
    {
        // Arrange
        var authoring = Normalise(ReadSql(typeof(BlogPostRepo), "SelectBySeriesSql"));
        var published = Normalise(ReadSql(typeof(BlogPostRepo), "SelectPublishedBySeriesSql"));

        // Act
        var authoringColumns = authoring[..authoring.IndexOf(" FROM ", StringComparison.Ordinal)];
        var publishedColumns = published[..published.IndexOf(" FROM ", StringComparison.Ordinal)];

        // Assert
        Assert.Equal(authoringColumns, publishedColumns);
    }

    /// <summary>
    /// The series-by-slug header read counts published parts only, so the "N Parts" badge on
    /// <c>/series/{slug}</c> equals the number of parts the reader can actually open. REQ-FN-019
    /// added the count to this projection after the page rendered "0 Parts" for every series.
    /// </summary>
    [Fact]
    public void SeriesBySlugCountsPublishedPartsOnly()
    {
        // Arrange
        var sql = Normalise(ReadSql(typeof(BlogSeriesRepo), "SelectBySlugSql"));

        // Act
        var countsPosts = sql.Contains("COUNT(P.POSTID) AS POSTCOUNT");
        var countsPublishedOnly = sql.Contains("P.PUBLISHED = TRUE");

        // Assert
        Assert.True(countsPosts, "BlogSeriesRepo.SelectBySlugSql no longer computes PostCount; the series page would render '0 Parts'.");
        Assert.True(countsPublishedOnly, "BlogSeriesRepo.SelectBySlugSql counts unpublished parts, so the badge would promise parts a reader cannot open.");
    }

    /// <summary>
    /// The all-series listing counts published parts only, matching the per-series header, so the
    /// card on <c>/series</c> and the badge on <c>/series/{slug}</c> never disagree.
    /// </summary>
    [Fact]
    public void SeriesListingCountsMatchTheSeriesHeaderCount()
    {
        // Arrange
        var listing = Normalise(ReadSql(typeof(BlogSeriesRepo), "SelectAllWithCountsSql"));
        var header = Normalise(ReadSql(typeof(BlogSeriesRepo), "SelectBySlugSql"));

        // Act
        var listingCountsPublishedOnly = listing.Contains("P.PUBLISHED = TRUE");
        var headerCountsPublishedOnly = header.Contains("P.PUBLISHED = TRUE");

        // Assert
        Assert.Equal(headerCountsPublishedOnly, listingCountsPublishedOnly);
    }
}
