using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Minimal <see cref="IConfiguration"/> backed by a dictionary.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Supplies <c>SiteSettings:BaseUrl</c> to the verification service without
/// dragging a configuration-provider package into the test project.</para>
/// <para><b>Code Flow:</b> Only the indexer is meaningful; the section and change-token members
/// exist to satisfy the interface and are never used by the code under test.</para>
/// <para><b>Dependencies:</b> Microsoft.Extensions.Configuration.Abstractions.</para>
/// <para><b>Usage:</b> <c>new FakeConfiguration(new Dictionary&lt;string, string&gt; { ["A:B"] = "c" })</c>.</para>
/// </remarks>
public class FakeConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> values;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeConfiguration"/> class.
    /// </summary>
    /// <param name="values">The key/value pairs to serve.</param>
    public FakeConfiguration(Dictionary<string, string?> values)
    {
        this.values = values ?? new Dictionary<string, string?>();
    }

    /// <summary>
    /// Gets or sets a configuration value by key.
    /// </summary>
    /// <param name="key">The configuration key.</param>
    /// <returns>The value, or null when the key is absent.</returns>
    public string? this[string key]
    {
        get => values.TryGetValue(key, out var value) ? value : null;
        set => values[key] = value;
    }

    /// <summary>
    /// Not used by the code under test.
    /// </summary>
    /// <returns>An empty sequence.</returns>
    public IEnumerable<IConfigurationSection> GetChildren() => Enumerable.Empty<IConfigurationSection>();

    /// <summary>
    /// Not used by the code under test.
    /// </summary>
    /// <returns>A token that never fires.</returns>
    public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

    /// <summary>
    /// Not used by the code under test.
    /// </summary>
    /// <param name="key">The section key.</param>
    /// <returns>Always null.</returns>
    public IConfigurationSection GetSection(string key) => null!;
}
