using System.Reflection;
using System.Threading;
using BlogEngine.Services;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Unit tests for the async service surface added by REQ-NFR-026 stage 3.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stage 3 moves the service layer onto the repositories' async members so a
/// request stops parking a thread-pool thread for the whole database round trip. Stage 2 proved
/// that "the build is green" says nothing about whether a conversion actually happened — a member
/// can inherit the synchronous bridge and compile perfectly. The same hazard exists one layer up:
/// a service can expose a <c>Task</c>-returning member whose body calls the blocking repository
/// twin, and nothing in the compiler, the type system or a fake-backed behavioural test will
/// notice. These tests pin the two properties that ARE mechanically checkable.</para>
///
/// <para><b>What is checked:</b></para>
/// <list type="number">
///   <item>Every service converted in this stage exposes the expected <c>…Async</c> twin, so a
///     caller that wants the non-blocking path has one to move to.</item>
///   <item>Every public <c>…Async</c> member on those services takes a
///     <see cref="CancellationToken"/> as its LAST parameter. Cancellation stopping at the service
///     boundary is precisely the gap stage 3 exists to close, and a twin added without a token
///     silently re-opens it.</item>
/// </list>
///
/// <para><b>What is deliberately NOT checked here:</b> that each body awaits the async repository
/// member rather than blocking on the sync one. Reflection cannot see a method body, and a fake
/// answers both twins identically, so that property is verified by review against
/// <c>docs/async-conversion-pattern.md</c> and by the Playwright smoke that exercises the real
/// database (<c>tests/verify/cluster-k-async-perf.spec.ts</c>).</para>
///
/// <para><b>Dependencies:</b> xUnit and reflection over the built <c>BlogEngine</c> assembly. No
/// database, no host, no container.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite. A failure means a service lost its async
/// twin or gained one without cancellation, and the stage-3 contract has regressed.</para>
/// </remarks>
public class AsyncServiceSurfaceTests
{
    /// <summary>
    /// The services converted in REQ-NFR-026 stage 3, paired with the async members each must
    /// expose. Names only — the parameter list is asserted separately by the cancellation test, so
    /// this table stays readable as the surface grows.
    /// </summary>
    public static TheoryData<Type, string> ExpectedAsyncMembers()
    {
        var data = new TheoryData<Type, string>();

        void Add(Type serviceType, params string[] memberNames)
        {
            foreach (var memberName in memberNames)
            {
                data.Add(serviceType, memberName);
            }
        }

        Add(
            typeof(SeriesSvc),
            "GetAllSeriesAsync",
            "GetAllWithCountsAsync",
            "GetSeriesAsync",
            "GetSeriesBySlugAsync",
            "GetPostsInSeriesAsync",
            "GetNextPartNumberAsync",
            "CreateSeriesAsync",
            "UpdateSeriesAsync",
            "SaveSeriesAsync",
            "DeleteSeriesAsync",
            "GetSeriesNavigationAsync");

        Add(
            typeof(TagSvc),
            "GetAllTagsAsync",
            "GetAllWithCountsAsync",
            "GetSingleTagAsync",
            "GetTagBySlugAsync",
            "SearchTagsAsync",
            "GetOrCreateTagAsync",
            "CreateTagAsync",
            "UpdateTagAsync",
            "SaveTagAsync",
            "DeleteTagAsync",
            "GetTagsForPostAsync",
            "SetTagsForPostAsync",
            "GetPostsByTagAsync",
            "GetPostCountByTagAsync");

        Add(
            typeof(UserStatsSvc),
            "GetStatsForUserAsync",
            "GetStatsForCategoryAsync",
            "GetStatAsync",
            "CreateStatAsync",
            "UpdateStatAsync",
            "SaveStatAsync",
            "DeleteStatAsync",
            "ReorderStatsAsync");

        Add(
            typeof(SubscriberSvc),
            "SubscribePendingAsync",
            "SubscribeAsync",
            "UnsubscribeAsync",
            "GetAllSubscribersAsync",
            "GetSubscribersByStatusAsync",
            "SearchSubscribersAsync",
            "UpdateSubscriberStatusAsync",
            "GetSubscriberStatsAsync",
            "GetSubscribersForExportAsync");

        Add(typeof(SitemapSvc), "GenerateSitemapAsync");

        // The last seven blocking Blazor call sites, closed on 2026-08-10. CommentSvc and RatingSvc
        // are NOT listed in ConvertedServices below: their pre-existing async members
        // (SubmitRatingAsync, RejectCommentAsync, the bulk moderation members …) predate this
        // requirement and take no CancellationToken, so the whole-surface gate would fail on members
        // this stage did not touch. The twins added here do carry one, which
        // EveryNewTwinFlowsACancellationToken asserts directly.
        Add(
            typeof(CommentSvc),
            "GetCommentsByPostIdAsync",
            "GetAllCommentsAsync",
            "ApproveCommentAsync",
            "DeleteCommentAsync");

        Add(
            typeof(RatingSvc),
            "GetPostRatingStatsAsync",
            "GetAverageRatingAsync",
            "GetRatingCountAsync");

        return data;
    }

    /// <summary>
    /// The service types whose whole public async surface must flow a cancellation token.
    /// </summary>
    public static TheoryData<Type> ConvertedServices() =>
    [
        typeof(SeriesSvc),
        typeof(TagSvc),
        typeof(UserStatsSvc),
        typeof(SubscriberSvc),
        typeof(SitemapSvc),
        typeof(CategorySvc),
    ];

    /// <summary>
    /// Each service converted in stage 3 exposes the async twin its callers are meant to move to.
    /// </summary>
    /// <remarks>
    /// A missing twin does not break the build — the synchronous member is still there and every
    /// caller still compiles — so without this assertion a dropped conversion is invisible until
    /// someone re-measures throughput.
    /// </remarks>
    /// <param name="serviceType">The service under test.</param>
    /// <param name="memberName">The async member the service must expose.</param>
    [Theory]
    [MemberData(nameof(ExpectedAsyncMembers))]
    public void ConvertedServiceExposesAsyncTwin(Type serviceType, string memberName)
    {
        var found = serviceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.Name == memberName);

        Assert.True(found, $"{serviceType.Name} is missing the async member {memberName}");
    }

    /// <summary>
    /// Every public async member on a converted service takes a cancellation token last.
    /// </summary>
    /// <remarks>
    /// Stage 3's stated gap is that "cancellation stops at the service boundary". A twin added
    /// without a token compiles, runs and looks converted, but a circuit that goes away mid-request
    /// still pays for the whole query — so the token is part of the contract, not a nicety.
    /// </remarks>
    /// <param name="serviceType">The service under test.</param>
    [Theory]
    [MemberData(nameof(ConvertedServices))]
    public void EveryAsyncMemberFlowsACancellationToken(Type serviceType)
    {
        var offenders = serviceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Where(method =>
            {
                var parameters = method.GetParameters();
                return parameters.Length == 0
                    || parameters[^1].ParameterType != typeof(CancellationToken);
            })
            .Select(method => method.Name)
            .Distinct()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{serviceType.Name} async members without a trailing CancellationToken: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// The twins added on 2026-08-10 to close the last seven blocking Blazor call sites, paired
    /// with the service that must expose each with a trailing cancellation token.
    /// </summary>
    public static TheoryData<Type, string> NewlyAddedTwins()
    {
        var data = new TheoryData<Type, string>();

        foreach (var memberName in new[]
                 {
                     "GetCommentsByPostIdAsync", "GetAllCommentsAsync",
                     "ApproveCommentAsync", "DeleteCommentAsync"
                 })
        {
            data.Add(typeof(CommentSvc), memberName);
        }

        foreach (var memberName in new[]
                 {
                     "GetPostRatingStatsAsync", "GetAverageRatingAsync", "GetRatingCountAsync"
                 })
        {
            data.Add(typeof(RatingSvc), memberName);
        }

        return data;
    }

    /// <summary>
    /// Every twin added in this pass takes a cancellation token last.
    /// </summary>
    /// <remarks>
    /// <c>CommentSvc</c> and <c>RatingSvc</c> cannot go through
    /// <see cref="EveryAsyncMemberFlowsACancellationToken"/> yet, because both carry older async
    /// members that predate the requirement and take no token — gating the whole surface would fail
    /// on code this stage was told not to touch. Gating the new members individually keeps the
    /// contract enforceable without pretending the rest of the surface is converted.
    /// </remarks>
    /// <param name="serviceType">The service under test.</param>
    /// <param name="memberName">The twin that must flow a token.</param>
    [Theory]
    [MemberData(nameof(NewlyAddedTwins))]
    public void EveryNewTwinFlowsACancellationToken(Type serviceType, string memberName)
    {
        var method = serviceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SingleOrDefault(candidate => candidate.Name == memberName);

        Assert.True(method is not null, $"{serviceType.Name} is missing the async member {memberName}");

        var parameters = method!.GetParameters();

        Assert.True(
            parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken),
            $"{serviceType.Name}.{memberName} does not take a CancellationToken as its last parameter, so cancellation stops at the service boundary — the exact gap this stage exists to close.");

        Assert.True(
            parameters[^1].HasDefaultValue,
            $"{serviceType.Name}.{memberName} requires an explicit CancellationToken, which every Blazor call site would have to invent. Give it a default.");
    }

    /// <summary>
    /// No converted service exposes an <c>async void</c> member.
    /// </summary>
    /// <remarks>
    /// An <c>async void</c> method cannot be awaited and its exceptions escape to the global
    /// unhandled handler instead of the caller's <c>try</c>, which on a Blazor Server circuit takes
    /// the whole circuit down rather than showing an error. The pattern doc bans it outright; this
    /// makes the ban enforceable.
    /// </remarks>
    /// <param name="serviceType">The service under test.</param>
    [Theory]
    [MemberData(nameof(ConvertedServices))]
    public void NoConvertedServiceExposesAsyncVoid(Type serviceType)
    {
        var offenders = serviceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Where(method => method.ReturnType == typeof(void))
            .Select(method => method.Name)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{serviceType.Name} exposes async void members: {string.Join(", ", offenders)}");
    }
}
