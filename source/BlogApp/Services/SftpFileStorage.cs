using BlogEngine.Storage;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace BlogApp.Services;

/// <summary>
/// Writes uploaded media straight onto the site's server over SSH (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The desktop head has no filesystem route to the site's uploads directory.
/// The site runs on a Linux VPS that answers on 443 and 22 only, so <c>/srv/data/techieblog/uploads</c>
/// — the host directory the container serves at <c>/uploads</c> — cannot be mounted from Windows.
/// Port 22 can be reached, and is the same access the operator already uses to reach the site's
/// database, so that is the channel this provider uses.</para>
///
/// <para><b>What it deliberately does NOT do.</b> It does not change the shape of what is stored.
/// <see cref="GetPublicUrl"/> returns the same site-relative <c>/uploads/{category}/{file}</c> the
/// website writes, so a row created from the desktop is indistinguishable from one created in the
/// browser and the website needs to know nothing about SFTP. This type lives in BlogApp, not in
/// BlogEngine, for the same reason: the web head has a local volume and needs none of it.</para>
///
/// <para><b>Path safety is the engine's, not a second opinion.</b> Every relative path goes through
/// <see cref="FileSystemStorage.NormalizeRelativePath"/> — the same static the local, network and
/// cloud providers call — so a rooted path or a <c>..</c> segment is refused here exactly as it is
/// there. That matters more over SFTP than on local disk: the remote root is outside anything this
/// process owns, and a traversal would be writing into a live server.</para>
///
/// <para><b>Connections are per-operation.</b> An upload is a rare, human-paced action, and a
/// long-lived SSH session held open behind a desktop app is a session that silently dies with the
/// laptop's wifi and fails the NEXT upload with a confusing error. Connecting per call costs about
/// a second and makes each operation independently diagnosable.</para>
///
/// <para><b>Dependencies:</b> SSH.NET (<see cref="SftpClient"/>), <see cref="ConnectionSettings"/>.</para>
///
/// <para><b>Usage:</b> Built by <see cref="DesktopFileStorageFactory"/> when the stored settings
/// name the <see cref="MediaTransports.Sftp"/> transport.</para>
/// </remarks>
public class SftpFileStorage : IFileStorage
{
    /// <summary>Seconds to wait for the SSH handshake before giving up.</summary>
    /// <remarks>
    /// Bounded because this runs behind a button on a desktop app. SSH.NET's default is infinite,
    /// which on an unreachable host presents as an application that has simply stopped responding.
    /// </remarks>
    private const int ConnectionTimeoutSeconds = 20;

    private readonly ConnectionSettings settings;
    private readonly ILogger<SftpFileStorage> logger;

    /// <summary>
    /// Creates the provider over the stored SSH coordinates.
    /// </summary>
    /// <param name="settings">The settings carrying the SSH host, credentials and uploads path.</param>
    /// <param name="logger">Structured logger for transfer outcomes.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public SftpFileStorage(ConnectionSettings settings, ILogger<SftpFileStorage> logger)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reports as the network provider: to everything upstream this is media living somewhere other
    /// than the host's own web root, which is exactly what <c>Network</c> means. Inventing a name
    /// the shared <c>StorageProviderNames</c> does not define would leak the desktop head's private
    /// arrangement into rows the website reads.
    /// </remarks>
    public string ProviderName => StorageProviderNames.Network;

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Creates the remote directory chain before writing, because a
    /// category folder legitimately does not exist until its first upload and SFTP will not create
    /// one implicitly. The write is then a straight stream copy; SSH.NET replaces an existing file
    /// at the same path, matching the local provider's contract.</para>
    /// <para>The reported size is read back from the REMOTE file rather than taken from the source
    /// stream. A short write over a dropped link is the failure mode that matters here, and taking
    /// the source's length would report success for a file that arrived truncated.</para>
    /// <para><b>Flow:</b> normalise → connect → create directories → upload → stat → build the result.</para>
    /// <para><b>Side Effects:</b> Opens an SSH session and writes a file on the server.</para>
    /// </remarks>
    public async Task<FileStorageResult> SaveAsync(
        Stream content, string relativePath, string contentType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        var remotePath = ResolveRemotePath(normalized);

        return await Task.Run(
            () =>
            {
                using var client = Connect();

                CreateRemoteDirectories(client, RemoteDirectoryOf(remotePath));
                client.UploadFile(content, remotePath, canOverride: true);

                var written = client.GetAttributes(remotePath).Size;
                logger.LogInformation(
                    "Uploaded {RemotePath} to {Host} ({Bytes} bytes)", remotePath, settings.SftpHost, written);

                return new FileStorageResult
                {
                    RelativePath = normalized,
                    PublicUrl = GetPublicUrl(normalized),
                    SizeInBytes = written,
                    ProviderName = ProviderName
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> An absent file reports <c>false</c> rather than throwing, so a
    /// caller can clear an orphaned database row without exception handling — the same contract the
    /// filesystem providers offer.</para>
    /// <para><b>Side Effects:</b> Opens an SSH session and may delete a file on the server.</para>
    /// </remarks>
    public async Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var remotePath = ResolveRemotePath(FileSystemStorage.NormalizeRelativePath(relativePath));

        return await Task.Run(
            () =>
            {
                using var client = Connect();
                if (!client.Exists(remotePath))
                {
                    return false;
                }

                client.DeleteFile(remotePath);
                logger.LogInformation("Deleted {RemotePath} from {Host}", remotePath, settings.SftpHost);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Side Effects:</b> Opens an SSH session. Reads only.</para>
    /// </remarks>
    public async Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        var remotePath = ResolveRemotePath(FileSystemStorage.NormalizeRelativePath(relativePath));

        return await Task.Run(
            () =>
            {
                using var client = Connect();
                return client.Exists(remotePath);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The remote file is copied into memory and the SSH session is
    /// closed before the stream is handed back. Returning a live network stream would tie the
    /// caller's lifetime to a session it did not open and cannot see — and every caller of this
    /// method is reading an image small enough to have passed the category size limits.</para>
    /// <para><b>Side Effects:</b> Opens an SSH session; the caller owns the returned stream.</para>
    /// </remarks>
    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var remotePath = ResolveRemotePath(FileSystemStorage.NormalizeRelativePath(relativePath));

        return await Task.Run<Stream?>(
            () =>
            {
                using var client = Connect();
                if (!client.Exists(remotePath))
                {
                    return null;
                }

                var buffer = new MemoryStream();
                client.DownloadFile(remotePath, buffer);
                buffer.Position = 0;
                return buffer;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Site-relative, identical to what the website writes for its own
    /// uploads. The file has been placed in the directory the site serves at <c>/uploads</c>, so
    /// <c>/uploads/logos/x.png</c> is already the correct public address and nothing about the row
    /// records that it arrived over SSH. Displaying it INSIDE the desktop app is a separate
    /// concern, handled by the host page against <c>SiteBaseUrl</c>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public string GetPublicUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        return "/" + relativePath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Opens an authenticated SSH session using whichever credential was supplied.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A private key is preferred when one is configured, because it
    /// is the credential an operator can scope and revoke; a password is accepted as the fallback
    /// so the feature is usable on a server that has not had a key installed. Both are offered to
    /// SSH.NET together when both exist, letting the server choose.</para>
    /// <para><b>Flow:</b> build the authentication methods → construct the client → bound the
    /// timeout → connect.</para>
    /// <para><b>Side Effects:</b> Opens a network connection and reads the private key file.</para>
    /// </remarks>
    /// <returns>A connected client the caller must dispose.</returns>
    /// <exception cref="InvalidOperationException">No credential was configured.</exception>
    private SftpClient Connect()
    {
        var methods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(settings.SftpPrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(settings.SftpPrivateKeyPassphrase)
                ? new PrivateKeyFile(settings.SftpPrivateKeyPath)
                : new PrivateKeyFile(settings.SftpPrivateKeyPath, settings.SftpPrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(settings.SftpUsername, keyFile));
        }

        if (!string.IsNullOrWhiteSpace(settings.SftpPassword))
        {
            methods.Add(new PasswordAuthenticationMethod(settings.SftpUsername, settings.SftpPassword));
        }

        if (methods.Count == 0)
        {
            throw new InvalidOperationException(
                "No SSH credential is configured. Supply a password or a private key file.");
        }

        var connectionInfo = new ConnectionInfo(
            settings.SftpHost, settings.SftpPort, settings.SftpUsername, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(ConnectionTimeoutSeconds)
        };

        var client = new SftpClient(connectionInfo);
        client.Connect();
        return client;
    }

    /// <summary>
    /// Creates a remote directory and every missing ancestor.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> SFTP has no "create parents" flag, so the chain is walked from
    /// the root. A category directory legitimately does not exist until its first upload, so this
    /// is the normal path rather than an error recovery. An <see cref="SftpPermissionDeniedException"/>
    /// on an ancestor that already exists is ignored — the interesting failure is the leaf, and a
    /// server may well refuse to let this account stat <c>/srv</c> while happily letting it write
    /// inside the uploads directory.</para>
    /// <para><b>Flow:</b> split into segments → create each missing one in turn.</para>
    /// <para><b>Side Effects:</b> Creates directories on the server.</para>
    /// </remarks>
    /// <param name="client">A connected client.</param>
    /// <param name="remoteDirectory">Absolute remote directory to ensure exists.</param>
    private static void CreateRemoteDirectories(SftpClient client, string remoteDirectory)
    {
        if (string.IsNullOrWhiteSpace(remoteDirectory) || remoteDirectory == "/")
        {
            return;
        }

        var current = string.Empty;
        foreach (var segment in remoteDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;

            try
            {
                if (!client.Exists(current))
                {
                    client.CreateDirectory(current);
                }
            }
            catch (SftpPermissionDeniedException)
            {
                // An ancestor this account may not inspect is not a failure; the leaf write is the
                // operation that decides, and it reports its own error.
            }
        }
    }

    /// <summary>
    /// Joins the configured remote storage root to a normalised relative path.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The relative path already begins with <c>uploads/</c> — that is
    /// <c>BlogImageService</c>'s contract — so the root is the PARENT of the served directory, which
    /// is what <see cref="ConnectionSettings.ResolveSftpStorageRoot"/> computes. Joining them
    /// reproduces the operator's own uploads path exactly, which is what makes the stored
    /// <c>/uploads/…</c> URL resolve on the website.</para>
    /// <para><b>Flow:</b> resolve the root → guard → join with a single separator.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="normalizedRelativePath">A path already through the engine's normaliser.</param>
    /// <returns>The absolute remote path to write.</returns>
    /// <exception cref="InvalidOperationException">No remote uploads path is configured.</exception>
    private string ResolveRemotePath(string normalizedRelativePath)
    {
        var root = settings.ResolveSftpStorageRoot()
            ?? throw new InvalidOperationException("No server uploads directory is configured.");

        return root.TrimEnd('/') + "/" + normalizedRelativePath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// The directory portion of a remote path.
    /// </summary>
    /// <remarks>
    /// Written out rather than using <c>Path.GetDirectoryName</c>, which applies the LOCAL
    /// platform's separator rules and on Windows would hand back a backslashed path for a Linux
    /// server.
    /// </remarks>
    /// <param name="remotePath">An absolute remote file path.</param>
    /// <returns>The containing directory, or <c>"/"</c> at the root.</returns>
    private static string RemoteDirectoryOf(string remotePath)
    {
        var lastSlash = remotePath.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : remotePath[..lastSlash];
    }
}
