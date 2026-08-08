namespace BlogModels.Models;

/// <summary>
/// Canonical provider names accepted by <c>StorageSettings.ProviderName</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the persisted provider identifier in one place so the settings
/// screen, the storage factory and the migration seed cannot drift apart.</para>
///
/// <para><b>Code Flow:</b> The settings service stores one of these strings; the storage factory
/// switches on it to choose an implementation, falling back to <see cref="Local"/>.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Compare with <c>StringComparison.OrdinalIgnoreCase</c> — values written by
/// earlier releases may differ in casing.</para>
/// </remarks>
public static class StorageProviderNames
{
    /// <summary>
    /// Writes beneath the host's web root. The default, zero-configuration provider.
    /// </summary>
    public const string Local = "Local";

    /// <summary>
    /// Writes to a UNC share or mounted network path.
    /// </summary>
    public const string Network = "Network";

    /// <summary>
    /// Writes to an HTTP object store (S3-compatible or any endpoint accepting PUT/GET/DELETE).
    /// </summary>
    public const string Cloud = "Cloud";
}
