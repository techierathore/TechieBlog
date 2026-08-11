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
///   <item>The four public post reads — <c>SelectPublishedSql</c>, <c>SelectFeaturedSql</c>,
///     <c>SelectByCategorySql</c>, <c>SearchSql</c> — omitted <c>PublishedOn</c>, so the RSS
///     <c>pubDate</c>, the sitemap <c>lastmod</c> and every listing card dated posts by when they were
///     drafted rather than by when they went live (REQ-FN-057).</item>
///   <item><c>BlogPostRepo.SelectPagedSql</c> had no author join and none of the publish, schedule,
///     soft-delete or series columns, so any read-modify-write through it would have NULLed a post's
///     publication date. It had no caller, which is the only reason it never did (REQ-FN-057).</item>
///   <item><c>BlogSeriesRepo</c>'s other four reads omitted the computed <c>PostCount</c> that
///     <c>SelectBySlugSql</c> provides — REQ-FN-019 waiting to happen again on the admin series grid
///     (REQ-FN-057).</item>
/// </list>
///
/// <para><b>REQ-FN-057 (2026-08-10):</b> all three of the above were FIXED rather than declared, and
/// each is pinned by a named assertion here — <see cref="PublicPostReadsProjectPublishedOn"/>,
/// <see cref="PagedPostReadMatchesTheUnpagedAdminRead"/> with
/// <see cref="PagedPostReadStaysAnAdminRead"/>, and
/// <see cref="EverySeriesReadComputesThePartCount"/>. The one narrowing deliberately kept —
/// <c>SelectPagedSql</c> stays an admin read with no <c>Published</c> filter, because bolting one on
/// would empty the admin grid the day somebody pages it — is registered as a decision the gate
/// enforces in both directions rather than as an omission the gate ignores.</para>
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

            // --- BlogSeriesRepo has NO declared narrowings. It had four until REQ-FN-057 (2026-08-10):
            // SelectAllSql, SelectByAuthorSql, SelectPagedSql and SelectByIdSql all omitted the
            // computed PostCount that SelectBySlugSql projects — the exact shape of REQ-FN-019, where
            // /series/{slug} rendered "0 Parts" for every series. All six reads are now composed from
            // one shared fragment on the repository, so they project the same columns by construction
            // and EverySeriesReadComputesThePartCount fails if any of them stops.

            // --- BlogPostRepo: deliberate read shapes, documented at length on the repository
            // itself. Each entry repeats the consequence, because the consequence is the point.
            ["BlogPostRepo.SelectAllSql"] =
                "Admin list read: full entity plus BlogWriter, without the SeriesName/SeriesSlug join. Safe to write back.",
            ["BlogPostRepo.SelectAllByUserSql"] =
                "Same shape as SelectAllSql, scoped to one author. Safe to write back.",
            ["BlogPostRepo.SelectPagedSql"] =
                "FIXED 2026-08-10 under REQ-FN-057, still declared for the series self-join alone: this is now the "
                + "paged twin of SelectAllSql, column for column, so it carries BlogWriter, PublishedOn, "
                + "ScheduledPublishOn, DeletedOn, SeriesId and SeriesPartNumber. It remains narrower than the widest "
                + "read only in lacking SeriesName/SeriesSlug, exactly as SelectAllSql does. The twinning is asserted "
                + "by PagedPostReadMatchesTheUnpagedAdminRead, so it cannot silently narrow again. It was previously "
                + "the narrowest read here — a post read through it reported Author 'Unknown' and Status 'Draft' "
                + "whatever the row said, and writing one back through UpdateAsync would have NULLed its publication "
                + "date and pending schedule. Nothing called it, which is the only reason that never cost data.",
            ["BlogPostRepo.SelectPublishedSql"] =
                "Public listing read: the entity, BlogWriter and PublishedOn, without the soft-delete, schedule or "
                + "series columns. Read-only path — never written back. REQ-FN-057 added PublishedOn on 2026-08-10; "
                + "PublicPostReadsProjectPublishedOn keeps it there.",
            ["BlogPostRepo.SelectFeaturedSql"] =
                "Same public shape as SelectPublishedSql, single row. Read-only path.",
            ["BlogPostRepo.SelectByCategorySql"] =
                "Same public shape as SelectPublishedSql, filtered by category. Read-only path.",
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
    /// Every statement in <c>BlogPostRepo</c> that feeds a surface an anonymous visitor can reach.
    /// These four share one read shape and one job, so they are asserted as a family.
    /// </summary>
    public static TheoryData<string> PublicPostReadNames() =>
    [
        "SelectPublishedSql",
        "SelectFeaturedSql",
        "SelectByCategorySql",
        "SearchSql"
    ];

    /// <summary>
    /// Every statement in <c>BlogSeriesRepo</c> that reads whole series rows.
    /// </summary>
    public static TheoryData<string> SeriesReadNames() =>
    [
        "SelectAllSql",
        "SelectAllWithCountsSql",
        "SelectByAuthorSql",
        "SelectByIdSql",
        "SelectBySlugSql",
        "SelectPagedSql"
    ];

    /// <summary>
    /// Every public post listing projects <c>PublishedOn</c>, so the date the site shows for a post is
    /// the date it went live rather than the date somebody started drafting it.
    /// </summary>
    /// <remarks>
    /// REQ-FN-057. These four reads omitted the column, and the omission was invisible precisely
    /// because every consumer resolves the date defensively as <c>PublishedOn ?? CreatedOn</c>:
    /// <c>RssFeedSvc.BuildItem</c> for the feed's <c>pubDate</c>, <c>SitemapSvc.AddPublishedPosts</c>
    /// and its async twin for <c>lastmod</c>. A column that is never selected does not make that
    /// fallback fire "when appropriate" — it makes it fire always, on every row, so the whole public
    /// site silently dated itself by <c>CreatedOn</c> and no test could tell.
    /// </remarks>
    /// <param name="statementName">Name of the SQL constant under test.</param>
    [Theory]
    [MemberData(nameof(PublicPostReadNames))]
    public void PublicPostReadsProjectPublishedOn(string statementName)
    {
        // Arrange
        var columns = ProjectionOf("BlogPostRepo", statementName);

        // Act
        var projected = columns.Contains("PUBLISHEDON");

        // Assert
        Assert.True(
            projected,
            $"BlogPostRepo.{statementName} no longer projects PublishedOn. Every consumer dates a post as 'PublishedOn ?? CreatedOn', so the column arriving null does not surface as an error — it silently re-dates the post to when it was drafted, in the RSS pubDate, in the sitemap lastmod and on every listing card.");
    }

    /// <summary>
    /// The four public post reads project exactly the same columns, so a column added for one public
    /// surface cannot be missing on the next one. They are one read shape serving one audience, and
    /// the drift between them is what REQ-FN-057 had to go and find by hand.
    /// </summary>
    [Fact]
    public void PublicPostReadsShareOneProjection()
    {
        // Arrange
        var names = new[] { "SelectPublishedSql", "SelectFeaturedSql", "SelectByCategorySql", "SearchSql" };
        var reference = ProjectionOf("BlogPostRepo", names[0]);
        var failures = new StringBuilder();

        // Act
        foreach (var name in names.Skip(1))
        {
            var columns = ProjectionOf("BlogPostRepo", name);

            var missing = reference.Except(columns, StringComparer.Ordinal).ToList();
            var extra = columns.Except(reference, StringComparer.Ordinal).ToList();

            if (missing.Count == 0 && extra.Count == 0)
                continue;

            failures.AppendLine(
                $"  {name} differs from {names[0]}: missing [{string.Join(", ", missing.Order(StringComparer.Ordinal))}], extra [{string.Join(", ", extra.Order(StringComparer.Ordinal))}].");
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"The public post reads have drifted apart:{Environment.NewLine}{failures}"
            + "They feed the home listing, the featured card, the category archive and search — one audience, one row shape. "
            + "If one of them genuinely needs a different shape, split it out with a name that says so rather than letting the family diverge.");
    }

    /// <summary>
    /// <c>SelectPagedSql</c> projects exactly what its unpaged twin <c>SelectAllSql</c> projects, so
    /// the only difference between the two admin reads is <c>LIMIT</c>/<c>OFFSET</c>.
    /// </summary>
    /// <remarks>
    /// REQ-FN-057(b). The paged read had no join and thirteen columns: no <c>BlogWriter</c>, no
    /// <c>PublishedOn</c>, no <c>ScheduledPublishOn</c>. It has no caller today, which is the only
    /// reason that never destroyed data — <c>UpdateSql</c> writes <c>PublishedOn</c> and
    /// <c>ScheduledPublishOn</c> unconditionally, so the first read-modify-write through this path
    /// would have erased both. It is an <c>override</c> of an abstract member and therefore cannot be
    /// deleted, so "no caller today" is a property of the calendar, not of the code. It was widened
    /// rather than declared, and this assertion is what stops it narrowing back.
    /// </remarks>
    [Fact]
    public void PagedPostReadMatchesTheUnpagedAdminRead()
    {
        // Arrange
        var paged = ProjectionOf("BlogPostRepo", "SelectPagedSql");
        var unpaged = ProjectionOf("BlogPostRepo", "SelectAllSql");

        // Act
        var missing = unpaged.Except(paged, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        // Assert
        Assert.True(
            missing.Count == 0,
            $"BlogPostRepo.SelectPagedSql omits [{string.Join(", ", missing)}] that its unpaged twin SelectAllSql projects. "
            + "A post read through the paged path would report those columns as their defaults — Author 'Unknown', a null publication date — and UpdateAsync writes PublishedOn and ScheduledPublishOn unconditionally, so writing one back stores NULL over a real date.");
    }

    /// <summary>
    /// <c>SelectPagedSql</c> stays an ADMIN read: no <c>Published</c> filter, matching its unpaged twin
    /// <c>SelectAllSql</c> and the dashboard count <c>SelectCountsSql</c>.
    /// </summary>
    /// <remarks>
    /// REQ-FN-057(b), the declared half. The requirement noted this statement "would leak drafts if
    /// ever wired to a public page". The resolution is not to bolt a published filter onto it — that
    /// would silently empty the admin grid the day somebody pages it, the mirror image of the
    /// REQ-FN-015 mistake — but to fix the projection and pin the filter as a deliberate decision. The
    /// three admin statements over <c>BlogPost</c> must agree on what a row is; if a public surface
    /// needs paging it gets <c>SelectPublishedSql</c>, which already filters and already pairs with
    /// <c>CountPublishedSql</c>.
    /// </remarks>
    [Fact]
    public void PagedPostReadStaysAnAdminRead()
    {
        // Arrange
        var statements = SqlStatementInventory.Statements(Repository("BlogPostRepo"));

        // Act
        var pagedFilters = SqlStatementInventory.FiltersOnPublished(statements["SelectPagedSql"]);
        var unpagedFilters = SqlStatementInventory.FiltersOnPublished(statements["SelectAllSql"]);
        var countFilters = SqlStatementInventory.FiltersOnPublished(statements["SelectCountsSql"]);

        // Assert
        Assert.False(
            pagedFilters,
            "BlogPostRepo.SelectPagedSql has acquired a Published filter. It is the paged twin of the ADMIN read SelectAllSql and is counted by SelectCountsSql, neither of which filters; adding one here empties the admin grid of drafts while the count still counts them. A public paged listing already exists — SelectPublishedSql, paired with CountPublishedSql.");

        Assert.Equal(unpagedFilters, pagedFilters);
        Assert.Equal(countFilters, pagedFilters);
    }

    /// <summary>
    /// Every read in <c>BlogSeriesRepo</c> computes <c>PostCount</c>, so no surface can render a series
    /// as having "0 Parts" merely because the read it used never asked for the number.
    /// </summary>
    /// <remarks>
    /// REQ-FN-057(c). <c>PostCount</c> is a computed <c>LEFT JOIN</c> aggregate, not a stored column,
    /// so omitting it does not produce null — it produces <c>0</c>, which renders as a confident,
    /// wrong answer. Four of these six reads omitted it, which is the identical shape of REQ-FN-019,
    /// where <c>/series/{slug}</c> shipped rendering "0 Parts" for every series.
    /// </remarks>
    /// <param name="statementName">Name of the SQL constant under test.</param>
    [Theory]
    [MemberData(nameof(SeriesReadNames))]
    public void EverySeriesReadComputesThePartCount(string statementName)
    {
        // Arrange
        var columns = ProjectionOf("BlogSeriesRepo", statementName);
        var sql = SqlStatementInventory.Normalise(
            SqlStatementInventory.Statements(Repository("BlogSeriesRepo"))[statementName]);

        // Act
        var projectsCount = columns.Contains("POSTCOUNT");
        var countsPublishedOnly = sql.Contains("P.PUBLISHED = TRUE", StringComparison.Ordinal);

        // Assert
        Assert.True(
            projectsCount,
            $"BlogSeriesRepo.{statementName} no longer computes PostCount. The count is a joined aggregate, not a column, so a series read through this statement reports 0 parts rather than 'unknown' — the REQ-FN-019 '0 Parts' defect, reopened on whichever surface uses this read.");

        Assert.True(
            countsPublishedOnly,
            $"BlogSeriesRepo.{statementName} counts unpublished parts, so its badge promises parts a reader cannot open.");
    }

    /// <summary>
    /// Every declared narrowing still describes a statement that exists and is still genuinely
    /// narrower than its repository's widest read, so the registry cannot fill up with exemptions for
    /// defects that were fixed years ago or for statements that no longer exist.
    /// </summary>
    /// <remarks>
    /// A declaration is a standing permission to omit a column. A stale one is worse than no gate at
    /// all: it silently pre-authorises the next narrowing of a statement that had been made whole. This
    /// assertion is what makes "registered as a declared narrowing" mean something — the registry has
    /// to keep matching reality in both directions.
    /// </remarks>
    [Fact]
    public void DeclaredNarrowingsStillDescribeRealNarrowings()
    {
        // Arrange
        var failures = new StringBuilder();

        // Act
        foreach (var declaration in DeclaredNarrowProjections.Keys.Order(StringComparer.Ordinal))
        {
            var separator = declaration.IndexOf('.', StringComparison.Ordinal);
            var repositoryName = declaration[..separator];
            var statementName = declaration[(separator + 1)..];

            var repositoryType = SqlStatementInventory.RepositoryTypes()
                .FirstOrDefault(candidate => candidate.Name == repositoryName);

            if (repositoryType is null)
            {
                failures.AppendLine($"  {declaration} names a repository that no longer exists.");
                continue;
            }

            var statements = SqlStatementInventory.Statements(repositoryType);
            if (!statements.ContainsKey(statementName))
            {
                failures.AppendLine($"  {declaration} names a statement {repositoryName} no longer declares.");
                continue;
            }

            var reads = EntityReads(statements);
            if (!reads.TryGetValue(statementName, out var columns) || reads.Count < 2)
                continue;

            var widest = reads.MaxBy(read => read.Value.Count)!;
            if (statementName == widest.Key)
            {
                failures.AppendLine($"  {declaration} is now the WIDEST read in {repositoryName}; the declaration is dead.");
                continue;
            }

            if (widest.Value.Except(columns, StringComparer.Ordinal).Any())
                continue;

            failures.AppendLine(
                $"  {declaration} omits nothing {widest.Key} projects; the narrowing it exempts no longer exists.");
        }

        // Assert
        Assert.True(
            failures.Length == 0,
            $"The declared-narrowing registry no longer matches the code:{Environment.NewLine}{failures}"
            + "Delete the stale entries. Each one is a standing permission to drop a column from that statement, so leaving it behind after the statement was made whole silently pre-authorises the next regression.");
    }

    /// <summary>
    /// The projected column set of one repository statement, upper-cased by the inventory's own
    /// normalisation, with a failure that names the statement when it has been renamed away.
    /// </summary>
    /// <param name="repositoryName">Simple repository type name.</param>
    /// <param name="statementName">Name of the SQL constant.</param>
    /// <returns>The projected output names.</returns>
    private static ISet<string> ProjectionOf(string repositoryName, string statementName)
    {
        var statements = SqlStatementInventory.Statements(Repository(repositoryName));

        Assert.True(
            statements.ContainsKey(statementName),
            $"{repositoryName} no longer declares '{statementName}'. If the statement was renamed, rename it here too — do not delete the guard.");

        var columns = SqlStatementInventory.ProjectedColumns(statements[statementName]);

        Assert.True(
            columns is not null,
            $"{repositoryName}.{statementName} projects '*', so nothing can be asserted about its column list.");

        return columns!;
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
