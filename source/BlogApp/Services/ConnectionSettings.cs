using BlogEngine.Storage;
using BlogModels;
using Npgsql;

namespace BlogApp.Services;

/// <summary>
/// The PostgreSQL coordinates BlogApp uses to reach the live TechieBlog site database.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the connection parameters captured on the first-run
/// connection-setup screen (REQ-FN-047 / BRD-96). BlogApp has no local database and no sync
/// layer: every read and write goes straight to the site's PostgreSQL server, so these values
/// are the app's single point of configuration.</para>
/// <para><b>Code Flow:</b> connection-setup screen binds to an instance → <see cref="ToConnectionString"/>
/// produces the value stored under the <c>AppDbConString</c> key → <c>ConnectionStore</c> encrypts
/// and persists the instance → <c>MauiProgram</c> reads it back at startup and hands the
/// connection string to <c>BlogSvcInitializer</c>.</para>
/// <para><b>Dependencies:</b> <see cref="NpgsqlConnectionStringBuilder"/> for well-formed,
/// correctly escaped connection strings.</para>
/// <para><b>Usage:</b> Never construct a connection string by concatenation — always go through
/// <see cref="ToConnectionString"/>.</para>
/// </remarks>
public class ConnectionSettings
{
    /// <summary>Default PostgreSQL port, pre-filled on the setup screen.</summary>
    public const int DefaultPort = 5432;

    /// <summary>Default SSH port, pre-filled on the setup screen.</summary>
    public const int DefaultSftpPort = 22;

    /// <summary>Default SSL mode, matching the mockup's "Require"-leaning guidance.</summary>
    public const string DefaultSslMode = "Prefer";

    /// <summary>SSL modes offered by the setup screen's picker.</summary>
    public static readonly string[] SslModes =
    {
        "Disable",
        "Allow",
        "Prefer",
        "Require",
        "VerifyCA",
        "VerifyFull"
    };

    /// <summary>Host name or IP address of the PostgreSQL server.</summary>
    public string Host { get; set; }

    /// <summary>TCP port the PostgreSQL server listens on.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Name of the TechieBlog database.</summary>
    public string Database { get; set; }

    /// <summary>Login role BlogApp connects as.</summary>
    public string Username { get; set; }

    /// <summary>Password for <see cref="Username"/>. Only ever persisted encrypted.</summary>
    public string Password { get; set; }

    /// <summary>Npgsql SSL mode name, one of <see cref="SslModes"/>.</summary>
    public string SslMode { get; set; } = DefaultSslMode;

    /// <summary>
    /// The site's JWT signing key. Only ever persisted encrypted.
    /// </summary>
    /// <remarks>
    /// Must be byte-for-byte the value the website runs with: <c>AppConstants.AccessKey</c> derives
    /// the browser storage slot from a fingerprint of this key, so a mismatch makes every session
    /// BlogApp issues unreadable by the site and vice versa.
    /// </remarks>
    public string JwtSigningKey { get; set; }

    /// <summary>
    /// The site's AES key. Only ever persisted encrypted.
    /// </summary>
    /// <remarks>
    /// Must match the website's <c>AppEncryptionKey</c> exactly. BlogApp reads the same
    /// <c>SiteSetting</c> rows the website encrypted at rest, and there is no key versioning — a
    /// different key does not fail loudly, it simply cannot decrypt those rows.
    /// </remarks>
    public string AppEncryptionKey { get; set; }

    /// <summary>
    /// How this head delivers uploaded media to the site (REQ-FN-062). One of
    /// <see cref="MediaTransports"/>.
    /// </summary>
    /// <remarks>
    /// <para>Images are not stored in the database — the website serves them from a directory on
    /// the server's disk — so a desktop head that knows only where the DATABASE lives writes every
    /// upload to its own machine and the picture never reaches the site. That is precisely what
    /// owner UAT hit on 2026-08-22.</para>
    /// <para><b>Why the transport is an explicit choice rather than inferred from a path.</b> The
    /// first attempt at this offered a folder box and assumed the operator could mount the server's
    /// uploads directory. They could not: the VPS is Linux and answers on 443 and 22 only, so no
    /// Windows path reaches it. Typing the server's Linux path into that box silently created a
    /// same-named folder on the local C: drive and five uploads went there. Naming the transport
    /// makes "which machine do these bytes end up on" a question the screen asks out loud.</para>
    /// </remarks>
    public string MediaTransport { get; set; } = MediaTransports.None;

    /// <summary>Host name or IP of the SSH server holding the site's uploads directory.</summary>
    public string SftpHost { get; set; }

    /// <summary>TCP port the SSH server listens on.</summary>
    public int SftpPort { get; set; } = DefaultSftpPort;

    /// <summary>Login the desktop head connects to the SSH server as.</summary>
    public string SftpUsername { get; set; }

    /// <summary>Password for <see cref="SftpUsername"/>. Only ever persisted encrypted.</summary>
    /// <remarks>
    /// Optional: leave it blank and supply <see cref="SftpPrivateKeyPath"/> instead. Key
    /// authentication is preferable and is tried first when both are present.
    /// </remarks>
    public string SftpPassword { get; set; }

    /// <summary>Path to an OpenSSH private key file on THIS machine.</summary>
    /// <remarks>
    /// The key file itself is never copied into the settings blob — only its path — so it keeps
    /// whatever filesystem permissions the operator gave it.
    /// </remarks>
    public string SftpPrivateKeyPath { get; set; }

    /// <summary>Passphrase protecting <see cref="SftpPrivateKeyPath"/>. Only ever persisted encrypted.</summary>
    public string SftpPrivateKeyPassphrase { get; set; }

    /// <summary>
    /// Absolute directory ON THE SERVER that the website serves at <c>/uploads</c>.
    /// </summary>
    /// <remarks>
    /// For the deployment this repository ships, that is <c>/srv/data/techieblog/uploads</c> — the
    /// host directory bind-mounted into the container at <c>/app/uploads</c>
    /// (<c>deploy/docker-compose.template.yml</c>). It is a REMOTE path and is never touched by the
    /// local filesystem APIs.
    /// </remarks>
    public string SftpUploadsPath { get; set; }

    /// <summary>
    /// Folder BlogApp writes uploaded media into when the transport is
    /// <see cref="MediaTransports.Folder"/>.
    /// </summary>
    /// <remarks>
    /// <para>Only meaningful when the server's uploads directory really is reachable as a path from
    /// this machine — a genuinely mounted share, not a local folder that happens to be named after
    /// the server's. <c>MediaLocationProbe</c> refuses a local fixed drive outright for that reason;
    /// see the remarks there, and <see cref="IsLocalFixedDrivePath"/>.</para>
    /// <para>Interpreted with the engine's own rule (see <see cref="ResolveMediaStorageRoot"/>), so
    /// the value the operator pastes is the folder they see, not a parent they have to work out.</para>
    /// </remarks>
    public string MediaRootPath { get; set; }

    /// <summary>
    /// The website's own address, used ONLY to display uploaded images inside BlogApp.
    /// </summary>
    /// <remarks>
    /// <para>Stored image paths stay site-relative (<c>/uploads/{category}/{file}</c>) whatever the
    /// transport, exactly as the website writes them — so nothing about the DATA changes here.</para>
    /// <para>The BlazorWebView, however, serves only the app's own packaged <c>wwwroot</c>, so a
    /// site-relative <c>/uploads/…</c> path resolves to nothing inside the desktop app and every
    /// logo renders broken. The host page rewrites those URLs against this value at display time.
    /// It is a rendering concern and touches neither the database nor the server.</para>
    /// </remarks>
    public string SiteBaseUrl { get; set; }

    /// <summary>
    /// Reports whether this head has been told how to deliver media to the site.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The question <c>DesktopFileStorageFactory</c> asks before
    /// overriding the engine's storage. <see cref="MediaTransports.None"/> means "leave the engine
    /// alone" — uploads stay on this machine, which is the honest default for an operator who only
    /// edits text from the desktop, and is what every installation had before REQ-FN-062.</para>
    /// <para><b>Flow:</b> transport check → per-transport completeness check.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when a usable media transport is configured.</returns>
    public bool HasMediaLocation()
    {
        if (IsSftpTransport())
        {
            return !string.IsNullOrWhiteSpace(SftpHost)
                && !string.IsNullOrWhiteSpace(SftpUsername)
                && !string.IsNullOrWhiteSpace(SftpUploadsPath)
                && SftpPort > 0
                && SftpPort <= 65535
                && (!string.IsNullOrWhiteSpace(SftpPassword) || !string.IsNullOrWhiteSpace(SftpPrivateKeyPath));
        }

        return IsFolderTransport() && !string.IsNullOrWhiteSpace(MediaRootPath);
    }

    /// <summary>Reports whether media is delivered over SFTP.</summary>
    /// <returns><c>true</c> for the SFTP transport.</returns>
    public bool IsSftpTransport()
    {
        return string.Equals(MediaTransport, MediaTransports.Sftp, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reports whether media is delivered to a reachable filesystem path.</summary>
    /// <returns><c>true</c> for the folder transport.</returns>
    public bool IsFolderTransport()
    {
        return string.Equals(MediaTransport, MediaTransports.Folder, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports whether a path points at a fixed drive attached to THIS machine.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the guard for the failure that started REQ-FN-062's
    /// second round. The operator was asked for "the folder your site serves /uploads from" and
    /// typed the server's Linux path with a drive letter in front —
    /// <c>C:\srv\data\techieblog\uploads</c>. Windows happily created it, the old probe found it
    /// writable, reported <b>"Media folder OK"</b>, and five uploads landed on the laptop while the
    /// operator believed they were on the server. A writability check cannot tell those two apart,
    /// so the check has to be about the DRIVE, not the permissions.</para>
    /// <para>A UNC path (<c>\\server\share</c>) and a mapped network drive both pass, because both
    /// genuinely lead off this machine. A fixed or removable local drive does not.</para>
    /// <para><b>Flow:</b> blank guard → UNC check → resolve the drive → classify it.</para>
    /// <para><b>Side Effects:</b> None. Queries drive metadata only; creates nothing.</para>
    /// </remarks>
    /// <param name="path">The path to classify.</param>
    /// <returns><c>true</c> when the path lives on a fixed or removable local drive.</returns>
    /// <summary>
    /// Reports whether this connection points at a site running on THIS machine.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The database host decides it. Working against a local database
    /// means the website is local too, and a media folder on this machine is then exactly right —
    /// the developer's own <c>wwwroot/uploads</c>. Working against a REMOTE database means the site
    /// is remote, and a local folder is the UAT-022 mistake (five uploads to a laptop while the
    /// site ran on a Linux VPS).</para>
    /// <para><b>Why the database host and not <see cref="SiteBaseUrl"/>:</b> the database is the one
    /// thing this head always has — it cannot run without it — whereas the site address is optional
    /// and is blank on a fresh install. Keying the rule on a field that may be empty would make the
    /// answer depend on unrelated configuration.</para>
    /// <para><b>Flow:</b> treat a blank, loopback or <c>localhost</c> host as local; anything else as
    /// remote.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when the database — and therefore the site — is on this machine.</returns>
    public bool IsLocalSite()
    {
        if (string.IsNullOrWhiteSpace(Host))
        {
            return true;
        }

        var host = Host.Trim();
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address);
    }

    public static bool IsLocalFixedDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmed = path.Trim();

        // A UNC path leads off this machine by definition.
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        string root;
        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(trimmed));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var drive = new DriveInfo(root);
            return drive.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // An unknown drive is not evidence of a local one; let the probe's write decide.
            return false;
        }
    }

    /// <summary>
    /// Turns <see cref="MediaRootPath"/> into the root the file-storage provider writes beneath.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>BlogImageService</c> hands the storage provider a relative
    /// path of <c>uploads/{category}/{file}</c>, so the provider's root is the PARENT of the
    /// uploads folder rather than the uploads folder itself. Asking the operator to enter that
    /// parent would be asking them to know an implementation detail, so a value whose last segment
    /// is already <c>uploads</c> is treated as the served directory and its parent is returned —
    /// exactly the rule <see cref="UploadsLocation"/> applies to the web head's
    /// <c>Uploads:Path</c>. The folder name comes from
    /// <see cref="UploadsLocation.FolderName"/> rather than a local literal, so the two readings
    /// cannot drift apart.</para>
    /// <para><b>Flow:</b> blank guard → normalise → compare the last segment → return it or its
    /// parent.</para>
    /// <para><b>Side Effects:</b> None; creating the directory is the caller's job.</para>
    /// </remarks>
    /// <returns>The absolute storage root, or <c>null</c> when no media folder is configured.</returns>
    public string? ResolveMediaStorageRoot()
    {
        if (string.IsNullOrWhiteSpace(MediaRootPath))
        {
            return null;
        }

        var absolutePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(MediaRootPath.Trim()));
        var leaf = Path.GetFileName(absolutePath);

        return string.Equals(leaf, UploadsLocation.FolderName, StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(absolutePath) ?? absolutePath
            : absolutePath;
    }

    /// <summary>
    /// Resolves the local directory uploads are written into under the folder transport.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Always <see cref="ResolveMediaStorageRoot"/> plus the uploads
    /// folder, derived rather than configured separately, so "written here, served from there" is
    /// unrepresentable — the same reasoning <see cref="UploadsLocation"/> records for the web head.</para>
    /// <para><b>Flow:</b> resolve the storage root → append the uploads folder.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The absolute uploads directory, or <c>null</c> when none is configured.</returns>
    public string? ResolveMediaUploadsPath()
    {
        var storageRoot = ResolveMediaStorageRoot();
        return storageRoot == null ? null : Path.Combine(storageRoot, UploadsLocation.FolderName);
    }

    /// <summary>
    /// Resolves the REMOTE directory that is the parent of the server's uploads folder.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same parent/child rule as the folder transport, applied with
    /// POSIX semantics because the target is a Linux server and <c>Path</c> would apply Windows
    /// ones. <c>/srv/data/techieblog/uploads</c> therefore yields <c>/srv/data/techieblog</c>, and a
    /// relative <c>uploads/logos/x.png</c> resolves back onto the served directory.</para>
    /// <para><b>Flow:</b> blank guard → trim trailing slashes → drop a trailing uploads segment.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The remote storage root, or <c>null</c> when no SFTP uploads path is set.</returns>
    public string? ResolveSftpStorageRoot()
    {
        if (string.IsNullOrWhiteSpace(SftpUploadsPath))
        {
            return null;
        }

        var remotePath = SftpUploadsPath.Trim().Replace('\\', '/').TrimEnd('/');
        if (remotePath.Length == 0)
        {
            return "/";
        }

        var lastSlash = remotePath.LastIndexOf('/');
        var leaf = lastSlash >= 0 ? remotePath[(lastSlash + 1)..] : remotePath;

        if (!string.Equals(leaf, UploadsLocation.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return remotePath;
        }

        var parent = lastSlash > 0 ? remotePath[..lastSlash] : "/";
        return parent.Length == 0 ? "/" : parent;
    }

    /// <summary>
    /// Builds the Npgsql connection string these settings describe.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates escaping and keyword naming to
    /// <see cref="NpgsqlConnectionStringBuilder"/> so a password containing <c>;</c> or <c>=</c>
    /// cannot corrupt the string. An unrecognised <see cref="SslMode"/> falls back to
    /// <see cref="DefaultSslMode"/> rather than throwing, because the value can only come from the
    /// setup screen's fixed picker.</para>
    /// <para><b>Flow:</b> populate builder → parse SSL mode → return the built string.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A connection string suitable for the <c>AppDbConString</c> configuration key.</returns>
    public string ToConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password
        };

        if (Enum.TryParse<SslMode>(SslMode, ignoreCase: true, out var parsedSslMode))
        {
            builder.SslMode = parsedSslMode;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Reports whether every field the server needs has been supplied.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Host, database and username are mandatory and the port must be
    /// a legal TCP port. The password is deliberately optional so trust-authenticated and
    /// certificate-authenticated servers stay reachable. Both secrets are mandatory and are held to
    /// the same minimum lengths the host enforces in <see cref="AppSecrets"/>, because a settings
    /// blob that passes here is handed straight to <c>AppSecrets.Initialise</c> at startup — failing
    /// on the setup screen, where the value can be corrected, beats failing during composition.
    /// A blob saved before the secrets existed therefore reads as incomplete and reopens setup,
    /// which is the intended upgrade path.</para>
    /// <para><b>Flow:</b> field checks combined with logical AND.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when the settings can be turned into a usable connection string.</returns>
    public bool IsComplete()
    {
        return !string.IsNullOrWhiteSpace(Host)
            && !string.IsNullOrWhiteSpace(Database)
            && !string.IsNullOrWhiteSpace(Username)
            && Port > 0
            && Port <= 65535
            && HasUsableSecrets();
    }

    /// <summary>
    /// Reports whether both site secrets are present and long enough to be accepted by the host.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the presence and minimum-length rules of
    /// <c>AppSecrets.Validate</c>. It deliberately does NOT re-implement the retired-literal
    /// blocklist — that check is owned by <see cref="AppSecrets"/> and stays in one place; a
    /// retired literal entered here passes this gate and is then rejected at startup with the
    /// host's own message.</para>
    /// <para><b>Flow:</b> blank checks → trimmed length checks.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when both secrets are supplied and meet their minimum lengths.</returns>
    public bool HasUsableSecrets()
    {
        return !string.IsNullOrWhiteSpace(JwtSigningKey)
            && JwtSigningKey.Trim().Length >= AppSecrets.MinimumJwtSigningKeyLength
            && !string.IsNullOrWhiteSpace(AppEncryptionKey)
            && AppEncryptionKey.Trim().Length >= AppSecrets.MinimumEncryptionKeyLength;
    }

    /// <summary>
    /// Produces a short, password-free label describing where BlogApp is pointed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used by the login screen's "Connected to" chip and by the admin
    /// topbar. Deliberately omits the password and the username so a screenshot of the running app
    /// never leaks a credential.</para>
    /// <para><b>Flow:</b> concatenate host, port and database.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A display string such as <c>localhost:5432/TechieBlog</c>.</returns>
    public string ToDisplayLabel()
    {
        return $"{Host}:{Port}/{Database}";
    }
}
