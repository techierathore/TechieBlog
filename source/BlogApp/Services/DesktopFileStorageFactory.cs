using BlogEngine.Storage;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogApp.Services;

/// <summary>
/// Points the desktop head's uploads at the server's media folder instead of the local machine
/// (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BlogApp registers the engine's DI graph unchanged (REQ-FN-046), which
/// includes <see cref="FileStorageFactory"/>. That factory resolves its local root from
/// <see cref="Microsoft.AspNetCore.Hosting.IWebHostEnvironment"/>, and a MAUI head has no web host,
/// so <see cref="DesktopHostEnvironment"/> stands in with a folder under the operator's own
/// <c>%LOCALAPPDATA%</c>. Every image uploaded from the desktop therefore landed on the desktop —
/// while the database row it wrote pointed at <c>/uploads/…</c> on the web server, so the picture
/// existed nowhere the site could serve it. This decorator is the missing half of "no local
/// database, no sync": the desktop head now has a media connection as well as a database one.</para>
///
/// <para><b>Why a decorator, and not a change to the engine.</b> The alternative — pointing the
/// shared <c>Storage.LocalRootPath</c> site setting at a Windows path — is not available: those
/// rows are read by the WEBSITE too, out of the same database this head is connected to, so it
/// would move the server's own uploads to a path that does not exist there. Overriding here
/// affects one process on one machine and leaves <c>BlogEngine</c>, <c>BlogUI</c> and the web host
/// untouched, which is what makes the website's storage behaviour provably unchanged.</para>
///
/// <para><b>What it does and does not override.</b> The <see cref="MediaTransports.Sftp"/>
/// transport replaces whatever the site selected, because it names a destination the engine has no
/// provider for and the site&#39;s own <c>Storage.*</c> rows describe a path belonging to the SERVER —
/// the one thing this head cannot reach. The <see cref="MediaTransports.Folder"/> transport
/// redirects only the <i>filesystem</i> providers; cloud storage is passed straight through, because
/// a cloud endpoint is reachable from the desktop exactly as it is from the server and there is
/// nothing to correct. With <see cref="MediaTransports.None"/> the engine factory answers every
/// call, so a head that has not opted in behaves precisely as it did before this type existed.</para>
///
/// <para><b>Why an SFTP provider at all (2026-08-22).</b> The first version of this offered only the
/// folder redirect and assumed the server&#39;s uploads directory could be mounted. For this
/// deployment it cannot: the site runs on a Linux VPS answering on 443 and 22 only. The folder box
/// then did real harm — a server path typed with a drive letter created a local directory and five
/// uploads went to the operator&#39;s laptop while the probe reported success. SFTP is the transport
/// that actually exists between these two machines.</para>
///
/// <para><b>Code Flow:</b> <c>BlogImageService</c> → <see cref="IFileStorageFactory"/> (this) →
/// <see cref="SftpFileStorage"/>, a <see cref="NetworkFileStorage"/> rooted at the configured media
/// folder, or the engine factory's own answer.</para>
///
/// <para><b>Dependencies:</b> <see cref="FileStorageFactory"/>, <see cref="ConnectionContext"/>,
/// <see cref="ILoggerFactory"/>.</para>
///
/// <para><b>Usage:</b> Registered as the singleton <see cref="IFileStorageFactory"/> AFTER
/// <c>BlogSvcInitializer.Initialize</c>, so it replaces the engine's registration rather than
/// competing with it.</para>
/// </remarks>
public class DesktopFileStorageFactory : IFileStorageFactory
{
    private readonly IFileStorageFactory engineFactory;
    private readonly ConnectionContext connectionContext;
    private readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Creates the decorator over the engine's own factory.
    /// </summary>
    /// <param name="engineFactory">The engine factory this type falls back to.</param>
    /// <param name="connectionContext">The settings the process booted with.</param>
    /// <param name="loggerFactory">Creates the typed logger the storage provider needs.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public DesktopFileStorageFactory(
        IFileStorageFactory engineFactory,
        ConnectionContext connectionContext,
        ILoggerFactory loggerFactory)
    {
        this.engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        this.connectionContext = connectionContext ?? throw new ArgumentNullException(nameof(connectionContext));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> A configured media folder wins over the site's local/network
    /// provider setting, because on this head that setting names a path that belongs to the server.
    /// The provider NAME still decides whether an override applies at all, so an administrator who
    /// has switched the site to cloud storage keeps cloud storage in the desktop head too.</para>
    /// <para><b>Flow:</b> ask the engine → return it unless it is a filesystem provider we can
    /// improve on.</para>
    /// <para><b>Side Effects:</b> May trigger the settings cache to fill.</para>
    /// </remarks>
    public async Task<IFileStorage> GetStorageAsync()
    {
        var engineStorage = await engineFactory.GetStorageAsync().ConfigureAwait(false);
        return Redirect(engineStorage);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Same rule as <see cref="GetStorageAsync"/>. The named-provider
    /// overload exists for media migration, where both backends are addressed at once, so it must
    /// apply the same redirection or a migration run from the desktop would read from the server
    /// and write to the local machine.</para>
    /// <para><b>Side Effects:</b> None beyond the engine call.</para>
    /// </remarks>
    public async Task<IFileStorage> GetStorageByNameAsync(string providerName)
    {
        var engineStorage = await engineFactory.GetStorageByNameAsync(providerName).ConfigureAwait(false);
        return Redirect(engineStorage);
    }

    /// <summary>
    /// Replaces a filesystem provider with one rooted at the configured media folder.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Whichever transport is chosen, the public URL recorded against
    /// the image stays the site-relative <c>/uploads/{category}/{file}</c> the website writes — the
    /// desktop head&#39;s private arrangement never reaches the data. For the folder transport
    /// <see cref="NetworkFileStorage"/> is the implementation used whatever the site selected: it is
    /// the same <see cref="FileSystemStorage"/> code with a different root, and it is the honest
    /// description of what this head is doing — writing somewhere that is not its own web root.</para>
    /// <para><b>Flow:</b> transport check → SFTP short-circuit → provider-kind check → construct the
    /// redirected provider, or hand back what the engine chose.</para>
    /// <para><b>Side Effects:</b> None; the provider creates directories lazily on first write.</para>
    /// </remarks>
    /// <param name="engineStorage">The provider the engine factory selected.</param>
    /// <returns>The redirected provider, or <paramref name="engineStorage"/> unchanged.</returns>
    private IFileStorage Redirect(IFileStorage engineStorage)
    {
        var settings = connectionContext.Settings;
        if (settings?.HasMediaLocation() != true)
        {
            return engineStorage;
        }

        // SFTP is a destination the engine has no provider for, so it is not a "filesystem provider
        // we can improve on" - it replaces whatever was selected. The site's own Storage.* rows name
        // a path that belongs to the SERVER, which is exactly what this head cannot reach.
        if (settings.IsSftpTransport())
        {
            return new SftpFileStorage(settings, loggerFactory.CreateLogger<SftpFileStorage>());
        }

        if (!IsFileSystemProvider(engineStorage?.ProviderName))
        {
            return engineStorage;
        }

        return new NetworkFileStorage(
            settings.ResolveMediaStorageRoot()!,
            string.Empty,
            loggerFactory.CreateLogger<NetworkFileStorage>());
    }

    /// <summary>
    /// Reports whether a provider name describes storage backed by a filesystem path.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Local and network are the two providers whose root is a
    /// directory on this machine's view of the world, and therefore the two the desktop head can
    /// meaningfully redirect. Anything else — cloud today, anything added later — is left to the
    /// engine, which is the conservative default when a new provider appears.</para>
    /// <para><b>Flow:</b> two case-insensitive comparisons.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="providerName">The provider name reported by the engine's chosen storage; null
    /// for a provider that does not report one, which is treated as "not ours to redirect".</param>
    /// <returns><c>true</c> for the local and network providers.</returns>
    private static bool IsFileSystemProvider(string? providerName)
    {
        return string.Equals(providerName, StorageProviderNames.Local, StringComparison.OrdinalIgnoreCase)
            || string.Equals(providerName, StorageProviderNames.Network, StringComparison.OrdinalIgnoreCase);
    }
}
