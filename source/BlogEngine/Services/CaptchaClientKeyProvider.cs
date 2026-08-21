using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace BlogEngine.Services;

/// <summary>
/// Resolves the captcha rate-limiting identity from the transport connection, falling back to the
/// Blazor circuit when no HTTP context is reachable. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The captcha is issued and validated inside a Blazor Server circuit, not
/// over an HTTP endpoint, so the ASP.NET Core rate-limiting middleware that guards the
/// authentication paths (REQ-NFR-005) never sees these calls and cannot supply the key.</para>
///
/// <para><b>The key, and why it is what it is:</b></para>
/// <list type="number">
///   <item><b>Transport IP address, when one is reachable.</b> Read from
///   <see cref="ConnectionInfo.RemoteIpAddress"/> - the address the socket is actually connected
///   to. It is NOT read from <c>X-Forwarded-For</c> or any other request header, because a header
///   is written by the client and would let an attacker mint a fresh identity per request simply
///   by changing a string, which is worse than having no limiter at all.</item>
///   <item><b>An IPv6 address is masked to its /64 prefix.</b> A single residential or hosting
///   customer is routinely handed a /64, so counting full IPv6 addresses would let one client
///   rotate through 18 quintillion identities. IPv4 is used whole; an IPv4-mapped IPv6 address is
///   unwrapped first so the same client cannot occupy two buckets.</item>
///   <item><b>Otherwise, the circuit.</b> If no HTTP context is reachable the key is a random id
///   generated once per DI scope. In Blazor Server a scope is a circuit, so this attributes every
///   captcha call to the connection that made it. It is a weaker key - an attacker willing to open
///   a new circuit per challenge gets a new bucket - but it is server-generated and therefore
///   never forgeable, and it still stops the cheap attack of hammering one open connection.</item>
/// </list>
///
/// <para><b>Reverse-proxy caveat (stated, not silently assumed):</b> the host does NOT configure
/// forwarded-headers handling - there is no <c>UseForwardedHeaders</c> and no
/// <c>ForwardedHeadersOptions</c> in <c>Program.cs</c>, and the same is true of the authentication
/// limiter's own partition key. Deployed behind nginx or a load balancer without that middleware,
/// <see cref="ConnectionInfo.RemoteIpAddress"/> is the PROXY's address, so every visitor shares one
/// bucket and the caps become site-wide. The fix belongs at the host, once and for both limiters:
/// enable <c>UseForwardedHeaders</c> with an explicit <c>KnownProxies</c>/<c>KnownNetworks</c>
/// allow-list. Trusting the header without that allow-list would be strictly worse than the
/// current behaviour, which is why this class does not do it unilaterally.</para>
///
/// <para><b>Dependencies:</b> <see cref="IHttpContextAccessor"/>, which the host registers.</para>
///
/// <para><b>Usage:</b> Registered per scope by <c>EngagementSvcInitializer</c>. The key is resolved
/// once and cached, so it cannot drift mid-circuit.</para>
/// </remarks>
public class CaptchaClientKeyProvider : ICaptchaClientKeyProvider
{
    /// <summary>Prefix marking a key derived from the transport IP address.</summary>
    public const string AddressKeyPrefix = "captcha-ip:";

    /// <summary>Prefix marking a key derived from the Blazor circuit.</summary>
    public const string CircuitKeyPrefix = "captcha-circuit:";

    /// <summary>Bits of an IPv6 address that identify the subscriber rather than the device.</summary>
    private const int IpV6PrefixBytes = 8;

    private readonly IHttpContextAccessor? httpContextAccessor;
    private readonly string circuitKey = CircuitKeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private string? resolvedKey;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaClientKeyProvider"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Access to the current HTTP context, when there is one.</param>
    public CaptchaClientKeyProvider(IHttpContextAccessor? httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string GetClientKey()
    {
        if (resolvedKey != null)
            return resolvedKey;

        var address = httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress;
        resolvedKey = address == null
            ? circuitKey
            : AddressKeyPrefix + BuildAddressKey(address);

        return resolvedKey;
    }

    /// <summary>
    /// Reduces an IP address to the span that identifies one client.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> IPv4 addresses are used whole. IPv4-mapped IPv6 addresses -
    /// which is how a dual-stack Kestrel reports an IPv4 client - are unwrapped so the same client
    /// never occupies two buckets. Native IPv6 is masked to its first 64 bits, because handing out
    /// a /64 per customer is standard and the host bits are the client's to rotate.</para>
    /// <para><b>Flow:</b> unwrap → branch on family → mask IPv6 → format.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="address">The transport address of the caller.</param>
    /// <returns>The bucket identifier for that address.</returns>
    public static string BuildAddressKey(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var normalised = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (normalised.AddressFamily != AddressFamily.InterNetworkV6)
            return normalised.ToString();

        var bytes = normalised.GetAddressBytes();
        for (var index = IpV6PrefixBytes; index < bytes.Length; index++)
        {
            bytes[index] = 0;
        }

        return new IPAddress(bytes) + "/64";
    }
}
