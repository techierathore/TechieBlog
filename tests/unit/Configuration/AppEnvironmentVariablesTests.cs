using Microsoft.Extensions.Configuration;
using System.Collections;
using TechieBlog.Configuration;

namespace TechieBlog.Tests.Configuration;

/// <summary>
/// The container's environment-variable contract — the PascalCase names the deployment sets and the
/// configuration paths the application reads.
/// </summary>
/// <remarks>
/// The coding standards mandate PascalCase environment variables with no separators
/// (<c>SiteSettingsBaseUrl</c>, not <c>SITE_SETTINGS_BASE_URL</c> and not
/// <c>SiteSettings__BaseUrl</c>) translated by a custom provider. That provider did not exist until
/// cluster H; these tests pin both halves of what it now guarantees — that every setting the
/// container needs is spelled exactly once in <see cref="AppEnvironmentVariables.Map"/>, and that an
/// unrelated variable can never leak into application configuration.
/// </remarks>
public class AppEnvironmentVariablesTests
{
    /// <summary>
    /// Every setting a production container must supply is present in the map, spelled the way the
    /// compose file will spell it. This is the deployment contract: if a name changes here, the
    /// compose file is wrong and the host will not start.
    /// </summary>
    /// <param name="environmentVariableName">The PascalCase environment variable.</param>
    /// <param name="configurationPath">The configuration path it must publish.</param>
    [Theory]
    [InlineData("AppDbConString", "AppDbConString")]
    [InlineData("JwtSigningKey", "JwtSigningKey")]
    [InlineData("AppEncryptionKey", "AppEncryptionKey")]
    [InlineData("SiteSettingsBaseUrl", "SiteSettings:BaseUrl")]
    [InlineData("AnalyticsVisitorSalt", "Analytics:VisitorSalt")]
    [InlineData("SeqUrl", "Seq:Url")]
    [InlineData("SeqApiKey", "Seq:ApiKey")]
    [InlineData("UploadsPath", "Uploads:Path")]
    [InlineData("LogFileEnabled", "LogFile:Enabled")]
    [InlineData("LogFilePath", "LogFile:Path")]
    public void MapCarriesTheDeploymentContract(
        string environmentVariableName, string configurationPath)
    {
        Assert.True(
            AppEnvironmentVariables.Map.TryGetValue(environmentVariableName, out var mapped),
            $"{environmentVariableName} is missing from the deployment contract.");
        Assert.Equal(configurationPath, mapped);
    }

    /// <summary>
    /// The map's names are the ones the startup gate and the secret loader read, referenced through
    /// the production constants rather than retyped — a rename there must break this test.
    /// </summary>
    [Fact]
    public void MapAgreesWithTheStartupGatePaths()
    {
        Assert.Equal(
            DeploymentConfiguration.BaseUrlPath, AppEnvironmentVariables.Map["SiteSettingsBaseUrl"]);
        Assert.Equal(
            DeploymentConfiguration.VisitorSaltPath,
            AppEnvironmentVariables.Map["AnalyticsVisitorSalt"]);
        Assert.Equal(SeqSettings.UrlKey, AppEnvironmentVariables.Map["SeqUrl"]);
        Assert.Equal(SeqSettings.ApiKeyKey, AppEnvironmentVariables.Map["SeqApiKey"]);
        Assert.Equal(LogFileSettings.EnabledKey, AppEnvironmentVariables.Map["LogFileEnabled"]);
        Assert.Equal(LogFileSettings.PathKey, AppEnvironmentVariables.Map["LogFilePath"]);
    }

    /// <summary>
    /// A PascalCase name is republished under its nested path, which is the translation the
    /// framework's own provider cannot do.
    /// </summary>
    [Fact]
    public void TranslateNestsAPascalCaseName()
    {
        var translated = AppEnvironmentVariables.Translate(
            new Hashtable { ["SiteSettingsBaseUrl"] = "https://blog.example.com" });

        Assert.Equal("https://blog.example.com", translated["SiteSettings:BaseUrl"]);
    }

    /// <summary>
    /// A flat key needs no translation and survives unchanged, so the connection string keeps
    /// working exactly as it always has.
    /// </summary>
    [Fact]
    public void TranslateKeepsAFlatKeyFlat()
    {
        var translated = AppEnvironmentVariables.Translate(
            new Hashtable { ["AppDbConString"] = "Host=db;Database=techieblog" });

        Assert.Equal("Host=db;Database=techieblog", translated["AppDbConString"]);
    }

    /// <summary>
    /// A variable that is not part of the contract is ignored. Without this the provider would
    /// publish the whole process environment — every CI token and every PATH — as configuration.
    /// </summary>
    [Fact]
    public void TranslateIgnoresUnmappedVariables()
    {
        var translated = AppEnvironmentVariables.Translate(
            new Hashtable { ["PATH"] = "/usr/bin", ["SomeCiToken"] = "secret" });

        Assert.Empty(translated);
    }

    /// <summary>
    /// A declared-but-empty variable is treated as absent. Publishing it would shadow the JSON value
    /// with nothing, which is how "configured but blank" becomes a silent misconfiguration.
    /// </summary>
    [Fact]
    public void TranslateTreatsABlankValueAsAbsent()
    {
        var translated = AppEnvironmentVariables.Translate(
            new Hashtable { ["SeqUrl"] = "   ", ["UploadsPath"] = string.Empty });

        Assert.Empty(translated);
    }

    /// <summary>
    /// End to end through a real configuration builder: the provider is added last, so a PascalCase
    /// variable overrides the JSON value of the same setting.
    /// </summary>
    [Fact]
    public void ProviderOverridesEarlierSources()
    {
        Environment.SetEnvironmentVariable("UploadsPath", "/app/uploads");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Uploads:Path"] = "wwwroot/uploads" })
                .AddAppEnvironmentVariables()
                .Build();

            Assert.Equal("/app/uploads", configuration["Uploads:Path"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("UploadsPath", null);
        }
    }

    /// <summary>
    /// With nothing set in the environment the provider contributes nothing, so a developer machine
    /// is unaffected by its presence.
    /// </summary>
    [Fact]
    public void ProviderContributesNothingWhenTheEnvironmentIsClean()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Uploads:Path"] = "wwwroot/uploads" })
            .AddAppEnvironmentVariables()
            .Build();

        Assert.Equal("wwwroot/uploads", configuration["Uploads:Path"]);
    }
}
