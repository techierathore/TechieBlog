using System.Net;
using BlogEngine.Services;
using Microsoft.AspNetCore.Http;
using TechieBlog.Tests.Analytics;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Covers <see cref="CaptchaClientKeyProvider"/> — the class that decides which bucket a captcha
/// request is counted against.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The captcha rate limiter is only as good as its partition key, and three
/// specific mistakes in that key each silently disable the limiter. Counting full IPv6 addresses
/// would let one customer holding a routine /64 rotate through 18 quintillion buckets. Failing to
/// unwrap an IPv4-mapped IPv6 address — the form a dual-stack Kestrel reports an IPv4 client in —
/// would put the same client in two buckets and double its allowance. And resolving a fresh key on
/// every call rather than once per scope would give an attacker a new bucket per request. None of
/// those is visible in a screenshot: the site looks identical whether the limiter works or not.
/// [REQ-NFR-024, REQ-NFR-016]</para>
///
/// <para><b>Dependencies:</b> <see cref="StubHttpContextAccessor"/> — the framework's own accessor
/// keeps its value in a static <c>AsyncLocal</c> and cannot represent two visitors in one test — and
/// <see cref="DefaultHttpContext"/> for the connection. No host and no network.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite.</para>
/// </remarks>
public class CaptchaClientKeyProviderTests
{
    /// <summary>
    /// Builds a provider whose HTTP context reports one transport address.
    /// </summary>
    /// <param name="address">The address the socket is connected to, or null for no address.</param>
    /// <returns>A provider bound to that connection.</returns>
    private static CaptchaClientKeyProvider ProviderFor(IPAddress? address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        return new CaptchaClientKeyProvider(new StubHttpContextAccessor(context));
    }

    /// <summary>
    /// A key derived from the transport address carries the address prefix, so an operator reading
    /// the limiter's state can tell an address bucket from a circuit bucket.
    /// </summary>
    [Fact]
    public void AddressKeyCarriesTheAddressPrefix()
    {
        // Arrange
        var provider = ProviderFor(IPAddress.Parse("203.0.113.7"));

        // Act
        var key = provider.GetClientKey();

        // Assert
        Assert.Equal(CaptchaClientKeyProvider.AddressKeyPrefix + "203.0.113.7", key);
    }

    /// <summary>
    /// With no HTTP context reachable the key falls back to the per-scope circuit identifier, which
    /// is server-generated and therefore cannot be forged by the client.
    /// </summary>
    [Fact]
    public void MissingHttpContextFallsBackToTheCircuitKey()
    {
        // Arrange
        var provider = new CaptchaClientKeyProvider(new StubHttpContextAccessor());

        // Act
        var key = provider.GetClientKey();

        // Assert
        Assert.StartsWith(CaptchaClientKeyProvider.CircuitKeyPrefix, key);
    }

    /// <summary>
    /// A null accessor — the shape a service constructed outside a request pipeline sees — also
    /// falls back to the circuit key rather than faulting.
    /// </summary>
    [Fact]
    public void NullAccessorFallsBackToTheCircuitKey()
    {
        // Arrange
        var provider = new CaptchaClientKeyProvider(null);

        // Act
        var key = provider.GetClientKey();

        // Assert
        Assert.StartsWith(CaptchaClientKeyProvider.CircuitKeyPrefix, key);
    }

    /// <summary>
    /// A connection with no remote address — which is how an in-process or unix-socket request
    /// presents — is treated as "no address" and uses the circuit key.
    /// </summary>
    [Fact]
    public void ConnectionWithoutAddressFallsBackToTheCircuitKey()
    {
        // Arrange
        var provider = ProviderFor(null);

        // Act
        var key = provider.GetClientKey();

        // Assert
        Assert.StartsWith(CaptchaClientKeyProvider.CircuitKeyPrefix, key);
    }

    /// <summary>
    /// Two circuits get two different keys, so one visitor exhausting their allowance cannot lock
    /// out everybody else on the site.
    /// </summary>
    [Fact]
    public void EachCircuitGetsItsOwnKey()
    {
        // Arrange
        var first = new CaptchaClientKeyProvider(new StubHttpContextAccessor());
        var second = new CaptchaClientKeyProvider(new StubHttpContextAccessor());

        // Act
        var firstKey = first.GetClientKey();

        // Assert
        Assert.NotEqual(firstKey, second.GetClientKey());
    }

    /// <summary>
    /// The key is resolved once and cached, so it cannot drift mid-circuit — a connection whose
    /// address changed under the provider must keep counting against its original bucket rather than
    /// being handed a fresh allowance.
    /// </summary>
    [Fact]
    public void ResolvedKeyIsCachedForTheLifeOfTheScope()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        var provider = new CaptchaClientKeyProvider(new StubHttpContextAccessor(context));
        var first = provider.GetClientKey();

        // Act
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");

        // Assert
        Assert.Equal(first, provider.GetClientKey());
    }

    /// <summary>
    /// An IPv4 address is used whole, because the whole address identifies one client.
    /// </summary>
    [Fact]
    public void IpV4AddressIsUsedWhole()
    {
        // Arrange, Act
        var key = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("198.51.100.42"));

        // Assert
        Assert.Equal("198.51.100.42", key);
    }

    /// <summary>
    /// An IPv4-mapped IPv6 address — how a dual-stack Kestrel reports an IPv4 client — is unwrapped
    /// to its IPv4 form, so the same client cannot occupy two buckets and get twice the allowance.
    /// </summary>
    [Fact]
    public void IpV4MappedAddressUnwrapsToTheSameKeyAsIpV4()
    {
        // Arrange
        var mapped = IPAddress.Parse("198.51.100.42").MapToIPv6();

        // Act
        var key = CaptchaClientKeyProvider.BuildAddressKey(mapped);

        // Assert
        Assert.Equal("198.51.100.42", key);
    }

    /// <summary>
    /// A native IPv6 address is masked to its /64 prefix, so a client handed a routine /64 cannot
    /// rotate through host bits to mint a fresh bucket per challenge.
    /// </summary>
    [Fact]
    public void IpV6AddressIsMaskedToItsPrefix()
    {
        // Arrange, Act
        var key = CaptchaClientKeyProvider.BuildAddressKey(
            IPAddress.Parse("2001:db8:1234:5678:9abc:def0:1234:5678"));

        // Assert
        Assert.Equal("2001:db8:1234:5678::/64", key);
    }

    /// <summary>
    /// Two addresses inside one /64 collapse to the same bucket, which is the property the mask
    /// exists to produce.
    /// </summary>
    [Fact]
    public void AddressesInOnePrefixShareOneBucket()
    {
        // Arrange
        var first = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:2::1"));

        // Act
        var second = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff"));

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>
    /// Two addresses in different /64 prefixes stay in different buckets, so the mask does not
    /// collapse unrelated customers onto one allowance.
    /// </summary>
    [Fact]
    public void AddressesInDifferentPrefixesStaySeparate()
    {
        // Arrange
        var first = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:2::1"));

        // Act
        var second = CaptchaClientKeyProvider.BuildAddressKey(IPAddress.Parse("2001:db8:1:3::1"));

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// A null address is rejected outright rather than producing a shared "null" bucket every
    /// caller would land in.
    /// </summary>
    [Fact]
    public void NullAddressIsRejected()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => CaptchaClientKeyProvider.BuildAddressKey(null!));
    }
}
