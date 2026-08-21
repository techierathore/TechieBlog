using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Storage;

/// <summary>
/// Stores uploaded media on the host's local disk. The default provider (ADR-009).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Preserves the original "clone and run" behaviour — files land under the
/// host web root and are served by the static file handler with no extra configuration — while
/// now sitting behind <c>IFileStorage</c> so a deployment can move to another backend
/// (REQ-FN-042).</para>
///
/// <para><b>Code Flow:</b> The storage factory resolves the root from
/// <c>StorageSettings.LocalRootPath</c>, falling back to the host web root, and the inherited
/// filesystem implementation does the rest.</para>
///
/// <para><b>Dependencies:</b> <see cref="FileSystemStorage"/>.</para>
///
/// <para><b>Usage:</b> Leave <c>StorageSettings.LocalRootPath</c> empty unless uploads must live
/// outside the web root; anything written outside it needs its own static file mapping.</para>
/// </remarks>
public class LocalFileStorage : FileSystemStorage
{
    /// <summary>
    /// Creates local disk storage over a resolved root directory.
    /// </summary>
    /// <param name="rootPath">Absolute directory uploads are written beneath.</param>
    /// <param name="publicBaseUrl">URL prefix mapping to the root; empty serves from the site root.</param>
    /// <param name="logger">Structured logger for I/O failures.</param>
    public LocalFileStorage(string rootPath, string publicBaseUrl, ILogger<LocalFileStorage> logger)
        : base(rootPath, publicBaseUrl, logger)
    {
    }

    /// <inheritdoc />
    public override string ProviderName => StorageProviderNames.Local;
}
