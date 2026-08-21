using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Repository contract for the key/value <c>SiteSetting</c> table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Isolates the settings service from Dapper and PostgreSQL so the service
/// can be unit-tested with an in-memory fake.</para>
///
/// <para><b>Code Flow:</b> <c>SiteSettingsService</c> calls <see cref="GetAllAsync()"/> once per
/// cache fill and <see cref="UpsertManyAsync(IEnumerable{SiteSetting})"/> once per save.</para>
///
/// <para><b>Dependencies:</b> <see cref="SiteSetting"/>.</para>
///
/// <para><b>Usage:</b> Values are stored verbatim — encryption of secret values is the service's
/// responsibility, not the repository's.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> the members below were already task-returning but
/// opened their connection with the blocking factory, so every one of them parked a thread-pool
/// thread for the whole round trip. <c>SiteSettingRepo</c> now implements them with genuine async
/// Dapper.</para>
///
/// <para><b>Why the token arrives as a companion overload rather than in place.</b> This interface
/// has a hand-written in-memory implementer, <c>FakeSiteSettingRepo</c> under <c>tests/unit</c>,
/// which is not derived from <c>GenericRepository</c>. Appending a <c>CancellationToken</c> to the
/// existing members would have broken it — and breaking an implementer you did not intend to
/// convert is the signal that a change is not additive. Each token-carrying member is therefore a
/// separate overload with a <b>default implementation</b> that delegates to its token-free twin, so
/// the fake keeps compiling and keeps its own instrumentation (read counts, forced failures) in the
/// path. The token parameter deliberately carries <i>no default value</i>: an optional parameter
/// there would make <c>UpsertAsync(setting)</c> a two-candidate call at every existing call site,
/// which is the overload trap this conversion has already been bitten by elsewhere. With no default,
/// <c>UpsertAsync(setting)</c> binds to the token-free member and <c>UpsertAsync(setting, ct)</c>
/// binds to the token-carrying one, unambiguously.</para>
///
/// <para><b>Reading everything</b> needs no new member here: the token-carrying
/// <c>GetAllAsync(CancellationToken)</c> is already inherited from
/// <see cref="IGenericRepository{TEntity}"/>, and re-declaring it would hide the inherited member
/// rather than add anything.</para>
/// </remarks>
public interface ISiteSettingRepo : IGenericRepository<SiteSetting>
{
    /// <summary>
    /// Reads every persisted setting row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Settings are a small, bounded set, so a single unfiltered read
    /// is cheaper than per-key round trips.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>All rows, ordered by group then key.</returns>
    Task<IEnumerable<SiteSetting>> GetAllAsync();

    /// <summary>
    /// Reads a single setting by its key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Key comparison is exact; keys are constants, never user input.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settingKey">The key to look up.</param>
    /// <returns>The matching row, or null when the key has never been written.</returns>
    Task<SiteSetting?> GetByKeyAsync(string settingKey);

    /// <summary>
    /// Reads a single setting by its key, honouring a cancellation token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="GetByKeyAsync(string)"/>; only the
    /// cancellation behaviour differs.</para>
    /// <para><b>Flow:</b> defaults to the token-free twin so an unconverted implementer keeps
    /// working; <c>SiteSettingRepo</c> overrides it with a genuinely cancellable query.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settingKey">The key to look up.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row, or null when the key has never been written.</returns>
    Task<SiteSetting?> GetByKeyAsync(string settingKey, CancellationToken cancellationToken)
        => GetByKeyAsync(settingKey);

    /// <summary>
    /// Inserts or updates a single setting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>UpsertSiteSetting</c> stored function so
    /// the insert-or-update decision is made atomically by the database.</para>
    /// <para><b>Side Effects:</b> Writes one row and stamps <c>UpdatedOn</c>.</para>
    /// </remarks>
    /// <param name="setting">The setting to persist.</param>
    /// <returns>The primary key of the affected row.</returns>
    Task<long> UpsertAsync(SiteSetting setting);

    /// <summary>
    /// Inserts or updates a single setting, honouring a cancellation token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="UpsertAsync(SiteSetting)"/>.</para>
    /// <para><b>Flow:</b> defaults to the token-free twin; <c>SiteSettingRepo</c> overrides it.</para>
    /// <para><b>Side Effects:</b> Writes one row and stamps <c>UpdatedOn</c>.</para>
    /// </remarks>
    /// <param name="setting">The setting to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The primary key of the affected row.</returns>
    Task<long> UpsertAsync(SiteSetting setting, CancellationToken cancellationToken)
        => UpsertAsync(setting);

    /// <summary>
    /// Inserts or updates a batch of settings in one transaction.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A settings save is all-or-nothing, so the whole batch shares a
    /// transaction and rolls back together.</para>
    /// <para><b>Side Effects:</b> Writes every supplied row.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> UpsertManyAsync(IEnumerable<SiteSetting> settings);

    /// <summary>
    /// Inserts or updates a batch of settings in one transaction, honouring a cancellation token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to
    /// <see cref="UpsertManyAsync(IEnumerable{SiteSetting})"/>. Cancelling mid-batch rolls the
    /// transaction back, so a cancelled save leaves the stored configuration untouched rather than
    /// half applied.</para>
    /// <para><b>Flow:</b> defaults to the token-free twin; <c>SiteSettingRepo</c> overrides it.</para>
    /// <para><b>Side Effects:</b> Writes every supplied row.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> UpsertManyAsync(IEnumerable<SiteSetting> settings, CancellationToken cancellationToken)
        => UpsertManyAsync(settings);

    /// <summary>
    /// Removes a setting, reverting it to its built-in default.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting is the supported way to "reset" a setting; the service
    /// substitutes the code default for any absent key.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="settingKey">The key to remove.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> DeleteByKeyAsync(string settingKey);

    /// <summary>
    /// Removes a setting, honouring a cancellation token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="DeleteByKeyAsync(string)"/>.</para>
    /// <para><b>Flow:</b> defaults to the token-free twin; <c>SiteSettingRepo</c> overrides it.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="settingKey">The key to remove.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>True when a row was removed.</returns>
    Task<bool> DeleteByKeyAsync(string settingKey, CancellationToken cancellationToken)
        => DeleteByKeyAsync(settingKey);
}
