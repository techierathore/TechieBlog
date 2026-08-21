using System.Reflection;
using BlogEngine.DaCore;
using BlogEngine.DbAccess;
using BlogModels;
using BlogModels.Interfaces;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Unit tests for the async data-access contract added by REQ-NFR-026.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The conversion's whole premise is that the async surface can be added to
/// <see cref="IGenericRepository{TEntity}"/> and <see cref="GenericRepository{TEntity}"/> without
/// breaking a single existing implementer, so that 24 repositories can then be converted
/// independently and verified one at a time. These tests hold that premise in place: they assert
/// that an unconverted implementer still answers correctly through the async members, that the
/// bridge preserves task semantics for cancellation and failure, and that the converted reference
/// repository really overrides every member rather than quietly inheriting the bridge.</para>
///
/// <para><b>Dependencies:</b> xUnit only. No database, no host — every double is in memory.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite. A failure here means the fan-out's core
/// assumption no longer holds and the remaining repository conversions are unsafe to start.</para>
/// </remarks>
public class AsyncDataAccessContractTests
{
    /// <summary>
    /// A repository that implements the interface with synchronous members only still returns the
    /// right rows through every async member, because each one carries a default implementation.
    /// This is what keeps the existing hand-written test doubles compiling and working untouched.
    /// </summary>
    [Fact]
    public async Task InterfaceDefaultsReturnSyncResults()
    {
        var first = new SyncOnlyEntity { EntityId = 1, Name = "first" };
        var second = new SyncOnlyEntity { EntityId = 2, Name = "second" };
        IGenericRepository<SyncOnlyEntity> repo = new SyncOnlyInterfaceRepo(first, second);

        var token = TestContext.Current.CancellationToken;

        Assert.Equal(new[] { first, second }, await repo.GetAllAsync(token));
        Assert.Equal(new[] { second }, await repo.GetAllByIdAsync(2, token));
        Assert.Equal(new[] { second }, await repo.GetPagedDataAsync(1, 1, token));
        Assert.Same(first, await repo.GetSingleAsync(1, token));
        Assert.Same(second, await repo.GetIntSingleAsync(2, token));
        Assert.Null(await repo.GetSingleAsync(99, token));
    }

    /// <summary>
    /// The write members of a sync-only implementer reach the underlying store through their async
    /// defaults, so a caller that has already migrated to the async surface is not silently no-oped.
    /// </summary>
    [Fact]
    public async Task InterfaceDefaultsPerformWrites()
    {
        var repo = new SyncOnlyInterfaceRepo();
        var entity = new SyncOnlyEntity { EntityId = 7, Name = "written" };

        var token = TestContext.Current.CancellationToken;

        await ((IGenericRepository<SyncOnlyEntity>)repo).InsertAsync(entity, token);
        var generatedId = await ((IGenericRepository<SyncOnlyEntity>)repo).InsertToGetIdAsync(entity, token);
        await ((IGenericRepository<SyncOnlyEntity>)repo).UpdateAsync(entity, token);

        Assert.Equal(2, repo.Inserted.Count);
        Assert.Equal(7, generatedId);
        Assert.Single(repo.Updated);
    }

    /// <summary>
    /// A repository deriving the base class but overriding only its synchronous members — the state
    /// every unconverted repository is in — answers its async callers correctly through the base
    /// class's temporary bridge.
    /// </summary>
    [Fact]
    public async Task BaseClassBridgeReturnsSyncResults()
    {
        var only = new SyncOnlyEntity { EntityId = 5, Name = "only" };
        var repo = new SyncOnlyBaseRepo(only);

        var token = TestContext.Current.CancellationToken;

        Assert.Equal(new[] { only }, await repo.GetAllAsync(token));
        Assert.Same(only, await repo.GetSingleAsync(5, token));
        Assert.Equal(5, await repo.InsertToGetIdAsync(only, token));
        Assert.Single(repo.Inserted);
    }

    /// <summary>
    /// An already-cancelled token produces a cancelled task rather than running the query, so a
    /// caller that abandons a Blazor circuit mid-request does not pay for work nobody will read.
    /// </summary>
    [Fact]
    public async Task BridgeHonoursCancelledToken()
    {
        var repo = new SyncOnlyBaseRepo(new SyncOnlyEntity { EntityId = 1, Name = "unused" });
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.GetAllAsync(source.Token));
    }

    /// <summary>
    /// A failure inside a bridged call arrives as a faulted task rather than as an exception thrown
    /// before the task is even created, so the same try/catch works whether the repository has been
    /// converted or not.
    /// </summary>
    [Fact]
    public async Task BridgeFaultsTaskRatherThanThrowingInline()
    {
        IGenericRepository<SyncOnlyEntity> repo = new SyncOnlyInterfaceRepo { FailNextCall = true };

        var pending = repo.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.True(pending.IsFaulted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
    }

    /// <summary>
    /// Every data-access async member of the converted reference repository is declared on the
    /// repository itself rather than inherited from the base class's bridge. A repository that
    /// inherits the bridge compiles, passes its tests and still blocks a thread per query, so this is
    /// the check that tells a conversion apart from the appearance of one — copy it for each
    /// converted repository. GetOpenConnectionAsync is excluded because the base class's version is
    /// already genuinely async: it awaits OpenAsync, and a repository has no reason to replace it.
    /// </summary>
    [Fact]
    public void ConvertedRepoOverridesEveryAsyncMember()
    {
        var inherited = typeof(CategoryRepo)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name.EndsWith("Async", StringComparison.Ordinal))
            .Where(method => method.Name != nameof(IGenericRepository<Category>.GetOpenConnectionAsync))
            .Where(method => method.DeclaringType != typeof(CategoryRepo))
            .Select(method => method.Name)
            .ToList();

        Assert.Empty(inherited);
    }

    /// <summary>
    /// The reference repository exposes an async twin for every synchronous member it inherits from
    /// the generic contract, so no caller is forced back onto the blocking surface for lack of an
    /// alternative.
    /// </summary>
    [Fact]
    public void ConvertedRepoExposesAsyncTwinForEverySyncMember()
    {
        var asyncNames = typeof(CategoryRepo)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .Where(name => name.EndsWith("Async", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var syncNames = typeof(ICategoryRepo)
            .GetMethods()
            .Select(method => method.Name)
            .Where(name => !name.EndsWith("Async", StringComparison.Ordinal))
            .Where(name => name != nameof(IGenericRepository<Category>.GetOpenConnection))
            .ToList();

        Assert.NotEmpty(syncNames);
        foreach (var syncName in syncNames)
        {
            Assert.Contains(syncName + "Async", asyncNames);
        }
    }
}
