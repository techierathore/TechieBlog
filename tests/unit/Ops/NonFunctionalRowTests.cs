using System.Text.RegularExpressions;
using BlogEngine.Common;
using Microsoft.AspNetCore.Http;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Repository-level and code-path checks for the non-functional rows (REQ-NFR-*) that no behavioural
/// test already carries by name.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>tf-verify-tests.sh</c> maps a unit test to a row only when the test's
/// display name STARTS with the row id. Each fact here starts with its id and asserts a property of
/// the checked-out repository (configuration, workflow, project files, pipeline wiring) or of one
/// small code path (rate-limit partitioning, correlation id, markdown sanitising).</para>
/// <para><b>Limits:</b> a source scan proves the wiring is present, not that a deployed server
/// behaves; the facts say so where that is the gap (REQ-NFR-038, REQ-NFR-039).</para>
/// </remarks>
public class NonFunctionalRowTests
{
    private static string Root()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.True(root != null, "the repository root (source/ + TechieBlog.slnx) was not found above the test assembly");
        return root!;
    }

    private static string Read(params string[] relativeParts)
    {
        var path = Path.Combine(new[] { Root() }.Concat(relativeParts).ToArray());
        Assert.True(File.Exists(path), $"expected file is missing: {path}");
        return File.ReadAllText(path);
    }

    private static bool IsBuildOutput(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var parts = path.Split(separators);
        return parts.Any(part => part is "bin" or "obj" or ".smokeout" or "node_modules" or ".vs");
    }

    private static IEnumerable<string> SourceFiles(string folder, string pattern)
    {
        return Directory.EnumerateFiles(Path.Combine(Root(), folder), pattern, SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file));
    }

    // ---------------------------------------------------------------- REQ-NFR-003

    private static readonly Regex SqlVerb = new(@"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM)\b", RegexOptions.IgnoreCase);
    private static readonly Regex InterpolationHole = new(@"\{(?<name>[A-Za-z_][A-Za-z0-9_.]*)\}");
    private static readonly Regex ConcatenatedIdentifier = new(@"""\s*\+\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*\+\s*[@$]?""");

    /// <summary>
    /// Finds identifiers spliced into an SQL string, by interpolation or by concatenation.
    /// </summary>
    private static IEnumerable<string> SplicedNames(string line)
    {
        foreach (Match hole in InterpolationHole.Matches(line))
        {
            if (line.Contains("$\"") || line.Contains("$@\"") || line.Contains("@$\""))
                yield return hole.Groups["name"].Value;
        }

        foreach (Match joined in ConcatenatedIdentifier.Matches(line))
            yield return joined.Groups["name"].Value;
    }

    /// <summary>
    /// REQ-NFR-003: every SQL statement built in the data-access layer reaches the database through
    /// bound parameters. The only text spliced into a statement is a constant declared in the same
    /// file (a column list or fixed predicate), never a method argument.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-003 no data-access SQL splices anything but a file constant into a statement")]
    public void SqlStatementsSpliceOnlyFileConstants()
    {
        // Arrange
        var constantDeclaration = new Regex(@"\b(?:const|static\s+readonly)\s+[\w<>\[\]?]+\s+(?<name>\w+)\s*=");
        var offenders = new List<string>();
        var statementsSeen = 0;

        // Act
        foreach (var file in SourceFiles("source/BlogEngine/DbAccess", "*.cs").Concat(SourceFiles("source/BlogEngine/DaCore", "*.cs")))
        {
            var text = File.ReadAllText(file);
            var constants = constantDeclaration.Matches(text).Select(match => match.Groups["name"].Value).ToHashSet();
            var lines = text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///"))
                    continue;

                if (SqlVerb.IsMatch(line) && line.Contains('"'))
                    statementsSeen++;

                foreach (var name in SplicedNames(line))
                {
                    if (!SqlVerb.IsMatch(line) && !SqlVerb.IsMatch(index > 0 ? lines[index - 1] : string.Empty))
                        continue;

                    if (!constants.Contains(name))
                        offenders.Add($"{Path.GetFileName(file)}:{index + 1} splices '{name}' into SQL: {line.Trim()}");
                }
            }
        }

        // Assert
        Assert.True(statementsSeen > 20, $"only {statementsSeen} SQL statements were found; the scan is looking in the wrong place");
        Assert.True(offenders.Count == 0, "SQL must be parameterised; these lines splice a non-constant:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// REQ-NFR-003: positive control for the scan above - a statement that splices a method argument
    /// is reported, one that splices a file constant is not.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-003 the SQL splice scan flags a spliced argument (positive control)")]
    public void SqlSpliceScanMatchesSyntheticViolation()
    {
        Assert.Contains("title", SplicedNames("var sql = $\"SELECT * FROM Post WHERE Title = '{title}'\";"));
        Assert.Contains("title", SplicedNames("var sql = \"SELECT * FROM Post WHERE Title = '\" + title + \"'\";"));
        Assert.Contains("PostColumns", SplicedNames("$\"SELECT {PostColumns} FROM Post\""));
        Assert.Empty(SplicedNames("\"SELECT * FROM Post WHERE PostId = @PostId\""));
    }

    // ---------------------------------------------------------------- REQ-NFR-004

    /// <summary>
    /// REQ-NFR-004: the host redirects HTTP to HTTPS everywhere and adds HSTS outside Development.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-004 the host redirects to HTTPS and sends HSTS outside Development")]
    public void HttpsIsEnforcedOutsideDevelopment()
    {
        var program = Read("source", "TechieBlog", "Program.cs");

        Assert.Matches(@"app\.UseHttpsRedirection\(\);", program);
        Assert.Matches(@"if\s*\(\s*!app\.Environment\.IsDevelopment\(\)\s*\)\s*\{[^}]*app\.UseHsts\(\);", program);
    }

    // ---------------------------------------------------------------- REQ-NFR-005

    /// <summary>
    /// REQ-NFR-005: the HTTP limiter partitions credential-handling paths per client address and
    /// leaves every other path unlimited. The limiter type lives in the host's top-level
    /// <c>Program.cs</c>, which the test project cannot reference, so the policy is read from source.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-005 sign-in paths are limited per client address and other paths are not")]
    public void AuthPathsArePartitionedPerClient()
    {
        var program = Read("source", "TechieBlog", "Program.cs");

        foreach (var path in new[] { "/login", "/forgot-password", "/reset-password", "/change-password" })
            Assert.Contains($"\"{path}\"", program);

        Assert.Contains("return $\"auth:{clientIp}\";", program);
        Assert.Contains("return UnlimitedPartitionKey;", program);
        Assert.Contains("RateLimitPartition.GetNoLimiter(partitionKey)", program);
        Assert.Contains("RateLimitPartition.GetFixedWindowLimiter(partitionKey", program);
        var limit = Regex.Match(program, @"DefaultPermitLimit\s*=\s*(\d+);");
        Assert.True(limit.Success && int.Parse(limit.Groups[1].Value) is >= 1 and <= 60, "the default permit limit must be a small positive number");
    }

    /// <summary>
    /// REQ-NFR-005: past the limit the host answers HTTP 429 with a Retry-After header and a body.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-005 a refused request answers 429 with Retry-After and an explanation")]
    public void RejectedRequestsAnswer429WithRetryAfter()
    {
        var program = Read("source", "TechieBlog", "Program.cs");

        Assert.Contains("options.RejectionStatusCode = StatusCodes.Status429TooManyRequests", program);
        Assert.Contains("Headers.RetryAfter", program);
        Assert.Contains("app.UseRateLimiter();", program);
    }

    // ---------------------------------------------------------------- REQ-NFR-006

    /// <summary>
    /// REQ-NFR-006: Markdown reaching a <c>MarkupString</c> has script, event handlers and script
    /// URLs removed.
    /// </summary>
    [Theory(DisplayName = "REQ-NFR-006 hostile markdown is sanitised before it can reach the page")]
    [InlineData("<script>alert(1)</script>hello", "<script")]
    [InlineData("<img src=x onerror=alert(1)>", "<img")]
    [InlineData("[click](javascript:alert(1))", "href=\"javascript:")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>", "<iframe")]
    [InlineData("<a href=\"x\" onclick=\"steal()\">x</a>", "<a href=\"x\"")]
    public void MarkdownIsSanitised(string hostile, string forbiddenMarkup)
    {
        var html = new MarkdownRenderer().ToHtml(hostile);

        Assert.DoesNotContain(forbiddenMarkup, html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// REQ-NFR-006: ordinary markdown still renders, so the sanitiser is not trivially deleting everything.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-006 benign markdown still renders as HTML (positive control)")]
    public void BenignMarkdownStillRenders()
    {
        var html = new MarkdownRenderer().ToHtml("# Title\n\nSome **bold** text and a [link](https://example.com).");

        Assert.Contains("<h1", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("href=\"https://example.com\"", html);
    }

    // ---------------------------------------------------------------- REQ-NFR-007

    /// <summary>
    /// REQ-NFR-007: every page shell declares the document language (WCAG 3.1.1).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-007 the page shell declares the document language")]
    public void PageShellDeclaresLanguage()
    {
        Assert.Matches(@"<html\s+lang=""en""", Read("source", "TechieBlog", "Components", "App.razor"));
    }

    // ---------------------------------------------------------------- REQ-NFR-010

    /// <summary>
    /// REQ-NFR-010: the site stylesheet adapts at four breakpoints (576, 768, 992 and 1200 px).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-010 the stylesheet defines the four responsive breakpoints")]
    public void StylesheetHasFourBreakpoints()
    {
        var css = Read("source", "BlogUI", "wwwroot", "site.css");

        foreach (var width in new[] { 576, 768, 992, 1200 })
            Assert.Matches($@"@media[^{{]*\(min-width:\s*{width}px\)", css);
    }

    // ---------------------------------------------------------------- REQ-NFR-011

    /// <summary>
    /// REQ-NFR-011: PostgreSQL through Npgsql, with numbered DbUp migration scripts that apply in order.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-011 migrations are numbered, unique and applied by DbUp against PostgreSQL")]
    public void MigrationsAreNumberedAndRunByDbUp()
    {
        // Arrange
        var scripts = Directory.GetFiles(Path.Combine(Root(), "source", "BlogDb", "PostgresScripts"), "*.sql")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var numbers = scripts.Select(name => Regex.Match(name!, @"^(\d{3})-\S+\.sql$")).ToList();
        var project = Read("source", "BlogDb", "BlogDb.csproj");

        // Assert
        Assert.NotEmpty(scripts);
        Assert.All(numbers, match => Assert.True(match.Success, "a script does not start with a three-digit number and a dash"));
        var values = numbers.Select(match => int.Parse(match.Groups[1].Value)).ToList();
        Assert.Equal(values.Count, values.Distinct().Count());
        Assert.Equal(values.OrderBy(value => value), values);
        Assert.Contains("dbup-postgresql", project);
        Assert.Contains("Npgsql", project);
        Assert.Contains("PostgresScripts", project);
    }

    // ---------------------------------------------------------------- REQ-NFR-013

    /// <summary>
    /// REQ-NFR-013: both heads (web host and BlogApp) configure a Serilog rolling file sink.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-013 the web host and BlogApp both write a Serilog rolling file")]
    public void EveryHeadWritesARollingFile()
    {
        var host = Read("source", "TechieBlog", "Program.cs");
        var app = Read("source", "BlogApp", "MauiProgram.cs");

        Assert.Matches(@"WriteTo\.File\(", host);
        Assert.Matches(@"WriteTo\.File\(", app);
        Assert.Contains("rollingInterval", host);
        Assert.Contains("rollingInterval", app);
    }

    // ---------------------------------------------------------------- REQ-NFR-014

    /// <summary>
    /// REQ-NFR-014: the host maps /health, /health/ready and /healthz, and the ready set verifies the
    /// database, the critical services and the schema.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-014 the host maps health endpoints that check the database and critical services")]
    public void HealthEndpointsCheckDatabaseAndServices()
    {
        var program = Read("source", "TechieBlog", "Program.cs");

        Assert.Contains("public const string Path = \"/healthz\";", program);
        Assert.Contains("app.MapHealthChecks(\"/health\"", program);
        Assert.Contains("app.MapHealthChecks(\"/health/ready\"", program);
        Assert.Contains("app.MapHealthChecks(DeploymentHealthProbe.Path", program);
        Assert.Contains("AddCheck<DatabaseHealthCheck>", program);
        Assert.Contains("AddCheck<CriticalServicesHealthCheck>", program);
    }

    // ---------------------------------------------------------------- REQ-NFR-015

    /// <summary>
    /// REQ-NFR-015: the correlation middleware reuses an inbound id, falls back to the trace
    /// identifier, caps the length, echoes the id on the response, pushes it into the log context,
    /// runs early in the pipeline, and every log template prints it. The middleware lives in the
    /// host project, which the test project does not reference, so it is read from source.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-015 the correlation id is resolved per request, echoed and printed in every log line")]
    public void CorrelationIdIsWiredThroughRequestAndLogs()
    {
        var middleware = Read("source", "TechieBlog", "Middleware", "CorrelationIdMiddleware.cs");
        var program = Read("source", "TechieBlog", "Program.cs");

        Assert.Contains("HeaderName = \"X-Correlation-ID\"", middleware);
        Assert.Contains("context.Request.Headers[HeaderName]", middleware);
        Assert.Contains("context.TraceIdentifier", middleware);
        Assert.Contains("const int maxLength = 64;", middleware);
        Assert.Contains("context.Response.Headers[HeaderName] = correlationId", middleware);
        Assert.Contains("LogContext.PushProperty(LogPropertyName, correlationId)", middleware);
        Assert.Contains("[{CorrelationId}]", program);
        Assert.Contains("Enrich.FromLogContext()", program);
        Assert.Contains("app.UseMiddleware<CorrelationIdMiddleware>();", program);
    }

    // ---------------------------------------------------------------- REQ-NFR-016

    /// <summary>
    /// REQ-NFR-016: the automated test project exists and references xUnit and bUnit.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-016 the test project references xUnit and bUnit")]
    public void TestProjectUsesXunitAndBunit()
    {
        var project = Read("tests", "TechieBlog.Tests", "TechieBlog.Tests.csproj");

        Assert.Matches(@"PackageReference\s+Include=""xunit(\.v3)?""", project);
        Assert.Matches(@"PackageReference\s+Include=""bunit""", project);
    }

    // ---------------------------------------------------------------- REQ-NFR-019

    /// <summary>
    /// REQ-NFR-019: password-reset tokens live in a database table behind a repository, not in
    /// process memory, so they survive a restart and work across instances.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-019 password-reset tokens are persisted in a table, not held in memory")]
    public void ResetTokensArePersisted()
    {
        var migration = Read("source", "BlogDb", "PostgresScripts", "017-SecurityAndTokenPersistence.sql");
        var auth = Read("source", "BlogEngine", "Services", "AuthSvc.cs");

        Assert.Matches(@"CREATE\s+TABLE\s+(IF\s+NOT\s+EXISTS\s+)?PasswordResetToken", migration);
        Assert.Contains("IPasswordResetTokenRepo", auth);
        Assert.DoesNotMatch(@"static\s+(readonly\s+)?(Concurrent)?Dictionary<", auth);
        Assert.DoesNotMatch(@"IMemoryCache|MemoryCache\b", auth);
    }

    // ---------------------------------------------------------------- REQ-NFR-020

    /// <summary>
    /// REQ-NFR-020: the legacy artifacts are gone - one AccessDenied page, no FluentDemo project, no
    /// MySql.Data package.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-020 no duplicate AccessDenied page, FluentDemo or MySql.Data remains")]
    public void LegacyArtifactsAreRemoved()
    {
        var accessDenied = SourceFiles("source", "AccessDenied*").ToList();
        var demoFiles = SourceFiles("source", "*FluentDemo*").ToList();
        var packageReferences = SourceFiles("source", "*.csproj").Concat(SourceFiles("tests", "*.csproj"))
            .Where(project => Regex.IsMatch(File.ReadAllText(project), @"Include=""(MySql\.Data|MySqlConnector|Microsoft\.FluentUI[^""]*)"""))
            .ToList();

        Assert.Single(accessDenied);
        Assert.Empty(demoFiles);
        Assert.Empty(packageReferences);
    }

    // ---------------------------------------------------------------- REQ-NFR-022

    /// <summary>
    /// REQ-NFR-022: nullable reference types are enabled in every project.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-022 every project enables nullable reference types")]
    public void EveryProjectEnablesNullable()
    {
        var projects = SourceFiles("source", "*.csproj").Concat(SourceFiles("tests", "*.csproj"))
            .Where(project => !project.Contains(".artifacts"))
            .ToList();

        Assert.True(projects.Count >= 6, $"only {projects.Count} projects found");
        foreach (var project in projects)
            Assert.True(Regex.IsMatch(File.ReadAllText(project), @"<Nullable>\s*enable\s*</Nullable>"), $"{project} does not enable nullable");
    }

    // ---------------------------------------------------------------- REQ-NFR-026

    private static List<string> BlockingRepositoryCalls(IEnumerable<string> files, out int blockingNameCount)
    {
        var repositoryInterfaces = typeof(BlogModels.ISubscriberRepo).Assembly
            .GetTypes()
            .Where(candidate => candidate.IsInterface && (candidate.Namespace ?? string.Empty).StartsWith("BlogModels", StringComparison.Ordinal) && (candidate.Name.EndsWith("Repo", StringComparison.Ordinal) || candidate.Name.EndsWith("Repository", StringComparison.Ordinal)))
            .ToList();
        var blockingNames = repositoryInterfaces
            .SelectMany(repositoryInterface => repositoryInterface.GetMethods())
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Select(method => method.Name[..^5])
            .Where(name => repositoryInterfaces.Any(i => i.GetMethods().Any(m => m.Name == name)))
            .ToHashSet();
        blockingNameCount = blockingNames.Count;
        var call = new Regex(@"\b\w*[rR]epo\w*\.(?<name>\w+)\(");
        var offenders = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                if (lines[index].TrimStart().StartsWith("//"))
                    continue;

                foreach (Match match in call.Matches(lines[index]))
                {
                    if (blockingNames.Contains(match.Groups["name"].Value))
                        offenders.Add($"{Path.GetRelativePath(Root(), file).Replace('\\', '/')}:{index + 1} {lines[index].Trim()}");
                }
            }
        }

        return offenders;
    }

    /// <summary>
    /// REQ-NFR-026: no engine service, page, component or host endpoint calls a blocking repository
    /// member that has an async twin. The acceptance line is "no synchronous DbAccess call remains",
    /// so the whole engine is scanned; the failure message lists every remaining call as file:line.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-026 no synchronous repository call remains anywhere in the engine, pages or host")]
    public void NoSynchronousRepositoryCallRemains()
    {
        var files = SourceFiles("source/BlogEngine/Services", "*.cs")
            .Concat(SourceFiles("source/BlogUI", "*.cs")).Concat(SourceFiles("source/BlogUI", "*.razor"))
            .Concat(SourceFiles("source/TechieBlog", "*.cs"));

        var offenders = BlockingRepositoryCalls(files, out var blockingNameCount);

        Assert.True(blockingNameCount > 0, "no sync/async repository pairs were found; the scan proves nothing");
        Assert.True(offenders.Count == 0, $"{offenders.Count} synchronous repository call(s) remain:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The caller layers alone (pages, components, host): a blocking call there would hold a Blazor
    /// circuit thread. Kept separate so the engine-wide row test above names the remaining work.
    /// </summary>
    [Fact]
    public void PagesAndHostMakeNoSynchronousRepositoryCall()
    {
        var files = SourceFiles("source/BlogUI", "*.cs").Concat(SourceFiles("source/BlogUI", "*.razor")).Concat(SourceFiles("source/TechieBlog", "*.cs"));

        var offenders = BlockingRepositoryCalls(files, out _);

        Assert.True(offenders.Count == 0, "blocking repository calls in callers:\n" + string.Join("\n", offenders));
    }

    // ---------------------------------------------------------------- REQ-NFR-032

    /// <summary>
    /// REQ-NFR-032: no namespace in the engine is named BlogSvc, so the BlogSvc type binds normally.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-032 no namespace shadows the BlogSvc type")]
    public void NoNamespaceShadowsBlogSvc()
    {
        var shadows = SourceFiles("source", "*.cs")
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(entry => Regex.IsMatch(entry.line, @"^\s*namespace\s+([\w.]+\.)?BlogSvc\s*[;{]?\s*$"))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.index + 1}")
            .ToList();

        Assert.Empty(shadows);
        Assert.NotNull(typeof(BlogEngine.Services.BlogSvc));
    }

    // ---------------------------------------------------------------- REQ-NFR-036

    /// <summary>
    /// REQ-NFR-036: build and smoke output directories are listed in .gitignore.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-036 build and smoke output directories are gitignored")]
    public void BuildAndSmokeOutputIsIgnored()
    {
        var lines = Read(".gitignore").Split('\n').Select(line => line.Trim()).ToHashSet();

        Assert.Contains("[Bb]in/", lines);
        Assert.Contains("[Oo]bj/", lines);
        Assert.Contains("logs/", lines);
        Assert.Contains(".smokeout/", lines);
        Assert.Contains("tests/.artifacts/", lines);
    }

    // ---------------------------------------------------------------- REQ-NFR-037

    /// <summary>
    /// REQ-NFR-037: neither head hard-codes its own log folder - both resolve one configured directory
    /// and both bound size and count.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-037 both heads resolve one log directory and bound file size and count")]
    public void LogFoldersAreResolvedAndBounded()
    {
        var host = Read("source", "TechieBlog", "Program.cs");
        var app = Read("source", "BlogApp", "MauiProgram.cs");

        Assert.Contains("LogFileSettings", host);
        Assert.Contains("fileSizeLimitBytes", host);
        Assert.Contains("retainedFileCountLimit", host);
        Assert.Contains("rollOnFileSizeLimit: true", host);
        Assert.Contains("fileSizeLimitBytes", app);
        Assert.Contains("retainedFileCountLimit", app);
        Assert.DoesNotMatch(@"WriteTo\.File\(\s*""logs[/\\]", host);
    }

    // ---------------------------------------------------------------- REQ-NFR-038

    /// <summary>
    /// REQ-NFR-038: the deploy workflow builds and pushes an image, deploys it by compose over SSH and
    /// then probes the public site. That the VPS actually serves the new build is NOT observable from
    /// a unit test; the live check is the workflow's own verify job.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-038 the deploy workflow builds an image, deploys it and probes the live site")]
    public void DeployWorkflowHasBuildDeployVerifyStages()
    {
        var workflow = Read(".github", "workflows", "deploy.yml");
        var dockerfile = Read("Dockerfile");

        Assert.Matches(@"(?s)(?m)^on:\s*\r?\n\s+push:.*?branches:\s*\[main\]", workflow);
        Assert.Matches(@"(?m)^  build:", workflow);
        Assert.Matches(@"(?m)^  deploy:", workflow);
        Assert.Matches(@"(?m)^  verify:", workflow);
        Assert.Contains("ghcr.io", workflow);
        Assert.Contains("Build and push", workflow);
        Assert.Contains("HEALTH_PATH: /healthz", workflow);
        Assert.Contains("FROM ", dockerfile);
        Assert.Contains("ENTRYPOINT", dockerfile);
        Assert.True(File.Exists(Path.Combine(Root(), "deploy", "docker-compose.template.yml")), "deploy/docker-compose.template.yml is missing");
    }

    // ---------------------------------------------------------------- REQ-NFR-039

    /// <summary>
    /// REQ-NFR-039: a green deploy is gated on the migrations - the pipeline's probe path serves the
    /// ready checks, and those include the schema check that compares the journal to the scripts
    /// folder. That the production database actually holds every migration is NOT observable here.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-039 the deploy probe path serves a ready check that includes the migration journal")]
    public void DeployProbeIncludesTheSchemaCheck()
    {
        var program = Read("source", "TechieBlog", "Program.cs");
        var workflow = Read(".github", "workflows", "deploy.yml");

        Assert.Contains("AddCheck<SchemaMigrationHealthCheck>(\"schema\", tags: new[] { HealthCheckTags.Ready })", program);
        Assert.Contains("registration.Tags.Contains(HealthCheckTags.Ready)", program);
        Assert.Contains("CacheOutput(policy => policy.NoCache())", program);
        Assert.Contains("url=\"https://${DOMAIN}${HEALTH_PATH}\"", workflow);
        Assert.Contains("did not return 200", workflow);
    }
}
