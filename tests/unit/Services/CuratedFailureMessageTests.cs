using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Tests for REQ-NFR-031 — a failed <c>Result</c> carries a curated sentence, the exception detail
/// goes to the log, and the log line carries the request's correlation id.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>Result.Failure</c>'s own documentation says the message "must never
/// contain a stack trace, a SQL fragment or anything else that would help an attacker", yet thirty
/// call sites across <c>BlogSvc</c>, <c>TagSvc</c>, <c>CategorySvc</c> and <c>SeriesSvc</c>
/// interpolated <c>ex.Message</c> straight into it. Every affected surface is admin-gated today, but
/// nothing in those services enforces that, so the disclosure would go live the moment one were
/// reached anonymously. These tests pin both halves of the fix, because pinning only the new wording
/// would let the disclosure return unnoticed.</para>
///
/// <para><b>The correlation id is not a second mechanism, and is not this layer's to fetch.</b>
/// REQ-NFR-015 already stamps one: <c>CorrelationIdMiddleware</c> resolves it per request and pushes
/// it onto the log context for that request's lifetime, and the host's sink template renders it as
/// <c>[{CorrelationId}]</c>. It arrives <i>ambiently</i>, which is exactly why a
/// <c>BlogEngine</c> service neither reads nor forwards it — the coding standard forbids this layer
/// from referencing the logging implementation at all, and the architecture's dependency direction
/// makes the host unreachable from here. What these tests can and do prove through
/// <see cref="ILogger{TCategoryName}"/> is the half that belongs to <c>BlogSvc</c>: the exception
/// reaches the logger, and it reaches it inside whatever correlation scope the caller established, so
/// the ambient id is attached to it. See
/// <see cref="ExceptionDetailReachesTheLoggerInsideTheCallersCorrelationScope"/>.</para>
///
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute, and <see cref="RecordingLogger{T}"/> — the spy
/// the rest of this suite already uses. No Serilog: <c>BlogEngine</c> is a class library and logs
/// only through <c>Microsoft.Extensions.Logging.Abstractions</c>, so asserting through a Serilog sink
/// would test the host's wiring rather than this service. No database.</para>
/// </remarks>
public class CuratedFailureMessageTests
{
    /// <summary>
    /// The log-context property name <c>CorrelationIdMiddleware</c> publishes under (REQ-NFR-015).
    /// </summary>
    private const string CorrelationProperty = "CorrelationId";

    /// <summary>
    /// Text no user-facing message may ever contain — it stands in for the SQL fragments and
    /// constraint names a real provider exception carries.
    /// </summary>
    private const string LeakyDetail = "23505: duplicate key value violates unique constraint \"IdxPostSlug\"";

    /// <summary>
    /// Every mutation across all four services converts a persistence exception into a curated
    /// sentence, and not one of them echoes the provider's text.
    /// </summary>
    /// <remarks>
    /// Driven as one table rather than thirty near-identical tests: the rule is uniform, and a single
    /// case per service member is what makes a newly reintroduced <c>ex.Message</c> impossible to miss.
    /// </remarks>
    [Fact(DisplayName = "REQ-NFR-031 no service mutation echoes exception text in its failure message")]
    public async Task NoMutationEchoesTheExceptionText()
    {
        // Arrange & Act
        var messages = await RunEveryFailingMutation();

        // Assert
        Assert.NotEmpty(messages);
        Assert.All(messages, message =>
        {
            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.DoesNotContain(LeakyDetail, message);
            Assert.DoesNotContain("23505", message);
            Assert.DoesNotContain("IdxPostSlug", message);
            Assert.EndsWith("Please try again later.", message);
        });
    }

    /// <summary>
    /// Every one of those failures is logged at Error with the exception attached, so nothing is lost
    /// by withholding the detail from the caller — the fix moves the information, it does not discard
    /// it.
    /// </summary>
    [Fact]
    public async Task EveryCuratedFailureIsLoggedWithItsException()
    {
        // Arrange
        var logger = new RecordingLogger<BlogSvc>();
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(LivePost(5));
        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException(LeakyDetail));

        // Act
        var result = await new BlogSvc(repo, logger).UpdatePostAsync(LivePost(5));

        // Assert
        Assert.Equal("Failed to update post. Please try again later.", result.ErrorMessage);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(LeakyDetail, error.Error?.Message);
        Assert.Contains("5", error.Message);
    }

    /// <summary>
    /// THE CORRELATION-ID CLAUSE, ASSERTED THROUGH THE ABSTRACTION THIS LAYER IS ALLOWED TO USE.
    /// A correlation id reaches a log event ambiently — <c>CorrelationIdMiddleware</c> establishes it
    /// for the request and every event raised inside that window inherits it (REQ-NFR-015). The half
    /// that belongs to <c>BlogSvc</c>, and the half this test pins, is that the exception is handed to
    /// <see cref="ILogger{TCategoryName}"/> <i>inside</i> the caller's correlation scope rather than
    /// swallowed or deferred out of it — while the caller's own message carries neither the exception
    /// text nor the id.
    /// </summary>
    /// <remarks>
    /// The scope is modelled with <c>ILogger.BeginScope</c> because that is the ambient carrier the
    /// abstraction offers; in the host the carrier is the Serilog log context the middleware pushes.
    /// Either way the service is the same: it knows nothing about the id and simply logs.
    /// </remarks>
    [Fact]
    public async Task ExceptionDetailReachesTheLoggerInsideTheCallersCorrelationScope()
    {
        // Arrange
        const string correlationId = "0HMV9C49I3A0F:00000001";
        var logger = new ScopeCapturingLogger<BlogSvc>();
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException(LeakyDetail));
        var service = new BlogSvc(repo, logger);

        // Act — inside the correlation scope the host establishes for the request.
        string? errorMessage;
        using (logger.BeginScope(new Dictionary<string, object> { [CorrelationProperty] = correlationId }))
        {
            errorMessage = (await service.CreatePostAsync(LivePost())).ErrorMessage;
        }

        // Assert — the operator gets the detail and the id; the caller gets neither.
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal(LeakyDetail, error.Error?.Message);
        Assert.Equal(correlationId, Assert.Contains(CorrelationProperty, error.Scope));
        Assert.Equal("Failed to create post. Please try again later.", errorMessage);
        Assert.DoesNotContain(LeakyDetail, errorMessage);
        Assert.DoesNotContain(correlationId, errorMessage);
    }

    /// <summary>
    /// Drives one failing mutation on every affected member of all four services and collects the
    /// message each one handed back.
    /// </summary>
    /// <returns>Every user-facing failure message produced by a persistence exception.</returns>
    private static async Task<List<string>> RunEveryFailingMutation()
    {
        var messages = new List<string>();

        var postRepo = Substitute.For<IBlogPostRepo>();
        var live = LivePost(5);
        live.Published = true;
        var scheduled = LivePost(6);
        scheduled.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        postRepo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(live);
        postRepo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(scheduled);
        postRepo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(LivePost(7));
        postRepo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        postRepo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        postRepo.When(r => r.SoftDeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        var blog = new BlogSvc(postRepo, new RecordingLogger<BlogSvc>());
        messages.Add((await blog.CreatePostAsync(LivePost())).ErrorMessage!);
        messages.Add((await blog.UpdatePostAsync(LivePost(7))).ErrorMessage!);
        messages.Add((await blog.DeletePostAsync(7)).ErrorMessage!);
        messages.Add((await blog.UnpublishPostAsync(5)).ErrorMessage!);
        messages.Add((await blog.QuickPublishAsync(7)).ErrorMessage!);
        messages.Add((await blog.CancelScheduleAsync(6)).ErrorMessage!);

        var categoryRepo = Substitute.For<ICategoryRepo>();
        categoryRepo.GetSingleAsync(3, Arg.Any<CancellationToken>()).Returns(new Category { CategoryId = 3, CategoryName = "Web" });
        categoryRepo.When(r => r.InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        categoryRepo.When(r => r.UpdateAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        categoryRepo.When(r => r.DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        var categories = new CategorySvc(categoryRepo, new RecordingLogger<CategorySvc>());
        messages.Add((await categories.CreateCategoryAsync(new Category { CategoryName = "Web" })).ErrorMessage!);
        messages.Add((await categories.UpdateCategoryAsync(new Category { CategoryId = 3, CategoryName = "Web" })).ErrorMessage!);
        messages.Add((await categories.DeleteCategoryAsync(3)).ErrorMessage!);

        var tagRepo = Substitute.For<IBlogTagRepo>();
        tagRepo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(new BlogTag { TagId = 4, TagName = "Blazor" });
        tagRepo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        tagRepo.When(r => r.UpdateAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        tagRepo.When(r => r.DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        var tags = new TagSvc(tagRepo, new RecordingLogger<TagSvc>());
        messages.Add((await tags.CreateTagAsync(new BlogTag { TagName = "Blazor" })).ErrorMessage!);
        messages.Add((await tags.UpdateTagAsync(new BlogTag { TagId = 4, TagName = "Blazor" })).ErrorMessage!);
        messages.Add((await tags.DeleteTagAsync(4)).ErrorMessage!);

        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        seriesRepo.GetSingleAsync(9, Arg.Any<CancellationToken>()).Returns(new BlogSeries { SeriesId = 9, Name = "Alpha" });
        seriesRepo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        seriesRepo.When(r => r.UpdateAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        seriesRepo.When(r => r.DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())).Do(_ => throw Leak());
        var series = new SeriesSvc(seriesRepo, Substitute.For<IBlogPostRepo>(), new RecordingLogger<SeriesSvc>());
        messages.Add((await series.CreateSeriesAsync(new BlogSeries { Name = "Alpha" })).ErrorMessage!);
        messages.Add((await series.UpdateSeriesAsync(new BlogSeries { SeriesId = 9, Name = "Alpha" })).ErrorMessage!);
        messages.Add((await series.DeleteSeriesAsync(9)).ErrorMessage!);

        return messages;
    }

    /// <summary>
    /// Builds the provider-shaped exception every arranged repository member throws.
    /// </summary>
    /// <returns>An exception whose message carries text no user may ever be shown.</returns>
    private static InvalidOperationException Leak()
    {
        return new InvalidOperationException(LeakyDetail);
    }

    /// <summary>
    /// Builds a post that passes every validation rule.
    /// </summary>
    /// <param name="postId">Identifier to carry; zero means "never persisted".</param>
    /// <returns>A valid, live post.</returns>
    private static BlogPost LivePost(long postId = 0)
    {
        return new BlogPost
        {
            PostID = postId,
            Title = "My Title",
            Slug = "my-title",
            PostContent = "Body copy that is long enough to be real."
        };
    }

    /// <summary>
    /// <see cref="RecordingLogger{T}"/> with one addition: it also records the ambient scope each
    /// entry was written inside.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists:</b> the shared <see cref="RecordingLogger{T}"/> ignores scopes, and the
    /// correlation id is delivered by a scope rather than by anything the service passes. This is the
    /// same spy pattern, extended by exactly that one field — not a second logging mechanism.</para>
    /// <para><b>Side Effects:</b> None beyond accumulating entries in memory.</para>
    /// </remarks>
    /// <typeparam name="T">Log category, matching the service under test.</typeparam>
    private sealed class ScopeCapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message, Exception? Error, IReadOnlyDictionary<string, object> Scope)> entries = new();
        private readonly Dictionary<string, object> ambient = new();

        /// <summary>
        /// Gets every entry written to this logger, in order, with the scope it was written inside.
        /// </summary>
        public IReadOnlyList<(LogLevel Level, string Message, Exception? Error, IReadOnlyDictionary<string, object> Scope)> Entries => entries;

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var keys = new List<string>();
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                foreach (var pair in pairs)
                {
                    ambient[pair.Key] = pair.Value;
                    keys.Add(pair.Key);
                }
            }

            return new ScopeHandle(ambient, keys);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add((logLevel, formatter(state, exception), exception, new Dictionary<string, object>(ambient)));
        }

        /// <summary>
        /// Removes the keys one <c>BeginScope</c> call added, when that scope is disposed.
        /// </summary>
        private sealed class ScopeHandle : IDisposable
        {
            private readonly Dictionary<string, object> ambient;
            private readonly List<string> keys;

            /// <summary>
            /// Captures the scope's keys so they can be withdrawn again.
            /// </summary>
            /// <param name="ambient">The logger's ambient property bag.</param>
            /// <param name="keys">The keys this scope contributed.</param>
            public ScopeHandle(Dictionary<string, object> ambient, List<string> keys)
            {
                this.ambient = ambient;
                this.keys = keys;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                foreach (var key in keys)
                {
                    ambient.Remove(key);
                }
            }
        }
    }
}
