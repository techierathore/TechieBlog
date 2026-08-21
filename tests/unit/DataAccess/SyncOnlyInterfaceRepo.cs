using System.Data;
using BlogModels.Interfaces;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// A repository that implements <see cref="IGenericRepository{TEntity}"/> with synchronous members only.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stands in for the hand-written test doubles under <c>tests/unit</c> and for
/// every not-yet-converted implementer. Its whole point is what it does <i>not</i> contain: no async
/// member is written here, so the fact that it compiles at all is the proof that the async surface
/// added under REQ-NFR-026 is additive and does not force implementers to change in lockstep. If
/// somebody later turns a default implementation into an abstract member, this class stops compiling
/// and says so before the 25 production repositories do.</para>
///
/// <para><b>Code Flow:</b> the test calls an <c>…Async</c> member → no override exists → the
/// interface's default implementation runs the synchronous twin below and wraps the outcome in a
/// completed task.</para>
///
/// <para><b>Dependencies:</b> None — the rows live in a list, so no database is involved.</para>
///
/// <para><b>Usage:</b> Construct with the rows the test needs, then assert that the async members
/// return the same values the synchronous ones do. Set <see cref="FailNextCall"/> to drive the
/// failure path.</para>
/// </remarks>
public class SyncOnlyInterfaceRepo : IGenericRepository<SyncOnlyEntity>
{
    private readonly List<SyncOnlyEntity> rows;

    /// <summary>
    /// Creates the fake over a fixed set of rows.
    /// </summary>
    /// <param name="rows">Rows every read member returns.</param>
    public SyncOnlyInterfaceRepo(params SyncOnlyEntity[] rows)
    {
        this.rows = rows.ToList();
    }

    /// <summary>When true, every synchronous member throws, so the failure path can be observed.</summary>
    public bool FailNextCall { get; set; }

    /// <summary>Rows handed to <see cref="Insert"/>, so writes can be asserted.</summary>
    public List<SyncOnlyEntity> Inserted { get; } = new();

    /// <summary>Rows handed to <see cref="Update"/>, so writes can be asserted.</summary>
    public List<SyncOnlyEntity> Updated { get; } = new();

    /// <inheritdoc />
    public IDbConnection GetOpenConnection()
    {
        throw new NotSupportedException("The contract fakes never reach a database.");
    }

    /// <inheritdoc />
    public IEnumerable<SyncOnlyEntity> GetAll()
    {
        GuardFailure();
        return rows;
    }

    /// <inheritdoc />
    public IEnumerable<SyncOnlyEntity> GetAllById(long singleId)
    {
        GuardFailure();
        return rows.Where(row => row.EntityId == singleId).ToList();
    }

    /// <inheritdoc />
    public IEnumerable<SyncOnlyEntity> GetPagedData(int pageSize, int offSet)
    {
        GuardFailure();
        return rows.Skip(offSet).Take(pageSize).ToList();
    }

    /// <inheritdoc />
    public SyncOnlyEntity? GetSingle(long singleId)
    {
        GuardFailure();
        return rows.FirstOrDefault(row => row.EntityId == singleId);
    }

    /// <inheritdoc />
    public SyncOnlyEntity? GetIntSingle(int singleId)
    {
        return GetSingle(singleId);
    }

    /// <inheritdoc />
    public void Insert(SyncOnlyEntity entity)
    {
        GuardFailure();
        Inserted.Add(entity);
    }

    /// <inheritdoc />
    public long InsertToGetId(SyncOnlyEntity entity)
    {
        GuardFailure();
        Inserted.Add(entity);
        return entity.EntityId;
    }

    /// <inheritdoc />
    public void Update(SyncOnlyEntity entityToUpdate)
    {
        GuardFailure();
        Updated.Add(entityToUpdate);
    }

    /// <summary>
    /// Throws when the test has armed the failure flag.
    /// </summary>
    /// <exception cref="InvalidOperationException">Always, when <see cref="FailNextCall"/> is set.</exception>
    private void GuardFailure()
    {
        if (FailNextCall)
            throw new InvalidOperationException("Simulated data-access failure.");
    }
}
