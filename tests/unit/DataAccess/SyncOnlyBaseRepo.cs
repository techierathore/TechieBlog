using BlogEngine.DaCore;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// A repository deriving <see cref="GenericRepository{TEntity}"/> that overrides the synchronous members only.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The class-hierarchy counterpart of <see cref="SyncOnlyInterfaceRepo"/>. It
/// represents each of the 24 repositories on the morning before its conversion agent starts:
/// inheriting the base class's temporary async bridge and nothing else. Proving that such a
/// repository still answers its async callers correctly is what lets the fan-out proceed one
/// repository at a time instead of as one atomic change (REQ-NFR-026).</para>
///
/// <para><b>Code Flow:</b> the test calls an <c>…Async</c> member → no override exists here → the base
/// class's virtual bridge runs the synchronous override below.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/>. The connection string is a
/// placeholder: every member is overridden, so no connection is ever opened.</para>
///
/// <para><b>Usage:</b> Construct with the rows the test needs and assert on the async members.</para>
/// </remarks>
public class SyncOnlyBaseRepo : GenericRepository<SyncOnlyEntity>
{
    private readonly List<SyncOnlyEntity> rows;

    /// <summary>
    /// Creates the fake over a fixed set of rows.
    /// </summary>
    /// <param name="rows">Rows every read member returns.</param>
    public SyncOnlyBaseRepo(params SyncOnlyEntity[] rows)
        : base("Host=unused;Database=unused")
    {
        this.rows = rows.ToList();
    }

    /// <summary>Rows handed to <see cref="Insert"/>, so writes can be asserted.</summary>
    public List<SyncOnlyEntity> Inserted { get; } = new();

    /// <summary>Rows handed to <see cref="Update"/>, so writes can be asserted.</summary>
    public List<SyncOnlyEntity> Updated { get; } = new();

    /// <inheritdoc />
    public override IEnumerable<SyncOnlyEntity> GetAll() => rows;

    /// <inheritdoc />
    public override IEnumerable<SyncOnlyEntity> GetAllById(long singleId)
        => rows.Where(row => row.EntityId == singleId).ToList();

    /// <inheritdoc />
    public override IEnumerable<SyncOnlyEntity> GetPagedData(int pageSize, int offSet)
        => rows.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public override SyncOnlyEntity? GetSingle(long singleId)
        => rows.FirstOrDefault(row => row.EntityId == singleId);

    /// <inheritdoc />
    public override SyncOnlyEntity? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <inheritdoc />
    public override void Insert(SyncOnlyEntity entity) => Inserted.Add(entity);

    /// <inheritdoc />
    public override long InsertToGetId(SyncOnlyEntity entity)
    {
        Inserted.Add(entity);
        return entity.EntityId;
    }

    /// <inheritdoc />
    public override void Update(SyncOnlyEntity entityToUpdate) => Updated.Add(entityToUpdate);
}
