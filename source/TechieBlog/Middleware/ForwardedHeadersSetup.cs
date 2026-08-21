using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using System.Net;

// Both Microsoft.AspNetCore.HttpOverrides and System.Net declare IPNetwork, and
// ForwardedHeadersOptions.KnownNetworks accepts either. The BCL type is the non-obsolete one.
using IPNetwork = System.Net.IPNetwork;

namespace TechieBlog.Middleware;

/// <summary>
/// Builds the forwarded-headers policy from configuration, with no implicit trust (REQ-NFR-028).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Behind nginx or any load balancer, <c>HttpContext.Connection.RemoteIpAddress</c>
/// is the proxy, not the caller. Both rate limiters key on that address —
/// <c>AuthRateLimit.BuildPartitionKey</c> for the sign-in paths (REQ-NFR-005) and
/// <c>CaptchaClientKeyProvider</c> for the captcha budget (REQ-NFR-024) — so without this policy
/// every per-client cap collapses into a single site-wide bucket and one attacker locks everybody
/// out. This type turns an explicit allow-list of proxy addresses and networks into
/// <see cref="ForwardedHeadersOptions"/>.</para>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> calls <see cref="Configure"/> while building the
/// container, and <c>app.UseForwardedHeaders()</c> runs the middleware as the very first step in the
/// pipeline so every later component — correlation id, request logging, the rate limiter — sees the
/// rewritten address. <see cref="Configure"/> clears the framework's default loopback trust and adds
/// back only what <c>ForwardedHeaders:KnownProxies</c> and <c>ForwardedHeaders:KnownNetworks</c>
/// name. An empty allow-list means nothing is trusted: <c>X-Forwarded-For</c> is ignored and the
/// transport address is used unchanged, which is exactly the behaviour required when no proxy is
/// deployed.</para>
///
/// <para><b>Dependencies:</b> <c>Microsoft.AspNetCore.HttpOverrides</c> and
/// <see cref="IConfiguration"/>.</para>
///
/// <para><b>Usage:</b> Configure the deployment's proxies in an environment-specific settings file:
/// <c>"ForwardedHeaders": { "KnownProxies": [ "10.0.0.4" ], "KnownNetworks": [ "10.0.0.0/24" ],
/// "ForwardLimit": 1 }</c>. Never add <c>0.0.0.0/0</c> — a blanket trust lets any client write its
/// own address and mint a fresh rate-limit identity per request, which is strictly worse than
/// having no limiter at all. Note the captcha limiter deliberately refuses to read
/// <c>X-Forwarded-For</c> itself; it keeps reading the transport address, and this middleware is
/// what makes that address true.</para>
/// </remarks>
public static class ForwardedHeadersSetup
{
    /// <summary>Configuration path holding the individual trusted proxy addresses.</summary>
    public const string KnownProxiesPath = "ForwardedHeaders:KnownProxies";

    /// <summary>Configuration path holding the trusted proxy networks in CIDR form.</summary>
    public const string KnownNetworksPath = "ForwardedHeaders:KnownNetworks";

    /// <summary>Configuration path holding the maximum number of header entries to walk back.</summary>
    public const string ForwardLimitPath = "ForwardedHeaders:ForwardLimit";

    /// <summary>
    /// Number of proxy hops honoured when the setting is absent: exactly one.
    /// </summary>
    public const int DefaultForwardLimit = 1;

    /// <summary>
    /// Applies the configured allow-list to a <see cref="ForwardedHeadersOptions"/> instance.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Honours <c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c> and
    /// <c>X-Forwarded-Host</c>, but only from an address the operator has named. The framework
    /// defaults trust IPv6 loopback, which would let anything running on the same host spoof a
    /// client address, so both lists are cleared before the configured entries are added. When
    /// nothing is configured the header set is switched off entirely — see the comment in the body
    /// for why an empty allow-list is the most dangerous state to leave the middleware in.</para>
    /// <para><b>Flow:</b> set the forward limit → clear the default trust → parse the allow-list →
    /// disable the middleware outright when it is empty → otherwise enable the headers and add each
    /// configured proxy and network.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="options"/>.</para>
    /// </remarks>
    /// <param name="options">The options instance the framework will use.</param>
    /// <param name="configuration">Configuration to read the allow-list from.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A configured proxy address or network is not parseable.
    /// </exception>
    public static void Configure(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        options.ForwardLimit = configuration.GetValue(ForwardLimitPath, DefaultForwardLimit);

        // The framework trusts IPv6 loopback out of the box. Anything co-located on the host could
        // then set X-Forwarded-For and pick its own rate-limit partition, so start from zero trust.
        options.KnownProxies.Clear();
        options.KnownNetworks.Clear();

        var proxies = ReadKnownProxies(configuration);
        var networks = ReadKnownNetworks(configuration);

        // ForwardedHeadersMiddleware only consults the allow-list when at least one entry exists:
        // its check is `KnownNetworks.Count > 0 || KnownProxies.Count > 0`, so leaving BOTH lists
        // empty makes it trust EVERY caller rather than none of them. That was confirmed at
        // runtime - an unconfigured host honoured an X-Forwarded-For sent straight from curl and
        // partitioned the rate limiter on the forged address. Switching the header set off is what
        // actually turns the middleware into a no-op, which is the required "unchanged behaviour
        // when no proxy is present".
        if (proxies.Count == 0 && networks.Count == 0)
        {
            options.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;

        foreach (var proxy in proxies)
            options.KnownProxies.Add(proxy);

        // KnownNetworks implements ICollection<> for both IPNetwork types; the unqualified Add
        // binds to the obsolete ASP.NET Core one, so dispatch explicitly through the BCL interface.
        var knownNetworks = (ICollection<IPNetwork>)options.KnownNetworks;
        foreach (var network in networks)
            knownNetworks.Add(network);
    }

    /// <summary>
    /// Reads and parses the individually named trusted proxy addresses.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every entry must be a valid IP address; a typo is a
    /// configuration error that must stop the host rather than silently shrink the allow-list and
    /// leave the limiter keying on the proxy.</para>
    /// <para><b>Flow:</b> read the array section → skip blank entries → parse each.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read from.</param>
    /// <returns>The parsed proxy addresses, empty when none are configured.</returns>
    /// <exception cref="InvalidOperationException">An entry is not a valid IP address.</exception>
    public static IReadOnlyList<IPAddress> ReadKnownProxies(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var parsed = new List<IPAddress>();
        foreach (var entry in ReadEntries(configuration, KnownProxiesPath))
        {
            if (!IPAddress.TryParse(entry, out var address))
            {
                throw new InvalidOperationException(
                    $"'{KnownProxiesPath}' contains '{entry}', which is not a valid IP address.");
            }

            parsed.Add(address);
        }

        return parsed;
    }

    /// <summary>
    /// Reads and parses the trusted proxy networks written in CIDR form.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Accepts <c>address/prefixLength</c> entries such as
    /// <c>10.0.0.0/24</c>. A zero-length prefix is refused outright because it trusts the whole
    /// internet, which is the precise failure this requirement exists to prevent.</para>
    /// <para><b>Flow:</b> read the array section → split on '/' → parse address and prefix → reject
    /// a prefix of zero → build the network.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read from.</param>
    /// <returns>The parsed networks, empty when none are configured.</returns>
    /// <exception cref="InvalidOperationException">
    /// An entry is malformed, or names a blanket-trust network.
    /// </exception>
    public static IReadOnlyList<IPNetwork> ReadKnownNetworks(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var parsed = new List<IPNetwork>();
        foreach (var entry in ReadEntries(configuration, KnownNetworksPath))
        {
            var parts = entry.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var prefixLength))
            {
                throw new InvalidOperationException(
                    $"'{KnownNetworksPath}' contains '{entry}', which is not valid CIDR notation " +
                    $"such as '10.0.0.0/24'.");
            }

            if (prefixLength <= 0)
            {
                throw new InvalidOperationException(
                    $"'{KnownNetworksPath}' contains '{entry}', which trusts every client. A " +
                    $"blanket trust lets any caller forge its own address; name the proxy network " +
                    $"explicitly instead.");
            }

            try
            {
                parsed.Add(new IPNetwork(prefix, prefixLength));
            }
            catch (ArgumentException ex)
            {
                // System.Net.IPNetwork refuses a base address with bits set outside the prefix,
                // e.g. 10.0.0.5/24 - almost always a typo for the network address 10.0.0.0/24.
                throw new InvalidOperationException(
                    $"'{KnownNetworksPath}' contains '{entry}', which is not a valid network: " +
                    $"{ex.Message}", ex);
            }
        }

        return parsed;
    }

    /// <summary>
    /// Reads a configuration array as a list of non-blank strings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both allow-lists are optional; an absent section yields an empty
    /// list, which means nothing is trusted.</para>
    /// <para><b>Flow:</b> get the section's children → take their values → drop blanks → trim.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read from.</param>
    /// <param name="path">The section path to enumerate.</param>
    /// <returns>The non-blank entries, in configuration order.</returns>
    private static IEnumerable<string> ReadEntries(IConfiguration configuration, string path) =>
        configuration.GetSection(path)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
}
