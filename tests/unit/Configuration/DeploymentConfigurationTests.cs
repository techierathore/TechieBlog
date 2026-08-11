using BlogEngine.Services;
using Microsoft.Extensions.Configuration;
using TechieBlog.Configuration;

namespace TechieBlog.Tests.Configuration;

/// <summary>
/// REQ-NFR-030 — the startup gate for <c>SiteSettings:BaseUrl</c> and <c>Analytics:VisitorSalt</c>.
/// </summary>
/// <remarks>
/// Both settings fail silently in a real deployment: the first mails unsubscribe links nobody can
/// reach, the second leaves visitor digests reversible. The gate has to be asymmetric — hard failure
/// outside Development, one loud warning inside it — so these tests pin both halves as well as the
/// individual detection rules.
/// </remarks>
public class DeploymentConfigurationTests
{
    private const string ProductionSalt = "0123456789abcdef0123456789abcdef0123";
    private const string ProductionBaseUrl = "https://blog.example.com";

    /// <summary>
    /// Builds an in-memory configuration from the supplied values, omitting any that is null.
    /// </summary>
    /// <param name="baseUrl">Value for <c>SiteSettings:BaseUrl</c>, or null to omit it.</param>
    /// <param name="visitorSalt">Value for <c>Analytics:VisitorSalt</c>, or null to omit it.</param>
    /// <returns>A configuration root carrying only the supplied values.</returns>
    private static IConfiguration BuildConfiguration(string? baseUrl, string? visitorSalt)
    {
        var values = new Dictionary<string, string?>();

        if (baseUrl != null)
        {
            values[DeploymentConfiguration.BaseUrlPath] = baseUrl;
        }

        if (visitorSalt != null)
        {
            values[DeploymentConfiguration.VisitorSaltPath] = visitorSalt;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// A fully configured deployment reports no problems at all.
    /// </summary>
    [Fact]
    public void InspectAcceptsAConfiguredDeployment()
    {
        var problems = DeploymentConfiguration.Inspect(
            BuildConfiguration(ProductionBaseUrl, ProductionSalt));

        Assert.Empty(problems);
    }

    /// <summary>
    /// An absent base URL is reported, and the message names the setting so an operator can act on it.
    /// </summary>
    [Fact]
    public void InspectRejectsAMissingBaseUrl()
    {
        var problems = DeploymentConfiguration.Inspect(BuildConfiguration(null, ProductionSalt));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.BaseUrlPath));
    }

    /// <summary>
    /// The exact value that shipped in appsettings.json is recognised as a development default.
    /// </summary>
    [Fact]
    public void InspectRejectsTheShippedLocalhostBaseUrl()
    {
        var problems = DeploymentConfiguration.Inspect(
            BuildConfiguration("https://localhost:5001", ProductionSalt));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.BaseUrlPath));
    }

    /// <summary>
    /// Any loopback origin counts as a development default, whatever the scheme or port.
    /// </summary>
    /// <param name="baseUrl">A loopback base URL that must be refused.</param>
    [Theory]
    [InlineData("http://localhost:5404")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://0.0.0.0")]
    [InlineData("not-a-url-at-all")]
    public void InspectRejectsEveryLoopbackBaseUrl(string baseUrl)
    {
        var problems = DeploymentConfiguration.Inspect(BuildConfiguration(baseUrl, ProductionSalt));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.BaseUrlPath));
    }

    /// <summary>
    /// An absent visitor salt is reported, because the fallback salt is published in this repository.
    /// </summary>
    [Fact]
    public void InspectRejectsAMissingVisitorSalt()
    {
        var problems = DeploymentConfiguration.Inspect(BuildConfiguration(ProductionBaseUrl, null));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.VisitorSaltPath));
    }

    /// <summary>
    /// Pasting the built-in development salt into configuration is refused, not accepted as "set".
    /// </summary>
    [Fact]
    public void InspectRejectsTheBuiltInVisitorSalt()
    {
        var problems = DeploymentConfiguration.Inspect(
            BuildConfiguration(ProductionBaseUrl, PostViewTracker.DefaultVisitorSalt));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.VisitorSaltPath));
    }

    /// <summary>
    /// A salt shorter than the minimum is refused, since it can be brute-forced with the address.
    /// </summary>
    [Fact]
    public void InspectRejectsAShortVisitorSalt()
    {
        var problems = DeploymentConfiguration.Inspect(BuildConfiguration(ProductionBaseUrl, "tooshort"));

        Assert.Contains(problems, problem => problem.Contains(DeploymentConfiguration.VisitorSaltPath));
    }

    /// <summary>
    /// Both settings wrong produce two separate messages, so one fix does not hide the other.
    /// </summary>
    [Fact]
    public void InspectReportsBothSettingsIndependently()
    {
        var problems = DeploymentConfiguration.Inspect(BuildConfiguration(null, null));

        Assert.Equal(2, problems.Count);
    }

    /// <summary>
    /// Outside Development an unusable setting stops the host, and the message carries the fix.
    /// </summary>
    [Fact]
    public void EnforceThrowsOutsideDevelopment()
    {
        var configuration = BuildConfiguration(null, null);

        var error = Assert.Throws<InvalidOperationException>(
            () => DeploymentConfiguration.Enforce(configuration, "Production", _ => { }));

        Assert.Contains(DeploymentConfiguration.BaseUrlPath, error.Message);
        Assert.Contains(DeploymentConfiguration.VisitorSaltPath, error.Message);
        Assert.Contains("Production", error.Message);
    }

    /// <summary>
    /// Staging is not Development, so it fails fast exactly as Production does.
    /// </summary>
    [Fact]
    public void EnforceThrowsForAnyNonDevelopmentEnvironment()
    {
        var configuration = BuildConfiguration("https://localhost:5001", null);

        Assert.Throws<InvalidOperationException>(
            () => DeploymentConfiguration.Enforce(configuration, "Staging", _ => { }));
    }

    /// <summary>
    /// In Development the same findings warn once and let the host start, so local work is unaffected.
    /// </summary>
    [Fact]
    public void EnforceWarnsOnceInDevelopment()
    {
        var configuration = BuildConfiguration(null, null);
        var warnings = new List<string>();

        DeploymentConfiguration.Enforce(configuration, "Development", warnings.Add);

        var warning = Assert.Single(warnings);
        Assert.Contains(DeploymentConfiguration.BaseUrlPath, warning);
        Assert.Contains(DeploymentConfiguration.VisitorSaltPath, warning);
    }

    /// <summary>
    /// The environment name is matched case-insensitively, as the hosting stack does.
    /// </summary>
    [Fact]
    public void EnforceMatchesTheDevelopmentNameCaseInsensitively()
    {
        var configuration = BuildConfiguration(null, null);
        var warnings = new List<string>();

        DeploymentConfiguration.Enforce(configuration, "development", warnings.Add);

        Assert.Single(warnings);
    }

    /// <summary>
    /// A correctly configured Production host starts silently, with nothing warned and nothing thrown.
    /// </summary>
    [Fact]
    public void EnforceIsSilentWhenTheDeploymentIsConfigured()
    {
        var configuration = BuildConfiguration(ProductionBaseUrl, ProductionSalt);
        var warnings = new List<string>();

        DeploymentConfiguration.Enforce(configuration, "Production", warnings.Add);

        Assert.Empty(warnings);
    }
}
