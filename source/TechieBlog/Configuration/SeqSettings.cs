using Microsoft.Extensions.Configuration;

namespace TechieBlog.Configuration;

/// <summary>
/// Decides whether log events are shipped to a Seq server, and with what credential
/// (REQ-NFR-013).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The VPS runs one shared Seq container that every deployed application logs
/// into, reachable from a sibling container at <c>http://seq:5341</c>. A developer's machine has no
/// Seq at all, and must not need one — an unreachable sink would either stall startup or fill the
/// console with connection errors on every clone-and-run. So the sink is attached only when
/// <see cref="UrlKey"/> is actually set, and this type is the single place that decision is
/// made.</para>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> resolves this from the bootstrap configuration while
/// building the logger, and calls <c>WriteTo.Seq</c> only when <see cref="IsEnabled"/> is
/// <c>true</c>.</para>
///
/// <para><b>The enrichment is not optional.</b> One Seq instance receives events from every
/// application on the host, so an event with no application name is an event nobody can filter.
/// <see cref="ApplicationPropertyName"/> / <see cref="ApplicationName"/> are attached to the logger
/// itself rather than to the Seq sink, so the same property appears in the console and the file —
/// which is what makes a local reproduction comparable with a production trace.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> only.</para>
///
/// <para><b>Security:</b> <see cref="ApiKey"/> is a live ingestion credential. It arrives from a
/// deployment secret, never from a committed file, and must not be logged — including by the
/// startup line that announces the sink, which names the URL only.</para>
///
/// <para><b>Usage:</b> Container: <c>SeqUrl=http://seq:5341</c> and <c>SeqApiKey=…</c>. Local
/// development: set neither.</para>
/// </remarks>
public sealed class SeqSettings
{
    /// <summary>Configuration path of the Seq ingestion URL.</summary>
    public const string UrlKey = "Seq:Url";

    /// <summary>Configuration path of the Seq API key.</summary>
    public const string ApiKeyKey = "Seq:ApiKey";

    /// <summary>Name of the property identifying this application on a shared Seq server.</summary>
    public const string ApplicationPropertyName = "App";

    /// <summary>
    /// Value of the application property — the deployment's <c>APP_NAME</c>, not the C# project
    /// name, because that is what names the container, the image and the Caddy route.
    /// </summary>
    public const string ApplicationName = "techieblog";

    /// <summary>
    /// Creates a resolved settings instance.
    /// </summary>
    /// <param name="url">The Seq ingestion URL, or an empty string when unconfigured.</param>
    /// <param name="apiKey">The API key, or <c>null</c>.</param>
    private SeqSettings(string url, string? apiKey)
    {
        Url = url;
        ApiKey = apiKey;
    }

    /// <summary>Ingestion URL; empty when no Seq server is configured.</summary>
    public string Url { get; }

    /// <summary>API key presented to Seq, or <c>null</c> when the server needs none.</summary>
    public string? ApiKey { get; }

    /// <summary>Whether the Seq sink should be attached.</summary>
    public bool IsEnabled => Url.Length > 0;

    /// <summary>
    /// Reads the Seq settings from configuration.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank or whitespace-only URL means "no Seq", not "Seq at the
    /// empty address" — a container that declares the variable but leaves it empty must behave
    /// exactly like a developer machine that never declared it. A key supplied without a URL is
    /// ignored along with it rather than being treated as a configuration error, because the URL is
    /// the thing that decides whether the sink exists.</para>
    /// <para><b>Flow:</b> read the URL → trim → read the key only if the URL survived.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read the <c>Seq</c> section from.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    public static SeqSettings Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var url = configuration[UrlKey]?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            return new SeqSettings(string.Empty, null);
        }

        var apiKey = configuration[ApiKeyKey]?.Trim();
        return new SeqSettings(url, string.IsNullOrEmpty(apiKey) ? null : apiKey);
    }
}
