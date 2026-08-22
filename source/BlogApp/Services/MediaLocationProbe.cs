using BlogModels;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace BlogApp.Services;

/// <summary>
/// Proves that media uploaded from this head will actually reach the SITE (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A wrong media setting does not raise an error at upload time.
/// <c>BlogImageService</c> writes into whatever destination it is handed, so a misconfigured head
/// reports a successful upload and only the website is missing the picture. This probe exists to
/// turn that silence into an answer while the operator is still on the screen that can fix it.</para>
///
/// <para><b>The lesson this class was rewritten for, 2026-08-22.</b> Its first version asked one
/// question — "can I write here?" — and answered it correctly. That was not enough, and the gap was
/// not theoretical: asked for "the folder your site serves /uploads from", the operator entered the
/// SERVER's Linux path with a drive letter in front, <c>C:\srv\data\techieblog\uploads</c>. Windows
/// created it, the probe found it writable, reported <b>"Media folder OK"</b>, and five uploads
/// landed on the laptop while the operator believed they were on the server. <b>Writability is not
/// reachability.</b> A probe that cannot distinguish "the server" from "a folder on your own C:
/// drive named after the server" is worse than no probe, because it converts a mistake into
/// confidence. Every check below is therefore about WHERE the bytes end up, and a local fixed drive
/// is now a hard failure rather than a pass.</para>
///
/// <para><b>Code Flow:</b> setup screen → <see cref="TestAsync"/> → dispatch on the configured
/// transport → prove a round trip → report the destination in the message, so the operator can read
/// back where their images are going.</para>
///
/// <para><b>Dependencies:</b> SSH.NET, <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered transient. It writes, so unlike <see cref="ConnectionProbe"/> it
/// is not read-only — but everything it writes it removes, and only inside the destination the
/// operator nominated.</para>
/// </remarks>
public class MediaLocationProbe
{
    /// <summary>
    /// Prefix of the temporary file the probe writes, chosen to be recognisable if one is ever left
    /// behind by a process that died mid-probe.
    /// </summary>
    private const string ProbeFilePrefix = "blogapp-write-probe-";

    /// <summary>Seconds to wait for the SSH handshake during a probe.</summary>
    private const int SshTimeoutSeconds = 20;

    /// <summary>
    /// Message returned when the nominated folder is on a drive attached to this machine.
    /// </summary>
    /// <remarks>
    /// The one message in this class that exists because of a specific incident rather than a
    /// general failure mode. It names the mistake outright, because "not writable" or "not found"
    /// would both have been false — the folder was there and it worked perfectly, on the wrong
    /// computer.
    /// </remarks>
    private const string LocalDriveMessage =
        "That folder is on a drive attached to THIS computer, so images written there would never " +
        "reach the website - which is exactly the problem this setting exists to fix. A path only " +
        "works here if it genuinely leads to the server: a mapped network drive, or a UNC path like " +
        "\\\\server\\share. If your site runs on a Linux server you almost certainly cannot mount it " +
        "at all - choose \"Send to the server over SSH\" instead.";

    /// <summary>
    /// Message returned when the folder exists but cannot be written to (REQ-NFR-033).
    /// </summary>
    private const string NotWritableMessage =
        "The folder was reached but could not be written to. Check that this Windows account has " +
        "write permission on it - if it is a network share, check the credentials the drive is " +
        "mapped with. The underlying error is recorded in the application log.";

    /// <summary>
    /// Message returned when the path cannot be reached at all. See <see cref="NotWritableMessage"/>.
    /// </summary>
    private const string UnreachableMessage =
        "The folder could not be reached. Check that the drive is mapped or the share is mounted, " +
        "and that the path is spelt correctly. The underlying error is recorded in the application log.";

    /// <summary>Message returned when the path is not a legal filesystem path.</summary>
    private const string InvalidPathMessage =
        "That is not a valid folder path. Enter a full path such as \\\\server\\techieblog\\uploads, " +
        "or a mapped drive such as Z:\\techieblog\\uploads.";

    /// <summary>Message returned when the SSH credentials were refused (REQ-NFR-033).</summary>
    private const string SshAuthMessage =
        "The server refused those SSH credentials. Check the username, and the password or private " +
        "key file. The underlying error is recorded in the application log.";

    /// <summary>Message returned when the SSH host could not be reached.</summary>
    private const string SshUnreachableMessage =
        "Could not reach that server over SSH. Check the host name and port, and that this machine " +
        "is allowed to connect. The underlying error is recorded in the application log.";

    private readonly ILogger<MediaLocationProbe> logger;

    /// <summary>
    /// Creates the probe.
    /// </summary>
    /// <param name="logger">Structured logger for probe outcomes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public MediaLocationProbe(ILogger<MediaLocationProbe> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Proves a round trip to the configured media destination.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Dispatches on the transport, because the two destinations fail
    /// in completely different ways and a shared message would describe neither. Both paths write a
    /// file, <b>read it back and compare</b>, then delete it: a destination that accepts a create
    /// and silently discards the bytes is a real failure mode, and a write-only check would score
    /// it as a pass.</para>
    /// <para>The success message always names the resolved destination in full, so the operator can
    /// read back where their images will land rather than trusting that the screen understood them.</para>
    /// <para><b>Flow:</b> transport dispatch → per-transport round trip → report.</para>
    /// <para><b>Side Effects:</b> May create the destination directory; creates and deletes one
    /// small file inside it.</para>
    /// </remarks>
    /// <param name="settings">The settings carrying the media transport to test.</param>
    /// <returns>
    /// A success result naming the destination that was proved, or a failure result whose message
    /// is safe to render directly on screen.
    /// </returns>
    public async Task<Result<string>> TestAsync(ConnectionSettings settings)
    {
        if (settings == null)
        {
            return Result<string>.Failure("Enter the media settings before testing them.");
        }

        if (settings.IsSftpTransport())
        {
            return await TestSftpAsync(settings).ConfigureAwait(false);
        }

        if (settings.IsFolderTransport())
        {
            return await TestFolderAsync(settings).ConfigureAwait(false);
        }

        return Result<string>.Success(
            "Uploads will stay on this machine. Nothing to test - and nothing you upload from " +
            "BlogApp will appear on the website until you choose a transport.");
    }

    /// <summary>
    /// Proves a write round trip to the server's uploads directory over SSH.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Connects with the real credentials, creates the uploads
    /// directory if it is absent (a fresh server legitimately has nothing there), writes a probe
    /// file, reads it back, compares, and deletes it. Proving the round trip against the ACTUAL
    /// remote directory is what makes this answer mean something the folder probe never could: the
    /// bytes demonstrably crossed the network.</para>
    /// <para><b>Flow:</b> completeness guard → connect → ensure the directory → write → read back →
    /// compare → delete → report.</para>
    /// <para><b>Side Effects:</b> Opens an SSH session; may create the remote uploads directory;
    /// creates and deletes one file on the server.</para>
    /// </remarks>
    /// <param name="settings">The settings carrying the SSH coordinates.</param>
    /// <returns>The probe result.</returns>
    private async Task<Result<string>> TestSftpAsync(ConnectionSettings settings)
    {
        if (!settings.HasMediaLocation())
        {
            return Result<string>.Failure(
                "Enter the server host, the username, a password or private key, and the uploads " +
                "directory on the server before testing.");
        }

        var remoteDirectory = settings.SftpUploadsPath.Trim().TrimEnd('/');
        var probeFile = remoteDirectory + "/" + ProbeFilePrefix + Guid.NewGuid().ToString("N") + ".tmp";
        var probeContent = Guid.NewGuid().ToString("N");

        return await Task.Run(
            () =>
            {
                try
                {
                    using var client = ConnectSftp(settings);

                    if (!client.Exists(remoteDirectory))
                    {
                        client.CreateDirectory(remoteDirectory);
                    }

                    client.WriteAllText(probeFile, probeContent);

                    var readBack = client.ReadAllText(probeFile);
                    client.DeleteFile(probeFile);

                    if (!string.Equals(readBack, probeContent, StringComparison.Ordinal))
                    {
                        logger.LogWarning(
                            "Media probe wrote to {RemoteDirectory} on {Host} but read back different content",
                            remoteDirectory, settings.SftpHost);
                        return Result<string>.Failure(
                            "The server accepted a file but did not return its contents. It is not a " +
                            "reliable place to store media.");
                    }

                    logger.LogInformation(
                        "Media probe succeeded over SFTP against {Host}:{Port}{RemoteDirectory}",
                        settings.SftpHost, settings.SftpPort, remoteDirectory);

                    return Result<string>.Success(
                        $"Server OK - images will be written to {settings.SftpHost}:{remoteDirectory}, " +
                        "and a file written there was read back and removed.");
                }
                catch (SshAuthenticationException ex)
                {
                    logger.LogWarning(ex, "Media probe was refused by {Host}", settings.SftpHost);
                    return Result<string>.Failure(SshAuthMessage);
                }
                catch (SftpPermissionDeniedException ex)
                {
                    logger.LogWarning(
                        ex, "Media probe denied write access to {RemoteDirectory} on {Host}",
                        remoteDirectory, settings.SftpHost);
                    return Result<string>.Failure(
                        $"Connected to the server, but this account may not write to {remoteDirectory}. " +
                        "Grant it write access there, or point at a directory it owns.");
                }
                catch (Exception ex) when (ex is SshConnectionException or SshOperationTimeoutException
                                              or System.Net.Sockets.SocketException)
                {
                    logger.LogWarning(ex, "Media probe could not reach {Host}:{Port}", settings.SftpHost, settings.SftpPort);
                    return Result<string>.Failure(SshUnreachableMessage);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                              or InvalidOperationException or ArgumentException)
                {
                    logger.LogWarning(ex, "Media probe failed against {Host}", settings.SftpHost);
                    return Result<string>.Failure(
                        "The SSH connection could not be completed. If you supplied a private key, " +
                        "check the file path and passphrase. The underlying error is recorded in the " +
                        "application log.");
                }
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Proves a write round trip to a filesystem path — and refuses one on this machine.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The local-drive check runs FIRST and is fatal, before anything
    /// is created. Ordering matters: the old probe's very first act was to create the directory, so
    /// by the time it reported "OK" it had already brought the wrong folder into existence. Refusing
    /// up front means a mistyped server path leaves no trace on the operator's disk.</para>
    /// <para><b>Flow:</b> completeness guard → local-drive refusal → resolve → create → write →
    /// read back → compare → delete → report.</para>
    /// <para><b>Side Effects:</b> May create the uploads directory; creates and deletes one file.</para>
    /// </remarks>
    /// <param name="settings">The settings carrying the media folder.</param>
    /// <returns>The probe result.</returns>
    private async Task<Result<string>> TestFolderAsync(ConnectionSettings settings)
    {
        if (!settings.HasMediaLocation())
        {
            return Result<string>.Failure("Enter the folder your site serves /uploads from before testing it.");
        }

        if (ConnectionSettings.IsLocalFixedDrivePath(settings.MediaRootPath))
        {
            logger.LogWarning(
                "Media probe REFUSED {MediaRootPath}: it resolves to a drive on this machine",
                settings.MediaRootPath);
            return Result<string>.Failure(LocalDriveMessage);
        }

        string uploadsPath;
        try
        {
            uploadsPath = settings.ResolveMediaUploadsPath()!;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            logger.LogWarning(ex, "Media probe rejected the supplied path");
            return Result<string>.Failure(InvalidPathMessage);
        }

        var probeFile = Path.Combine(uploadsPath, ProbeFilePrefix + Guid.NewGuid().ToString("N") + ".tmp");
        var probeContent = Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(uploadsPath);

            await File.WriteAllTextAsync(probeFile, probeContent).ConfigureAwait(false);
            var readBack = await File.ReadAllTextAsync(probeFile).ConfigureAwait(false);

            if (!string.Equals(readBack, probeContent, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Media probe wrote to {UploadsPath} but read back different content", uploadsPath);
                return Result<string>.Failure(
                    "The folder accepted a file but did not return its contents. It is not a reliable " +
                    "place to store media.");
            }

            logger.LogInformation("Media probe succeeded against {UploadsPath}", uploadsPath);
            return Result<string>.Success(
                $"Media folder OK - images will be written to {uploadsPath}, which is not on this machine.");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Media probe was denied access to {UploadsPath}", uploadsPath);
            return Result<string>.Failure(NotWritableMessage);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Media probe could not reach {UploadsPath}", uploadsPath);
            return Result<string>.Failure(UnreachableMessage);
        }
        finally
        {
            TryDelete(probeFile);
        }
    }

    /// <summary>
    /// Opens an authenticated SSH session for the probe.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors <see cref="SftpFileStorage"/>'s own connection rules —
    /// key preferred, password accepted — so a probe that passes is evidence about the credential
    /// an upload will actually use, not about a different one.</para>
    /// <para><b>Side Effects:</b> Opens a network connection and reads the private key file.</para>
    /// </remarks>
    /// <param name="settings">The settings carrying the SSH coordinates.</param>
    /// <returns>A connected client the caller must dispose.</returns>
    private static SftpClient ConnectSftp(ConnectionSettings settings)
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

        var connectionInfo = new ConnectionInfo(
            settings.SftpHost, settings.SftpPort, settings.SftpUsername, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(SshTimeoutSeconds)
        };

        var client = new SftpClient(connectionInfo);
        client.Connect();
        return client;
    }

    /// <summary>
    /// Removes the local probe file, ignoring a failure to do so.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Cleanup must never turn a successful probe into a reported
    /// failure — the question has already been answered by then, and a leftover file named by
    /// <see cref="ProbeFilePrefix"/> is recognisable and harmless.</para>
    /// <para><b>Side Effects:</b> Deletes one file.</para>
    /// </remarks>
    /// <param name="path">The probe file to remove.</param>
    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Media probe could not remove its temporary file {ProbeFile}", path);
        }
    }
}
