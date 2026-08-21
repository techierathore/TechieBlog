using BlogModels.Interfaces;
using Dapper;
using System.Data;
using System.Data.Common;

namespace BlogEngine.DaCore;

/// <summary>
/// Base repository class implementing generic data access patterns with Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a base implementation of IGenericRepository for all
/// entity repositories. Uses Dapper for PostgreSQL data access operations.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Derived repositories inherit from this class</item>
///   <item>Connection string injected via constructor</item>
///   <item>GetOpenConnectionAsync() provides database connections via factory</item>
///   <item>Abstract methods implemented by derived classes for entity-specific operations</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>Dapper - Micro-ORM for data access</item>
///   <item>DbConnectionFactory - Connection creation</item>
///   <item>Npgsql - PostgreSQL driver (via factory)</item>
/// </list>
///
/// <para><b>Usage:</b> Inherit from this class and implement the abstract members for each entity
/// type (e.g., BlogPostRepo, BlogUserRepo), then override the <c>…Async</c> members with genuine
/// async Dapper calls.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> Every operation exists twice. The abstract
/// synchronous members are the legacy surface; the <c>virtual</c> <c>…Async</c> members are the
/// surface every caller should move to. The async members ship with a <i>temporary bridge</i>
/// implementation that simply runs the synchronous twin and wraps the result in a completed task, so
/// the whole solution compiles and behaves correctly from the moment the contract lands. A bridged
/// call is no slower and no more blocking than the synchronous call it replaces — it is just not yet
/// any better. Each repository then overrides its <c>…Async</c> members with real
/// <c>QueryAsync</c>/<c>ExecuteAsync</c> calls, and only at that point does the repository stop
/// consuming a thread-pool thread per query. Overriding is therefore mandatory, not optional: a
/// repository that leaves the bridge in place contributes nothing to the throughput fix. The final
/// stage of the conversion deletes the synchronous members and the bridge with them.</para>
///
/// <para><b>Helpers:</b> the <c>protected</c> <c>QueryAsync</c>, <c>QueryFirstOrDefaultAsync</c>,
/// <c>QuerySingleAsync</c>, <c>ExecuteAsync</c> and <c>ExecuteScalarAsync</c> members exist so a
/// derived repository cannot forget the three things that are easy to get wrong: opening the
/// connection asynchronously, flowing the <c>CancellationToken</c> into the command, and
/// <c>ConfigureAwait(false)</c>. Prefer them over hand-rolling a connection. They are the seam that
/// made the 25-repository conversion uniform: because every converted query goes through one of five
/// methods, the three easy-to-forget details are written once and reviewed once rather than 200
/// times.</para>
///
/// <para><b>Binding <c>DateTime</c> parameters:</b> wrap every timestamp in
/// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> before handing it to any of these helpers. A
/// <c>DateTime</c> whose <c>Kind</c> is <c>Utc</c> — which is what <c>DateTime.UtcNow</c> returns — is
/// sent by Npgsql as <c>timestamptz</c>, matches none of this schema's <c>TIMESTAMP</c> stored-function
/// signatures, and fails at runtime with SQLSTATE <c>42883</c> ("function … does not exist"), which
/// reads like a missing migration rather than a parameter problem. See <see cref="DbTimestamp"/> for
/// why setting <c>DbType</c> does not fix it.</para>
///
/// <para><b>Transactions:</b> each helper opens and disposes its own connection, so two calls are two
/// independent transactions. There is no unit of work here; a service that needs several writes to
/// commit atomically must do it in a single statement or a single stored function.</para>
/// </remarks>
/// <typeparam name="TEntity">The entity type this repository manages.</typeparam>
/// <example>
/// <code>
/// public class BlogPostRepo : GenericRepository&lt;BlogPost&gt;, IBlogPostRepo
/// {
///     private const string SelectByIdSql = "SELECT * FROM PostSelect(@pPostId)";
///
///     public BlogPostRepo(string connectionString) : base(connectionString) { }
///
///     public override async Task&lt;BlogPost?&gt; GetSingleAsync(
///         long postId, CancellationToken cancellationToken = default)
///     {
///         return await QueryFirstOrDefaultAsync&lt;BlogPost&gt;(
///             SelectByIdSql, new { pPostId = postId }, cancellationToken).ConfigureAwait(false);
///     }
/// }
/// </code>
/// </example>
public abstract class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : class
{
    private readonly string connectionString;
    private readonly EDbConnectionTypes dbType;

    /// <summary>
    /// Initializes a new instance of GenericRepository with PostgreSQL connection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Stores the connection string and fixes the provider to
    /// PostgreSQL. No connection is opened here — connections are created per operation and disposed
    /// immediately, so a repository instance is cheap to construct and holds no unmanaged
    /// resource.</para>
    /// <para><b>Flow:</b> set the provider → store the connection string.</para>
    /// <para><b>Side Effects:</b> None. The constructor does not validate or probe the connection
    /// string, so a bad value surfaces at the first query rather than at registration.</para>
    /// <para><b>Configuration:</b> the value comes from the configuration key <c>AppDbConString</c>,
    /// resolved once during service registration in <c>BlogSvcInitializer</c> and passed to every
    /// repository. This project deliberately does not use <c>ConnectionStrings:Default</c>.</para>
    /// </remarks>
    /// <param name="connectionString">PostgreSQL connection string, from the <c>AppDbConString</c> configuration key.</param>
    public GenericRepository(string connectionString)
    {
        dbType = EDbConnectionTypes.PostgreSql;
        this.connectionString = connectionString;
    }

    /// <summary>
    /// Creates and returns an open database connection.
    /// </summary>
    /// <remarks>
    /// <para><b>Important:</b> Callers are responsible for disposing the connection.
    /// Always use with a using statement or dispose manually.</para>
    /// <para><b>Deprecated by REQ-NFR-026:</b> this blocks the calling thread for the whole
    /// connection handshake. New code calls <see cref="GetOpenConnectionAsync"/>.</para>
    /// </remarks>
    /// <returns>An open IDbConnection to the PostgreSQL database.</returns>
    /// <example>
    /// <code>
    /// using var connection = GetOpenConnection();
    /// var results = connection.Query&lt;Entity&gt;("SELECT * FROM Entity");
    /// </code>
    /// </example>
    public IDbConnection GetOpenConnection()
    {
        return DbConnectionFactory.GetDbConnection(dbType, connectionString);
    }

    /// <summary>
    /// Creates and returns an open database connection without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to <see cref="DbConnectionFactory.GetDbConnectionAsync"/>,
    /// which awaits <c>OpenAsync</c> rather than blocking on <c>Open</c>.</para>
    ///
    /// <para><b>Flow:</b> factory call → awaited open → owned connection returned.</para>
    ///
    /// <para><b>Side Effects:</b> Opens a physical or pooled connection. The caller owns it and must
    /// dispose it — use <c>await using</c> so the disposal is asynchronous too.</para>
    ///
    /// <para><b>Important:</b> never mix the two. Calling <see cref="GetOpenConnection"/> inside an
    /// <c>async</c> method blocks a thread-pool thread for the handshake, which is exactly the stall
    /// REQ-NFR-026 removes; the method compiles and passes tests either way, so this is the single
    /// easiest mistake to make during the conversion.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the connection attempt.</param>
    /// <returns>An open <see cref="DbConnection"/> to the PostgreSQL database.</returns>
    /// <example>
    /// <code>
    /// await using var connection = await GetOpenConnectionAsync(cancellationToken)
    ///     .ConfigureAwait(false);
    /// </code>
    /// </example>
    public virtual Task<DbConnection> GetOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return DbConnectionFactory.GetDbConnectionAsync(dbType, connectionString, cancellationToken);
    }

    // =================================================================================================
    // Legacy synchronous surface — REQ-NFR-026.
    //
    // Every member below blocks the calling thread for the whole database round trip. They remain
    // abstract because the 25 repositories still implement them; the final stage of REQ-NFR-026
    // deletes this entire block together with the bridges further down. Do not add a call site.
    // =================================================================================================

    /// <summary>
    /// Retrieves all entities of type TEntity.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The unfiltered read. Because it has no paging, the cost grows with
    /// the table — use <see cref="GetPagedData"/> for anything a user can grow without bound.</para>
    /// <para><b>Flow:</b> implemented by the derived repository; typically open connection → Dapper
    /// query against a stored function → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only. <b>Blocks the calling thread</b>; prefer
    /// <see cref="GetAllAsync"/>.</para>
    /// </remarks>
    /// <returns>Collection of all entities, or an empty sequence when the table is empty.</returns>
    public abstract IEnumerable<TEntity> GetAll();

    /// <summary>
    /// Retrieves all entities associated with the specified ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The "children of" read. Which column
    /// <paramref name="singleId"/> is matched against is decided by each repository — for a comment
    /// repository it is the post id, for an image repository the owning entity — so read the override
    /// before assuming.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> None — read-only. <b>Blocks the calling thread</b>; prefer
    /// <see cref="GetAllByIdAsync"/>.</para>
    /// </remarks>
    /// <param name="singleId">The parent/foreign key ID to filter by.</param>
    /// <returns>Collection of matching entities, or an empty sequence when none match.</returns>
    public abstract IEnumerable<TEntity> GetAllById(long singleId);

    /// <summary>
    /// Retrieves a paginated subset of entities.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Offset paging — the database skips <paramref name="offSet"/> rows
    /// and returns the next <paramref name="pageSize"/>. Note that offset paging re-reads the skipped
    /// rows server-side, so deep pages get progressively more expensive, and a row inserted between
    /// two page requests shifts the window and can make a row appear twice or not at all.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> None — read-only. <b>Blocks the calling thread</b>; prefer
    /// <see cref="GetPagedDataAsync"/>.</para>
    /// </remarks>
    /// <param name="pageSize">Number of entities per page.</param>
    /// <param name="offSet">Number of entities to skip.</param>
    /// <returns>Paginated collection of entities.</returns>
    public abstract IEnumerable<TEntity> GetPagedData(int pageSize, int offSet);

    /// <summary>
    /// Retrieves a single entity by its primary key (BIGINT).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Not found" is an ordinary answer and is reported as <c>null</c>,
    /// not as an exception — callers must null-check rather than try/catch.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> None — read-only. <b>Blocks the calling thread</b>; prefer
    /// <see cref="GetSingleAsync"/>.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <returns>The entity if found, <c>null</c> otherwise.</returns>
    public abstract TEntity? GetSingle(long singleId);

    /// <summary>
    /// Retrieves a single entity by its primary key (INT).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The <c>INT</c>-keyed twin of <see cref="GetSingle"/>, for the
    /// lookup tables whose primary key is a 32-bit integer rather than a <c>BIGINT</c>. A repository
    /// whose entity has only one key width still has to implement both members and typically throws
    /// or returns <c>null</c> from the one that does not apply.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> None — read-only. <b>Blocks the calling thread</b>; prefer
    /// <see cref="GetIntSingleAsync"/>.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <returns>The entity if found, <c>null</c> otherwise.</returns>
    public abstract TEntity? GetIntSingle(int singleId);

    /// <summary>
    /// Inserts a new entity into the database.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The fire-and-forget write, for rows whose generated key the caller
    /// does not need. When the key is needed, call <see cref="InsertToGetId"/> instead rather than
    /// inserting and then querying for the row back — that second query is a race.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> Writes one row. No transaction is opened by this class, so a caller
    /// that needs several writes to succeed or fail together must manage that itself. <b>Blocks the
    /// calling thread</b>; prefer <see cref="InsertAsync"/>.</para>
    /// </remarks>
    /// <param name="entity">The entity to insert.</param>
    public abstract void Insert(TEntity entity);

    /// <summary>
    /// Inserts a new entity and returns its generated ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The insert used whenever the caller needs the new key — to write a
    /// child row, or to redirect to the created item. The key is read back in the same statement
    /// (a <c>RETURNING</c> clause or a stored function's return value), which is what makes it
    /// race-free.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> Writes one row. <b>Blocks the calling thread</b>; prefer
    /// <see cref="InsertToGetIdAsync"/>.</para>
    /// </remarks>
    /// <param name="entity">The entity to insert.</param>
    /// <returns>The generated primary key ID.</returns>
    public abstract long InsertToGetId(TEntity entity);

    /// <summary>
    /// Updates an existing entity in the database.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A last-write-wins overwrite of the whole row — there is no
    /// optimistic-concurrency token in this schema, so two users editing the same record concurrently
    /// will silently overwrite one another.</para>
    /// <para><b>Flow:</b> implemented by the derived repository.</para>
    /// <para><b>Side Effects:</b> Updates one row. Implementations generally do not report whether a
    /// row actually matched, so an update against a deleted id can complete without changing anything.
    /// <b>Blocks the calling thread</b>; prefer <see cref="UpdateAsync"/>.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The entity with updated values.</param>
    public abstract void Update(TEntity entityToUpdate);

    // =================================================================================================
    // Async surface — REQ-NFR-026.
    //
    // Each member below ships with a bridge to its synchronous twin so the contract can land without
    // breaking a single one of the 25 repositories. Every repository is expected to override all of
    // them with real async Dapper; until it does, its async callers are correct but still blocking.
    // =================================================================================================

    /// <summary>
    /// Retrieves all entities of type TEntity without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAll"/>, same results.</para>
    /// <para><b>Flow:</b> bridged to <see cref="GetAll"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Collection of all entities.</returns>
    public virtual Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return BridgeToSync(GetAll, cancellationToken);
    }

    /// <summary>
    /// Retrieves all entities associated with the specified ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllById"/>, same results.</para>
    /// <para><b>Flow:</b> bridged to <see cref="GetAllById"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The parent/foreign key ID to filter by.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Collection of matching entities, or an empty sequence when none match.</returns>
    public virtual Task<IEnumerable<TEntity>> GetAllByIdAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => GetAllById(singleId), cancellationToken);
    }

    /// <summary>
    /// Retrieves a paginated subset of entities without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPagedData"/>, same results.</para>
    /// <para><b>Flow:</b> bridged to <see cref="GetPagedData"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Number of entities per page.</param>
    /// <param name="offSet">Number of entities to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Paginated collection of entities.</returns>
    public virtual Task<IEnumerable<TEntity>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => GetPagedData(pageSize, offSet), cancellationToken);
    }

    /// <summary>
    /// Retrieves a single entity by its BIGINT primary key without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSingle"/>, same results.</para>
    /// <para><b>Flow:</b> bridged to <see cref="GetSingle"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The entity if found, <c>null</c> otherwise.</returns>
    public virtual Task<TEntity?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => GetSingle(singleId), cancellationToken);
    }

    /// <summary>
    /// Retrieves a single entity by its INT primary key without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetIntSingle"/>, same results.</para>
    /// <para><b>Flow:</b> bridged to <see cref="GetIntSingle"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The entity's unique identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The entity if found, <c>null</c> otherwise.</returns>
    public virtual Task<TEntity?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => GetIntSingle(singleId), cancellationToken);
    }

    /// <summary>
    /// Inserts a new entity into the database without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="Insert"/>.</para>
    /// <para><b>Flow:</b> bridged to <see cref="Insert"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public virtual Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => Insert(entity), cancellationToken);
    }

    /// <summary>
    /// Inserts a new entity and returns its generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="InsertToGetId"/>.</para>
    /// <para><b>Flow:</b> bridged to <see cref="InsertToGetId"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The entity to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated primary key ID.</returns>
    public virtual Task<long> InsertToGetIdAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => InsertToGetId(entity), cancellationToken);
    }

    /// <summary>
    /// Updates an existing entity in the database without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="Update"/>.</para>
    /// <para><b>Flow:</b> bridged to <see cref="Update"/> until the repository overrides this member.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The entity with updated values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public virtual Task UpdateAsync(TEntity entityToUpdate, CancellationToken cancellationToken = default)
    {
        return BridgeToSync(() => Update(entityToUpdate), cancellationToken);
    }

    // =================================================================================================
    // Protected async Dapper helpers. Derived repositories should route every query through these.
    // =================================================================================================

    /// <summary>
    /// Runs a query asynchronously and returns every matching row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Opens a connection asynchronously, executes the command with the
    /// cancellation token attached, and buffers the whole result set before the connection closes.
    /// Buffering is deliberate — an unbuffered Dapper result reads lazily, and by the time the caller
    /// enumerates it the <c>await using</c> has already closed the connection.</para>
    ///
    /// <para><b>Flow:</b> open async → <c>QueryAsync</c> with a <see cref="CommandDefinition"/> →
    /// materialise → dispose the connection asynchronously.</para>
    ///
    /// <para><b>Side Effects:</b> None beyond opening and closing one connection.</para>
    /// </remarks>
    /// <typeparam name="TResult">Row type Dapper should map each record to.</typeparam>
    /// <param name="sql">The SQL statement or stored-function call to execute.</param>
    /// <param name="parameters">Parameter object or <c>DynamicParameters</c>; never interpolate values into <paramref name="sql"/>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching rows, or an empty sequence when none match.</returns>
    protected async Task<IEnumerable<TResult>> QueryAsync<TResult>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        var rows = await connection.QueryAsync<TResult>(command).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Runs a query asynchronously and returns the first row, or <c>null</c> when there is none.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The lookup shape used by every <c>GetSingle</c>-style method:
    /// "not found" is a normal answer, expressed as <c>null</c> rather than an exception.</para>
    /// <para><b>Flow:</b> open async → <c>QueryFirstOrDefaultAsync</c> → dispose asynchronously.</para>
    /// <para><b>Side Effects:</b> None beyond opening and closing one connection.</para>
    /// </remarks>
    /// <typeparam name="TResult">Row type Dapper should map the record to.</typeparam>
    /// <param name="sql">The SQL statement or stored-function call to execute.</param>
    /// <param name="parameters">Parameter object or <c>DynamicParameters</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The first matching row, or <c>null</c>.</returns>
    protected async Task<TResult?> QueryFirstOrDefaultAsync<TResult>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<TResult>(command).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a query asynchronously that must return exactly one row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used for stored functions that always produce a value — a
    /// generated identity, a count, a computed flag. Zero rows or more than one row is a programming
    /// or schema error, so Dapper throws rather than returning a default.</para>
    /// <para><b>Flow:</b> open async → <c>QuerySingleAsync</c> → dispose asynchronously.</para>
    /// <para><b>Side Effects:</b> Whatever the statement does; frequently a write.</para>
    /// </remarks>
    /// <typeparam name="TResult">Row type Dapper should map the record to.</typeparam>
    /// <param name="sql">The SQL statement or stored-function call to execute.</param>
    /// <param name="parameters">Parameter object or <c>DynamicParameters</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The single row's mapped value.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the query returns other than one row.</exception>
    protected async Task<TResult> QuerySingleAsync<TResult>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<TResult>(command).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a non-query statement asynchronously.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The write path for INSERT, UPDATE and DELETE statements whose
    /// generated key is not needed.</para>
    /// <para><b>Flow:</b> open async → <c>ExecuteAsync</c> → dispose asynchronously.</para>
    /// <para><b>Side Effects:</b> Writes rows.</para>
    /// </remarks>
    /// <param name="sql">The SQL statement or stored-function call to execute.</param>
    /// <param name="parameters">Parameter object or <c>DynamicParameters</c>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The number of rows affected, as reported by the provider.</returns>
    protected async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(command).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a statement asynchronously and reads the first column of the first row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The <c>RETURNING</c>-clause and <c>COUNT(…)</c> path. Note that an
    /// empty result set yields <c>default</c>, so a <c>0</c> from a counting query can mean either
    /// "zero rows matched" or "no row came back at all"; use
    /// <see cref="QuerySingleAsync{TResult}"/> when the difference matters.</para>
    /// <para><b>Flow:</b> open async → <c>ExecuteScalarAsync</c> → dispose asynchronously.</para>
    /// <para><b>Side Effects:</b> Whatever the statement does; frequently a write.</para>
    /// </remarks>
    /// <typeparam name="TResult">Type of the scalar value.</typeparam>
    /// <param name="sql">The SQL statement or stored-function call to execute.</param>
    /// <param name="parameters">Parameter object or <c>DynamicParameters</c>.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The scalar value, or <c>default</c> when the statement produced no rows.</returns>
    protected async Task<TResult?> ExecuteScalarAsync<TResult>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteScalarAsync<TResult>(command).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a synchronous repository operation and presents its outcome as a completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The temporary bridge that lets the async contract exist before the
    /// repositories implement it. It preserves task semantics faithfully — a cancelled token produces
    /// a cancelled task and a thrown exception produces a faulted task, rather than either escaping
    /// synchronously to a caller that is only prepared to observe them through <c>await</c>.</para>
    ///
    /// <para><b>Flow:</b> check the token → invoke the synchronous operation → wrap the value, the
    /// cancellation or the exception.</para>
    ///
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline on the
    /// calling thread — this bridge is not asynchrony, only its shape.</para>
    /// </remarks>
    /// <typeparam name="TResult">Return type of the synchronous operation.</typeparam>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the operation starts.</param>
    /// <returns>A completed, cancelled or faulted task carrying the operation's outcome.</returns>
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
    /// Runs a synchronous repository command and presents its outcome as a completed task.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Void-returning counterpart of the value-returning bridge; see that
    /// overload for why cancellation and exceptions are wrapped rather than thrown inline.</para>
    /// <para><b>Flow:</b> check the token → invoke → wrap.</para>
    /// <para><b>Side Effects:</b> Those of <paramref name="syncOperation"/>, executed inline.</para>
    /// </remarks>
    /// <param name="syncOperation">The synchronous member being bridged.</param>
    /// <param name="cancellationToken">Token observed before the operation starts.</param>
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
