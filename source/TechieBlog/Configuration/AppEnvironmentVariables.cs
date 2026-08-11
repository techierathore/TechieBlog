using Microsoft.Extensions.Configuration;
using System.Collections;

namespace TechieBlog.Configuration;

/// <summary>
/// Translates the PascalCase environment variables the coding standards mandate into the
/// <c>:</c>-nested configuration paths the application actually reads.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The coding standards' "Environment Variables" rule says
/// <c>TechieBlogBaseUrl</c>, not <c>TECHIEBLOG_BASE_URL</c> and not <c>TechieBlog__BaseUrl</c>, and
/// requires "a custom configuration provider mapping PascalCase env vars → <c>:</c>-nested config
/// paths". <b>No such provider existed</b> — the host relied entirely on the framework's default
/// provider, whose only nesting convention is the double underscore the standard forbids. This type
/// is that missing provider.</para>
///
/// <para><b>Why the mapping is an explicit table rather than an algorithm.</b>
/// <c>SiteSettingsBaseUrl</c> cannot be split back into <c>SiteSettings:BaseUrl</c> by any rule that
/// also turns <c>AppDbConString</c> into a single flat key — PascalCase throws the separator away, so
/// the inverse is genuinely ambiguous. An explicit table is therefore the only honest
/// implementation, and it buys something a clever algorithm could not: <see cref="Map"/> IS the
/// deployment contract. Anything a container may set appears here, spelled exactly once, and the
/// unit tests assert against this table rather than against a copy in a document.</para>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> calls <see cref="AddAppEnvironmentVariables"/> on the
/// bootstrap builder (so the logger sees the same values) and again on
/// <c>builder.Configuration</c> after <c>WebApplication.CreateBuilder</c>. The provider walks the
/// process environment once at load time, keeps only the names in <see cref="Map"/>, and republishes
/// each one under its nested path.</para>
///
/// <para><b>The framework's own provider is left in place, deliberately.</b> Both conventions
/// therefore work: <c>SiteSettingsBaseUrl</c> (the standard) and <c>SiteSettings__BaseUrl</c> (the
/// framework default, already documented in <c>appsettings.json</c> and used by existing
/// deployments). This provider is added LAST, so where both are set the PascalCase name wins. Keys
/// that are already flat — <c>AppDbConString</c>, <c>JwtSigningKey</c>, <c>AppEncryptionKey</c> —
/// need no translation at all and are listed in <see cref="Map"/> as identity entries so the
/// contract is complete in one place.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfigurationBuilder"/> only.</para>
///
/// <para><b>Usage:</b> Add a new deployment setting by adding one row to <see cref="Map"/>. Reading
/// it stays <c>configuration["Section:Key"]</c> everywhere in app code — the standard's ban on
/// <see cref="Environment.GetEnvironmentVariable(string)"/> in application code is exactly why the
/// single call to the environment lives inside this provider.</para>
/// </remarks>
public static class AppEnvironmentVariables
{
    /// <summary>
    /// Every PascalCase environment variable the container may set, and the configuration path it
    /// is published under.
    /// </summary>
    /// <remarks>
    /// <para>Ordered the way a deployment thinks about them: identity and secrets, then the two
    /// settings the startup gate refuses to start without, then logging, then storage. An identity
    /// entry (key equal to value) documents a setting that needs no translation but is still part of
    /// the container contract.</para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Database. This project's connection-string key is a FLAT PascalCase name, not
            // ConnectionStrings:Default — see the note on ConnectionStringsDefault below.
            ["AppDbConString"] = "AppDbConString",
            ["ConnectionStringsDefault"] = "ConnectionStrings:Default",

            // Cryptographic secrets (REQ-NFR-027). Flat keys already; listed so the contract is whole.
            ["JwtSigningKey"] = "JwtSigningKey",
            ["AppEncryptionKey"] = "AppEncryptionKey",

            // The two settings DeploymentConfiguration.Enforce refuses to start without (REQ-NFR-030).
            ["SiteSettingsBaseUrl"] = "SiteSettings:BaseUrl",
            ["AnalyticsVisitorSalt"] = "Analytics:VisitorSalt",

            // Centralised logging (REQ-NFR-013).
            ["SeqUrl"] = "Seq:Url",
            ["SeqApiKey"] = "Seq:ApiKey",

            // Rolling file sink (REQ-NFR-029). Off in a container, where the file would land in an
            // ephemeral layer and Docker already captures stdout.
            ["LogFileEnabled"] = "LogFile:Enabled",
            ["LogFilePath"] = "LogFile:Path",
            ["LogFileSizeLimitBytes"] = "LogFile:SizeLimitBytes",
            ["LogFileRetainedFileCountLimit"] = "LogFile:RetainedFileCountLimit",
            ["LogFileShared"] = "LogFile:Shared",

            // Uploaded media on a mounted host path (REQ-FN-025).
            ["UploadsPath"] = "Uploads:Path"
        };

    /// <summary>
    /// Adds the PascalCase environment-variable provider to a configuration builder.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Added last so a PascalCase name overrides both the JSON files
    /// and the framework's <c>__</c> form of the same setting.</para>
    /// <para><b>Flow:</b> append the source → the builder loads it on the next build.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="builder"/>; a
    /// <c>ConfigurationManager</c> rebuilds itself immediately.</para>
    /// </remarks>
    /// <param name="builder">The builder to extend.</param>
    /// <returns><paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
    public static IConfigurationBuilder AddAppEnvironmentVariables(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(new AppEnvironmentVariablesSource());
        return builder;
    }

    /// <summary>
    /// Projects a set of environment variables onto their configuration paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only names present in <see cref="Map"/> are emitted, so an
    /// unrelated variable — <c>PATH</c>, <c>HOME</c>, a CI token — can never become application
    /// configuration. A variable set to an empty or whitespace-only string is treated as absent:
    /// publishing it would shadow the JSON value with nothing, which is how "the setting is
    /// configured but blank" turns into a silent misconfiguration.</para>
    /// <para><b>Flow:</b> walk the supplied entries → keep the mapped names → emit the nested
    /// paths.</para>
    /// <para><b>Side Effects:</b> None; pure. Kept separate from the provider so the translation is
    /// unit-testable without touching the real process environment.</para>
    /// </remarks>
    /// <param name="environment">Environment entries, as returned by
    /// <see cref="Environment.GetEnvironmentVariables()"/>.</param>
    /// <returns>Configuration data keyed by nested path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="environment"/> is <c>null</c>.</exception>
    public static IDictionary<string, string?> Translate(IDictionary environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in environment)
        {
            var name = entry.Key as string;
            if (name == null || !Map.TryGetValue(name, out var configurationPath))
            {
                continue;
            }

            var value = entry.Value as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            data[configurationPath] = value;
        }

        return data;
    }
}

/// <summary>
/// Configuration source for <see cref="AppEnvironmentVariables"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the provider into a configuration builder. Split from the static
/// helper only because <see cref="IConfigurationSource"/> must be an instance type.</para>
/// <para><b>Usage:</b> Added through
/// <see cref="AppEnvironmentVariables.AddAppEnvironmentVariables"/>; not intended to be constructed
/// directly outside a test.</para>
/// </remarks>
public sealed class AppEnvironmentVariablesSource : IConfigurationSource
{
    /// <summary>
    /// Builds the provider.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="builder">The builder requesting the provider.</param>
    /// <returns>The provider instance.</returns>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new AppEnvironmentVariablesProvider();
    }
}

/// <summary>
/// Reads the process environment once and publishes the mapped PascalCase names.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This class is the ONE place in the solution allowed to call
/// <see cref="Environment.GetEnvironmentVariables()"/>. The coding standards ban that call in
/// application code precisely so it can live here instead, behind
/// <c>IConfiguration["Section:Key"]</c>.</para>
/// <para><b>Code Flow:</b> <see cref="Load"/> runs at build time and on an explicit reload; the
/// translation itself is <see cref="AppEnvironmentVariables.Translate"/>.</para>
/// <para><b>Usage:</b> A container that changes a variable must be restarted, exactly as it must for
/// the framework's own environment provider.</para>
/// </remarks>
public sealed class AppEnvironmentVariablesProvider : ConfigurationProvider
{
    /// <summary>
    /// Loads the mapped variables from the process environment.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Replaces the provider's data wholesale, so a reload cannot leave
    /// a stale value behind for a variable that has been removed.</para>
    /// <para><b>Flow:</b> read the environment → translate → publish.</para>
    /// <para><b>Side Effects:</b> Replaces <see cref="ConfigurationProvider.Data"/>.</para>
    /// </remarks>
    public override void Load()
    {
        Data = AppEnvironmentVariables.Translate(Environment.GetEnvironmentVariables());
    }
}
