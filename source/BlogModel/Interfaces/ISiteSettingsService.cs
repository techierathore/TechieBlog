using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Service contract for reading and writing site-wide configuration (BRD-69).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The one published entry point for site settings. Every consumer — public
/// pages needing posts-per-page, the theme provider needing the site theme, the SMTP sender
/// needing mail credentials, the image service needing a storage provider — reads through this
/// interface rather than its own configuration.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The first read loads every row from the <c>SiteSetting</c> table and projects it onto
///     <see cref="SiteSettings"/>.</item>
///   <item>The projection is cached for the lifetime of the singleton service.</item>
///   <item>A save writes the rows and invalidates the cache, then raises
///     <see cref="SettingsChanged"/> so live circuits can re-render — this is what makes a change
///     take effect without a restart.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="ISiteSettingRepo"/>.</para>
///
/// <para><b>Usage:</b> Register as a singleton. Callers must tolerate the built-in defaults being
/// returned when the database is unreachable — settings never throw on read.</para>
///
/// <para><b>Aliasing contract — read with <see cref="GetSettingsAsync"/>, edit with
/// <see cref="GetEditableSettingsAsync"/> (REQ-FN-061).</b> Because the service is a singleton and
/// caches its projection, <see cref="GetSettingsAsync"/> returns <i>the shared instance</i>, not a
/// per-caller copy. That is deliberate — a layout reads it on every render and a copy per render
/// would be waste — but it makes the returned object read-only by convention: assigning to a
/// property of it changes what every circuit and every anonymous visitor sees, immediately, with
/// nothing persisted and no way to undo it. An editor must therefore call
/// <see cref="GetEditableSettingsAsync"/>, which hands back a detached copy safe to bind a form to.
/// <see cref="SaveSettingsAsync"/> likewise returns a detached copy, so re-binding the form to its
/// result does not re-alias the cache.</para>
/// </remarks>
public interface ISiteSettingsService
{
    /// <summary>
    /// Raised after a successful save, carrying the newly effective settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lets long-lived Blazor circuits and background services react
    /// to an administrator's change without polling or restarting.</para>
    /// </remarks>
    event EventHandler<SiteSettings> SettingsChanged;

    /// <summary>
    /// Returns the effective site settings, loading and caching them on first use.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Absent keys fall back to the built-in defaults declared on
    /// <see cref="SiteSettings"/>, so a fresh database renders a working site.</para>
    /// <para><b>Flow:</b> Cache hit returns immediately; a miss reads all rows, projects, caches.</para>
    /// <para><b>Side Effects:</b> Populates the in-memory cache. Never throws — a read failure is
    /// logged and the defaults are returned.</para>
    /// </remarks>
    /// <returns>
    /// The effective settings. Never null. <b>Shared and must not be mutated</b> — see the aliasing
    /// contract on this interface; use <see cref="GetEditableSettingsAsync"/> to edit.
    /// </returns>
    Task<SiteSettings> GetSettingsAsync();

    /// <summary>
    /// Returns a detached copy of the effective settings, safe to bind an edit form to (REQ-FN-061).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The same values as <see cref="GetSettingsAsync"/>, but as a
    /// private copy rather than the process-wide cached instance. This is the member every editing
    /// surface must use: mutating what <see cref="GetSettingsAsync"/> returns re-configures the live
    /// site for all users — including anonymous visitors — the moment a keystroke lands, without a
    /// save and without a rollback path.</para>
    /// <para><b>Flow:</b> Read the effective settings, then deep-copy them via
    /// <see cref="SiteSettings.Clone"/>.</para>
    /// <para><b>Side Effects:</b> May populate the cache on first use. One aggregate allocation per
    /// call, so call it once per form load rather than on a render path.</para>
    /// <para><b>Discarding the copy discards the edits</b> — nothing is persisted until the copy is
    /// handed to <see cref="SaveSettingsAsync"/>, which is precisely the property that makes an
    /// abandoned form harmless.</para>
    /// </remarks>
    /// <returns>An independent copy of the effective settings. Never null.</returns>
    Task<SiteSettings> GetEditableSettingsAsync();

    /// <summary>
    /// Persists the supplied settings and makes them effective immediately.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Values are validated before any write; secret values are
    /// encrypted at rest. A save is all-or-nothing.</para>
    /// <para><b>Flow:</b> Validate, project to rows, upsert in one transaction, drop the cache,
    /// raise <see cref="SettingsChanged"/>.</para>
    /// <para><b>Side Effects:</b> Writes to the database, invalidates the cache, raises an event.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <returns>
    /// Success carrying a <b>detached copy</b> of the newly effective settings (REQ-FN-061), or a
    /// failure describing the problem. The copy means a form that re-binds to the result stays
    /// detached from the cache rather than silently re-aliasing it.
    /// </returns>
    Task<Result<SiteSettings>> SaveSettingsAsync(SiteSettings settings);

    /// <summary>
    /// Reads one raw setting value by key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Serves callers that need a key not represented on the typed
    /// aggregate. Secret values are decrypted before being returned.</para>
    /// <para><b>Side Effects:</b> May populate the cache.</para>
    /// </remarks>
    /// <param name="settingKey">A key from <c>SiteSettingKeys</c>.</param>
    /// <param name="defaultValue">Returned when the key has never been written.</param>
    /// <returns>The stored value, or <paramref name="defaultValue"/>.</returns>
    Task<string> GetValueAsync(string settingKey, string defaultValue);

    /// <summary>
    /// Writes one setting value by key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used for targeted updates that should not require sending the
    /// whole aggregate back.</para>
    /// <para><b>Side Effects:</b> Writes one row, invalidates the cache, raises
    /// <see cref="SettingsChanged"/>.</para>
    /// </remarks>
    /// <param name="settingKey">A key from <c>SiteSettingKeys</c>.</param>
    /// <param name="settingValue">The value to store.</param>
    /// <param name="settingGroup">The owning group from <c>SiteSettingKeys.Groups</c>.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    Task<Result> SetValueAsync(string settingKey, string settingValue, string settingGroup);

    /// <summary>
    /// Returns the outbound e-mail configuration with the password decrypted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The published contract for the SMTP sender (REQ-FN-033).
    /// Callers must check <c>SmtpSettings.IsConfigured</c> before attempting a send.</para>
    /// <para><b>Side Effects:</b> May populate the cache.</para>
    /// </remarks>
    /// <returns>The SMTP configuration. Never null.</returns>
    Task<SmtpSettings> GetSmtpSettingsAsync();

    /// <summary>
    /// Returns the uploaded-media storage configuration with the cloud key decrypted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The published contract for <see cref="IFileStorageFactory"/>
    /// (REQ-FN-042). Defaults to the local provider.</para>
    /// <para><b>Side Effects:</b> May populate the cache.</para>
    /// </remarks>
    /// <returns>The storage configuration. Never null.</returns>
    Task<StorageSettings> GetStorageSettingsAsync();

    /// <summary>
    /// Discards the cached projection so the next read reloads from the database.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Needed when settings are changed out of band — by a second
    /// instance, by the desktop head, or by direct SQL.</para>
    /// <para><b>Side Effects:</b> Clears the cache only; performs no I/O.</para>
    /// </remarks>
    void InvalidateCache();
}
