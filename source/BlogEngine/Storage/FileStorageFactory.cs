using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Storage;

/// <summary>
/// Resolves the <see cref="IFileStorage"/> implementation selected in site settings.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provider selection is a runtime setting, not a start-up choice, so the
/// implementation is built per operation from the current <see cref="StorageSettings"/>. Switching
/// backends on the Settings screen therefore affects the very next upload with no restart
/// (REQ-FN-042).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read the current storage settings through <see cref="ISiteSettingsService"/>.</item>
///   <item>Match <c>ProviderName</c> against <see cref="StorageProviderNames"/>.</item>
///   <item>Construct the matching implementation, defaulting to local storage.</item>
/// </list>
///
/// <para><b>The three providers, their names and their path semantics.</b> The stored
/// <c>ProviderName</c> is matched case-insensitively against the constants in
/// <c>StorageProviderNames</c>:</para>
/// <list type="table">
///   <listheader>
///     <term>Provider</term>
///     <description>Root, and what a relative path means under it</description>
///   </listheader>
///   <item>
///     <term><c>Local</c> (the default, ADR-009)</term>
///     <description>Root is <c>StorageLocalRootPath</c>, or the host's web root when that setting
///     is empty — the zero-configuration "clone and run" case, where files land under
///     <c>wwwroot</c> and the static file handler serves them. A relative path is a filesystem
///     path beneath that root. Public URL is <c>StoragePublicBaseUrl</c> + the path, or
///     <c>/</c> + the path when no prefix is set.</description>
///   </item>
///   <item>
///     <term><c>Network</c></term>
///     <description>Root is <c>StorageNetworkRootPath</c>, a UNC path or a mount point. Identical
///     path handling to local — it is the same <see cref="FileSystemStorage"/> code with a
///     different root. Because a share is normally outside the web root,
///     <c>StoragePublicBaseUrl</c> is effectively required or rendered URLs will not resolve.
///     On Linux the share must already be mounted; .NET does not authenticate to UNC paths.</description>
///   </item>
///   <item>
///     <term><c>Cloud</c></term>
///     <description>No filesystem root. The relative path is an <i>object key</i> appended to
///     <c>{CloudServiceUrl}/{CloudContainerName}/</c>, and the container segment is optional
///     because some endpoints already encode the bucket in the service URL. Public URL is
///     <c>StoragePublicBaseUrl</c> + the key, falling back to the object URL itself.</description>
///   </item>
/// </list>
///
/// <para><b>Every provider shares one path-safety contract.</b> All three run the caller's relative
/// path through <see cref="FileSystemStorage.NormalizeRelativePath"/> — the cloud provider calls the
/// static method directly rather than reimplementing it — so a rooted path or a <c>..</c> segment
/// is refused whichever backend is selected. That matters for the cloud case too: without it,
/// <c>../</c> in an object key is a request path that can escape the container prefix. Extension,
/// size and content-type policy is <i>not</i> applied at this layer; see
/// <see cref="FileSystemStorage"/>.</para>
///
/// <para><b>Misconfiguration degrades to local rather than failing.</b> An unknown provider name,
/// a network provider with no share, or a cloud provider with no service URL all fall back to local
/// disk with a logged warning. The reasoning is that a bad setting should not stop the site
/// accepting uploads — but the consequence is worth knowing: <b>a deployment that believes it is
/// writing to cloud storage may silently be writing to the container filesystem</b>, where a
/// redeploy loses the files. The warning in the log is the only signal, so it is worth alerting
/// on.</para>
///
/// <para><b>Dependencies:</b> <see cref="ISiteSettingsService"/>, <see cref="IWebHostEnvironment"/>,
/// <see cref="IHttpClientFactory"/>, <see cref="ILoggerFactory"/>.</para>
///
/// <para><b>Usage:</b> Registered as a singleton; the returned storage objects are cheap, stateless
/// and safe to discard after each operation. The factory is a singleton that resolves settings per
/// call rather than caching them, which is what makes a provider switch take effect immediately.</para>
/// </remarks>
public class FileStorageFactory : IFileStorageFactory
{
    private const string CloudHttpClientName = "TechieBlogCloudStorage";

    private readonly ISiteSettingsService siteSettingsService;
    private readonly IWebHostEnvironment environment;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILoggerFactory loggerFactory;

    /// <summary>
    /// Creates the factory over the services needed to build every provider.
    /// </summary>
    /// <param name="siteSettingsService">Source of the current storage configuration.</param>
    /// <param name="environment">Host environment supplying the default web root.</param>
    /// <param name="httpClientFactory">Client source for the cloud provider.</param>
    /// <param name="loggerFactory">Creates the typed logger each provider needs.</param>
    public FileStorageFactory(
        ISiteSettingsService siteSettingsService,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory)
    {
        this.siteSettingsService = siteSettingsService ?? throw new ArgumentNullException(nameof(siteSettingsService));
        this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
        this.httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public async Task<IFileStorage> GetStorageAsync()
    {
        var settings = await siteSettingsService.GetStorageSettingsAsync().ConfigureAwait(false);
        return Build(settings, settings.ProviderName);
    }

    /// <inheritdoc />
    public async Task<IFileStorage> GetStorageByNameAsync(string providerName)
    {
        var settings = await siteSettingsService.GetStorageSettingsAsync().ConfigureAwait(false);
        return Build(settings, providerName);
    }

    /// <summary>
    /// Builds the named provider from a settings snapshot.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown, empty or misconfigured provider name degrades to
    /// local storage so a bad setting cannot stop uploads working.</para>
    /// <para><b>Flow:</b> Match the name, then construct with the roots resolved from settings.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settings">The current storage configuration.</param>
    /// <param name="providerName">The provider to build.</param>
    /// <returns>The constructed storage. Never null.</returns>
    private IFileStorage Build(StorageSettings settings, string providerName)
    {
        if (IsProvider(providerName, StorageProviderNames.Cloud))
        {
            return BuildCloud(settings);
        }

        if (IsProvider(providerName, StorageProviderNames.Network))
        {
            return BuildNetwork(settings);
        }

        return BuildLocal(settings);
    }

    /// <summary>
    /// Builds local disk storage rooted at the configured path or the host web root.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty <c>LocalRootPath</c> means "use the web root", which
    /// is the zero-configuration behaviour the template shipped with (ADR-009).</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settings">The current storage configuration.</param>
    /// <returns>Local disk storage.</returns>
    private IFileStorage BuildLocal(StorageSettings settings)
    {
        var root = string.IsNullOrWhiteSpace(settings.LocalRootPath)
            ? ResolveWebRoot()
            : settings.LocalRootPath;
        return new LocalFileStorage(root, settings.PublicBaseUrl, loggerFactory.CreateLogger<LocalFileStorage>());
    }

    /// <summary>
    /// Builds network share storage, falling back to local storage when no share is configured.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Selecting the network provider without a root path is a
    /// misconfiguration; failing over to local disk keeps uploads working and logs the problem.</para>
    /// <para><b>Side Effects:</b> Writes a warning when falling back.</para>
    /// </remarks>
    /// <param name="settings">The current storage configuration.</param>
    /// <returns>Network storage, or local storage when the share is unset.</returns>
    private IFileStorage BuildNetwork(StorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.NetworkRootPath))
        {
            loggerFactory.CreateLogger<FileStorageFactory>()
                .LogWarning("Network storage selected without a root path; falling back to local storage");
            return BuildLocal(settings);
        }

        return new NetworkFileStorage(
            settings.NetworkRootPath, settings.PublicBaseUrl, loggerFactory.CreateLogger<NetworkFileStorage>());
    }

    /// <summary>
    /// Builds cloud storage, falling back to local storage when no endpoint is configured.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Without a service URL there is nowhere to send the object, so
    /// the same degrade-to-local rule applies as for an unset network share.</para>
    /// <para><b>Side Effects:</b> Writes a warning when falling back.</para>
    /// </remarks>
    /// <param name="settings">The current storage configuration.</param>
    /// <returns>Cloud storage, or local storage when the endpoint is unset.</returns>
    private IFileStorage BuildCloud(StorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.CloudServiceUrl))
        {
            loggerFactory.CreateLogger<FileStorageFactory>()
                .LogWarning("Cloud storage selected without a service URL; falling back to local storage");
            return BuildLocal(settings);
        }

        return new CloudFileStorage(
            httpClientFactory.CreateClient(CloudHttpClientName),
            settings,
            loggerFactory.CreateLogger<CloudFileStorage>());
    }

    /// <summary>
    /// Resolves the host's web root, tolerating a host that has not set one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A console or test host leaves <c>WebRootPath</c> null, so the
    /// conventional <c>wwwroot</c> under the content root is used instead.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>An absolute directory path.</returns>
    private string ResolveWebRoot()
    {
        return string.IsNullOrWhiteSpace(environment.WebRootPath)
            ? Path.Combine(environment.ContentRootPath ?? AppContext.BaseDirectory, "wwwroot")
            : environment.WebRootPath;
    }

    /// <summary>
    /// Compares a stored provider name against a canonical one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Casing of persisted values is not guaranteed, so the comparison
    /// ignores case.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="candidate">The persisted provider name.</param>
    /// <param name="expected">The canonical name to test against.</param>
    /// <returns>True when they name the same provider.</returns>
    private static bool IsProvider(string candidate, string expected)
    {
        return string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase);
    }
}
