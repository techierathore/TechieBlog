using BlogEngine.Common;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Reads and writes site-wide configuration, caching the effective settings in memory (BRD-69).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Replaces the per-browser local-storage preferences that previously stood
/// in for site configuration. Everything an administrator sets on the Settings screen — title,
/// tagline, posts-per-page, comment moderation, the site theme, SMTP and storage — is persisted
/// here and read back by the whole application.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>First read loads every <c>SiteSetting</c> row, decrypts the secret values, and projects
///     them onto <see cref="SiteSettings"/> through <see cref="SiteSettingsMapper"/>.</item>
///   <item>The projection is cached for the lifetime of the singleton.</item>
///   <item>A save validates, encrypts secrets, writes the whole set in one transaction, drops the
///     cache and raises <see cref="SettingsChanged"/> — so the change is effective immediately and
///     no restart is needed.</item>
/// </list>
///
/// <para><b>Cache-invalidation contract — the rule that keeps administrators honest.</b> The
/// effective settings are held in a field for the lifetime of the singleton, with <b>no expiry of
/// any kind</b>: nothing refreshes it on a timer, so it is correct only for as long as every write
/// goes through this class. Three things follow, and they are the contract every change here must
/// preserve:</para>
/// <list type="number">
///   <item><b>Every write path must invalidate.</b> <see cref="SaveSettingsAsync"/> and
///     <see cref="SetValueAsync"/> both call <c>PublishChangeAsync</c>, which drops the cache,
///     reloads it and raises <see cref="SettingsChanged"/>. A new write path that forgets this
///     leaves administrators looking at a value that is <i>not</i> what is stored — and because the
///     screen re-reads through the same cache, saving again appears to change nothing.</item>
///   <item><b>Invalidate-then-reload, not invalidate-and-hope.</b> The reload happens inside the
///     save, so the aggregate returned to the caller and broadcast on the event is exactly what the
///     next reader will see. A save therefore takes effect immediately and no restart is
///     needed.</item>
///   <item><b>Anything that writes the <c>SiteSetting</c> table behind this class's back is
///     invisible to it.</b> A direct SQL update, a migration, or a second process will not be
///     picked up until the host restarts. There is no cross-process invalidation — in a
///     multi-instance deployment each instance caches independently, so a save on one instance is
///     not seen by the others.</item>
/// </list>
/// <para>The cache is also read <i>outside</i> the lock on the fast path (a plain field read),
/// which is safe because the field is only ever replaced with a fully-built aggregate, never
/// mutated in place. <see cref="InvalidateCache"/> exists for the case where an external writer is
/// known to have changed a row; it is the escape hatch, not the normal path.</para>
///
/// <para><b>Batch save is transactional.</b> <see cref="SaveSettingsAsync"/> projects the whole
/// aggregate to rows and hands them to <c>ISiteSettingRepo.UpsertManyAsync</c>, which wraps the
/// upserts in a single database transaction (<c>BeginTransactionAsync</c> /
/// <c>CommitAsync</c> / <c>RollbackAsync</c>). That matters because the settings are read back as a
/// set: a partial write would leave, say, a new SMTP host beside the old port, and the site would
/// run on a combination the administrator never approved. Either every row lands or none does — and
/// on a rollback the cache is left untouched, because the invalidation only runs after the write
/// returns successfully.</para>
///
/// <para><b>DANGER — the encryption key cannot be rotated without re-entering the secrets.</b>
/// Secret-flagged rows (the SMTP password and the cloud storage access key) are encrypted at rest by
/// <c>AppEncrypt</c> under the AES key supplied as <c>AppEncryptionKey</c>. <b>Rotating that key
/// makes every existing ciphertext permanently undecryptable.</b> There is no key versioning: the
/// stored value carries no indication of which key produced it, the application cannot fall back to
/// the previous key, and no recovery path exists. Worse, the failure is quiet — nothing breaks at
/// startup, and <c>RevealSecret</c> deliberately swallows a decryption error and returns the raw
/// ciphertext, so the first visible symptom is mail failing to send or an upload being rejected with
/// a credential that looks superficially present. <b>Anyone rotating <c>AppEncryptionKey</c> must
/// immediately re-enter every secret setting through the admin Settings screen</b>; treat it as a
/// maintenance window, not a configuration tweak. See <c>AppSecrets</c> for the full warning.</para>
///
/// <para><b>Dependencies:</b> <see cref="ISiteSettingRepo"/>, <see cref="ILogger{TCategoryName}"/>,
/// <c>AppEncrypt</c> for encrypting credentials at rest, <c>SiteSettingsMapper</c> for the
/// row-to-aggregate projection and the secret-key list.</para>
///
/// <para><b>Usage:</b> Registered as a singleton by <c>BlogSvcInitializer</c> — required, so the
/// cache and the change event are shared by every circuit; registering it scoped would give each
/// user their own cache and their own view of the settings. Reads never throw: when the database is
/// unreachable the built-in defaults are returned and the failure is logged, so a settings outage
/// degrades the site's branding rather than taking it down.</para>
///
/// <para><b>Authorization:</b> none is enforced here. Every write member is reached from the admin
/// Settings screen behind <c>AppPolicies.AdminOnly</c>, and the calling page owns that check. Note
/// that the read members return <b>decrypted</b> secrets — <see cref="GetSmtpSettingsAsync"/> and
/// <see cref="GetValueAsync"/> will hand back a plaintext SMTP password to anyone who can reach
/// them, so never expose them from an unauthenticated surface or echo them into a page.</para>
/// </remarks>
public class SiteSettingsService : ISiteSettingsService
{
    private readonly ISiteSettingRepo siteSettingRepo;
    private readonly ILogger<SiteSettingsService> logger;
    private readonly SemaphoreSlim cacheGate = new SemaphoreSlim(1, 1);
    private SiteSettings? cachedSettings;

    /// <summary>
    /// Creates the service over a settings repository.
    /// </summary>
    /// <param name="siteSettingRepo">Persistence for the key/value settings table.</param>
    /// <param name="logger">Structured logger for read and write failures.</param>
    public SiteSettingsService(ISiteSettingRepo siteSettingRepo, ILogger<SiteSettingsService> logger)
    {
        this.siteSettingRepo = siteSettingRepo ?? throw new ArgumentNullException(nameof(siteSettingRepo));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Raised after a save has been persisted <i>and</i> the cache reloaded, so a handler reading
    /// <see cref="GetSettingsAsync"/> sees the new values. Handlers run synchronously on the saving
    /// thread and their exceptions would propagate into the save — keep them short and
    /// non-throwing. Because the service is a singleton, a subscriber with a shorter lifetime (a
    /// Blazor circuit, a component) <b>must unsubscribe</b> or it will be held alive for the life
    /// of the process.
    /// </remarks>
    public event EventHandler<SiteSettings>? SettingsChanged;

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the cached aggregate, loading it on first use. The
    /// cache has no expiry — see the type-level invalidation contract — so this is a field read on
    /// every call after the first, which is what makes it cheap enough for a layout to call on
    /// every render.</para>
    /// <para><b>Flow:</b> lock-free read of the cached snapshot → on a miss take the gate →
    /// double-check → load → cache.</para>
    /// <para><b>Side Effects:</b> Populates the cache on the first call. Never throws: a load
    /// failure logs and yields the built-in defaults, so an unreachable database costs the site its
    /// configured branding rather than its availability. <b>A caller therefore cannot distinguish
    /// "defaults because nothing is configured" from "defaults because the database is down"</b> —
    /// check the log before concluding a setting was never saved.</para>
    /// </remarks>
    public async Task<SiteSettings> GetSettingsAsync()
    {
        var snapshot = cachedSettings;
        if (snapshot != null)
        {
            return snapshot;
        }

        await cacheGate.WaitAsync().ConfigureAwait(false);
        try
        {
            cachedSettings ??= await LoadSettingsAsync().ConfigureAwait(false);
            return cachedSettings;
        }
        finally
        {
            cacheGate.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes the aggregate as a whole. Validation runs first and the
    /// first failure wins, so nothing is written when any field is unacceptable — a partially valid
    /// save is never half-applied. Secret-flagged values are encrypted on the way out, and the
    /// entire row set is upserted inside one database transaction, so the settings can never be
    /// observed mid-update.</para>
    /// <para><b>Flow:</b> validate → project to rows → encrypt secrets → transactional upsert →
    /// invalidate, reload and broadcast → return the reloaded aggregate.</para>
    /// <para><b>Side Effects:</b> Writes every settings row; encrypts the SMTP password and cloud
    /// access key at rest; <b>drops the cache and raises <see cref="SettingsChanged"/></b> so the
    /// change takes effect immediately across every circuit without a restart. Logs the row
    /// count.</para>
    /// <para><b>Result contract:</b> a validation failure and a write failure are both
    /// <i>returned</i>, never thrown. The write-failure message interpolates <c>ex.Message</c>,
    /// which is acceptable only because the caller is the admin-only Settings screen.</para>
    /// <para><b>Returns the reloaded settings, not the supplied ones</b> — read the
    /// <c>Result.Data</c> rather than reusing the object you passed in, since defaults and
    /// normalisation are applied during the reload.</para>
    /// </remarks>
    public async Task<Result<SiteSettings>> SaveSettingsAsync(SiteSettings settings)
    {
        var validation = ValidateSettings(settings);
        if (validation.IsFailure)
        {
            return Result<SiteSettings>.Failure(validation.ErrorMessage);
        }

        try
        {
            var rows = SiteSettingsMapper.ToRows(settings).Select(ProtectSecret).ToList();
            await siteSettingRepo.UpsertManyAsync(rows).ConfigureAwait(false);
            logger.LogInformation("Persisted {Count} site settings", rows.Count);
            var effective = await PublishChangeAsync().ConfigureAwait(false);
            return Result<SiteSettings>.Success(effective);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist site settings");
            return Result<SiteSettings>.Failure($"Failed to save settings: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads one key straight from the database, <b>bypassing the
    /// cache entirely</b>, and decrypts it when the row is flagged secret. Use it for an ad-hoc key
    /// that has no place on the <c>SiteSettings</c> aggregate; for anything on the aggregate prefer
    /// <see cref="GetSettingsAsync"/>, which is a cached field read rather than a round trip.</para>
    /// <para><b>Flow:</b> blank-key guard → read the row → return the default when absent, or the
    /// revealed value.</para>
    /// <para><b>Side Effects:</b> One database read per call — do not put this on a hot render
    /// path. Never throws: a read failure logs and yields <paramref name="defaultValue"/>, so the
    /// default is indistinguishable from a failure.</para>
    /// <para><b>Returns decrypted secrets.</b> If the key names a secret, the plaintext comes back.
    /// Treat the result as sensitive.</para>
    /// </remarks>
    public async Task<string> GetValueAsync(string settingKey, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return defaultValue;
        }

        try
        {
            var row = await siteSettingRepo.GetByKeyAsync(settingKey).ConfigureAwait(false);
            return row == null ? defaultValue : RevealSecret(row);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read site setting {SettingKey}", settingKey);
            return defaultValue;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes a single key. Whether the value is encrypted is decided
    /// by the key itself through <c>SiteSettingsMapper.IsSecretKey</c>, not by the caller — so a
    /// credential written through this path is protected even by a caller that did not think about
    /// it.</para>
    /// <para><b>Flow:</b> blank-key guard → build and protect the row → upsert → invalidate,
    /// reload and broadcast.</para>
    /// <para><b>Side Effects:</b> Writes one row, <b>drops the whole settings cache</b> and raises
    /// <see cref="SettingsChanged"/> — the invalidation is deliberately coarse, because the single
    /// key may well be one the aggregate projects from. A missing group defaults to
    /// <c>General</c>.</para>
    /// <para><b>Result contract:</b> a missing key and a write failure are both returned, not
    /// thrown. Unlike <see cref="SaveSettingsAsync"/> this member applies <b>no validation</b> to
    /// the value, so it will happily store an SMTP port of <c>-1</c> that the aggregate save would
    /// have rejected; validate before calling it.</para>
    /// </remarks>
    public async Task<Result> SetValueAsync(string settingKey, string settingValue, string settingGroup)
    {
        if (string.IsNullOrWhiteSpace(settingKey))
        {
            return Result.Failure("Setting key is required");
        }

        try
        {
            await siteSettingRepo.UpsertAsync(BuildRow(settingKey, settingValue, settingGroup))
                .ConfigureAwait(false);
            await PublishChangeAsync().ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write site setting {SettingKey}", settingKey);
            return Result.Failure($"Failed to save setting: {ex.Message}");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Projects the mail section out of the cached aggregate, with the
    /// password already decrypted. Never null — an unconfigured site yields an empty
    /// <c>SmtpSettings</c>, and <c>SmtpSettings.IsConfigured</c> is the guard a caller should test
    /// before attempting a send.</para>
    /// <para><b>Side Effects:</b> None beyond the first-call cache load.</para>
    /// <para><b>Returns a plaintext credential</b> — never log the result, bind it to a page, or
    /// include it in a diagnostic dump.</para>
    /// <para><b>Currently unused by the sender.</b> <c>SmtpEmailService</c> reads
    /// <c>IConfiguration</c> instead, so values returned here do not presently affect mail
    /// delivery. See the note on that class.</para>
    /// </remarks>
    public async Task<SmtpSettings> GetSmtpSettingsAsync()
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        return settings.Smtp ?? new SmtpSettings();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Projects the storage section out of the cached aggregate, with
    /// the cloud access key already decrypted. Never null — an unconfigured site yields an empty
    /// <c>StorageSettings</c>, which the image pipeline reads as "use local disk".</para>
    /// <para><b>Side Effects:</b> None beyond the first-call cache load.</para>
    /// <para><b>Returns a plaintext credential</b> — same handling rules as
    /// <see cref="GetSmtpSettingsAsync"/>.</para>
    /// </remarks>
    public async Task<StorageSettings> GetStorageSettingsAsync()
    {
        var settings = await GetSettingsAsync().ConfigureAwait(false);
        return settings.Storage ?? new StorageSettings();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The escape hatch for the one case the normal contract cannot
    /// cover — something changed the <c>SiteSetting</c> table without going through this service (a
    /// direct SQL update, a migration, another instance). The writes on this class already
    /// invalidate for themselves, so a caller reaching for this after a save is duplicating
    /// work.</para>
    /// <para><b>Flow:</b> null out the cached snapshot; the next read reloads it.</para>
    /// <para><b>Side Effects:</b> The next <see cref="GetSettingsAsync"/> pays a database round
    /// trip. Does <b>not</b> raise <see cref="SettingsChanged"/> — subscribers are not told, so
    /// anything holding a copy of the settings will keep using it until it re-reads. Safe to call
    /// concurrently: it is a single reference write, and a reader racing it either sees the old
    /// snapshot (harmless, it was valid a moment ago) or reloads.</para>
    /// </remarks>
    public void InvalidateCache()
    {
        cachedSettings = null;
    }

    /// <summary>
    /// Validates a settings aggregate before any write occurs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Rejects the values that would visibly break the public site —
    /// an empty title, a non-positive page size, a negative pagination threshold, or an SMTP port
    /// outside the legal TCP range.</para>
    /// <para><b>Flow:</b> Null guard, then one check per constrained field, first failure wins.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settings">The candidate settings.</param>
    /// <returns>Success, or a failure naming the offending field.</returns>
    public static Result ValidateSettings(SiteSettings settings)
    {
        if (settings == null)
        {
            return Result.Failure("Settings cannot be null");
        }

        if (string.IsNullOrWhiteSpace(settings.SiteTitle))
        {
            return Result.Failure("Site title is required");
        }

        if (settings.PostsPerPage <= 0)
        {
            return Result.Failure("Posts per page must be greater than zero");
        }

        if (settings.PaginationWordCount < 0)
        {
            return Result.Failure("Pagination word count cannot be negative");
        }

        return ValidateSmtp(settings.Smtp);
    }

    /// <summary>
    /// Validates the SMTP portion of a settings aggregate.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unconfigured mail section is legal — the console sender is
    /// the documented development fallback — but a supplied port must be a real TCP port.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="smtp">The SMTP settings, which may be null.</param>
    /// <returns>Success, or a failure describing the invalid port.</returns>
    private static Result ValidateSmtp(SmtpSettings smtp)
    {
        if (smtp == null)
        {
            return Result.Success();
        }

        if (smtp.Port < 1 || smtp.Port > 65535)
        {
            return Result.Failure("SMTP port must be between 1 and 65535");
        }

        return Result.Success();
    }

    /// <summary>
    /// Loads and projects every persisted setting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A read failure must not take the site down, so the built-in
    /// defaults are returned and the exception is logged with context.</para>
    /// <para><b>Flow:</b> Read rows, decrypt secrets into a dictionary, project through the mapper.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// </remarks>
    /// <returns>The effective settings. Never null.</returns>
    private async Task<SiteSettings> LoadSettingsAsync()
    {
        try
        {
            var rows = (await siteSettingRepo.GetAllAsync().ConfigureAwait(false)).ToList();
            var values = rows.ToDictionary(row => row.SettingKey, RevealSecret, StringComparer.Ordinal);
            var updatedOn = rows.Count == 0 ? DateTime.MinValue : rows.Max(row => row.UpdatedOn);
            return SiteSettingsMapper.ToSettings(values, updatedOn);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load site settings; falling back to built-in defaults");
            return new SiteSettings();
        }
    }

    /// <summary>
    /// Drops the cache and announces the new effective settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reloading immediately means the returned aggregate is exactly
    /// what the next reader will see, which is what "takes effect without a restart" requires.</para>
    /// <para><b>Side Effects:</b> Clears the cache and raises <see cref="SettingsChanged"/>.</para>
    /// </remarks>
    /// <returns>The freshly loaded effective settings.</returns>
    private async Task<SiteSettings> PublishChangeAsync()
    {
        InvalidateCache();
        var refreshed = await GetSettingsAsync().ConfigureAwait(false);
        SettingsChanged?.Invoke(this, refreshed);
        return refreshed;
    }

    /// <summary>
    /// Builds a persistable row for a single key, encrypting it when the key is a credential.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Group defaults to General so a caller writing an ad-hoc key
    /// still produces a well-formed row.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settingKey">The key being written.</param>
    /// <param name="settingValue">The plain value.</param>
    /// <param name="settingGroup">The owning group, or null for General.</param>
    /// <returns>A row ready for the repository.</returns>
    private static SiteSetting BuildRow(string settingKey, string settingValue, string settingGroup)
    {
        var row = new SiteSetting
        {
            SettingKey = settingKey,
            SettingValue = settingValue ?? string.Empty,
            SettingGroup = string.IsNullOrWhiteSpace(settingGroup)
                ? SiteSettingKeys.Groups.General
                : settingGroup,
            IsSecret = SiteSettingsMapper.IsSecretKey(settingKey)
        };
        return ProtectSecret(row);
    }

    /// <summary>
    /// Encrypts a row's value when the row is flagged secret.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Credentials must not sit in the database in the clear. Empty
    /// values are left alone so "no password set" stays legible.</para>
    /// <para><b>Side Effects:</b> Mutates and returns the supplied row.</para>
    /// </remarks>
    /// <param name="row">The row about to be written.</param>
    /// <returns>The same row with its value protected where required.</returns>
    private static SiteSetting ProtectSecret(SiteSetting row)
    {
        if (!row.IsSecret || string.IsNullOrEmpty(row.SettingValue))
        {
            return row;
        }

        row.SettingValue = AppEncrypt.EncryptText(row.SettingValue);
        return row;
    }

    /// <summary>
    /// Returns a row's value in the clear, decrypting it when the row is flagged secret.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A value that fails to decrypt — typically a plaintext value
    /// written before this feature existed — is returned as-is rather than throwing, so one bad
    /// row cannot break the whole settings load.</para>
    /// <para><b>Side Effects:</b> None beyond logging a warning.</para>
    /// </remarks>
    /// <param name="row">The row just read.</param>
    /// <returns>The plain value, never null.</returns>
    private string RevealSecret(SiteSetting row)
    {
        if (!row.IsSecret || string.IsNullOrEmpty(row.SettingValue))
        {
            return row.SettingValue ?? string.Empty;
        }

        try
        {
            return AppEncrypt.DecryptText(row.SettingValue);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Site setting {SettingKey} could not be decrypted", row.SettingKey);
            return row.SettingValue;
        }
    }
}
