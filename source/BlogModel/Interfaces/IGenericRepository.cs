using System.Data;
using System.Data.Common;

namespace BlogModels.Interfaces;

/// <summary>
/// The data-access contract shared by every entity repository in the application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines the common CRUD and lookup surface so that services depend on an
/// abstraction rather than on a concrete Dapper repository. Entity-specific queries live on the
/// derived interfaces (for example <c>IBlogPostRepo</c>); this contract carries only the operations
/// that are meaningful for every entity.</para>
///
/// <para><b>Code Flow:</b> Implemented once by <c>GenericRepository&lt;TEntity&gt;</c> in
/// <c>BlogEngine.DaCore</c>, which each concrete repository inherits, and consumed by the services in
/// <c>BlogEngine.Services</c> through constructor injection.</para>
///
/// <para><b>Dependencies:</b> <see cref="IDbConnection"/> for the connection factory hand-off.</para>
///
/// <para><b>Usage:</b> Single-entity lookups return <c>null</c> when no row matches, so callers must
/// null-check. Collection lookups return an empty sequence rather than <c>null</c>.</para>
///
/// <para><b>Async surface (REQ-NFR-026):</b> every operation exists twice — a legacy blocking member
/// and an <c>…Async</c> member. The async member is the one to call: synchronous Dapper parks a
/// thread-pool thread for the whole round trip, which caps the application at roughly 3.5 requests a
/// second no matter how much concurrency arrives. The blocking members are retained only so the
/// solution keeps compiling while repositories, services and pages migrate one at a time; they are
/// deleted in the final stage of the conversion. Do not add new callers of them.</para>
///
/// <para><b>Contract of the async members:</b> they honour their <c>CancellationToken</c>, they never
/// block, and they return exactly what their synchronous twin returns — the same <c>null</c>-for-not-
/// found and empty-sequence-for-no-rows semantics described above.</para>
///
/// <para><b>Why the async members carry default implementations.</b> Adding a plain abstract member
/// to this interface would break every implementer at once — the 25 production repositories and the
/// hand-written test doubles under <c>tests/unit</c> alike — and nothing would compile again until
/// the last of them had been converted. That would make the conversion a single atomic change of
/// several thousand lines that nobody could build or test in pieces. Each async member therefore
/// ships with a default implementation that runs its synchronous twin and returns a completed task.
/// Existing implementers keep compiling untouched and keep behaving exactly as they did; converted
/// ones override the default with genuine async I/O and stop consuming a thread per query. The
/// defaults are removed in the final stage together with the synchronous members.</para>
///
/// <para><b>A default is correct but not yet a fix.</b> An unoverridden member still blocks the
/// calling thread for the whole round trip — the conversion buys nothing until the repository
/// overrides it. Treat a repository that still inherits these defaults as unconverted.</para>
/// </remarks>
/// <typeparam name="TEntity">The entity type this repository manages. Reference types only, because
/// a "not found" lookup is expressed as <c>null</c>.</typeparam>
public interface IGenericRepository<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Creates and opens a connection to the configured database.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Opens a physical or pooled connection. The caller owns the returned
    /// connection and must dispose it, normally with a <c>using</c> declaration.</para>
    /// </remarks>
    /// <returns>An open connection to the application database.</returns>
    IDbConnection GetOpenConnection();

    /// <summary>
    /// Inserts an entity and returns the primary key the database generated for it.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to persist.</param>
    /// <returns>The generated primary key.</returns>
    long InsertToGetId(TEntity entity);

    /// <summary>
    /// Inserts an entity without returning its generated key.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to persist.</param>
    void Insert(TEntity entity);

    /// <summary>
    /// Updates an existing entity in place.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Updates one row, matched on the entity's primary key.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The entity carrying the new values.</param>
    void Update(TEntity entityToUpdate);

    /// <summary>
    /// Retrieves a single entity by its <c>BIGINT</c> primary key.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <returns>The matching entity, or <c>null</c> when no row carries that key.</returns>
    TEntity? GetSingle(long singleId);

    /// <summary>
    /// Retrieves a single entity by its <c>INT</c> primary key.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <returns>The matching entity, or <c>null</c> when no row carries that key.</returns>
    TEntity? GetIntSingle(int singleId);

    /// <summary>
    /// Retrieves every entity of this type.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <returns>All entities, or an empty sequence when the table holds no rows.</returns>
    IEnumerable<TEntity> GetAll();

    /// <summary>
    /// Retrieves one page of entities.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Maximum number of entities to return.</param>
    /// <param name="offSet">Number of entities to skip before collecting the page.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    IEnumerable<TEntity> GetPagedData(int pageSize, int offSet);

    /// <summary>
    /// Retrieves every entity associated with the supplied parent identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The parent or foreign-key identifier to filter on.</param>
    /// <returns>All matching entities, or an empty sequence when none match.</returns>
    IEnumerable<TEntity> GetAllById(long singleId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates and opens a connection to the configured database without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async counterpart of <see cref="GetOpenConnection"/>. Opening
    /// a connection is I/O — TCP, TLS and authentication round trips — so doing it synchronously
    /// inside an <c>async</c> method still parks a thread-pool thread and defeats the point of the
    /// conversion.</para>
    ///
    /// <para><b>Flow:</b> build the provider connection → <c>OpenAsync</c> → hand ownership over.</para>
    ///
    /// <para><b>Side Effects:</b> Opens a physical or pooled connection. The caller owns the returned
    /// connection and must dispose it, normally with an <c>await using</c> declaration.</para>
    ///
    /// <para><b>Returns <see cref="DbConnection"/> deliberately:</b> Dapper's async extensions require
    /// the connection to really be a <see cref="DbConnection"/> and throw at runtime when it is not,
    /// and <c>DisposeAsync</c> only exists there. The stronger return type turns that runtime failure
    /// into a compile-time guarantee.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An open connection to the application database.</returns>
    Task<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
        => BridgeToSync(() => (DbConnection)GetOpenConnection(), cancellationToken);

    /// <summary>
    /// Inserts an entity and returns the primary key the database generated for it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="InsertToGetId"/>. This is the
    /// primitive of the insert pair — <see cref="InsertAsync"/> is expected to be written in terms of
    /// it so the SQL exists once.</para>
    /// <para><b>Flow:</b> bind parameters → open connection asynchronously → execute → read key.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated primary key.</returns>
    Task<long> InsertToGetIdAsync(TEntity entity, CancellationToken cancellationToken = default)
        => BridgeToSync(() => InsertToGetId(entity), cancellationToken);

    /// <summary>
    /// Inserts an entity without returning its generated key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Insert"/>.</para>
    /// <para><b>Flow:</b> bind parameters → open connection asynchronously → execute.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
        => BridgeToSync(() => Insert(entity), cancellationToken);

    /// <summary>
    /// Updates an existing entity in place.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="Update"/>.</para>
    /// <para><b>Flow:</b> bind parameters → open connection asynchronously → execute.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on the entity's primary key.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The entity carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task UpdateAsync(TEntity entityToUpdate, CancellationToken cancellationToken = default)
        => BridgeToSync(() => Update(entityToUpdate), cancellationToken);

    /// <summary>
    /// Retrieves a single entity by its <c>BIGINT</c> primary key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetSingle"/>.</para>
    /// <para><b>Flow:</b> open connection asynchronously → query → return first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching entity, or <c>null</c> when no row carries that key.</returns>
    Task<TEntity?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
        => BridgeToSync(() => GetSingle(singleId), cancellationToken);

    /// <summary>
    /// Retrieves a single entity by its <c>INT</c> primary key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetIntSingle"/>.</para>
    /// <para><b>Flow:</b> widen the key → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching entity, or <c>null</c> when no row carries that key.</returns>
    Task<TEntity?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
        => BridgeToSync(() => GetIntSingle(singleId), cancellationToken);

    /// <summary>
    /// Retrieves every entity of this type.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetAll"/>.</para>
    /// <para><b>Flow:</b> open connection asynchronously → buffered query → return the materialised set.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All entities, or an empty sequence when the table holds no rows.</returns>
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
        => BridgeToSync(GetAll, cancellationToken);

    /// <summary>
    /// Retrieves one page of entities.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetPagedData"/>.</para>
    /// <para><b>Flow:</b> open connection asynchronously → buffered query with LIMIT/OFFSET.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Maximum number of entities to return.</param>
    /// <param name="offSet">Number of entities to skip before collecting the page.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    Task<IEnumerable<TEntity>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
        => BridgeToSync(() => GetPagedData(pageSize, offSet), cancellationToken);

    /// <summary>
    /// Retrieves every entity associated with the supplied parent identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async counterpart of <see cref="GetAllById"/>.</para>
    /// <para><b>Flow:</b> open connection asynchronously → buffered query filtered on the parent key.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The parent or foreign-key identifier to filter on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All matching entities, or an empty sequence when none match.</returns>
    Task<IEnumerable<TEntity>> GetAllByIdAsync(long singleId, CancellationToken cancellationToken = default)
        => BridgeToSync(() => GetAllById(singleId), cancellationToken);

    /// <summary>
    /// Runs a synchronous member and presents its outcome as an already-completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs every default implementation above. It preserves task
    /// semantics faithfully — a cancelled token yields a cancelled task and a thrown exception yields
    /// a faulted task — so a caller that only observes failures through <c>await</c> sees them
    /// whichever implementation it happens to reach. Without this, an unconverted repository would
    /// throw synchronously where a converted one faults its task, and the same <c>try</c> block would
    /// behave differently on either side of the migration.</para>
    ///
    /// <para><b>Flow:</b> check the token → invoke the synchronous member → wrap value, cancellation or
    /// exception.</para>
    ///
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline on the
    /// calling thread. This is the shape of asynchrony, not asynchrony.</para>
    /// </remarks>
    /// <typeparam name="TResult">Return type of the synchronous member.</typeparam>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the member runs.</param>
    /// <returns>A completed, cancelled or faulted task carrying the outcome.</returns>
    private static Task<TResult> BridgeToSync<TResult>(Func<TResult> syncOperation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<TResult>(cancellationToken);

        try
        {
            return Task.FromResult(syncOperation());
        }
        catch (Exception ex)
        {
            return Task.FromException<TResult>(ex);
        }
    }

    /// <summary>
    /// Runs a synchronous void member and presents its outcome as an already-completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Void-returning counterpart of the value-returning bridge; see that
    /// overload for why cancellation and exceptions are wrapped rather than thrown inline.</para>
    /// <para><b>Flow:</b> check the token → invoke → wrap.</para>
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline.</para>
    /// </remarks>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the member runs.</param>
    /// <returns>A completed, cancelled or faulted task.</returns>
    private static Task BridgeToSync(Action syncOperation, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        try
        {
            syncOperation();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }
}
