using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using System.Net;
using TechieBlog.Middleware;
using IPNetwork = System.Net.IPNetwork;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Covers the forwarded-headers allow-list that makes the rate limiters key on the real client
/// behind a proxy (REQ-NFR-028).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The requirement is specific about what must NOT happen: no blanket trust,
/// and no change of behaviour when there is no proxy. These tests pin both, plus the fact that the
/// framework's implicit loopback trust is cleared rather than inherited.</para>
///
/// <para><b>Code Flow:</b> each test builds an in-memory configuration, runs
/// <see cref="ForwardedHeadersSetup.Configure"/> over a fresh
/// <see cref="ForwardedHeadersOptions"/>, and inspects the result. The end-to-end proof that a
/// request from a known proxy is attributed to the forwarded client, and one from an unknown proxy
/// is not, is a runtime smoke test against the booted host.</para>
///
/// <para><b>Dependencies:</b> <see cref="ForwardedHeadersSetup"/>, compiled into the test assembly
/// as linked source because referencing the host project would drag in the whole UI graph.</para>
///
/// <para><b>Usage:</b> <c>dotnet test</c>.</para>
/// </remarks>
public class ForwardedHeadersSetupTests
{
    /// <summary>
    /// With no ForwardedHeaders section configured nothing is trusted, so a direct-to-Kestrel
    /// deployment keeps using the transport address exactly as it did before the change.
    /// </summary>
    [Fact]
    public void EmptyConfigurationTrustsNothing()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>()));

        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownNetworks);
    }

    /// <summary>
    /// An empty allow-list switches the header set off rather than leaving it enabled. This is the
    /// case that matters most: ForwardedHeadersMiddleware only consults its allow-list when the list
    /// has at least one entry, so an enabled middleware with two empty lists trusts every caller —
    /// observed at runtime as curl successfully forging its own rate-limit identity.
    /// </summary>
    [Fact]
    public void EmptyAllowListDisablesTheMiddlewareRatherThanTrustingEveryone()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>()));

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
    }

    /// <summary>
    /// The framework trusts IPv6 loopback by default; the setup clears that, so a process
    /// co-located on the same host cannot set X-Forwarded-For and choose its own rate-limit bucket.
    /// </summary>
    [Fact]
    public void DefaultLoopbackTrustIsCleared()
    {
        var options = new ForwardedHeadersOptions();
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>()));

        Assert.DoesNotContain(IPAddress.IPv6Loopback, options.KnownProxies);
    }

    /// <summary>
    /// A configured proxy address is trusted, and the header flags cover the client address, scheme
    /// and host so an HTTPS-terminating proxy does not leave the app thinking it is serving HTTP.
    /// </summary>
    [Fact]
    public void ConfiguredProxyIsTrusted()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.4",
            ["ForwardedHeaders:KnownProxies:1"] = "::1"
        }));

        Assert.Contains(IPAddress.Parse("10.0.0.4"), options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedProto));
    }

    /// <summary>
    /// A configured CIDR network is trusted, so an operator can name a proxy subnet instead of
    /// enumerating every load-balancer instance.
    /// </summary>
    [Fact]
    public void ConfiguredNetworkIsTrusted()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/24"
        }));

        var networks = (ICollection<IPNetwork>)options.KnownNetworks;
        Assert.Single(networks);
        Assert.Contains(new IPNetwork(IPAddress.Parse("10.0.0.0"), 24), networks);
    }

    /// <summary>
    /// A blanket-trust network such as 0.0.0.0/0 is refused outright, because trusting everyone lets
    /// any caller forge an address and mint a fresh rate-limit identity for every request.
    /// </summary>
    [Fact]
    public void BlanketTrustNetworkIsRefused()
    {
        var options = new ForwardedHeadersOptions();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersSetup.Configure(options, configuration));

        Assert.Contains("trusts every client", error.Message);
    }

    /// <summary>
    /// A misspelled proxy address stops startup instead of being silently dropped, which would leave
    /// the limiter keying on the proxy without anybody noticing.
    /// </summary>
    [Fact]
    public void MalformedProxyAddressIsRefused()
    {
        var options = new ForwardedHeadersOptions();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.999"
        });

        Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersSetup.Configure(options, configuration));
    }

    /// <summary>
    /// A CIDR entry that is not in network form is refused with a message naming the offending entry.
    /// </summary>
    [Fact]
    public void MalformedNetworkIsRefused()
    {
        var options = new ForwardedHeadersOptions();
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ForwardedHeadersSetup.Configure(options, configuration));

        Assert.Contains("10.0.0.0", error.Message);
    }

    /// <summary>
    /// Only one hop is honoured unless the deployment says otherwise, so a chain of client-written
    /// X-Forwarded-For entries cannot be walked back past the trusted proxy.
    /// </summary>
    [Fact]
    public void ForwardLimitDefaultsToASingleHop()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>()));

        Assert.Equal(ForwardedHeadersSetup.DefaultForwardLimit, options.ForwardLimit);
    }

    /// <summary>
    /// A deployment sitting behind two proxies can raise the hop count through configuration.
    /// </summary>
    [Fact]
    public void ForwardLimitIsConfigurable()
    {
        var options = new ForwardedHeadersOptions();

        ForwardedHeadersSetup.Configure(options, BuildConfiguration(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:ForwardLimit"] = "2"
        }));

        Assert.Equal(2, options.ForwardLimit);
    }

    /// <summary>
    /// Builds an in-memory configuration from flat key/value pairs.
    /// </summary>
    /// <param name="entries">The configuration entries.</param>
    /// <returns>The built configuration.</returns>
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> entries) =>
        new ConfigurationBuilder().AddInMemoryCollection(entries).Build();
}
