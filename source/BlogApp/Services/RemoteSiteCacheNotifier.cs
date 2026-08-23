using Blazored.LocalStorage;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Common;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace BlogApp.Services;

/// <summary>
/// BlogApp's <see cref="ISiteCacheNotifier"/>: asks the website's own process to drop its cache
/// over HTTP (UAT-023 mechanism B).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BlogApp writes straight to the database and never runs a line of the
/// website's code, so the website's ten-minute content cache never sees the edit. The website
/// exposes an authenticated <c>POST /api/admin/cache/refresh</c> endpoint for exactly this — this
/// class is the caller.</para>
///
/// <para><b>Code Flow:</b> read <c>ConnectionContext.Settings.SiteBaseUrl</c> → read the operator's
/// OWN access token from local storage (<see cref="AppConstants.AccessKey"/> — the same token
/// <c>DesktopAuthStateProvider</c> already resolves against the site database for every render) →
/// <c>POST</c> it as a Bearer credential → the website re-validates it through the identical
/// <c>IAuthService.GetUserByAccessTokenAsync</c> lookup a website request would use, so a revoked or
/// expired BlogApp session is refused there exactly as it would be here.</para>
///
/// <para><b>Why no new secret.</b> The token presented is the one the signed-in operator already
/// holds because they logged into BlogApp through the shared <c>LoginPage</c> — there is nothing to
/// provision, rotate or leak beyond what BlogApp already needed to function.</para>
///
/// <para><b>Never claims success it cannot prove.</b> Every branch below — no site address
/// configured, no session token, request timeout, non-success HTTP status, network failure —
/// returns <see cref="CacheRefreshOutcome.Failed"/> or <see cref="CacheRefreshOutcome.NotApplicable"/>
/// with a caller-safe <see cref="CacheRefreshResult.Detail"/>. Only a genuine
/// <see cref="HttpResponseMessage.IsSuccessStatusCode"/> reports
/// <see cref="CacheRefreshOutcome.Succeeded"/>. An earlier round of BlogApp shipped a probe that
/// answered "OK" for something it had not actually verified; this class exists specifically not to
/// repeat that.</para>
///
/// <para><b>Dependencies:</b> <see cref="HttpClient"/>, <see cref="ILocalStorageService"/>,
/// <see cref="ConnectionContext"/>.</para>
///
/// <para><b>Usage:</b> Registered as a scoped <see cref="ISiteCacheNotifier"/> in
/// <c>MauiProgram.cs</c>, replacing the website's <see cref="NullSiteCacheNotifier"/> for this
/// head.</para>
/// </remarks>
public class RemoteSiteCacheNotifier : ISiteCacheNotifier
{
    /// <summary>Relative path of the website's cache-refresh endpoint.</summary>
    private const string RefreshPath = "/api/admin/cache/refresh";

    /// <summary>How long a refresh attempt is given before it is reported as failed.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient httpClient;
    private readonly ILocalStorageService localStorage;
    private readonly ConnectionContext connectionContext;
    private readonly ILogger<RemoteSiteCacheNotifier> logger;

    /// <summary>
    /// Creates the notifier.
    /// </summary>
    /// <param name="httpClient">Client used to call the website's endpoint.</param>
    /// <param name="localStorage">WebView local storage holding the operator's access token.</param>
    /// <param name="connectionContext">The connection BlogApp booted with, for <c>SiteBaseUrl</c>.</param>
    /// <param name="logger">Logger for the underlying failure detail.</param>
    /// <exception cref="ArgumentNullException">Any parameter is <c>null</c>.</exception>
    public RemoteSiteCacheNotifier(
        HttpClient httpClient,
        ILocalStorageService localStorage,
        ConnectionContext connectionContext,
        ILogger<RemoteSiteCacheNotifier> logger)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
        this.connectionContext = connectionContext ?? throw new ArgumentNullException(nameof(connectionContext));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Flow:</b> resolve the site address → resolve the session token → POST with a
    /// bounded timeout → classify the response → log and return the honest result at every exit.</para>
    /// <para><b>Side Effects:</b> One HTTP request to the configured website; one warning log entry
    /// on any non-success outcome (never <c>ex.Message</c> — see the Coding Standards' exception-text
    /// disclosure rule).</para>
    /// </remarks>
    public async Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshUrl = SiteUrlResolver.Combine(connectionContext.Settings?.SiteBaseUrl, RefreshPath);
        if (refreshUrl == null)
        {
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.NotApplicable,
                Detail = "No website address is configured for this connection, so the site was not asked to refresh its cache."
            };
        }

        string? accessToken;
        try
        {
            accessToken = await localStorage.GetItemAsync<string>(AppConstants.AccessKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the session token to refresh the site cache");
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.Failed,
                Detail = "Could not read the current session. The change is saved, but the site was not asked to refresh."
            };
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.Failed,
                Detail = "No active session was found. The change is saved, but the site was not asked to refresh."
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, refreshUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return new CacheRefreshResult { Outcome = CacheRefreshOutcome.Succeeded };
            }

            logger.LogWarning(
                "Site cache refresh returned {StatusCode} from {RefreshUrl}", response.StatusCode, refreshUrl);
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.Failed,
                Detail = $"The site refused the refresh request ({(int)response.StatusCode}). The change is saved, but the public page may still show the old version for a while."
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Site cache refresh to {RefreshUrl} timed out", refreshUrl);
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.Failed,
                Detail = "The site did not respond in time. The change is saved, but the public page may still show the old version for a while."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            logger.LogWarning(ex, "Site cache refresh to {RefreshUrl} failed", refreshUrl);
            return new CacheRefreshResult
            {
                Outcome = CacheRefreshOutcome.Failed,
                Detail = "Could not reach the site. The change is saved, but the public page may still show the old version for a while."
            };
        }
    }
}
