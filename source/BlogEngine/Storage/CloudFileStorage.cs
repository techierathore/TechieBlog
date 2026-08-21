using System.Net;
using System.Net.Http.Headers;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Storage;

/// <summary>
/// Stores uploaded media in an HTTP object store such as S3, R2 or Azure Blob Storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Completes the local/network/cloud trio required by BRD-45/46 without
/// taking a vendor SDK dependency. Every mainstream object store exposes an HTTP interface where a
/// key maps to <c>PUT</c>/<c>GET</c>/<c>HEAD</c>/<c>DELETE</c> on
/// <c>{serviceUrl}/{container}/{key}</c>, which is exactly what this implementation speaks.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The relative path is normalised and appended to the service URL and container name.</item>
///   <item>The configured access key is presented as a bearer credential when one is set.</item>
///   <item>The response status is translated into the contract's return values.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="HttpClient"/> supplied by <c>IHttpClientFactory</c>,
/// <see cref="StorageSettings"/>.</para>
///
/// <para><b>Usage:</b> Point <c>CloudServiceUrl</c> at an endpoint that accepts the configured
/// credential directly — a presigned-URL gateway or a bucket fronted by an API key. Request
/// signing schemes that derive a per-request signature (AWS SigV4) need a vendor SDK and are out
/// of scope for the template's dependency-free default.</para>
/// </remarks>
public class CloudFileStorage : IFileStorage
{
    private readonly HttpClient httpClient;
    private readonly StorageSettings storageSettings;
    private readonly ILogger<CloudFileStorage> logger;

    /// <summary>
    /// Creates cloud storage over an HTTP object-store endpoint.
    /// </summary>
    /// <param name="httpClient">Client used for every request; supplied by the factory.</param>
    /// <param name="storageSettings">Endpoint, container, credential and public URL prefix.</param>
    /// <param name="logger">Structured logger for transport failures.</param>
    public CloudFileStorage(
        HttpClient httpClient,
        StorageSettings storageSettings,
        ILogger<CloudFileStorage> logger)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.storageSettings = storageSettings ?? throw new ArgumentNullException(nameof(storageSettings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => StorageProviderNames.Cloud;

    /// <inheritdoc />
    public async Task<FileStorageResult> SaveAsync(
        Stream content,
        string relativePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        using var payload = new StreamContent(content);
        payload.Headers.ContentType = ParseContentType(contentType);

        using var request = CreateRequest(HttpMethod.Put, normalized);
        request.Content = payload;

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        logger.LogInformation("Cloud storage wrote {RelativePath}", normalized);
        return BuildResult(normalized, payload.Headers.ContentLength ?? 0);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        using var request = CreateRequest(HttpMethod.Delete, normalized);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        logger.LogInformation("Cloud storage deleted {RelativePath}", normalized);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        using var request = CreateRequest(HttpMethod.Head, normalized);
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        using var request = CreateRequest(HttpMethod.Get, normalized);
        var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string GetPublicUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = FileSystemStorage.NormalizeRelativePath(relativePath);
        var prefix = string.IsNullOrWhiteSpace(storageSettings.PublicBaseUrl)
            ? BuildObjectUrl(normalized)
            : storageSettings.PublicBaseUrl.TrimEnd('/') + "/" + normalized;
        return prefix;
    }

    /// <summary>
    /// Builds a request against the configured object-store endpoint.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The access key is presented as a bearer credential only when
    /// one is configured, so public buckets work without a credential.</para>
    /// <para><b>Side Effects:</b> None; the caller owns and disposes the request.</para>
    /// </remarks>
    /// <param name="method">The HTTP verb for this operation.</param>
    /// <param name="normalizedPath">A path already passed through path normalisation.</param>
    /// <returns>The prepared request.</returns>
    private HttpRequestMessage CreateRequest(HttpMethod method, string normalizedPath)
    {
        var request = new HttpRequestMessage(method, BuildObjectUrl(normalizedPath));
        if (!string.IsNullOrWhiteSpace(storageSettings.CloudAccessKey))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", storageSettings.CloudAccessKey);
        }

        return request;
    }

    /// <summary>
    /// Composes the absolute object URL for a stored key.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The container segment is optional — some endpoints already
    /// encode the bucket in the service URL.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="normalizedPath">The object key.</param>
    /// <returns>The absolute request URL.</returns>
    private string BuildObjectUrl(string normalizedPath)
    {
        var serviceUrl = (storageSettings.CloudServiceUrl ?? string.Empty).TrimEnd('/');
        var container = (storageSettings.CloudContainerName ?? string.Empty).Trim('/');
        return string.IsNullOrEmpty(container)
            ? serviceUrl + "/" + normalizedPath
            : serviceUrl + "/" + container + "/" + normalizedPath;
    }

    /// <summary>
    /// Turns a MIME string into a header value, tolerating a missing or malformed type.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unusable content type must not fail the upload; the store
    /// falls back to the generic binary type.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="contentType">The caller-supplied MIME type.</param>
    /// <returns>A valid content-type header value.</returns>
    private static MediaTypeHeaderValue ParseContentType(string contentType)
    {
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsed))
        {
            return parsed;
        }

        return new MediaTypeHeaderValue("application/octet-stream");
    }

    /// <summary>
    /// Packages a completed upload into the shared result contract.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="normalizedPath">The key the object was written under.</param>
    /// <param name="sizeInBytes">Bytes sent, where the transport reported a length.</param>
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
