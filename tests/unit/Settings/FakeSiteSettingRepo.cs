using System.Data;
using BlogModels.Interfaces;
using BlogModels.Models;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// In-memory stand-in for <see cref="ISiteSettingRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the settings tests prove the save/read round trip without a
/// PostgreSQL instance. [REQ-FN-040]</para>
///
/// <para><b>Code Flow:</b> <see cref="UpsertManyAsync"/> reproduces the key-matched upsert the
/// <c>UpsertSiteSetting</c> stored function performs, including the <c>UpdatedOn</c> stamp, so a
/// second save of the same key updates the existing row instead of adding a duplicate.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Seed with <see cref="UpsertAsync"/>, act through
/// <c>SiteSettingsService</c>, then assert on <see cref="Rows"/> or read back through the
/// service. <see cref="FailNextRead"/> and <see cref="FailNextWrite"/> force the failure paths.
/// </para>
/// </remarks>
public class FakeSiteSettingRepo : ISiteSettingRepo
{
    private readonly List<SiteSetting> rows = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the rows this fake currently holds.
    /// </summary>
    public IReadOnlyList<SiteSetting> Rows => rows;

    /// <summary>
    /// Gets the number of times <see cref="GetAllAsync"/> has been called.
    /// </summary>
    /// <remarks>
    /// The cache tests assert on this: a cached read must not reach the repository, and a save
    /// must force the next read to.
    /// </remarks>
    public int ReadCount { get; private set; }

    /// <summary>
    /// When true the next read throws, simulating an unreachable database.
    /// </summary>
    public bool FailNextRead { get; set; }

    /// <summary>
    /// When true the next write throws, simulating a failed transaction.
    /// </summary>
    public bool FailNextWrite { get; set; }

    /// <inheritdoc />
    public Task<IEnumerable<SiteSetting>> GetAllAsync()
    {
        ReadCount++;
        if (FailNextRead)
        {
            FailNextRead = false;
            throw new InvalidOperationException("Simulated read failure");
        }

        IEnumerable<SiteSetting> snapshot = rows
            .Select(Clone)
            .OrderBy(row => row.SettingGroup, StringComparer.Ordinal)
            .ThenBy(row => row.SettingKey, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<SiteSetting?> GetByKeyAsync(string settingKey)
    {
        var found = rows.FirstOrDefault(row => string.Equals(row.SettingKey, settingKey, StringComparison.Ordinal));
        return Task.FromResult(found == null ? null : Clone(found));
    }

    /// <inheritdoc />
    public Task<long> UpsertAsync(SiteSetting setting)
    {
        if (FailNextWrite)
        {
            FailNextWrite = false;
            throw new InvalidOperationException("Simulated write failure");
        }

        return Task.FromResult(Write(setting));
    }

    /// <inheritdoc />
    public Task<int> UpsertManyAsync(IEnumerable<SiteSetting> settings)
    {
        if (FailNextWrite)
        {
            FailNextWrite = false;
            throw new InvalidOperationException("Simulated write failure");
        }

        var pending = settings.Where(setting => setting != null).ToList();
        foreach (var setting in pending)
        {
            Write(setting);
        }

        return Task.FromResult(pending.Count);
    }

    /// <inheritdoc />
    public Task<bool> DeleteByKeyAsync(string settingKey)
    {
        var removed = rows.RemoveAll(row => string.Equals(row.SettingKey, settingKey, StringComparison.Ordinal));
        return Task.FromResult(removed > 0);
    }

    /// <inheritdoc />
    public IEnumerable<SiteSetting> GetAll() => rows.Select(Clone).ToList();

    /// <inheritdoc />
    public IEnumerable<SiteSetting> GetAllById(long singleId)
    {
        var single = GetSingle(singleId);
        return single == null ? Enumerable.Empty<SiteSetting>() : new[] { single };
    }

    /// <inheritdoc />
    public SiteSetting? GetSingle(long singleId)
    {
        var found = rows.FirstOrDefault(row => row.SettingId == singleId);
        return found == null ? null : Clone(found);
    }

    /// <inheritdoc />
    public SiteSetting? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <inheritdoc />
    public IEnumerable<SiteSetting> GetPagedData(int pageSize, int offSet) =>
        rows.Skip(offSet).Take(pageSize).Select(Clone).ToList();

    /// <inheritdoc />
    public void Insert(SiteSetting entity) => Write(entity);

    /// <inheritdoc />
    public long InsertToGetId(SiteSetting entity) => Write(entity);

    /// <inheritdoc />
    public void Update(SiteSetting entityToUpdate) => Write(entityToUpdate);

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() =>
        throw new NotSupportedException("The in-memory fake has no connection.");

    /// <summary>
    /// Inserts or updates one row, matching on <c>SettingKey</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the <c>UpsertSiteSetting</c> stored function — the key
    /// is the natural key, and every write refreshes the group, the secret flag and the timestamp.
    /// </para>
    /// <para><b>Side Effects:</b> Mutates the backing list.</para>
    /// </remarks>
    /// <param name="setting">The setting to persist.</param>
    /// <returns>The affected row's primary key.</returns>
    private long Write(SiteSetting setting)
    {
        var existing = rows.FirstOrDefault(row =>
            string.Equals(row.SettingKey, setting.SettingKey, StringComparison.Ordinal));

        if (existing == null)
        {
            var inserted = Clone(setting);
            inserted.SettingId = nextId++;
            inserted.UpdatedOn = DateTime.UtcNow;
            rows.Add(inserted);
            return inserted.SettingId;
        }

        existing.SettingValue = setting.SettingValue;
        existing.SettingGroup = setting.SettingGroup;
        existing.IsSecret = setting.IsSecret;
        existing.UpdatedOn = DateTime.UtcNow;
        return existing.SettingId;
    }

    /// <summary>
    /// Copies a row so callers cannot mutate the fake's state by holding a reference.
    /// </summary>
    /// <param name="source">The row to copy.</param>
    /// <returns>A detached copy.</returns>
    private static SiteSetting Clone(SiteSetting source) => new()
    {
        SettingId = source.SettingId,
        SettingKey = source.SettingKey,
        SettingValue = source.SettingValue,
        SettingGroup = source.SettingGroup,
        IsSecret = source.IsSecret,
        UpdatedOn = source.UpdatedOn
    };
}
