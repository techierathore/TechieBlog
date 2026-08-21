namespace BlogModels.Models;

/// <summary>
/// Uploaded-media storage configuration held in site settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Selects and configures the <c>IFileStorage</c> implementation that sits
/// behind the image service, so a deployment can move uploads from local disk to a network share
/// or an object store without a code change (BRD-45/46).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Admin picks a provider on the Settings screen.</item>
///   <item><c>IFileStorageFactory</c> reads these values and returns the matching implementation.</item>
///   <item><c>BlogImageService</c> writes and deletes through that implementation only.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Leave <see cref="ProviderName"/> at <c>Local</c> for the default
/// clone-and-run experience; only the fields relevant to the selected provider are read, so the
/// others may hold stale values from a previous configuration without affecting anything. Like
/// <see cref="SmtpSettings"/>, this is not a persistence shape — each property is one
/// <see cref="SiteSetting"/> row, reassembled by <c>SiteSettingsMapper</c>. Switching providers
/// does not migrate existing files: paths already recorded against uploaded images keep pointing at
/// the old location.</para>
///
/// <para><b>Security:</b> <see cref="CloudAccessKey"/> is the only encrypted member (key
/// <c>StorageCloudAccessKey</c>). It shares the single <c>AppEncryptionKey</c> with the SMTP
/// password and has the same consequence — <b>rotating that key makes the stored access key
/// permanently undecryptable</b>, because nothing versions the ciphertext, and it must be
/// re-entered.</para>
/// </remarks>
public class StorageSettings
{
    /// <summary>
    /// Which provider the factory instantiates — one of the constants on
    /// <see cref="StorageProviderNames"/>. Compared case-insensitively, and an unrecognised value
    /// falls back to <see cref="StorageProviderNames.Local"/> rather than failing, so a typo here
    /// silently sends uploads to local disk instead of the intended destination.
    /// </summary>
    public string ProviderName { get; set; } = StorageProviderNames.Local;

    /// <summary>
    /// Absolute filesystem root for the <see cref="StorageProviderNames.Local"/> provider. Empty
    /// means "use the host web root", which is the clone-and-run default. Read only when the local
    /// provider is selected.
    /// </summary>
    public string LocalRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Root for the <see cref="StorageProviderNames.Network"/> provider — a UNC path such as
    /// <c>\\nas\techieblog\uploads</c>, or a mount point on Linux. The host process must already
    /// have credentials for the share; nothing on this type supplies them, so an unauthenticated
    /// share fails at write time rather than at configuration time.
    /// </summary>
    public string NetworkRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Base URL of the object-store endpoint for the <see cref="StorageProviderNames.Cloud"/>
    /// provider. Administrator-supplied and used to build outbound requests, so it is effectively a
    /// server-side request destination — validate it before saving rather than trusting the field.
    /// </summary>
    public string CloudServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Bucket or container the cloud provider writes into. Combined with
    /// <see cref="CloudServiceUrl"/> to form the object path; empty puts objects at the endpoint
    /// root, which most object stores reject.
    /// </summary>
    public string CloudContainerName { get; set; } = string.Empty;

    /// <summary>
    /// Bearer or API credential presented to the object store. <b>Encrypted at rest</b> under the
    /// setting key <c>StorageCloudAccessKey</c>; this property always carries the decrypted value.
    /// </summary>
    /// <remarks>
    /// A live credential with write access to the media store — never log it, never render it back
    /// into the Settings form as readable text, and never include it in a settings dump. See the
    /// security note on the type for what an encryption-key rotation does to it.
    /// </remarks>
    public string CloudAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// Public URL prefix that maps to the storage root. Empty means "serve from the site root",
    /// which is the correct answer for the default local provider.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Returns an independent copy of this configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every member is a string, so a member-wise copy is already a
    /// full copy. The method exists for the same reason as <see cref="SmtpSettings.Clone"/> — so a
    /// reference member added later has an obvious place to be copied properly.</para>
    /// <para><b>Flow:</b> Construct a new instance and assign each member.</para>
    /// <para><b>Side Effects:</b> None. The copy carries the decrypted
    /// <see cref="CloudAccessKey"/> and is exactly as sensitive as the original.</para>
    /// </remarks>
    /// <returns>A new instance sharing no mutable state with this one.</returns>
    public StorageSettings Clone()
    {
        return new StorageSettings
        {
            ProviderName = ProviderName,
            LocalRootPath = LocalRootPath,
            NetworkRootPath = NetworkRootPath,
            CloudServiceUrl = CloudServiceUrl,
            CloudContainerName = CloudContainerName,
            CloudAccessKey = CloudAccessKey,
            PublicBaseUrl = PublicBaseUrl
        };
    }
}
