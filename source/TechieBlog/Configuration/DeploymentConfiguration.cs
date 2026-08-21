using BlogEngine.Services;
using Microsoft.Extensions.Configuration;

namespace TechieBlog.Configuration;

/// <summary>
/// Startup gate for the two settings that fail SILENTLY in a real deployment (REQ-NFR-030).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>SiteSettings:BaseUrl</c> and <c>Analytics:VisitorSalt</c> share a nasty
/// property: getting them wrong raises no error anywhere, and the damage is only visible from
/// outside the process.</para>
/// <list type="number">
///   <item><b><c>SiteSettings:BaseUrl</c></b> is read ONCE, at construction, by <c>NewsletterSvc</c>,
///   <c>EmailVerificationSvc</c>, <c>SitemapSvc</c> and <c>RssFeedSvc</c>. Left at the development
///   value, every unsubscribe link in every newsletter a real deployment sends points at
///   <c>localhost</c> — a mailing nobody outside the server can opt out of, which is a
///   CAN-SPAM/GDPR problem rather than a cosmetic one. No exception is thrown and no log line is
///   written; the mail simply goes out wrong.</item>
///   <item><b><c>Analytics:VisitorSalt</c></b> is the salt in
///   <c>SHA-256(salt | ip | userAgent)</c>. Unset, <c>PostViewTracker</c> logs one warning and uses a
///   built-in constant that is in this repository — and an IP hash with a KNOWN salt is invertible
///   by brute force over the whole IPv4 space in seconds, so the "pseudonymous" visitor digest
///   described in that class's own privacy documentation becomes a plain record of who read what.
///   It is also effectively write-once: rotating it makes every stored digest stop matching, so
///   unique-view counts jump and de-duplication restarts.</item>
/// </list>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> calls <see cref="Enforce"/> immediately after
/// <c>AppSecrets.Initialise</c> and before any service is registered — early enough that a
/// misconfigured deployment never serves a request. <see cref="Inspect"/> does the deciding and is
/// pure, so the rules are unit-testable without a host.</para>
///
/// <para><b>The Development split, and why it is not a loophole.</b> "Fail fast" must not make the
/// repository un-runnable on a developer's machine or in the smoke harness, both of which run with
/// <c>ASPNETCORE_ENVIRONMENT=Development</c> and neither of which sends mail or needs a real salt.
/// So the rule is asymmetric BY DESIGN: <b>any environment other than Development throws and the
/// host does not start</b>; Development logs one loud, actionable warning naming each setting and
/// how to supply it. The asymmetry is safe because the value that would leak — the salt — only has
/// to be secret where real visitors are hashed, and the URL only has to be right where real mail is
/// sent. Both of those are, by definition, not Development.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> and
/// <see cref="PostViewTracker.DefaultVisitorSalt"/>, referenced rather than copied so "the built-in
/// development salt" has exactly one definition.</para>
///
/// <para><b>Usage:</b> Supply both through user secrets or environment configuration — never
/// through a committed <c>appsettings.json</c>, per the coding standards' Security rule:</para>
/// <code>
/// dotnet user-secrets set "SiteSettings:BaseUrl" "https://blog.example.com"
/// dotnet user-secrets set "Analytics:VisitorSalt" "&lt;32+ random characters&gt;"
/// </code>
/// </remarks>
public static class DeploymentConfiguration
{
    /// <summary>
    /// Configuration path of the public base URL every mailed link is built from.
    /// </summary>
    public const string BaseUrlPath = "SiteSettings:BaseUrl";

    /// <summary>
    /// Configuration path of the salt folded into every stored visitor digest.
    /// </summary>
    public const string VisitorSaltPath = "Analytics:VisitorSalt";

    /// <summary>
    /// Environment name whose failures are downgraded to a warning.
    /// </summary>
    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>
    /// Shortest visitor salt accepted, in characters.
    /// </summary>
    /// <remarks>
    /// A short salt is brute-forceable alongside the address itself, which would defeat the whole
    /// point of salting. Thirty-two characters is the same floor the JWT signing key uses.
    /// </remarks>
    public const int MinimumVisitorSaltLength = 32;

    /// <summary>
    /// Base URLs that identify a value nobody has changed from a development default.
    /// </summary>
    /// <remarks>
    /// Matched on the host and scheme rather than exactly, so <c>http://localhost:5404</c> — the
    /// smoke harness — is caught as readily as the <c>https://localhost:5001</c> that shipped in
    /// <c>appsettings.json</c>. Any loopback address is a development default by definition: a
    /// deployment that mails a loopback link is broken whichever port it uses.
    /// </remarks>
    public static readonly string[] DevelopmentHostNames =
    {
        "localhost",
        "127.0.0.1",
        "[::1]",
        "0.0.0.0"
    };

    /// <summary>
    /// Examines the configuration and describes every deployment setting that is unusable.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A setting is unusable when it is absent, blank, still at a
    /// development default, or — for the salt — too short to be worth salting with. Each problem is
    /// returned as a complete sentence naming the setting and the fix, because this text is the
    /// only thing an operator staring at a crashed container will see.</para>
    /// <para><b>Flow:</b> check the base URL → check the salt → return the list.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="configuration">The configuration to examine.</param>
    /// <returns>One message per problem; empty when the deployment is configured correctly.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <c>null</c>.</exception>
    public static IReadOnlyList<string> Inspect(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        var baseUrl = configuration[BaseUrlPath];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            problems.Add(
                $"{BaseUrlPath} is not configured. Every unsubscribe, verification and feed link is "
                + "built from it, so mail sent without it cannot be acted on. Set it to the site's "
                + $"public origin, for example: dotnet user-secrets set \"{BaseUrlPath}\" "
                + "\"https://blog.example.com\" (or supply SiteSettings__BaseUrl in the environment).");
        }
        else if (IsDevelopmentBaseUrl(baseUrl))
        {
            problems.Add(
                $"{BaseUrlPath} is still the development value '{baseUrl.Trim()}'. Every unsubscribe "
                + "link mailed from this deployment would point at the server's own loopback address "
                + "and nobody could opt out. Set it to the site's public origin, for example: "
                + $"dotnet user-secrets set \"{BaseUrlPath}\" \"https://blog.example.com\".");
        }

        var visitorSalt = configuration[VisitorSaltPath];
        if (string.IsNullOrWhiteSpace(visitorSalt))
        {
            problems.Add(
                $"{VisitorSaltPath} is not configured, so visitor digests would fall back to the "
                + "built-in development salt. That salt is published in this repository, and an IP "
                + "hash with a known salt is reversible across the whole IPv4 address space, so the "
                + "stored digests would identify readers rather than pseudonymise them. Set at least "
                + $"{MinimumVisitorSaltLength} random characters, for example: dotnet user-secrets "
                + $"set \"{VisitorSaltPath}\" \"$(openssl rand -hex 32)\". TREAT IT AS WRITE-ONCE — "
                + "rotating it resets every stored visitor pseudonym.");
        }
        else if (string.Equals(visitorSalt.Trim(), PostViewTracker.DefaultVisitorSalt, StringComparison.Ordinal))
        {
            problems.Add(
                $"{VisitorSaltPath} is set to the built-in development salt, which is published in "
                + "this repository and therefore provides no protection at all — an IP hash with a "
                + "known salt is reversible across the whole IPv4 address space. Replace it with at "
                + $"least {MinimumVisitorSaltLength} random characters supplied through user secrets "
                + "or the environment.");
        }
        else if (visitorSalt.Trim().Length < MinimumVisitorSaltLength)
        {
            problems.Add(
                $"{VisitorSaltPath} is only {visitorSalt.Trim().Length} characters long. A salt short "
                + "enough to brute-force alongside the address defeats the purpose of salting; supply "
                + $"at least {MinimumVisitorSaltLength} random characters.");
        }

        return problems;
    }

    /// <summary>
    /// Applies the deployment rules: throw outside Development, warn inside it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Outside Development an unusable setting is a deployment defect
    /// that must stop the host, because both failure modes are invisible once traffic starts. Inside
    /// Development the same findings are reported as ONE warning — one, not one per setting, so it
    /// cannot be scrolled past — and the host carries on, which is what keeps the repository
    /// runnable and the smoke harness green.</para>
    /// <para><b>Flow:</b> inspect → return when clean → warn in Development → otherwise throw.</para>
    /// <para><b>Side Effects:</b> Invokes <paramref name="warn"/> in Development; throws elsewhere.</para>
    /// </remarks>
    /// <param name="configuration">The configuration to validate.</param>
    /// <param name="environmentName">The value of <c>ASPNETCORE_ENVIRONMENT</c>.</param>
    /// <param name="warn">Receives the combined warning text when running in Development.</param>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A setting is unusable and the environment is not Development.
    /// </exception>
    public static void Enforce(IConfiguration configuration, string environmentName, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(warn);

        var problems = Inspect(configuration);
        if (problems.Count == 0)
        {
            return;
        }

        var detail = string.Join(Environment.NewLine + "  * ", problems);

        if (string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase))
        {
            warn(
                "REQ-NFR-030 DEPLOYMENT CONFIGURATION INCOMPLETE — tolerated because this host is "
                + "running in Development. A non-Development host REFUSES TO START with these "
                + $"findings:{Environment.NewLine}  * {detail}");
            return;
        }

        throw new InvalidOperationException(
            $"Deployment configuration is invalid for environment '{environmentName}'. "
            + $"Fix the following and restart:{Environment.NewLine}  * {detail}");
    }

    /// <summary>
    /// Reports whether a base URL points at the local machine.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A malformed value is treated as a development default too — it
    /// is certainly not a usable public origin, and reporting it through the same message gives the
    /// operator the fix rather than a parse error.</para>
    /// <para><b>Flow:</b> parse → compare the host against <see cref="DevelopmentHostNames"/>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <returns><c>true</c> when the URL is a development default or unusable.</returns>
    private static bool IsDevelopmentBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed))
        {
            return true;
        }

        return DevelopmentHostNames.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase);
    }
}
