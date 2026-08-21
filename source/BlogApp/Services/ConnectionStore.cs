using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlogApp.Services;

/// <summary>
/// Stores BlogApp's PostgreSQL credentials in the operating system's credential store.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Satisfies the REQ-FN-047 requirement that the site connection string
/// survives restarts without ever being written in plain text. MAUI <c>SecureStorage</c> is the
/// primary backend — the Windows credential/DPAPI store on WinUI and the Keychain on Mac
/// Catalyst.</para>
/// <para><b>Code Flow:</b> settings object → JSON → <c>SecureStorage</c>. Unpackaged WinUI builds
/// (<c>WindowsPackageType=None</c>) cannot always reach <c>ApplicationData.Current</c>, so a
/// DPAPI-encrypted file under the user's local application data is used as a fallback. Both paths
/// are encrypted at rest and scoped to the current Windows user; neither is readable as text.</para>
/// <para><b>Dependencies:</b> <c>Microsoft.Maui.Storage.SecureStorage</c>,
/// <see cref="ProtectedData"/>, <see cref="ILogger{TCategoryName}"/>.</para>
/// <para><b>Usage:</b> Registered as a singleton and injected into the connection-setup screen and
/// <c>MauiProgram</c>.</para>
/// </remarks>
public class ConnectionStore : IConnectionStore
{
    /// <summary>Key the settings blob is filed under in secure storage.</summary>
    private const string StorageKey = "techieblog.blogapp.connection";

    /// <summary>Name of the DPAPI-protected fallback file.</summary>
    private const string FallbackFileName = "connection.dat";

    private readonly ILogger<ConnectionStore> logger;
    private readonly string fallbackFilePath;
    private string storageDescription;

    /// <summary>
    /// Creates the store and computes the fallback file location.
    /// </summary>
    /// <param name="logger">Structured logger for storage-backend selection and failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public ConnectionStore(ILogger<ConnectionStore> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        fallbackFilePath = Path.Combine(appDataRoot, "TechieBlog", "BlogApp", FallbackFileName);
        storageDescription = "Platform secure storage (OS credential store)";
    }

    /// <inheritdoc />
    public string StorageDescription => storageDescription;

    /// <inheritdoc />
    public async Task<ConnectionSettings> LoadAsync()
    {
        var json = await ReadRawAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ConnectionSettings>(json);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Stored connection settings could not be deserialised; treating as absent");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(ConnectionSettings settings)
    {
        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var json = JsonSerializer.Serialize(settings);

        try
        {
            await SecureStorage.Default.SetAsync(StorageKey, json).ConfigureAwait(false);
            storageDescription = "Platform secure storage (OS credential store)";
            logger.LogInformation("Connection settings saved to platform secure storage");
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Platform secure storage is unavailable; falling back to a DPAPI-protected file");
        }

        WriteFallback(json);
    }

    /// <inheritdoc />
    public async Task ClearAsync()
    {
        try
        {
            SecureStorage.Default.Remove(StorageKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove the secure-storage entry; continuing with the fallback file");
        }

        if (File.Exists(fallbackFilePath))
        {
            File.Delete(fallbackFilePath);
        }

        logger.LogInformation("Connection settings cleared; BlogApp will open at the connection-setup screen");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the stored JSON from whichever backend holds it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Secure storage wins when it is available; the DPAPI file is only
    /// consulted when secure storage is unavailable or empty, which keeps a machine that later gains
    /// packaged identity from silently ignoring an existing saved connection.</para>
    /// <para><b>Flow:</b> try secure storage → fall back to the protected file → return null.</para>
    /// <para><b>Side Effects:</b> Updates <see cref="StorageDescription"/> to name the backend used.</para>
    /// </remarks>
    /// <returns>The stored JSON, or <c>null</c> when neither backend holds a value.</returns>
    private async Task<string> ReadRawAsync()
    {
        try
        {
            var stored = await SecureStorage.Default.GetAsync(StorageKey).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                storageDescription = "Platform secure storage (OS credential store)";
                return stored;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Platform secure storage could not be read; trying the DPAPI-protected file");
        }

        return ReadFallback();
    }

    /// <summary>
    /// Writes the settings JSON to the DPAPI-protected fallback file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <see cref="DataProtectionScope.CurrentUser"/> ties the ciphertext
    /// to the signed-in Windows account, so copying the file to another machine or account yields
    /// nothing. On a platform without DPAPI the write is refused outright rather than degrading to
    /// plain text.</para>
    /// <para><b>Flow:</b> guard platform → create directory → encrypt → write bytes.</para>
    /// <para><b>Side Effects:</b> Creates <c>%LOCALAPPDATA%\TechieBlog\BlogApp\connection.dat</c>.</para>
    /// </remarks>
    /// <param name="json">The serialised settings.</param>
    /// <exception cref="PlatformNotSupportedException">
    /// Secure storage failed on a platform that has no DPAPI fallback.
    /// </exception>
    private void WriteFallback(string json)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secure storage is unavailable and no encrypted fallback exists on this platform. " +
                "The connection string will not be stored in plain text.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fallbackFilePath));
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        File.WriteAllBytes(fallbackFilePath, cipher);

        storageDescription = $"DPAPI-encrypted file (CurrentUser scope): {fallbackFilePath}";
        logger.LogInformation("Connection settings saved to the DPAPI-protected file at {FallbackPath}", fallbackFilePath);
    }

    /// <summary>
    /// Reads and decrypts the DPAPI-protected fallback file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing file simply means "not configured". A file that fails to
    /// decrypt — copied from another account, or corrupted — is treated the same way so the app can
    /// always recover by walking the user through setup again.</para>
    /// <para><b>Flow:</b> guard platform and existence → read → unprotect → decode.</para>
    /// <para><b>Side Effects:</b> Updates <see cref="StorageDescription"/> on success.</para>
    /// </remarks>
    /// <returns>The stored JSON, or <c>null</c> when the file is absent or unreadable.</returns>
    private string ReadFallback()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(fallbackFilePath))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(fallbackFilePath),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            storageDescription = $"DPAPI-encrypted file (CurrentUser scope): {fallbackFilePath}";
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(ex, "The protected connection file could not be decrypted; treating as absent");
            return null;
        }
    }
}
