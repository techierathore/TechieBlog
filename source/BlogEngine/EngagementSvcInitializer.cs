using BlogEngine.Common;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using BlogModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BlogEngine;

/// <summary>
/// Registers the anonymous-engagement services: captcha, spam screening and persisted
/// double opt-in email verification.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the registrations for [REQ-FN-022], [REQ-FN-023],
/// [REQ-FN-048] and [REQ-FN-049] in one place instead of scattering them through
/// <see cref="BlogSvcInitializer"/>.</para>
///
/// <para><b>Code Flow:</b> <see cref="BlogSvcInitializer.Initialize"/> calls
/// <see cref="AddEngagementServices"/> as its last step, after the comment and rating
/// repositories those services depend on are already registered.</para>
///
/// <para><b>Dependencies:</b> An in-memory cache (added here if the host has not added one) and
/// the PostgreSQL connection string.</para>
///
/// <para><b>The captcha is registered as a decorator, and that is the security guarantee.</b>
/// <c>CaptchaSvc</c> — the unlimited implementation — is registered as its own concrete type, and
/// <b>nothing resolves it directly</b>. The <see cref="ICaptchaService"/> interface, which is the
/// only thing any caller ever asks for, resolves to <c>RateLimitedCaptchaSvc</c> wrapping it. That
/// is what makes "every captcha call is rate limited" a structural property rather than a
/// convention someone has to remember: there is no registration through which a page could obtain
/// an unlimited captcha, even by accident. Anyone tempted to register <c>ICaptchaService</c>
/// straight onto <c>CaptchaSvc</c> — to "simplify", or in a test host — is removing REQ-NFR-024
/// from the entire application.</para>
///
/// <para><b>Not registered here (or anywhere):</b> <c>SvcTokenRepo</c>, deleted under REQ-FN-052.
/// It had never appeared in this container or in <see cref="BlogSvcInitializer"/>, which is why it
/// could be removed without a single call site changing.</para>
///
/// <para><b>Usage:</b> <see cref="IVerificationEmailSender"/> is registered with
/// <c>TryAdd</c> semantics, so a host that registers a real SMTP-backed sender BEFORE calling
/// this method keeps it - the logging fallback only fills a gap, it never overwrites.</para>
/// </remarks>
public static class EngagementSvcInitializer
{
    /// <summary>
    /// Adds every service behind anonymous comments and ratings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <see cref="Services.CaptchaSvc"/> is a SINGLETON because the
    /// expected answers live in its cache and must outlive a single request, and so is
    /// <see cref="Common.ICaptchaRateLimiter"/> because its counters must outlive a circuit. The
    /// registered <see cref="Services.ICaptchaService"/> is the rate-limited decorator, which is
    /// SCOPED so it can carry a per-circuit client key (REQ-NFR-024). Everything else is
    /// transient, matching the repository lifetimes already used in this codebase.</para>
    /// <para><b>Flow:</b> cache, captcha, verification repositories, sender, services.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied service collection.</para>
    /// </remarks>
    /// <param name="services">The application's service collection.</param>
    /// <param name="dbConnectionString">PostgreSQL connection string (config key <c>AppDbConString</c>).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static void AddEngagementServices(IServiceCollection services, string dbConnectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Holds captcha answers server-side for a few minutes. Safe to call twice.
        services.AddMemoryCache();

        // Self-hosted captcha (REQ-FN-049) - singleton so challenges survive between requests.
        services.AddSingleton<CaptchaSvc>();

        // Per-client caps on captcha issuance and captcha failures (REQ-NFR-024).
        // The limiter is a SINGLETON so its counters outlive a circuit; the client-key provider is
        // SCOPED because in Blazor Server a scope is a circuit, which is what makes its fallback
        // key stable for one connection. ICaptchaService therefore resolves to the rate-limited
        // decorator over the singleton CaptchaSvc, so no caller can reach an unlimited captcha.
        services.AddHttpContextAccessor();
        services.AddSingleton<ICaptchaRateLimiter>(x => new CaptchaRateLimiter(
            CaptchaRateLimitOptions.FromConfiguration(x.GetService<IConfiguration>()),
            x.GetRequiredService<ILogger<CaptchaRateLimiter>>()));
        services.AddScoped<ICaptchaClientKeyProvider>(x => new CaptchaClientKeyProvider(
            x.GetService<IHttpContextAccessor>()));
        services.AddScoped<ICaptchaService>(x => new RateLimitedCaptchaSvc(
            x.GetRequiredService<CaptchaSvc>(),
            x.GetRequiredService<ICaptchaRateLimiter>(),
            x.GetRequiredService<ICaptchaClientKeyProvider>(),
            x.GetRequiredService<ILogger<RateLimitedCaptchaSvc>>()));

        // Persisted double opt-in stores (REQ-FN-048).
        services.AddTransient<IEmailVerificationTokenRepo>(x => new EmailVerificationTokenRepo(dbConnectionString));
        services.AddTransient<IVerifiedEmailRepo>(x => new VerifiedEmailRepo(dbConnectionString));

        // Development fallback only - a real SMTP-backed sender (REQ-FN-033) wins over this.
        services.TryAddTransient<IVerificationEmailSender, LoggingVerificationEmailSender>();

        services.AddTransient<IEmailVerificationService, EmailVerificationSvc>();

        // Spam screening for anonymous comments (REQ-FN-022).
        services.AddTransient<ICommentSpamGuard, CommentSpamGuard>();
    }
}
