using System.Diagnostics;
using BlogEngine.DbAccess;
using BlogModels;
using Npgsql;
using Xunit;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// Runs a browser check of the real website against a throwaway database that this test makes and
/// drops — never against the owner's database (owner rule, 2026-11-26).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> a check of the public pages needs a running site with published posts. The
/// owner's database must not be used, and a database must not be made or removed by hand-typed SQL. So
/// this test asks <see cref="TempDatabase"/> for a fresh <c>tbtest_…</c> database (migrated with every
/// script), adds sample posts through the application's own <see cref="BlogPostRepo"/>, runs the
/// command it is given with <c>AppDbConString</c> pointing at that database, and drops the database
/// when the command ends, pass or fail.</para>
/// <para><b>Settings (environment):</b> <c>TechieBlogTestServer</c> — the server only (see
/// <see cref="TempDatabase"/>); <c>TechieBlogSiteCheckCommand</c> — the shell command to run, e.g. a
/// script that boots the site and runs Playwright. Without both the test is skipped, so a normal test
/// run never touches a database.</para>
/// <para><b>Guard:</b> the command is refused unless the connection string it is handed names a
/// <c>tbtest_</c> database.</para>
/// </remarks>
public sealed class ThrowawaySiteRun : IClassFixture<TempDatabase>
{
    /// <summary>The environment value holding the command to run.</summary>
    public const string CommandSwitchName = "TechieBlogSiteCheckCommand";

    /// <summary>Published sample posts added: more than one page at the default ten per page.</summary>
    private const int SamplePostCount = 14;

    private readonly TempDatabase database;

    /// <summary>
    /// Receives the throwaway database fixture.
    /// </summary>
    /// <param name="database">The class's throwaway database.</param>
    public ThrowawaySiteRun(TempDatabase database)
    {
        this.database = database;
    }

    /// <summary>
    /// Seeds the throwaway database and runs the site check command against it.
    /// </summary>
    /// <remarks>
    /// Scenario: with the server and the command set, a fresh <c>tbtest_</c> database receives 14
    /// published posts sharing one category and tag, the command runs with <c>AppDbConString</c> and
    /// <c>TB_SMOKE_DB</c> set to it and must exit 0; the fixture then drops the database.
    /// </remarks>
    /// <returns>A task that completes when the command has ended.</returns>
    [Fact(DisplayName = "Site check on a throwaway database")]
    public async Task RunSiteCheckOnThrowawayDatabase()
    {
        var command = Environment.GetEnvironmentVariable(CommandSwitchName);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(command), $"Set {CommandSwitchName} to run a site check.");
        Assert.SkipWhen(database.SkipReason != null, database.SkipReason ?? string.Empty);

        var connectionString = database.ConnectionString!;
        var databaseName = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        Assert.StartsWith("tbtest_", databaseName, StringComparison.Ordinal);

        await SeedPostsAsync(connectionString);

        var start = new ProcessStartInfo("bash", new[] { "-c", command! }) { UseShellExecute = false };
        start.Environment["AppDbConString"] = connectionString;
        start.Environment["TB_SMOKE_DB"] = databaseName;
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>
    /// Adds the sample posts through the application's repositories.
    /// </summary>
    /// <param name="connectionString">The throwaway database.</param>
    /// <returns>A task that completes when every post is written.</returns>
    private static async Task SeedPostsAsync(string connectionString)
    {
        var owner = (await new BlogUserRepo(connectionString).GetAllAsync()).OrderByDescending(u => u.IsSiteOwner).First();
        var category = (await new CategoryRepo(connectionString).GetAllAsync()).OrderBy(c => c.CategoryId).First();
        var posts = new BlogPostRepo(connectionString);
        var newest = DateTime.UtcNow.Date;

        for (var number = 1; number <= SamplePostCount; number++)
        {
            var publishedOn = newest.AddDays(-number);
            await posts.InsertToGetIdAsync(new BlogPost
            {
                Title = $"Sample post {number:00}",
                Slug = $"sample-post-{number:00}",
                Abstract = $"The abstract of sample post {number:00}, published {publishedOn:MMM dd}.",
                PostContent = $"## Sample post {number:00}\n\nBody text of the sample post, long enough to read as an article.",
                UserID = owner.UserId,
                Tags = "Architecture,Best Practices",
                CategoryId = (int)category.CategoryId,
                CreatedOn = publishedOn,
                PublishedOn = publishedOn,
                Published = true
            });
        }
    }
}
