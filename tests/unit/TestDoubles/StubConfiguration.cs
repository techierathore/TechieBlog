using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace TechieBlog.Tests.TestDoubles;

/// <summary>
/// Minimal in-memory <see cref="IConfiguration"/> for services that read settings by key.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets tests drive configuration-dependent behaviour — SMTP host presence,
/// the analytics visitor salt, the site base URL — without adding a configuration-builder package
/// to the test project.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A test constructs the stub with the keys it cares about.</item>
///   <item>The service under test reads them through the indexer, exactly as it would in
///         production.</item>
///   <item>Section and change-token members are inert — no service under test uses them.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>Microsoft.Extensions.Configuration.Abstractions</c>.</para>
///
/// <para><b>Usage:</b> <c>new StubConfiguration(new Dictionary&lt;string, string&gt; { ["A:B"] = "c" })</c>.</para>
/// </remarks>
public class StubConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> values;

    /// <summary>
    /// Creates a stub carrying the supplied key/value pairs.
    /// </summary>
    /// <param name="values">Configuration keys and values; null yields an empty configuration.</param>
    public StubConfiguration(Dictionary<string, string?>? values = null)
    {
        this.values = values ?? new Dictionary<string, string?>();
    }

    /// <summary>
    /// Reads or writes a configuration value by full key.
    /// </summary>
    /// <param name="key">Colon-separated configuration key.</param>
    /// <returns>The value, or null when the key is absent.</returns>
    public string? this[string key]
    {
        get => values.TryGetValue(key, out var value) ? value : null;
        set => values[key] = value;
    }

    /// <summary>
    /// Not used by any service under test.
    /// </summary>
    /// <returns>An empty sequence.</returns>
    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();

    /// <summary>
    /// Not used by any service under test.
    /// </summary>
    /// <returns>A token that never fires.</returns>
    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    /// <summary>
    /// Not used by any service under test.
    /// </summary>
    /// <param name="key">Section key.</param>
    /// <returns>Always null.</returns>
    public IConfigurationSection GetSection(string key) => null!;
}
