using System.Reflection;
using System.Text;
using BlogModels;
using BlogModels.Interfaces;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Pins the interaction between NSubstitute and the temporary sync-to-async bridge, because getting
/// it wrong makes a test pass while exercising the opposite of what it claims to.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-016. REQ-NFR-026 added an <c>…Async</c> twin to every repository
/// interface as a C# <b>default interface implementation</b> that forwards to the synchronous member
/// through <c>RepoSyncBridge</c>. That is what let 24 repositories and every hand-written test double
/// keep compiling through the conversion. It also created a trap that has already cost this project a
/// real defect.</para>
///
/// <para><b>The trap.</b> Castle DynamicProxy — the engine under NSubstitute — intercepts default
/// interface implementations exactly as it intercepts abstract ones. A substitute therefore does
/// <b>not</b> fall through to the bridge. Stub the synchronous member, and the async twin still
/// returns a completed task holding <c>null</c>, silently, with no configuration error. Cluster K hit
/// this when a service moved from <c>GetByEmail</c> to <c>GetByEmailAsync</c>: the substitute reported
/// a known subscriber as brand new, and the service mailed a duplicate confirmation email. Nothing in
/// the compiler, the type system or the test output pointed at the cause.</para>
///
/// <para><b>What this class does about it.</b> Two things, deliberately kept separate:</para>
/// <list type="number">
///   <item><see cref="StubbingASyncMemberDoesNotAnswerItsAsyncTwin"/> proves the behaviour on a real
///     repository interface rather than asserting it from memory, so if a future NSubstitute or
///     runtime change makes substitutes fall through, this test fails and the guidance below can be
///     retired instead of being cargo-culted.</item>
///   <item><see cref="EveryBridgedAsyncTwinIsADefaultImplementation"/> enumerates the trap's blast
///     radius across every repository interface, so the inventory is generated rather than
///     remembered.</item>
/// </list>
///
/// <para><b>The rule for anyone writing a repository fake:</b> stub the member the service under test
/// actually calls, and when both twins exist, stub both. A service's synchronous method reaches the
/// synchronous repository member and its async method reaches the async one — they are separate
/// paths, and a stub on one says nothing about the other.</para>
///
/// <para><b>Dependencies:</b> NSubstitute and reflection over <c>BlogModels</c>. No database.</para>
/// </remarks>
public class SubstituteBridgeTrapTests
{
    /// <summary>
    /// A substitute whose SYNCHRONOUS member is stubbed answers <c>null</c> from the async twin,
    /// because the proxy intercepts the interface's default implementation instead of letting it run
    /// the bridge. This is the exact mechanism behind the duplicate-confirmation defect: a service
    /// that had switched to the async twin saw "no such subscriber" for a subscriber the test had
    /// carefully arranged.
    /// </summary>
    [Fact]
    public async Task StubbingASyncMemberDoesNotAnswerItsAsyncTwin()
    {
        // Arrange
        var repo = Substitute.For<ISubscriberRepo>();
        var known = new Subscriber { SubscriberId = 7, Email = "known@example.com", IsConfirmed = true };
        repo.GetByEmail("known@example.com").Returns(known);

        // Act
        var fromSync = repo.GetByEmail("known@example.com");
        var fromAsync = await repo.GetByEmailAsync("known@example.com", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(known, fromSync);
        Assert.Null(fromAsync);
    }

    /// <summary>
    /// Stubbing the async twin as well makes the substitute answer both paths, which is the remedy
    /// every repository fake in this suite has to apply. Kept next to the failing case so the fix is
    /// visible in the same file as the trap.
    /// </summary>
    [Fact]
    public async Task StubbingBothTwinsAnswersBothPaths()
    {
        // Arrange
        var repo = Substitute.For<ISubscriberRepo>();
        var known = new Subscriber { SubscriberId = 7, Email = "known@example.com", IsConfirmed = true };
        repo.GetByEmail("known@example.com").Returns(known);
        repo.GetByEmailAsync("known@example.com", Arg.Any<CancellationToken>()).Returns(known);

        // Act
        var fromSync = repo.GetByEmail("known@example.com");
        var fromAsync = await repo.GetByEmailAsync("known@example.com", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(known, fromSync);
        Assert.Same(known, fromAsync);
    }

    /// <summary>
    /// Every <c>…Async</c> repository member that still has a synchronous twin is a default interface
    /// implementation, so the trap applies to all of them uniformly and no fake can rely on falling
    /// through for "just this one". Generated by reflection, so the inventory cannot go stale; when
    /// REQ-NFR-026's final stage deletes the synchronous surface the pairs disappear and this test
    /// quietly has nothing left to assert, which is the correct end state.
    /// </summary>
    [Fact]
    public void EveryBridgedAsyncTwinIsADefaultImplementation()
    {
        // Arrange
        var interfaces = typeof(ISubscriberRepo).Assembly
            .GetTypes()
            .Where(candidate => candidate.IsInterface && candidate.Namespace == "BlogModels.Interfaces")
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal);

        var abstractTwins = new StringBuilder();
        var pairCount = 0;

        // Act
        foreach (var repositoryInterface in interfaces)
        {
            foreach (var asyncMember in repositoryInterface.GetMethods())
            {
                if (!asyncMember.Name.EndsWith("Async", StringComparison.Ordinal))
                    continue;

                var syncName = asyncMember.Name[..^5];
                if (repositoryInterface.GetMethods().All(candidate => candidate.Name != syncName))
                    continue;

                pairCount++;

                if (asyncMember.IsAbstract)
                    abstractTwins.AppendLine($"  {repositoryInterface.Name}.{asyncMember.Name}");
            }
        }

        // Assert
        Assert.True(
            abstractTwins.Length == 0,
            "These async twins are abstract rather than bridged default implementations, so the sync/async stubbing "
            + $"guidance on this class no longer describes them uniformly:{Environment.NewLine}{abstractTwins}");

        Assert.True(
            pairCount > 0,
            "No sync/async member pairs were found on any repository interface. Either REQ-NFR-026's final stage has "
            + "deleted the synchronous surface — in which case delete this class, the trap is gone — or the reflection "
            + "above is looking in the wrong assembly and is proving nothing.");
    }
}
