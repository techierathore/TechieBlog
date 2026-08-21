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
