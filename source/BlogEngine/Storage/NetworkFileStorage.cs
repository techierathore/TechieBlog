using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Storage;

/// <summary>
/// Stores uploaded media on a UNC share or mounted network path.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets several application instances — or a web head and the planned
/// desktop head — share one media library, and keeps uploads outside the container image so a
/// redeploy cannot lose them (BRD-45/46, §9.7 risk).</para>
///
/// <para><b>Code Flow:</b> Identical to local storage apart from the root, which comes from
/// <c>StorageSettings.NetworkRootPath</c>. Because the share is normally not under the web root,
/// <c>StorageSettings.PublicBaseUrl</c> is usually required so rendered URLs resolve.</para>
///
/// <para><b>Dependencies:</b> <see cref="FileSystemStorage"/>; the host process account must hold
/// write permission on the share.</para>
///
/// <para><b>Usage:</b> On Linux, mount the share first and configure the mount point — the .NET
/// filesystem APIs do not authenticate to UNC paths themselves.</para>
/// </remarks>
public class NetworkFileStorage : FileSystemStorage
{
    /// <summary>
    /// Creates network share storage over a resolved root directory.
    /// </summary>
    /// <param name="rootPath">UNC or mounted directory uploads are written beneath.</param>
    /// <param name="publicBaseUrl">URL prefix mapping to the share.</param>
    /// <param name="logger">Structured logger for I/O failures.</param>
    public NetworkFileStorage(string rootPath, string publicBaseUrl, ILogger<NetworkFileStorage> logger)
        : base(rootPath, publicBaseUrl, logger)
    {
    }

    /// <inheritdoc />
    public override string ProviderName => StorageProviderNames.Network;
}
