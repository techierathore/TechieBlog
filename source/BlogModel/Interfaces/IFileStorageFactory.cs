namespace BlogModels.Interfaces;

/// <summary>
/// Resolves the <see cref="IFileStorage"/> implementation selected in site settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps provider selection out of the image service. Because the choice
/// lives in site settings rather than start-up configuration, an administrator can switch
/// backends and the very next upload uses the new one — no restart (REQ-FN-042).</para>
///
/// <para><b>Code Flow:</b> Read <c>StorageSettings.ProviderName</c> through
/// <see cref="ISiteSettingsService"/>, then return the matching registered implementation,
/// falling back to local storage for an unknown or empty name.</para>
///
/// <para><b>Dependencies:</b> <see cref="ISiteSettingsService"/>, the registered
/// <see cref="IFileStorage"/> implementations.</para>
///
/// <para><b>Usage:</b> Resolve per operation rather than caching the returned instance, so a
/// settings change is picked up promptly.</para>
/// </remarks>
public interface IFileStorageFactory
{
    /// <summary>
    /// Returns the storage implementation currently selected in site settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unrecognised or blank provider name resolves to local
    /// storage so a misconfiguration degrades to the working default instead of failing uploads.</para>
    /// <para><b>Flow:</b> Read settings, match the provider name, return the implementation.</para>
    /// <para><b>Side Effects:</b> May trigger the settings cache to fill.</para>
    /// </remarks>
    /// <returns>The configured storage implementation. Never null.</returns>
    Task<IFileStorage> GetStorageAsync();

    /// <summary>
    /// Returns a specific storage implementation by name, ignoring the configured selection.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Supports administrative tasks such as migrating existing media
    /// from one backend to another, where both must be addressed at once.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="providerName">A value from <c>StorageProviderNames</c>.</param>
    /// <returns>The named implementation, or the local implementation when the name is unknown.</returns>
    Task<IFileStorage> GetStorageByNameAsync(string providerName);
}
