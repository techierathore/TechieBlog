using System.Reflection;
using BlogEngine.DbAccess;
using BlogModels;
using BlogModels.Interfaces;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Contract tests for the async conversion of <see cref="NewsletterRepo"/> (REQ-NFR-026).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> An async conversion is the kind of change that compiles, passes every
/// behavioural test and still delivers nothing, because a repository that quietly inherits the base
/// class's synchronous bridge is indistinguishable from a converted one at the call site. These
/// tests assert the structural facts that tell the two apart, and they pin the deliberate
/// resolution of the return-type clash that made this repository the build break.</para>
///
/// <para><b>Dependencies:</b> xUnit and reflection only — no database and no host, so they run in
/// the ordinary unit pass. Behaviour against real PostgreSQL is covered by the Playwright smoke,
/// because a green build is explicitly not evidence that the SQL still runs.</para>
///
/// <para><b>Usage:</b> A failure here means the repository has regressed to the bridge, has lost a
/// cancellation token, or has re-introduced the duplicate "read everything" member.</para>
/// </remarks>
public class NewsletterRepoAsyncTests
{
    /// <summary>
    /// Every public async member of the repository is declared on the repository itself rather than
    /// inherited from the base class's temporary bridge. A bridged member compiles, answers
    /// correctly and still parks a thread-pool thread for the whole round trip, so this is the check
    /// that tells a conversion apart from the appearance of one. GetOpenConnectionAsync is excluded
    /// because the base implementation is already genuinely async.
    /// </summary>
    [Fact]
    public void OverridesEveryAsyncMember()
    {
        var inherited = AsyncMethodsOf(typeof(NewsletterRepo))
            .Where(method => method.DeclaringType != typeof(NewsletterRepo))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(inherited);
    }

    /// <summary>
    /// Every async member of the repository accepts a cancellation token as its last parameter, so
    /// a reader who abandons a Blazor circuit mid-request stops paying for the query rather than
    /// leaving it to run to completion for nobody.
    /// </summary>
    [Fact]
    public void EveryAsyncMemberTakesCancellationToken()
    {
        var missing = AsyncMethodsOf(typeof(NewsletterRepo))
            .Where(method => !EndsWithCancellationToken(method))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// Every member of the repository's own contract is task-returning and cancellable, so no caller
    /// is forced back onto a blocking path for lack of an async alternative.
    /// </summary>
    [Fact]
    public void ContractIsFullyAsync()
    {
        var members = typeof(INewsletterRepo).GetMethods();

        Assert.NotEmpty(members);
        foreach (var member in members)
        {
            Assert.EndsWith("Async", member.Name, StringComparison.Ordinal);
            Assert.True(typeof(Task).IsAssignableFrom(member.ReturnType), member.Name);
            Assert.True(EndsWithCancellationToken(member), member.Name);
        }
    }

    /// <summary>
    /// The contract's "read everything" member returns exactly what the generic contract's does.
    /// This is the regression guard for the defect that broke the build: once the cancellation token
    /// was added, INewsletterRepo.GetAllAsync and IGenericRepository&lt;Newsletter&gt;.GetAllAsync
    /// collapsed onto one signature differing only by return type, which no single class can
    /// implement (CS0738). Re-widening one of them to IReadOnlyList would reintroduce the break.
    /// </summary>
    [Fact]
    public void ReadEverythingAgreesWithGenericContract()
    {
        var specific = typeof(INewsletterRepo)
            .GetMethod(nameof(INewsletterRepo.GetAllAsync), [typeof(CancellationToken)]);
        var generic = typeof(IGenericRepository<Newsletter>)
            .GetMethod(nameof(IGenericRepository<Newsletter>.GetAllAsync), [typeof(CancellationToken)]);

        Assert.NotNull(specific);
        Assert.NotNull(generic);
        Assert.Equal(generic!.ReturnType, specific!.ReturnType);
    }

    /// <summary>
    /// The repository declares exactly one "read everything" member, so a caller cannot pick a
    /// second, subtly different one. Two members running the same SQL is the duplication the return
    /// type was aligned to remove, not merely a stylistic preference.
    /// </summary>
    [Fact]
    public void DeclaresSingleReadEverythingMember()
    {
        var readEverything = typeof(NewsletterRepo)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == nameof(NewsletterRepo.GetAllAsync))
            .ToList();

        Assert.Single(readEverything);
    }

    /// <summary>
    /// Returns the public async data-access methods of a repository type, excluding the connection
    /// factory member whose base implementation is already genuinely asynchronous.
    /// </summary>
    /// <param name="repoType">The repository type to inspect.</param>
    /// <returns>The async methods that the conversion is responsible for.</returns>
    private static IEnumerable<MethodInfo> AsyncMethodsOf(Type repoType)
    {
        return repoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Where(method => method.Name != nameof(IGenericRepository<Newsletter>.GetOpenConnectionAsync));
    }

    /// <summary>
    /// Reports whether a method's last parameter is a cancellation token.
    /// </summary>
    /// <param name="method">The method to inspect.</param>
    /// <returns><c>true</c> when the token is present and last.</returns>
    private static bool EndsWithCancellationToken(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken);
    }
}
