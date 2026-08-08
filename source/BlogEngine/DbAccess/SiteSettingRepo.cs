using System.Data.Common;
using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for the key/value <c>SiteSetting</c> table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists site-wide configuration (BRD-69). Settings are stored one row
/// per key so a new setting never needs a schema migration.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>SiteSettingsService</c> calls <see cref="GetAllAsync()"/> to fill its cache.</item>
///   <item>Saves arrive as a batch and are written through the <c>UpsertSiteSetting</c> stored
///     function inside a single transaction.</item>
///   <item>The synchronous <see cref="GenericRepository{TEntity}"/> members are implemented for
///     interface completeness; new code should use the async members.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper, Npgsql, the <c>UpsertSiteSetting</c> PostgreSQL function
/// created by migration 016.</para>
///
/// <para><b>Usage:</b> Registered transiently with the connection string closed over, matching
/// every other repository in this project.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> The members here were already task-returning but
/// every one of them opened its connection with the blocking factory, so each parked a thread-pool
/// thread for the whole TCP, TLS and authentication handshake before any query ran — a
/// <c>Task</c>-shaped signature over a synchronous round trip. Each is now genuinely asynchronous
/// and the shape below is deliberate:</para>
/// <list type="number">
///   <item><b>The token-carrying overload is the real implementation</b> and the token-free twin
///   delegates to it with <see cref="CancellationToken.None"/>. The pair exists because
///   <c>FakeSiteSettingRepo</c> under <c>tests/unit</c> implements the token-free members and is not
///   derived from <c>GenericRepository</c>; <c>ISiteSettingRepo</c> therefore declares the
///   token-carrying members as default implementations that delegate the other way, which is what
///   keeps the fake compiling untouched.</item>
///   <item><b>Every inherited async member is overridden</b> rather than left on the base class's
///   bridge. A bridged member compiles, passes its tests and still blocks a thread per query, so
///   inheriting it would leave this repository unconverted however green the build looked.</item>
///   <item><b>The batch save owns its own connection and transaction</b>, because it is the one path
///   here that spans several statements. It opens with <c>GetOpenConnectionAsync</c> and uses
///   <c>BeginTransactionAsync</c>/<c>CommitAsync</c>/<c>RollbackAsync</c>, so the whole save is
///   asynchronous end to end rather than async up to the transaction boundary.</item>
/// </list>
/// </remarks>
public class SiteSettingRepo : GenericRepository<SiteSetting>, ISiteSettingRepo
{
    private const string SelectColumns =
        "SELECT SettingId, SettingKey, SettingValue, SettingGroup, IsSecret, UpdatedOn FROM SiteSetting";

    private const string OrderByGroupThenKey = " ORDER BY SettingGroup, SettingKey";

    private const string SelectAllSql = SelectColumns + OrderByGroupThenKey;

    private const string SelectPagedSql =
        SelectColumns + OrderByGroupThenKey + " LIMIT @PageSize OFFSET @OffSet";

    private const string SelectByIdSql = SelectColumns + " WHERE SettingId = @SettingId";

    private const string SelectByKeySql = SelectColumns + " WHERE SettingKey = @SettingKey";

    private const string DeleteByKeySql = "DELETE FROM SiteSetting WHERE SettingKey = @SettingKey";

    // The PostgreSQL upsert function shared by every write path, created by migration 016.
    private const string UpsertFunctionSql =
        "SELECT UpsertSiteSetting(@SettingKey, @SettingValue, @SettingGroup, @IsSecret)";

    /// <summary>
    /// Creates the repository over a PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">The <c>AppDbConString</c> value supplied by the host.</param>
    public SiteSettingRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Reads every persisted setting row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Settings are a small, bounded set, so a single unfiltered read is
    /// cheaper than per-key round trips — this is the query the service's cache is filled from. Group
    /// then key is the order the admin screen renders its sections in, applied in SQL so every caller
    /// agrees.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All rows, ordered by group then key; empty when nothing has been written.</returns>
    public override async Task<IEnumerable<SiteSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<SiteSetting>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads every persisted setting row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="GetAllAsync(CancellationToken)"/>; the
    /// token-free shape exists because <c>ISiteSettingRepo</c> declares it and the in-memory fake
    /// implements it.</para>
    /// <para><b>Flow:</b> delegate to the token-carrying member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <returns>All rows, ordered by group then key.</returns>
    public Task<IEnumerable<SiteSetting>> GetAllAsync()
    {
        return GetAllAsync(CancellationToken.None);
    }

    /// <summary>
    /// Reads the setting with the supplied primary key as a collection, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Settings have no parent entity, so the generic "all by id" shape
    /// degenerates to a primary-key lookup returning at most one row.</para>
    /// <para><b>Flow:</b> keyed read → wrap the hit, or return empty.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The setting's primary key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row as a single-item collection, or an empty collection.</returns>
    public override async Task<IEnumerable<SiteSetting>> GetAllByIdAsync(
        long singleId,
        CancellationToken cancellationToken = default)
    {
        var single = await GetSingleAsync(singleId, cancellationToken).ConfigureAwait(false);
        return single == null ? Array.Empty<SiteSetting>() : new[] { single };
    }

    /// <summary>
    /// Reads a page of settings, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Present for generic-contract completeness; the settings screen
    /// reads the whole set, which is bounded by the number of keys the application defines.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page ordered by group then key.</returns>
    public override async Task<IEnumerable<SiteSetting>> GetPagedDataAsync(
        int pageSize,
        int offSet,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync<SiteSetting>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one setting by primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown key is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The setting's primary key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row, or <c>null</c>.</returns>
    public override async Task<SiteSetting?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<SiteSetting>(
            SelectByIdSql, new { SettingId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one setting by an INT primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The setting's primary key.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row, or <c>null</c>.</returns>
    public override Task<SiteSetting?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Reads a single setting by its key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Key comparison is exact; keys are the constants in
    /// <c>SiteSettingKeys</c>, never user input. A blank key is answered with <c>null</c> without a
    /// round trip, because it can never match a stored row.</para>
    /// <para><b>Flow:</b> guard the key → helper opens the connection asynchronously → query by key →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="settingKey">The key to look up.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row, or <c>null</c> when the key has never been written.</returns>
    public async Task<SiteSetting?> GetByKeyAsync(string settingKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return null;
        }

        return await QueryFirstOrDefaultAsync<SiteSetting>(
            SelectByKeySql, new { SettingKey = settingKey }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a single setting by its key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="GetByKeyAsync(string, CancellationToken)"/>.</para>
    /// <para><b>Flow:</b> delegate to the token-carrying member.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="settingKey">The key to look up.</param>
    /// <returns>The matching row, or <c>null</c> when the key has never been written.</returns>
    public Task<SiteSetting?> GetByKeyAsync(string settingKey)
    {
        return GetByKeyAsync(settingKey, CancellationToken.None);
    }

    /// <summary>
    /// Inserts or updates a single setting, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>UpsertSiteSetting</c> stored function so the
    /// insert-or-update decision is made atomically by the database and two concurrent saves cannot
    /// race into duplicate keys.</para>
    /// <para><b>Flow:</b> null guard → helper opens the connection asynchronously → call the function →
    /// read the returned key.</para>
    /// <para><b>Side Effects:</b> Writes one row and stamps <c>UpdatedOn</c>.</para>
    /// </remarks>
    /// <param name="setting">The setting to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The primary key of the affected row.</returns>
    public async Task<long> UpsertAsync(SiteSetting setting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(setting);

        return await QuerySingleAsync<long>(
            UpsertFunctionSql, BuildUpsertParameters(setting), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates a single setting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="UpsertAsync(SiteSetting, CancellationToken)"/>.</para>
    /// <para><b>Flow:</b> delegate to the token-carrying member.</para>
    /// <para><b>Side Effects:</b> Writes one row and stamps <c>UpdatedOn</c>.</para>
    /// </remarks>
    /// <param name="setting">The setting to persist.</param>
    /// <returns>The primary key of the affected row.</returns>
    public Task<long> UpsertAsync(SiteSetting setting)
    {
        return UpsertAsync(setting, CancellationToken.None);
    }

    /// <summary>
    /// Inserts or updates a batch of settings in one transaction, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A settings save is all-or-nothing — a half-applied configuration
    /// would be worse than a rejected one — so the whole batch shares a transaction and rolls back
    /// together. Null entries are dropped and an empty batch is a no-op that never opens a
    /// connection. Cancelling mid-batch rolls back, so a cancelled save leaves the stored
    /// configuration exactly as it was.</para>
    /// <para><b>Flow:</b> filter and guard emptiness → open the connection asynchronously → begin →
    /// upsert each row → commit, or roll back on any failure and rethrow.</para>
    /// <para><b>Side Effects:</b> Writes every supplied row.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The number of rows written.</returns>
    public async Task<int> UpsertManyAsync(IEnumerable<SiteSetting> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var pending = settings.Where(setting => setting != null).ToList();
        if (pending.Count == 0)
        {
            return 0;
        }

        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var setting in pending)
            {
                var command = new CommandDefinition(
                    UpsertFunctionSql,
                    BuildUpsertParameters(setting),
                    transaction,
                    cancellationToken: cancellationToken);

                await connection.ExecuteScalarAsync<long>(command).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return pending.Count;
        }
        catch
        {
            // Rolled back with an uncancellable token: the caller's token is very likely the reason
            // we are here, and a rollback that skipped itself because of it would leave the
            // transaction to be abandoned rather than undone.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Inserts or updates a batch of settings in one transaction.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to
    /// <see cref="UpsertManyAsync(IEnumerable{SiteSetting}, CancellationToken)"/>.</para>
    /// <para><b>Flow:</b> delegate to the token-carrying member.</para>
    /// <para><b>Side Effects:</b> Writes every supplied row.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <returns>The number of rows written.</returns>
    public Task<int> UpsertManyAsync(IEnumerable<SiteSetting> settings)
    {
        return UpsertManyAsync(settings, CancellationToken.None);
    }

    /// <summary>
    /// Removes a setting, reverting it to its built-in default, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting is the supported way to "reset" a setting; the service
    /// substitutes the code default for any absent key. A blank key is refused without a round trip
    /// rather than issuing a DELETE that could only ever match nothing.</para>
    /// <para><b>Flow:</b> guard the key → helper opens the connection asynchronously → execute DELETE →
    /// compare the affected count to zero.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="settingKey">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns><c>true</c> when a row was removed.</returns>
    public async Task<bool> DeleteByKeyAsync(string settingKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return false;
        }

        var affected = await ExecuteAsync(
            DeleteByKeySql, new { SettingKey = settingKey }, cancellationToken).ConfigureAwait(false);

        return affected > 0;
    }

    /// <summary>
    /// Removes a setting, reverting it to its built-in default.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="DeleteByKeyAsync(string, CancellationToken)"/>.</para>
    /// <para><b>Flow:</b> delegate to the token-carrying member.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="settingKey">The key to remove.</param>
    /// <returns><c>true</c> when a row was removed.</returns>
    public Task<bool> DeleteByKeyAsync(string settingKey)
    {
        return DeleteByKeyAsync(settingKey, CancellationToken.None);
    }

    /// <summary>
    /// Inserts or updates one setting, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A setting's natural key is <c>SettingKey</c>, not its surrogate
    /// id, so the generic "insert" is an upsert here — writing a key that already exists must update
    /// it rather than fail on the unique index.</para>
    /// <para><b>Flow:</b> delegate to <see cref="UpsertAsync(SiteSetting, CancellationToken)"/>.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The setting to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(SiteSetting entity, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or updates one setting and returns its primary key, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="InsertAsync"/> — the write is an upsert on the
    /// natural key, and the stored function returns the affected row's identity either way.</para>
    /// <para><b>Flow:</b> delegate to <see cref="UpsertAsync(SiteSetting, CancellationToken)"/>.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entity">The setting to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The affected row's primary key.</returns>
    public override Task<long> InsertToGetIdAsync(SiteSetting entity, CancellationToken cancellationToken = default)
    {
        return UpsertAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Updates one setting, creating it when the key is new, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Update and insert are the same statement here; see
    /// <see cref="InsertAsync"/>.</para>
    /// <para><b>Flow:</b> delegate to <see cref="UpsertAsync(SiteSetting, CancellationToken)"/>.</para>
    /// <para><b>Side Effects:</b> Writes one row.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The setting to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(SiteSetting entityToUpdate, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(entityToUpdate, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Reads every setting synchronously.
    /// </summary>
    /// <returns>All rows ordered by group then key.</returns>
    public override IEnumerable<SiteSetting> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<SiteSetting>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Reads the setting with the supplied primary key as a collection.
    /// </summary>
    /// <remarks>
    /// Settings have no parent entity, so the generic "all by id" shape degenerates to a
    /// primary-key lookup returning at most one row.
    /// </remarks>
    /// <param name="singleId">The setting's primary key.</param>
    /// <returns>The matching row as a single-item collection, or an empty collection.</returns>
    public override IEnumerable<SiteSetting> GetAllById(long singleId)
    {
        var single = GetSingle(singleId);
        return single == null ? Enumerable.Empty<SiteSetting>() : new[] { single };
    }

    /// <summary>
    /// Reads one setting by primary key.
    /// </summary>
    /// <param name="singleId">The setting's primary key.</param>
    /// <returns>The matching row, or null.</returns>
    public override SiteSetting? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<SiteSetting>(SelectByIdSql, new { SettingId = singleId });
    }

    /// <summary>
    /// Reads one setting by primary key supplied as an int.
    /// </summary>
    /// <param name="singleId">The setting's primary key.</param>
    /// <returns>The matching row, or null.</returns>
    public override SiteSetting? GetIntSingle(int singleId)
    {
        return GetSingle(singleId);
    }

    /// <summary>
    /// Reads a page of settings.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page ordered by group then key.</returns>
    public override IEnumerable<SiteSetting> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection
            .Query<SiteSetting>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet })
            .ToList();
    }

    /// <summary>
    /// Inserts or updates one setting.
    /// </summary>
    /// <param name="entity">The setting to persist.</param>
    public override void Insert(SiteSetting entity)
    {
        InsertToGetId(entity);
    }

    /// <summary>
    /// Inserts or updates one setting and returns its primary key.
    /// </summary>
    /// <param name="entity">The setting to persist.</param>
    /// <returns>The affected row's primary key.</returns>
    public override long InsertToGetId(SiteSetting entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(UpsertFunctionSql, BuildUpsertParameters(entity));
    }

    /// <summary>
    /// Updates one setting, creating it when the key is new.
    /// </summary>
    /// <param name="entityToUpdate">The setting to persist.</param>
    public override void Update(SiteSetting entityToUpdate)
    {
        InsertToGetId(entityToUpdate);
    }

    /// <summary>
    /// Builds the parameter set for the upsert function.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A null value is stored as SQL NULL and read back as the code
    /// default, which is how a setting is "reset" without deleting its row.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="setting">The setting being written.</param>
    /// <returns>Parameters bound by name to the stored function's arguments.</returns>
    private static DynamicParameters BuildUpsertParameters(SiteSetting setting)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SettingKey", setting.SettingKey);
        parameters.Add("SettingValue", setting.SettingValue);
        parameters.Add("SettingGroup", setting.SettingGroup);
        parameters.Add("IsSecret", setting.IsSecret);
        return parameters;
    }
}
