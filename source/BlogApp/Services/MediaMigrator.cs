using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogApp.Services;

/// <summary>
/// Sends images already sitting on this machine up to the server (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Every upload made before the SFTP transport existed was written to the
/// operator's own disk while its database row recorded <c>/uploads/{category}/{file}</c> on the
/// server — so the rows are correct and only the FILES are in the wrong place. Copying those files
/// into the server's uploads directory therefore repairs the existing rows outright: no re-upload,
/// no new database writes, nothing to clean up afterwards.</para>
///
/// <para><b>Why this is in the app and not a shell command.</b> The first advice given for this was
/// an <c>scp</c> line with a placeholder username, which the operator quite reasonably ran verbatim
/// and got a password prompt for an account that does not exist. The app already holds working SSH
/// credentials and already knows the server's uploads directory; asking someone to restate both in
/// a terminal, correctly, is asking them to repeat work the app can do — and to get a shell quoting
/// detail right on the first try. A button that reuses the connection they just proved with
/// <c>Test</c> has neither problem.</para>
///
/// <para><b>Code Flow:</b> connection screen → <see cref="MigrateAsync"/> → walk the local folder →
/// hand each file to <see cref="SftpFileStorage"/> → report counts.</para>
///
/// <para><b>Dependencies:</b> <see cref="SftpFileStorage"/>, <see cref="ConnectionSettings"/>.</para>
///
/// <para><b>Usage:</b> Registered transient. Safe to re-run: it overwrites by path, so a second
/// pass over the same folder is a no-op in effect rather than a duplication.</para>
/// </remarks>
public class MediaMigrator
{
    /// <summary>
    /// Folder name the walked directory represents, and the prefix every uploaded path carries.
    /// </summary>
    /// <remarks>
    /// The operator points at their <c>uploads</c> folder, so a file at <c>logos/x.jpeg</c> inside
    /// it becomes the storage-relative <c>uploads/logos/x.jpeg</c> that
    /// <c>BlogImageService</c> would itself have produced — which is what makes the existing
    /// database rows resolve.
    /// </remarks>
    private const string UploadsPrefix = "uploads/";

    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<MediaMigrator> logger;

    /// <summary>
    /// Creates the migrator.
    /// </summary>
    /// <param name="loggerFactory">Creates the logger the storage provider needs.</param>
    /// <param name="logger">Structured logger for migration outcomes.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public MediaMigrator(ILoggerFactory loggerFactory, ILogger<MediaMigrator> logger)
    {
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Copies every file under a local uploads folder to the same place on the server.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The folder handed in is treated AS the uploads directory, so the
    /// relative layout beneath it is reproduced exactly — <c>logos/x.jpeg</c> lands at
    /// <c>{server uploads}/logos/x.jpeg</c> and the database row that already points at
    /// <c>/uploads/logos/x.jpeg</c> starts resolving. Nothing is written to the database, because
    /// nothing there is wrong.</para>
    /// <para>One failed file does not abandon the rest: each is counted and the run continues, since
    /// a single unreadable or oversized file is not a reason to leave the other twenty stranded. The
    /// summary reports both counts, and every failure is logged with its path.</para>
    /// <para><b>Flow:</b> guard the transport → guard the folder → enumerate → upload each →
    /// summarise.</para>
    /// <para><b>Side Effects:</b> Opens one SSH session per file and writes files on the server.</para>
    /// </remarks>
    /// <param name="settings">Settings carrying the SSH coordinates; the SFTP transport must be selected.</param>
    /// <param name="localUploadsFolder">A local folder that plays the role of <c>uploads</c>.</param>
    /// <returns>
    /// A success result summarising what was sent, or a failure result whose message is safe to
    /// render directly on screen.
    /// </returns>
    public async Task<Result<string>> MigrateAsync(ConnectionSettings settings, string localUploadsFolder)
    {
        if (settings?.IsSftpTransport() != true || !settings.HasMediaLocation())
        {
            return Result<string>.Failure(
                "Choose \"Send to the server over SSH\" and fill in the server details first - this " +
                "sends the files over that same connection.");
        }

        if (string.IsNullOrWhiteSpace(localUploadsFolder) || !Directory.Exists(localUploadsFolder))
        {
            return Result<string>.Failure("Pick a folder on this machine that holds the images to send.");
        }

        var files = Directory.GetFiles(localUploadsFolder, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return Result<string>.Failure($"There are no files under {localUploadsFolder}.");
        }

        var storage = new SftpFileStorage(settings, loggerFactory.CreateLogger<SftpFileStorage>());
        var sent = 0;
        var failed = 0;

        foreach (var file in files)
        {
            var relativePath = UploadsPrefix
                + Path.GetRelativePath(localUploadsFolder, file).Replace('\\', '/');

            try
            {
                await using var source = File.OpenRead(file);
                await storage.SaveAsync(source, relativePath, string.Empty, CancellationToken.None)
                    .ConfigureAwait(false);
                sent++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "Could not send {LocalFile} to the server as {RelativePath}", file, relativePath);
            }
        }

        logger.LogInformation(
            "Media migration finished: {Sent} sent, {Failed} failed, from {LocalFolder} to {Host}",
            sent, failed, localUploadsFolder, settings.SftpHost);

        if (sent == 0)
        {
            return Result<string>.Failure(
                $"None of the {files.Length} files could be sent. The reasons are recorded in the " +
                "application log.");
        }

        var summary = $"Sent {sent} file(s) to {settings.SftpHost}:{settings.SftpUploadsPath.TrimEnd('/')}. " +
            "Images already recorded in the database will now resolve - nothing needs re-uploading.";

        return failed == 0
            ? Result<string>.Success(summary)
            : Result<string>.Success(summary + $" {failed} file(s) failed; see the application log.");
    }

    /// <summary>
    /// The desktop head's own uploads folder — where every pre-SFTP upload was written.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Offered as the default so the common case needs no browsing at
    /// all. It is derived the same way <c>MauiProgram</c> derives the app-data root, so it cannot
    /// drift from the folder <c>DesktopHostEnvironment</c> actually hands the engine.</para>
    /// <para><b>Side Effects:</b> None; the folder may not exist.</para>
    /// </remarks>
    /// <returns>The absolute path of the desktop head's local uploads folder.</returns>
    public static string DefaultLocalUploadsFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TechieBlog", "BlogApp", "wwwroot", "uploads");
    }
}
