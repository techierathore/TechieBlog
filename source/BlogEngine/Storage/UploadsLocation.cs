using Microsoft.Extensions.Configuration;

namespace BlogEngine.Storage;

/// <summary>
/// Resolves the one directory uploaded media is written to and served from (REQ-FN-025).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Uploads have two halves that must agree or images break silently — the
/// directory <c>BlogImageService</c> writes bytes into, and the directory the host publishes at
/// <c>/uploads</c>. Before this type each half computed its own answer from
/// <c>IWebHostEnvironment.WebRootPath</c>, which is fine on a developer's machine and wrong in a
/// container: <c>wwwroot</c> lives INSIDE the image, so every image an editor uploads is destroyed
/// by the next redeploy. The deployment contract puts them on a mounted host path instead —
/// <c>/srv/data/techieblog/uploads</c> on the server, <c>/app/uploads</c> in the container — and
/// this type is what both halves now ask.</para>
///
/// <para><b>Two paths, and why they are not the same path.</b> <c>BlogImageService</c> composes a
/// storage-relative path of <c>uploads/{category}/{file}</c> and hands it to
/// <see cref="FileSystemStorage"/>, so the storage root is the PARENT of the uploads folder, not the
/// uploads folder itself:</para>
/// <list type="table">
///   <listheader><term>Property</term><description>Meaning</description></listheader>
///   <item>
///     <term><see cref="StorageRootPath"/></term>
///     <description>Handed to <c>LocalFileStorage</c>. A relative path of
///     <c>uploads/blog/x.jpg</c> resolves beneath it.</description>
///   </item>
///   <item>
///     <term><see cref="UploadsRootPath"/></term>
///     <description>Always <c>StorageRootPath/uploads</c>. Published by the host at
///     <see cref="RequestPath"/>, so the stored public URL <c>/uploads/blog/x.jpg</c> resolves to
///     the file that was just written.</description>
///   </item>
/// </list>
/// <para>Deriving the second from the first — rather than letting a deployment set them
/// independently — is what makes "written here, served from there" unrepresentable.</para>
///
/// <para><b>How <c>Uploads:Path</c> is interpreted.</b> It names the directory served at
/// <c>/uploads</c>. When its last segment is already <c>uploads</c> — which is the deployment
/// contract, <c>/app/uploads</c> — it is used verbatim and the storage root is its parent. When it
/// is not (say <c>/srv/media</c>), the value is taken as the STORAGE root and uploads land in
/// <c>/srv/media/uploads</c>; the alternative would be to serve a directory the writer never writes
/// into. A relative value resolves against the content root, never the working directory.</para>
///
/// <para><b>The local fallback is the old behaviour, unchanged.</b> With <c>Uploads:Path</c> unset,
/// the storage root is the host web root and uploads land in <c>wwwroot/uploads</c> exactly as they
/// always have — so a clone of this repository still runs with nothing configured, and no developer
/// has to invent a path.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> only, so the host pipeline and
/// <see cref="FileStorageFactory"/> can both resolve it without a host reference.</para>
///
/// <para><b>Usage:</b> The host resolves this once at startup, registers it as a singleton, serves
/// <see cref="UploadsRootPath"/> at <see cref="RequestPath"/>, and the storage factory uses
/// <see cref="StorageRootPath"/> as the local provider's default root. A
/// <c>StorageSettings.LocalRootPath</c> saved on the Settings screen still overrides it — an
/// explicit administrator choice outranks a deployment default.</para>
/// </remarks>
public sealed class UploadsLocation
{
    /// <summary>Configuration path naming the uploads directory.</summary>
    public const string PathConfigurationKey = "Uploads:Path";

    /// <summary>
    /// Folder name that uploads live under, both on disk and in the URL. Must stay equal to
    /// <c>BlogImageService</c>'s upload root folder or stored URLs stop resolving.
    /// </summary>
    public const string FolderName = "uploads";

    /// <summary>URL prefix <see cref="UploadsRootPath"/> is published at.</summary>
    public const string RequestPath = "/" + FolderName;

    /// <summary>
    /// Creates a resolved location.
    /// </summary>
    /// <param name="storageRootPath">Absolute root the local storage provider writes beneath.</param>
    /// <param name="isConfigured">Whether the location came from configuration.</param>
    private UploadsLocation(string storageRootPath, bool isConfigured)
    {
        StorageRootPath = storageRootPath;
        UploadsRootPath = Path.Combine(storageRootPath, FolderName);
        IsConfigured = isConfigured;
    }

    /// <summary>Absolute root handed to the local file-storage provider.</summary>
    public string StorageRootPath { get; }

    /// <summary>Absolute directory uploaded media lives in and is served from.</summary>
    public string UploadsRootPath { get; }

    /// <summary>
    /// Whether <see cref="PathConfigurationKey"/> supplied the location, as opposed to the web-root
    /// fallback. Worth logging at startup: an operator who believes uploads are on a mounted volume
    /// and sees <c>false</c> has found their next redeploy's data loss before it happens.
    /// </summary>
    public bool IsConfigured { get; }

    /// <summary>
    /// Resolves the uploads location from configuration, falling back to the host web root.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A configured path is made absolute against the content root so a
    /// relative setting cannot follow the working directory around. An unconfigured deployment gets
    /// the web root, which is the zero-configuration behaviour the repository has always had.</para>
    /// <para><b>Flow:</b> read the setting → fall back to the web root → strip a trailing
    /// <c>uploads</c> segment so the storage root is its parent → build both paths.</para>
    /// <para><b>Side Effects:</b> None; pure. Creating the directory is the caller's job, because
    /// only the caller knows whether a failure to create it should stop the host.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read <see cref="PathConfigurationKey"/> from.</param>
    /// <param name="webRootPath">The host's web root; may be <c>null</c> or empty on a host that has
    /// none, such as a test host or a console head.</param>
    /// <param name="contentRootPath">The host's content root, used to anchor relative values and to
    /// locate <c>wwwroot</c> when <paramref name="webRootPath"/> is absent.</param>
    /// <returns>The resolved location.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="configuration"/> or <paramref name="contentRootPath"/> is <c>null</c>.
    /// </exception>
    public static UploadsLocation Resolve(
        IConfiguration configuration, string? webRootPath, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(contentRootPath);

        var configuredPath = configuration[PathConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            var webRoot = string.IsNullOrWhiteSpace(webRootPath)
                ? Path.Combine(contentRootPath, "wwwroot")
                : webRootPath;
            return new UploadsLocation(Path.GetFullPath(webRoot), isConfigured: false);
        }

        var absolutePath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(configuredPath.Trim(), contentRootPath));

        // "/app/uploads" names the served directory, so the storage root is its parent. Anything
        // else is taken as the storage root itself and uploads land in a child of it - see the
        // remarks on the type.
        var leaf = Path.GetFileName(absolutePath);
        var storageRoot = string.Equals(leaf, FolderName, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(absolutePath) ?? absolutePath
            : absolutePath;

        return new UploadsLocation(storageRoot, isConfigured: true);
    }
}
