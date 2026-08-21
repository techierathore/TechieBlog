using Microsoft.AspNetCore.Components.Forms;

namespace TechieBlog.Tests.Media;

/// <summary>
/// An <see cref="IBrowserFile"/> backed by an in-memory byte array.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The upload path reads the browser stream exactly once and then hands the
/// same bytes to both the dimension reader and the storage provider (REQ-FN-026). Testing that
/// contract needs a file whose content the test controls and whose stream can be observed, which the
/// framework's own implementation — bound to a live SignalR circuit — cannot provide.</para>
///
/// <para><b>Code Flow:</b> The test supplies a name and a byte array; the service under test calls
/// <see cref="OpenReadStream"/> and receives a fresh reader over that array.</para>
///
/// <para><b>Dependencies:</b> None beyond the ASP.NET Core forms abstraction.</para>
///
/// <para><b>Usage:</b> <see cref="OpenReadStream"/> enforces the size ceiling exactly as the real
/// implementation does, so a test can assert that the category limit is passed down.</para>
/// </remarks>
/// <param name="name">The original file name reported to the service.</param>
/// <param name="content">The file's bytes.</param>
/// <param name="declaredSize">Size the file <i>claims</i> to be. Defaults to the real length; supply
/// a different value to model a client that lies about its upload, which is what
/// <c>BufferUploadAsync</c>'s bounded stream exists to stop (REQ-FN-025).</param>
public class StubBrowserFile(string name, byte[] content, long? declaredSize = null) : IBrowserFile
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public long Size { get; } = declaredSize ?? content.Length;

    /// <inheritdoc />
    public string ContentType { get; } = "application/octet-stream";

    /// <summary>
    /// Opens a reader over the stubbed bytes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the real implementation by refusing a file larger than
    /// the caller's ceiling, so a service that forgets to pass the category limit is caught here.</para>
    /// <para><b>Side Effects:</b> None; each call returns an independent stream.</para>
    /// </remarks>
    /// <param name="maxAllowedSize">Largest file the caller will accept.</param>
    /// <param name="cancellationToken">Unused; the content is already in memory.</param>
    /// <returns>A readable stream over the file's bytes.</returns>
    /// <exception cref="IOException">The content exceeds <paramref name="maxAllowedSize"/>.</exception>
    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        if (content.Length > maxAllowedSize)
        {
            throw new IOException("Supplied file exceeds the maximum allowed size.");
        }

        return new MemoryStream(content, writable: false);
    }
}
