using System.Reflection;
using BlogEngine.DbAccess;
using BlogModels.Interfaces;
using BlogModels.Models;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Contract tests for the async conversion of <see cref="SiteSettingRepo"/> (REQ-NFR-026).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This repository compiled perfectly before the conversion and was still
/// entirely blocking — every member was <c>Task</c>-returning but opened its connection with the
/// synchronous factory, so the signature promised asynchrony the implementation never delivered.
/// That is precisely the failure a behavioural test cannot see, so these tests assert the
/// structural facts instead: nothing is left on the base class's bridge, and every operation is
/// reachable through a cancellable overload.</para>
///
/// <para><b>Dependencies:</b> xUnit and reflection only — no database and no host.</para>
///
/// <para><b>Usage:</b> A failure here means a member has regressed to the bridge, or that the
/// token-carrying half of an overload pair has been dropped and callers have quietly lost
/// cancellation.</para>
/// </remarks>
public class SiteSettingRepoAsyncTests
{
    /// <summary>
    /// Every public async member of the repository is declared on the repository itself rather than
    /// inherited from the base class's temporary bridge, which would keep a thread parked per query
    /// however green the build looked.
    /// </summary>
    [Fact]
    public void OverridesEveryAsyncMember()
    {
        var inherited = AsyncMethodsOf(typeof(SiteSettingRepo))
            .Where(method => method.DeclaringType != typeof(SiteSettingRepo))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(inherited);
    }

    /// <summary>
    /// Every async operation the repository exposes is reachable through an overload that accepts a
    /// cancellation token. The token-free twins are kept deliberately — the in-memory
    /// FakeSiteSettingRepo implements them and is not derived from GenericRepository — but no
    /// operation may exist in the token-free shape alone.
    /// </summary>
    [Fact]
    public void EveryAsyncOperationIsCancellable()
    {
        var uncancellable = AsyncMethodsOf(typeof(SiteSettingRepo))
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .Where(group => !group.Any(EndsWithCancellationToken))
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(uncancellable);
    }

    /// <summary>
    /// Every member of the settings contract is task-returning, so no caller is offered a blocking
    /// path to configuration that the whole application reads on its first request.
    /// </summary>
    [Fact]
    public void ContractIsFullyAsync()
    {
        var members = typeof(ISiteSettingRepo).GetMethods();

        Assert.NotEmpty(members);
        foreach (var member in members)
        {
            Assert.EndsWith("Async", member.Name, StringComparison.Ordinal);
            Assert.True(typeof(Task).IsAssignableFrom(member.ReturnType), member.Name);
        }
    }

    /// <summary>
    /// The token-carrying overloads declared on the interface as default implementations are really
    /// implemented by the repository, not merely inherited. An inherited default delegates back to
    /// the token-free twin and silently discards the token, which is the shape this conversion
    /// exists to remove.
    /// </summary>
    [Fact]
    public void ImplementsInterfaceDefaultsRatherThanInheritingThem()
    {
        var interfaceDefaults = typeof(ISiteSettingRepo)
            .GetMethods()
            .Where(EndsWithCancellationToken)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(interfaceDefaults);
        foreach (var name in interfaceDefaults)
        {
            var declared = typeof(SiteSettingRepo)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == name)
                .Where(EndsWithCancellationToken)
                .Where(method => method.DeclaringType == typeof(SiteSettingRepo));

            Assert.True(declared.Any(), name);
        }
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
            .Where(method => method.Name != nameof(IGenericRepository<SiteSetting>.GetOpenConnectionAsync));
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
