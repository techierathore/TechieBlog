using Microsoft.Extensions.Configuration;
using TechieBlog.Configuration;

namespace TechieBlog.Tests.Configuration;

/// <summary>
/// REQ-NFR-013 — the Seq sink is attached only when a deployment actually has a Seq server.
/// </summary>
/// <remarks>
/// The VPS runs one shared Seq container; a developer's machine runs none, and cloning this
/// repository must not require standing one up. The whole rule is "attach the sink if and only if
/// <c>Seq:Url</c> is set", so these tests pin the edge that would break it — a variable declared in
/// a compose file but left empty, which must behave exactly like a variable that was never
/// declared.
/// </remarks>
public class SeqSettingsTests
{
    /// <summary>
    /// Builds an in-memory configuration from the supplied values, omitting any that is null.
    /// </summary>
    /// <param name="url">Value for <c>Seq:Url</c>, or null to omit it.</param>
    /// <param name="apiKey">Value for <c>Seq:ApiKey</c>, or null to omit it.</param>
    /// <returns>A configuration root carrying only the supplied values.</returns>
    private static IConfiguration BuildConfiguration(string? url, string? apiKey)
    {
        var values = new Dictionary<string, string?>();

        if (url != null)
        {
            values[SeqSettings.UrlKey] = url;
        }

        if (apiKey != null)
        {
            values[SeqSettings.ApiKeyKey] = apiKey;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Local development, where nothing is configured: no sink, no connection attempt.
    /// </summary>
    [Fact]
    public void SeqIsDisabledWhenNoUrlIsConfigured()
    {
        Assert.False(SeqSettings.Resolve(BuildConfiguration(null, null)).IsEnabled);
    }

    /// <summary>
    /// A declared-but-blank URL means "no Seq", not "Seq at the empty address".
    /// </summary>
    [Fact]
    public void SeqIsDisabledWhenTheUrlIsBlank()
    {
        Assert.False(SeqSettings.Resolve(BuildConfiguration("   ", "key")).IsEnabled);
    }

    /// <summary>
    /// An API key without a URL cannot enable the sink on its own.
    /// </summary>
    [Fact]
    public void ApiKeyAloneDoesNotEnableSeq()
    {
        var settings = SeqSettings.Resolve(BuildConfiguration(null, "key"));

        Assert.False(settings.IsEnabled);
        Assert.Null(settings.ApiKey);
    }

    /// <summary>
    /// The deployment case: the internal Seq address and a key from a secret.
    /// </summary>
    [Fact]
    public void SeqIsEnabledWithUrlAndApiKey()
    {
        var settings = SeqSettings.Resolve(BuildConfiguration("http://seq:5341", "ingest-key"));

        Assert.True(settings.IsEnabled);
        Assert.Equal("http://seq:5341", settings.Url);
        Assert.Equal("ingest-key", settings.ApiKey);
    }

    /// <summary>
    /// A Seq server that needs no key is still a Seq server.
    /// </summary>
    [Fact]
    public void SeqIsEnabledWithoutAnApiKey()
    {
        var settings = SeqSettings.Resolve(BuildConfiguration("http://seq:5341", null));

        Assert.True(settings.IsEnabled);
        Assert.Null(settings.ApiKey);
    }

    /// <summary>
    /// The application property is the deployment's APP_NAME, because one Seq instance receives
    /// events from every application on the host and an unnamed event cannot be filtered.
    /// </summary>
    [Fact]
    public void ApplicationNameIsTheDeploymentIdentity()
    {
        Assert.Equal("App", SeqSettings.ApplicationPropertyName);
        Assert.Equal("techieblog", SeqSettings.ApplicationName);
    }
}
