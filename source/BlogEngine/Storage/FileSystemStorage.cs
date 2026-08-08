using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Storage;

/// <summary>
/// Shared implementation for every storage backend that is reachable as a filesystem path.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Local disk and a mounted network share differ only in where their root
/// lives and how their files are published, so both derive from this class and the actual I/O is
/// written once (REQ-FN-042).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Every operation normalises the caller's relative path and rejects traversal.</item>
///   <item>The normalised path is combined with the configured root and re-checked against it.</item>
///   <item>Reads and writes then use ordinary asynchronous <c>FileStream</c> operations.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IFileStorage"/>, <see cref="ILogger"/>.</para>
///
/// <para><b>PATH-TRAVERSAL DEFENCE — two independent checks, and why there are two.</b> The
/// relative path reaching these methods derives from a browser upload, so it is attacker-controlled
/// and a single mistake writes a file anywhere the host process can reach —
/// <c>../../etc/cron.d/x</c> or <c>../../wwwroot/evil.aspx</c>. The defence is therefore layered:</para>
/// <list type="number">
///   <item><see cref="NormalizeRelativePath"/> <b>rejects</b> rather than sanitises. A rooted path
///     or any <c>..</c> segment throws; it does not strip the segment and carry on. Stripping is
///     how traversal filters get defeated (<c>....//</c> collapses back to <c>../</c> once the
///     inner <c>../</c> is removed), and it also silently rewrites a caller's intent. Backslashes
///     are unified to forward slashes first, so a Windows-style traversal is caught by the same
///     test on every platform.</item>
///   <item><see cref="ResolveFullPath"/> <b>re-checks containment after resolution.</b> Even a path
///     that passed step 1 is combined with the root, fully resolved through
///     <see cref="Path.GetFullPath(string)"/> — which is what collapses symlinks, short names and
///     any residual relative segment — and then compared against the resolved root. Normalisation
///     is a check on the input; this is a check on the answer, and only the second one is proof.</item>
/// </list>
/// <para>Note for anyone editing step 2: the containment test is a string prefix comparison, which
/// on its own would also accept a sibling directory whose name merely starts with the root
/// (<c>/srv/uploads-old</c> against a root of <c>/srv/uploads</c>). It is not reachable today
/// because step 1 has already guaranteed the combined path descends into the root, so the two
/// checks are only safe <i>together</i> — <b>do not remove or relax either one</b>. A comparison
/// against the root with a trailing directory separator appended would make step 2 self-sufficient
/// and is the right hardening if step 1 ever changes.</para>
///
/// <para><b>What this layer does NOT validate.</b> Its contract is containment only: it guarantees
/// the bytes land inside the configured root, and nothing else. It does not check the file
/// extension, the declared content type, the actual file signature, or the size — a caller can
/// store an executable through it. That policy lives upstream in <c>BlogImageService</c>, which
/// pins the extension, derives the MIME type itself rather than trusting the browser, caps the
/// stream length per category, and composes the relative path from a server-generated name. Keep it
/// there: relaxing the caller is what would turn this into an arbitrary-file-write.</para>
///
/// <para><b>Usage:</b> Derive and supply a root path, a public URL prefix and a provider name;
/// do not use directly. Never pass a raw client-supplied filename through as the relative path —
/// pass a name the server composed.</para>
/// </remarks>
public abstract class FileSystemStorage : IFileStorage
{
    private const int CopyBufferSize = 81920;

    private readonly string rootPath;
    private readonly string publicBaseUrl;
    private readonly ILogger logger;

    /// <summary>
    /// Creates the storage over a filesystem root.
    /// </summary>
    /// <param name="rootPath">Absolute directory every relative path is resolved beneath.</param>
    /// <param name="publicBaseUrl">URL prefix mapping to <paramref name="rootPath"/>; may be empty
    /// when files are served from the site root.</param>
    /// <param name="logger">Structured logger for I/O failures.</param>
    protected FileSystemStorage(string rootPath, string publicBaseUrl, ILogger logger)
    {
        this.rootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        this.publicBaseUrl = publicBaseUrl ?? string.Empty;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public abstract string ProviderName { get; }

    /// <summary>
    /// The absolute directory every relative path is resolved beneath.
    /// </summary>
    protected string RootPath => rootPath;

    /// <inheritdoc />
    public async Task<FileStorageResult> SaveAsync(
        Stream content,
        string relativePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = NormalizeRelativePath(relativePath);
        var fullPath = ResolveFullPath(normalized);
        EnsureDirectoryExists(fullPath);

        // NOTE: the implicit DisposeAsync of this `await using` does not carry ConfigureAwait(false)
        // like every explicit await in this assembly does. Adding it changes the variable's type to
        // ConfiguredAsyncDisposable, which the target.FlushAsync / target.Length uses below then
        // cannot see, so it needs a small restructure rather than a one-token edit. Harmless here
        // (the host has no synchronisation context), tracked as a coding-standards tidy-up.
        await using var target = new FileStream(
            fullPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        await content.CopyToAsync(target, CopyBufferSize, cancellationToken).ConfigureAwait(false);
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("{Provider} storage wrote {RelativePath}", ProviderName, normalized);
        return BuildResult(normalized, target.Length);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(NormalizeRelativePath(relativePath));
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        logger.LogInformation("{Provider} storage deleted {RelativePath}", ProviderName, relativePath);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(NormalizeRelativePath(relativePath));
        return Task.FromResult(File.Exists(fullPath));
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var fullPath = ResolveFullPath(NormalizeRelativePath(relativePath));
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream source = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, useAsync: true);
        return Task.FromResult<Stream?>(source);
    }

    /// <inheritdoc />
    public string GetPublicUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = NormalizeRelativePath(relativePath);
        return string.IsNullOrWhiteSpace(publicBaseUrl)
            ? "/" + normalized
            : publicBaseUrl.TrimEnd('/') + "/" + normalized;
    }

    /// <summary>
    /// Reduces a caller-supplied path to a safe, forward-slashed, root-relative form.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uploaded file names reach this method from the browser, so a
    /// rooted path or any <c>..</c> segment is treated as an attack and rejected outright rather
    /// than sanitised.</para>
    /// <para><b>Flow:</b> Reject empty, unify separators, trim leading slashes, reject traversal.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="relativePath">The path to normalise.</param>
    /// <returns>The normalised, root-relative path.</returns>
    /// <exception cref="ArgumentException">The path is empty, rooted, or contains a traversal segment.</exception>
    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A relative storage path is required.", nameof(relativePath));
        }

        var unified = relativePath.Replace('\\', '/').TrimStart('/');
        if (unified.Length == 0 || Path.IsPathRooted(unified))
        {
            throw new ArgumentException("Storage paths must be relative.", nameof(relativePath));
        }

        if (unified.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException("Storage paths must not traverse outside the root.", nameof(relativePath));
        }

        return unified;
    }

    /// <summary>
    /// Converts a normalised relative path into an absolute path inside the storage root.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Normalisation alone is not proof of containment on every
    /// platform, so the resolved path is compared against the root a second time.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="normalizedPath">A path already passed through <see cref="NormalizeRelativePath"/>.</param>
    /// <returns>The absolute path to read or write.</returns>
    /// <exception cref="ArgumentException">The resolved path falls outside the storage root.</exception>
    protected string ResolveFullPath(string normalizedPath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(root, normalizedPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage paths must resolve inside the storage root.", nameof(normalizedPath));
        }

        return candidate;
    }

    /// <summary>
    /// Creates the parent directory of a file about to be written.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Upload categories are created on first use rather than being
    /// pre-provisioned, so a new category never fails its first upload.</para>
    /// <para><b>Side Effects:</b> May create directories on the backend.</para>
    /// </remarks>
    /// <param name="fullPath">Absolute path of the file being written.</param>
    private static void EnsureDirectoryExists(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Packages a completed write into the shared result contract.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="normalizedPath">The path the file was written under.</param>
    /// <param name="sizeInBytes">Bytes written.</param>
    /// <returns>The populated result.</returns>
    private FileStorageResult BuildResult(string normalizedPath, long sizeInBytes)
    {
        return new FileStorageResult
        {
            RelativePath = normalizedPath,
            PublicUrl = GetPublicUrl(normalizedPath),
            SizeInBytes = sizeInBytes,
            ProviderName = ProviderName
        };
    }
}
