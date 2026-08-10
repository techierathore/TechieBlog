using System.Text;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// The structural gate for this codebase's most repeated defect class: a SQL read projection that
/// omits a column, or a filter, its sibling statement includes.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-016. Eight instances of one bug have been recorded here, and every
/// one was invisible to both the compiler and the behavioural unit suite:</para>
/// <list type="number">
///   <item><c>BlogPostRepo.GetAll</c>/<c>GetAllById</c> omitted <c>BlogWriter</c>,
///     <c>PublishedOn</c> and <c>ScheduledPublishOn</c> — every author read "Unknown"
///     (REQ-UI-017).</item>
///   <item><c>SelectBlogUserById</c> omitted <c>MustChangePassword</c> (script 021).</item>
///   <item><c>BlogPostRepo.SelectByIdSql</c>/<c>SelectBySlugSql</c> omitted <c>PublishedOn</c> and
///     <c>ScheduledPublishOn</c>, so unpublishing a post ERASED its first-publication date
///     (REQ-NFR-008).</item>
///   <item><c>SelectBlogUserById</c> omitted nine <c>BlogUser</c> columns, so opening Manage Profile
///     and pressing Save with no edits erased the site owner's whole resume and blanked the public
///     portfolio (REQ-FN-053).</item>
///   <item><c>BlogPostRepo.SelectBySeriesSql</c> had no <c>Published = TRUE</c> while its own
///     <c>CountBySeriesSql</c> did, leaking every unpublished part of a series to anonymous
///     visitors (REQ-FN-015).</item>
///   <item><c>BlogSeriesRepo.SelectBySlugSql</c> omitted <c>PostCount</c>, so every series page
///     rendered "0 Parts" (REQ-FN-019).</item>
/// </list>
///
/// <para><b>Why a gate and not more assertions:</b> REQ-FN-053's own write-up recommended a
/// projection-completeness gate and none existed. Per-statement assertions only cover the statements
/// somebody remembered to write an assertion for, which by construction excludes the next defect.
/// The tests below are therefore <b>self-wiring</b>: they enumerate every repository in
/// <c>BlogEngine.DbAccess</c> by reflection and every SQL constant on it, so a repository or a
/// statement added tomorrow is gated the day it is written, with no edit here.</para>
///
/// <para><b>The three properties gated:</b></para>
/// <list type="number">
///   <item><b>Write-back safety.</b> Every column a repository's UPDATE writes must be projected by
///     the read that loads the entity for editing — whether that read embeds its own column list or
///     calls a stored function defined in a migration script. This is the assertion that fails on
///     defects 1 to 4 above, including the two that destroyed data.</item>
///   <item><b>Listing/COUNT filter parity.</b> A listing and the COUNT that pages or badges it must
///     agree about what a row is. This is the assertion that fails on defect 5.</item>
///   <item><b>Declared narrowing.</b> Where two reads of the same entity legitimately differ, the
///     narrow one is registered here with a reason. A statement that silently loses a column its
///     siblings project fails until somebody decides, in writing, that the loss is intended.</item>
/// </list>
///
/// <para><b>Dependencies:</b> reflection over <c>BlogEngine</c> plus the migration scripts on disk.
/// No database, no Docker, no host — this reads SQL, it does not execute it.</para>
///
/// <para><b>When one of these fails</b>, the message names the statement and the exact columns or
/// filter at issue. Restore the column, or — if the narrowing is deliberate — add the statement to
/// <see cref="DeclaredNarrowProjections"/> with the reason, which is the point: the loss becomes a
/// decision somebody made rather than one nobody noticed.</para>
/// </remarks>
public class ProjectionCompletenessTests
{
    /// <summary>
    /// Reads whose projection is deliberately narrower than their repository's widest read, each
    /// with the reason it is safe. A statement listed here is exempt from
    /// <see cref="NarrowEntityReadsAreDeclared"/> — and must never be used to load an entity that is
    /// then written back, because the columns it does not select arrive as <c>null</c>, not as
    /// "unchanged".
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DeclaredNarrowProjections =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- AnalyticsRepo: these statements return different row shapes, not narrower reads of
            // one row shape. The gate groups by repository, not by result type, so they are declared.
            ["AnalyticsRepo.SelectCategoryEngagementSql"] =
                "Returns one row per CATEGORY, not per post; it shares no row shape with the post-engagement reads.",
            ["AnalyticsRepo.SelectPopularPostsSql"] =
                "Popularity DTO: post identity plus view count only. Never written back.",
            ["AnalyticsRepo.SelectPopularPostsInRangeSql"] =
                "Same DTO as SelectPopularPostsSql, restricted to a date range. Never written back.",
            ["AnalyticsRepo.SelectViewTrendSql"] =
                "Returns one row per DAY — date and view count — not a post row. Never written back.",

            // --- Aggregate reads that share a repository with entity reads.
            ["PostViewRepo.SelectCountsSql"] =
                "Aggregate: post id and its view count. Not a PostView row and never written back.",
            ["BlogTagRepo.SelectAllWithCountsSql"] =
                "Returns TAG rows with a usage count; SelectPostsByTagSql returns POST rows. Different entities "
                + "in the same repository.",

            // --- NewsletterRepo: these three read Subscriber rows, not Newsletter rows.
            ["NewsletterRepo.SelectRecipientsSql"] =
                "Reads Subscriber, not Newsletter — a different entity that happens to live in the same repository.",
            ["NewsletterRepo.SelectSendHistorySql"] =
                "Reads the send-history join, not a Newsletter row.",
            ["NewsletterRepo.SelectSubscriberByTokenSql"] =
                "Reads Subscriber by unsubscribe token, not Newsletter.",

            // --- BlogSeriesRepo: PostCount is a computed aggregate, present only on the reads that
            // join BlogPost for it. Declared rather than fixed, but see the LATENT note below.
            ["BlogSeriesRepo.SelectAllSql"] =
                "Bare series rows without the computed PostCount; callers that need the count use SelectAllWithCountsSql.",
            ["BlogSeriesRepo.SelectByAuthorSql"] =
                "Author's series list; PostCount is not rendered on that surface.",
            ["BlogSeriesRepo.SelectPagedSql"] =
                "Admin paging read; PostCount is not rendered in the grid.",
            ["BlogSeriesRepo.SelectByIdSql"] =
                "LATENT (2026-08-09, REQ-NFR-016): omits the computed PostCount that SelectBySlugSql projects. "
                + "This is the same shape as REQ-FN-019, where /series/{slug} rendered '0 Parts' because GetBySlug "
                + "had no PostCount. Harmless only for as long as no surface loading a series BY ID renders a part "
                + "count — the admin series editor is one edit away from doing so. Not a data-loss risk: "
                + "BlogSeriesRepo.UpdateSql does not write PostCount.",

            // --- BlogPostRepo: four deliberate read shapes, documented at length on the repository
            // itself. Each entry repeats the consequence, because the consequence is the point.
            ["BlogPostRepo.SelectAllSql"] =
                "Admin list read: full entity plus BlogWriter, without the SeriesName/SeriesSlug join. Safe to write back.",
            ["BlogPostRepo.SelectAllByUserSql"] =
                "Same shape as SelectAllSql, scoped to one author. Safe to write back.",
            ["BlogPostRepo.SelectPagedSql"] =
                "LATENT (documented on BlogPostRepo, re-recorded 2026-08-09 under REQ-NFR-016): the narrowest read in "
                + "the repository — no join, so no BlogWriter, and no PublishedOn, ScheduledPublishOn, DeletedOn, "
                + "SeriesId or SeriesPartNumber. A post read here reports Author 'Unknown' and Status 'Draft' whatever "
                + "the row says, and writing one back through UpdateAsync would NULL its publication date and pending "
                + "schedule. MUST NOT be used to load an entity for editing.",
            ["BlogPostRepo.SelectPublishedSql"] =
                "LATENT (documented on BlogPostRepo, re-recorded 2026-08-09 under REQ-NFR-016): public listing read "
                + "with BlogWriter but WITHOUT PublishedOn, so any page built on it must date posts by CreatedOn, not "
                + "by when they actually went live. Read-only path.",
            ["BlogPostRepo.SelectFeaturedSql"] =
                "Same narrow public shape as SelectPublishedSql, single row. Read-only path.",
            ["BlogPostRepo.SelectByCategorySql"] =
                "Same narrow public shape as SelectPublishedSql, filtered by category. Read-only path.",
            ["BlogPostRepo.SelectScheduledSql"] =
                "Scheduling read: carries ScheduledPublishOn and PublishedOn, omits the series join. Read-only path.",
            ["BlogPostRepo.SelectDueScheduledSql"] =
                "Same shape as SelectScheduledSql, restricted to posts whose scheduled time has passed.",
            ["BlogPostRepo.SelectBySeriesSql"] =
                "Authoring series read; omits the series self-join because the caller already knows the series.",
            ["BlogPostRepo.SelectPublishedBySeriesSql"] =
                "Public twin of SelectBySeriesSql — same columns, plus Published = TRUE. The pair is asserted "
                + "column-for-column by BlogPostRepoProjectionTests.",
        };

    /// <summary>
    /// Columns an UPDATE sets from a value the statement itself supplies rather than from the loaded
    /// entity, so the read that precedes it does not need to project them. Keyed
    /// <c>Repository.Statement</c>, valued by the columns and the reason.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DeclaredWriteOnlyColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NewsletterRepo.UpdateSql"] =
                "UpdatedOn is stamped by the repository from DateTime.UtcNow in BuildUpdateParameters, never read off "
                + "the entity, and the Newsletter model has no UpdatedOn property to lose.",
            ["NewsletterRepo.MarkSentSql"] =
                "UpdatedOn is set to the same instant as SentOn by the repository, not carried on the entity.",
        };

    /// <summary>
    /// Listings whose published filter deliberately differs from that of the COUNT their name pairs
    /// them with, each with the reason. Exempt from
    /// <see cref="ListingsAndTheirCountsAgreeOnThePublishedFilter"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> DeclaredFilterSplits =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BlogPostRepo.SelectBySeriesSql"] =
                "REQ-FN-015: this is the ADMIN read and must keep returning drafts so the series editor can list "
                + "unpublished parts. The public page uses SelectPublishedBySeriesSql, which does share CountBySeriesSql's "
                + "filter. Removing this split is what leaked every draft part to anonymous visitors, and 'fixing' "
                + "this statement instead would silently empty the editor.",
        };

    /// <summary>
    /// Every repository in <c>BlogEngine.DbAccess</c>, discovered by reflection so the gates cover
    /// repositories that do not exist yet.
    /// </summary>
    public static TheoryData<string> RepositoryNames()
    {
        var data = new TheoryData<string>();

        foreach (var repositoryType in SqlStatementInventory.RepositoryTypes())
        {
            data.Add(repositoryType.Name);
        }

        return data;
    }

    /// <summary>
    /// Resolves a repository type from the name a theory row carries, so the failure output names
    /// the repository rather than a fully-qualified type.
    /// </summary>
    /// <param name="repositoryName">Simple type name.</param>
    /// <returns>The repository type.</returns>
    private static Type Repository(string repositoryName)
    {
        return SqlStatementInventory.RepositoryTypes().Single(candidate => candidate.Name == repositoryName);
    }

    /// <summary>
    /// The migration scripts are found and parsed, so the stored-function gate below is asserting
    /// against real definitions rather than passing vacuously on an empty catalogue. A gate that
    /// cannot see its subject is worse than no gate, because it reports success.
    /// </summary>
    [Fact]
    public void MigrationScriptCatalogueIsPopulated()
    {
        // Arrange, Act
        var functions = StoredFunctionCatalog.Functions();

        // Assert
        Assert.True(
            StoredFunctionCatalog.ScriptFolder is not null,
            "source/BlogDb/PostgresScripts was not found by walking up from the test assembly. The stored-function projection gate would silently pass without it; set TechieBlogMigrationScripts to the folder.");

        Assert.True(
            functions.Count > 0,
            "No RETURNS TABLE stored functions were parsed out of the migration scripts, so the projection gate has nothing to assert against.");
    }

    /// <summary>
    /// The stored function that widens <c>SelectBlogUserById</c> is still the effective definition,
    /// projecting all nine columns whose absence erased the site owner's resume on a no-edit save.
    /// This pins the specific REQ-FN-053 fix, so a later migration that recreates the function with
    /// a shorter column list fails here instead of in production.
    /// </summary>
    [Theory]
    [InlineData("Username")]
    [InlineData("IsSiteOwner")]
    [InlineData("Title")]
    [InlineData("Tagline")]
    [InlineData("InstagramUrl")]
    [InlineData("PhoneNumber")]
    [InlineData("Location")]
    [InlineData("CVFilePath")]
    [InlineData("ResumeEnabled")]
    [InlineData("MustChangePassword")]
    public void SelectBlogUserByIdStillProjectsTheResumeColumns(string columnName)
    {
        // Arrange
        var columns = StoredFunctionCatalog.ReturnedColumns("SelectBlogUserById");

        // Act
        var projected = columns is not null
            && columns.Contains(columnName.ToUpperInvariant());

        // Assert
        Assert.True(
            projected,
            $"SelectBlogUserById no longer returns {columnName}. Manage Profile loads through this function and writes the loaded model back, so the column would arrive null and the very next Save would persist that null — the REQ-FN-053 data-loss defect, reopened.");
    }

    /// <summary>
    /// Every column a repository's UPDATE statements write is projected by the read that loads the
    /// entity for editing, so no read-modify-write can persist a null it never loaded. Covers both
    /// read styles: an embedded column list and a call to a stored function whose projection lives
    /// in a migration script.
    /// </summary>
    /// <param name="repositoryName">Repository under test.</param>
    [Theory]
    [MemberData(nameof(RepositoryNames))]
    public void WriteBackColumnsAreProjectedByTheReadThatLoadsThem(string repositoryName)
    {
        // Arrange
        var repositoryType = Repository(repositoryName);
        var statements = SqlStatementInventory.Statements(repositoryType);

        var loader = LoadingRead(statements);
        if (loader is null)
            return;

        var failures = new StringBuilder();

        // Act
        foreach (var (name, sql) in statements)
        {
            var updated = SqlStatementInventory.UpdatedColumns(sql);
            if (updated is null)
                continue;

            // Several repositories own more than one table — NewsletterRepo also writes Subscriber.
            // An UPDATE against a different table than the loading read is not a round trip at all.
            if (loader.Value.Table is not null
                && !string.Equals(updated.Value.Table, loader.Value.Table, StringComparison.OrdinalIgnoreCase))
                continue;

            if (DeclaredWriteOnlyColumns.ContainsKey($"{repositoryName}.{name}"))
                continue;

            var missing = updated.Value.Columns.Except(loader.Value.Columns, StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
                continue;

            failures.AppendLine(
                $"  {repositoryName}.{name} writes [{string.Join(", ", missing.Order(StringComparer.Ordinal))}], which {loader.Value.Name} does not project.");
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"Read-modify-write columns are not loaded before they are written:{Environment.NewLine}{failures}"
            + $"An entity loaded by {loader.Value.Name} carries null in those columns — not 'unchanged', null — so the update stores null and the value is gone with no error. "
            + "Add the column to the read, or register the statement in DeclaredWriteOnlyColumns with the reason it never comes from the loaded entity.");
    }

    /// <summary>
    /// A public listing and the COUNT that pages or badges it apply the same published filter, so a
    /// page can never show rows its own count never counted — the shape of the draft leak on
    /// <c>/series/{slug}</c>. Pairs are matched by name across every repository, so a listing added
    /// with a COUNT beside it is gated automatically.
    /// </summary>
    /// <param name="repositoryName">Repository under test.</param>
    [Theory]
    [MemberData(nameof(RepositoryNames))]
    public void ListingsAndTheirCountsAgreeOnThePublishedFilter(string repositoryName)
    {
        // Arrange
        var repositoryType = Repository(repositoryName);
        var statements = SqlStatementInventory.Statements(repositoryType);
        var failures = new StringBuilder();

        // Act
        foreach (var (name, sql) in statements)
        {
            if (!name.StartsWith("Select", StringComparison.Ordinal) || !name.EndsWith("Sql", StringComparison.Ordinal))
                continue;

            var subject = name[6..^3];
            if (subject.Length == 0)
                continue;

            var counterpart = statements.Keys.FirstOrDefault(candidate =>
                candidate == $"Count{subject}Sql" || candidate == $"Select{subject}CountSql");

            if (counterpart is null)
                continue;

            // "Is this slug already taken, ignoring the row I am editing" is not the COUNT that
            // pages a listing; it merely shares the naming pattern.
            if (SqlStatementInventory.IsUniquenessProbe(statements[counterpart]))
                continue;

            if (DeclaredFilterSplits.ContainsKey($"{repositoryName}.{name}"))
                continue;

            var listingFilters = SqlStatementInventory.FiltersOnPublished(sql);
            var countFilters = SqlStatementInventory.FiltersOnPublished(statements[counterpart]);

            if (listingFilters == countFilters)
                continue;

            failures.AppendLine(
                $"  {repositoryName}.{name} {(listingFilters ? "filters" : "does NOT filter")} on Published = TRUE but {counterpart} {(countFilters ? "does" : "does not")}.");
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"A listing and its own COUNT describe different sets of rows:{Environment.NewLine}{failures}"
            + "When the listing is the unfiltered one this leaks unpublished content to anonymous visitors, which is exactly how every draft part of a series reached the public series page (REQ-FN-015).");
    }

    /// <summary>
    /// Reads of the same entity within one repository project the same columns, unless the narrowing
    /// is registered in <see cref="DeclaredNarrowProjections"/> with a reason. Silent divergence
    /// between sibling reads is the mechanism behind every projection defect this codebase has had.
    /// </summary>
    /// <param name="repositoryName">Repository under test.</param>
    [Theory]
    [MemberData(nameof(RepositoryNames))]
    public void NarrowEntityReadsAreDeclared(string repositoryName)
    {
        // Arrange
        var repositoryType = Repository(repositoryName);
        var reads = EntityReads(SqlStatementInventory.Statements(repositoryType));

        if (reads.Count < 2)
            return;

        var widest = reads.MaxBy(read => read.Value.Count)!;
        var failures = new StringBuilder();

        // Act
        foreach (var (name, columns) in reads)
        {
            if (name == widest.Key)
                continue;

            if (DeclaredNarrowProjections.ContainsKey($"{repositoryName}.{name}"))
                continue;

            var missing = widest.Value.Except(columns, StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
                continue;

            failures.AppendLine(
                $"  {repositoryName}.{name} omits [{string.Join(", ", missing.Order(StringComparer.Ordinal))}] that {widest.Key} projects.");
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"Sibling reads of the same entity have drifted apart:{Environment.NewLine}{failures}"
            + "Every omitted column arrives at the caller as its default — part number 0, author \"Unknown\", status \"Draft\", a null date — with nothing to indicate the value was never read. "
            + "Restore the column, or register the statement in ProjectionCompletenessTests.DeclaredNarrowProjections with the reason the narrowing is intended and the note that its rows must never be written back.");
    }

    /// <summary>
    /// The statement a repository uses to load one entity for editing, with the columns it makes
    /// available — resolved through the stored-function catalogue when the read calls a function
    /// rather than embedding a projection.
    /// </summary>
    /// <param name="statements">The repository's statements.</param>
    /// <returns>The loading read, or <c>null</c> when the repository has no single-entity read.</returns>
    private static (string Name, string? Table, ISet<string> Columns)? LoadingRead(
        IReadOnlyDictionary<string, string> statements)
    {
        foreach (var candidate in new[] { "SelectByIdSql", "SelectByIdListSql", "SelectSingleSql" })
        {
            if (!statements.TryGetValue(candidate, out var sql))
                continue;

            var functionName = SqlStatementInventory.StoredFunctionRead(sql);
            if (functionName is not null)
            {
                var returned = StoredFunctionCatalog.ReturnedColumns(functionName);

                // A stored function names no table, so the repository's own SELECT * read supplies
                // the table the UPDATEs must match.
                var table = statements.TryGetValue("SelectAllSql", out var allSql)
                    ? SqlStatementInventory.SourceTable(allSql)
                    : null;

                return returned is null
                    ? null
                    : ($"{candidate} (stored function {functionName})",
                        table,
                        new HashSet<string>(returned, StringComparer.Ordinal));
            }

            var projected = SqlStatementInventory.ReadableColumns(sql);

            // SELECT * tracks the table, so nothing can be omitted and nothing needs asserting.
            if (projected is not null)
                return (candidate, SqlStatementInventory.SourceTable(sql), projected);

            return null;
        }

        return null;
    }

    /// <summary>
    /// The statements in a repository that read whole entity rows, as opposed to counts, aggregates
    /// and single-value lookups. Judged by projection width so the classification needs no list of
    /// statement names to keep up to date.
    /// </summary>
    /// <param name="statements">The repository's statements.</param>
    /// <returns>Statement name to projected column set.</returns>
    private static IReadOnlyDictionary<string, ISet<string>> EntityReads(
        IReadOnlyDictionary<string, string> statements)
    {
        var reads = new SortedDictionary<string, ISet<string>>(StringComparer.Ordinal);

        foreach (var (name, sql) in statements)
        {
            if (!name.StartsWith("Select", StringComparison.Ordinal))
                continue;

            var columns = SqlStatementInventory.ProjectedColumns(sql);
            if (columns is null || columns.Count < 5)
                continue;

            reads[name] = columns;
        }

        return reads;
    }
}
