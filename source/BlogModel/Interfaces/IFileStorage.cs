using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Backend-agnostic contract for storing uploaded media (BRD-45/46, FR19).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Removes the direct-to-disk assumption from the image service. The same
/// upload path works against local disk, a network share or an object store, chosen at runtime
/// from site settings, so a container redeploy or a move to shared storage needs no code change.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>BlogImageService</c> validates the upload against its category rules.</item>
///   <item>It asks <see cref="IFileStorageFactory"/> for the configured implementation.</item>
///   <item>It calls <see cref="SaveAsync"/> and records the returned public URL.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="FileStorageResult"/>.</para>
///
/// <para><b>Usage:</b> Paths are always backend-relative and forward-slashed — never absolute and
/// never containing <c>..</c>. Implementations must reject traversal attempts.</para>
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Name of this provider, from <see cref="StorageProviderNames"/>.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Writes a stream to the backend, replacing any existing file at the same path.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller has already validated size and format; the storage
    /// layer only enforces path safety and durability.</para>
    /// <para><b>Flow:</b> Validate the relative path, ensure the container exists, copy the stream,
    /// return the resolved public URL.</para>
    /// <para><b>Side Effects:</b> Creates directories or object keys as needed.</para>
    /// </remarks>
    /// <param name="content">The source stream, positioned at the first byte to write.</param>
    /// <param name="relativePath">Backend-relative, forward-slashed destination path.</param>
    /// <param name="contentType">MIME type recorded with the file where the backend supports it.</param>
    /// <param name="cancellationToken">Token used to abort a long copy.</param>
    /// <returns>The stored file's relative path, public URL and byte count.</returns>
    /// <exception cref="ArgumentException">The relative path is empty or escapes the storage root.</exception>
    Task<FileStorageResult> SaveAsync(
        Stream content,
        string relativePath,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a file from the backend.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an absent file is not an error — it reports false so
    /// callers can clean up orphaned database rows without exception handling.</para>
    /// <para><b>Side Effects:</b> Removes the stored object.</para>
    /// </remarks>
    /// <param name="relativePath">Backend-relative path of the file to remove.</param>
    /// <param name="cancellationToken">Token used to abort the call.</param>
    /// <returns>True when a file was removed, false when nothing was there.</returns>
    Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether a file is present in the backend.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used to detect media lost to a redeploy without a mounted volume.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="relativePath">Backend-relative path to test.</param>
    /// <param name="cancellationToken">Token used to abort the call.</param>
    /// <returns>True when the file exists.</returns>
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a stored file for reading.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Required by backends whose files are not reachable by a static
    /// file handler, such as a private network share fronted by the application.</para>
    /// <para><b>Side Effects:</b> The caller owns and must dispose the returned stream.</para>
    /// </remarks>
    /// <param name="relativePath">Backend-relative path to open.</param>
    /// <param name="cancellationToken">Token used to abort the call.</param>
    /// <returns>A readable stream, or null when the file does not exist.</returns>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the browser-facing URL for a stored file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Local storage serves from the site root; network and cloud
    /// storage prefix the configured public base URL.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="relativePath">Backend-relative path of the file.</param>
    /// <returns>An absolute or site-relative URL, or an empty string for an empty input.</returns>
    string GetPublicUrl(string relativePath);
}
