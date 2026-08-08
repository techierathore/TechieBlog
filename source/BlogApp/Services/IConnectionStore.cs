namespace BlogApp.Services;

/// <summary>
/// Encrypted persistence for BlogApp's PostgreSQL connection settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the site database credentials off disk in plain text (REQ-FN-047).
/// The connection string is the only secret BlogApp holds, and it grants full read/write access to
/// the live blog, so it is written exclusively through platform secure storage.</para>
/// <para><b>Code Flow:</b> connection-setup screen → <see cref="SaveAsync"/> → OS credential store;
/// startup → <see cref="LoadAsync"/> → <c>BlogSvcInitializer</c>; settings surface →
/// <see cref="ClearAsync"/> → app returns to the setup screen on next launch.</para>
/// <para><b>Dependencies:</b> Implemented by <see cref="ConnectionStore"/> over MAUI
/// <c>SecureStorage</c>.</para>
/// <para><b>Usage:</b> Injected as a singleton; never cache the returned settings elsewhere.</para>
/// </remarks>
public interface IConnectionStore
{
    /// <summary>
    /// Human-readable description of where the credentials physically live.
    /// </summary>
    /// <remarks>
    /// Surfaced on the connection-settings screen so an operator can audit the storage location
    /// without reading the source, and used by the smoke test to prove the value is not in a
    /// plaintext settings file.
    /// </remarks>
    string StorageDescription { get; }

    /// <summary>
    /// Reads the stored connection settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A first run — or a run after <see cref="ClearAsync"/> — has no
    /// stored value and yields <c>null</c>, which is the signal that the app must open at the
    /// connection-setup screen.</para>
    /// <para><b>Flow:</b> read the encrypted blob → decrypt → deserialise.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The stored settings, or <c>null</c> when nothing has been saved.</returns>
    Task<ConnectionSettings> LoadAsync();

    /// <summary>
    /// Persists the connection settings in encrypted platform storage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Replaces any previously stored value. The caller is expected to
    /// have probed the server first, so only settings known to work are persisted.</para>
    /// <para><b>Flow:</b> serialise → encrypt → write.</para>
    /// <para><b>Side Effects:</b> Writes to the OS credential store.</para>
    /// </remarks>
    /// <param name="settings">The settings to store. Must not be <c>null</c>.</param>
    /// <returns>A task that completes when the settings have been written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <c>null</c>.</exception>
    Task SaveAsync(ConnectionSettings settings);

    /// <summary>
    /// Removes the stored connection settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns BlogApp to its first-run state. The acceptance criterion
    /// for REQ-FN-047 is that the next launch shows the connection-setup screen again.</para>
    /// <para><b>Flow:</b> delete the secure entry → delete any fallback file.</para>
    /// <para><b>Side Effects:</b> Removes the credential from the OS store.</para>
    /// </remarks>
    /// <returns>A task that completes when the settings have been removed.</returns>
    Task ClearAsync();
}
