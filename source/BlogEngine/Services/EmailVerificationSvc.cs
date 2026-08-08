using System.Security.Cryptography;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Persisted double opt-in email verification.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves that whoever typed an email address into a comment, rating or
/// subscription form can actually read that inbox. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> <see cref="IssueAsync"/> generates 256 bits of entropy, stores the
/// token with a 24-hour expiry and mails a <c>/verify/{token}</c> link.
/// <see cref="ConsumeAsync"/> redeems it through a stored function that checks and flips the
/// state in one statement, then promotes the pending row and records the address as verified.</para>
///
/// <para><b>Dependencies:</b> <see cref="IEmailVerificationTokenRepo"/> (database-backed, NOT
/// the in-memory pattern used by <c>PasswordResetTokenRepo</c>),
/// <see cref="IVerifiedEmailRepo"/>, <see cref="IBlogCommentRepo"/>,
/// <see cref="IPostRatingRepo"/>, <see cref="ISubscriberRepo"/>,
/// <see cref="IVerificationEmailSender"/> and
/// <see cref="IConfiguration"/> for <c>SiteSettings:BaseUrl</c>.</para>
///
/// <para><b>Usage:</b> All three purposes are promoted HERE, including
/// <see cref="EmailVerificationPurpose.Subscription"/>. An earlier revision left the subscriber
/// flip to "the newsletter service", but no such promotion existed anywhere, so a pending
/// subscriber could never become confirmed. Redemption and promotion now live together, which
/// is the only arrangement in which a caller cannot forget half the job. [REQ-UI-055]</para>
/// </remarks>
public class EmailVerificationSvc : IEmailVerificationService
{
    /// <summary>Bytes of entropy in a verification token.</summary>
    private const int TokenByteLength = 32;

    /// <summary>Tokens one address may be issued inside the rate-limit window.</summary>
    private const int MaximumTokensPerEmail = 5;

    /// <summary>Configuration key holding the public base URL of the site.</summary>
    private const string BaseUrlConfigKey = "SiteSettings:BaseUrl";

    /// <summary>How long a verification link stays valid.</summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    /// <summary>Width of the issue rate-limit window.</summary>
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    private readonly IEmailVerificationTokenRepo tokenRepo;
    private readonly IVerifiedEmailRepo verifiedEmailRepo;
    private readonly IBlogCommentRepo blogCommentRepo;
    private readonly IPostRatingRepo postRatingRepo;
    private readonly ISubscriberRepo subscriberRepo;
    private readonly IVerificationEmailSender emailSender;
    private readonly IConfiguration configuration;
    private readonly ISiteSettingsService siteSettingsService;
    private readonly ILogger<EmailVerificationSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailVerificationSvc"/> class.
    /// </summary>
    /// <param name="tokenRepo">Persistent token store.</param>
    /// <param name="verifiedEmailRepo">Registry of confirmed addresses.</param>
    /// <param name="blogCommentRepo">Used to promote a confirmed comment.</param>
    /// <param name="postRatingRepo">Used to promote a confirmed rating.</param>
    /// <param name="subscriberRepo">Used to promote a confirmed newsletter subscription.</param>
    /// <param name="emailSender">Delivers the confirmation link.</param>
    /// <param name="configuration">Supplies the public base URL.</param>
    /// <param name="siteSettingsService">Supplies the comment-moderation site setting [BRD-38].</param>
    /// <param name="logger">Logger for security events.</param>
    public EmailVerificationSvc(
        IEmailVerificationTokenRepo tokenRepo,
        IVerifiedEmailRepo verifiedEmailRepo,
        IBlogCommentRepo blogCommentRepo,
        IPostRatingRepo postRatingRepo,
        ISubscriberRepo subscriberRepo,
        IVerificationEmailSender emailSender,
        IConfiguration configuration,
        ISiteSettingsService siteSettingsService,
        ILogger<EmailVerificationSvc> logger)
    {
        this.tokenRepo = tokenRepo;
        this.verifiedEmailRepo = verifiedEmailRepo;
        this.blogCommentRepo = blogCommentRepo;
        this.postRatingRepo = postRatingRepo;
        this.subscriberRepo = subscriberRepo;
        this.emailSender = emailSender;
        this.configuration = configuration;
        this.siteSettingsService = siteSettingsService;
        this.logger = logger;
    }

    /// <summary>
    /// Reads whether an approved-before-display step is currently required for comments.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [BRD-38] The site setting decides. Any failure reading it is
    /// answered with <c>true</c>, which leaves the comment in the queue - the safe direction.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when a moderator must approve a comment before it is shown.</returns>
    private async Task<bool> IsModerationRequiredAsync()
    {
        try
        {
            var settings = await siteSettingsService.GetSettingsAsync().ConfigureAwait(false);
            return settings?.AreCommentsModerated ?? true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the comment-moderation setting; moderating by default");
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<Result<EmailVerificationToken>> IssueAsync(
        string email, string displayName, string purpose, long targetId, string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<EmailVerificationToken>.Failure("An email address is required.");

        if (string.IsNullOrWhiteSpace(purpose))
            return Result<EmailVerificationToken>.Failure("A verification purpose is required.");

        var isWithinLimit = await IsWithinIssueLimitAsync(email).ConfigureAwait(false);
        if (!isWithinLimit)
            return Result<EmailVerificationToken>.Failure(
                "Too many confirmation emails have been requested for this address. Please try again later.");

        try
        {
            var token = await PersistTokenAsync(email, displayName, purpose, targetId, ipAddress)
                .ConfigureAwait(false);
            await emailSender.SendVerificationEmailAsync(
                token.Email, displayName, purpose, BuildVerificationUrl(token.Token)).ConfigureAwait(false);
            logger.LogInformation(
                "Issued {Purpose} verification token {TokenId} for target {TargetId}",
                purpose, token.TokenId, targetId);
            return Result<EmailVerificationToken>.Success(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to issue a {Purpose} verification token for target {TargetId}",
                purpose, targetId);
            return Result<EmailVerificationToken>.Failure(
                "We could not send the confirmation email. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<Result<EmailVerificationToken>> ConsumeAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result<EmailVerificationToken>.Failure("This confirmation link is not valid.");

        try
        {
            var consumed = await tokenRepo.ConsumeAsync(token).ConfigureAwait(false);
            if (consumed == null)
            {
                logger.LogWarning("A verification token was rejected as unknown, used or expired");
                return Result<EmailVerificationToken>.Failure(
                    "This confirmation link has already been used or has expired.");
            }

            await PromoteTargetAsync(consumed).ConfigureAwait(false);
            await verifiedEmailRepo.RecordVerifiedAsync(consumed.Email, consumed.DisplayName)
                .ConfigureAwait(false);
            logger.LogInformation("Verification token {TokenId} consumed for {Purpose} target {TargetId}",
                consumed.TokenId, consumed.Purpose, consumed.TargetId);
            return Result<EmailVerificationToken>.Success(consumed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to consume a verification token");
            return Result<EmailVerificationToken>.Failure(
                "We could not complete the confirmation. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsAddressVerifiedAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            return await verifiedEmailRepo.IsVerifiedAsync(email).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read the verified-address registry");
            return false;
        }
    }

    /// <summary>
    /// Checks the per-address issue rate limit.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Without this, anyone could point the comment form at a
    /// stranger's inbox and have the site mail them repeatedly.</para>
    /// <para><b>Side Effects:</b> One read-only round trip.</para>
    /// </remarks>
    /// <param name="email">The address being issued to.</param>
    /// <returns>True when another token may be issued.</returns>
    private async Task<bool> IsWithinIssueLimitAsync(string email)
    {
        var since = DateTime.UtcNow.Subtract(RateLimitWindow);
        var issued = await tokenRepo.CountRecentByEmailAsync(email, since).ConfigureAwait(false);
        if (issued < MaximumTokensPerEmail)
            return true;

        logger.LogWarning("Verification token rate limit reached for an address ({Issued} in the window)", issued);
        return false;
    }

    /// <summary>
    /// Builds and stores the token row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is 256 bits of cryptographic randomness rendered
    /// base64url, so it is safe in a path segment and cannot be guessed or enumerated.</para>
    /// <para><b>Side Effects:</b> Inserts one row into <c>EmailVerificationToken</c>.</para>
    /// </remarks>
    /// <param name="email">The address to confirm.</param>
    /// <param name="displayName">The submitted display name.</param>
    /// <param name="purpose">What is being confirmed.</param>
    /// <param name="targetId">The pending row id.</param>
    /// <param name="ipAddress">The request origin.</param>
    /// <returns>The persisted token, with its generated id.</returns>
    private async Task<EmailVerificationToken> PersistTokenAsync(
        string email, string displayName, string purpose, long targetId, string? ipAddress)
    {
        var issuedOn = DateTime.UtcNow;
        var token = new EmailVerificationToken
        {
            Token = BuildTokenValue(),
            Email = email.Trim(),
            Purpose = purpose,
            TargetId = targetId,
            DisplayName = displayName,
            IssuedOn = issuedOn,
            ExpiresOn = issuedOn.Add(TokenLifetime),
            IsUsed = false,
            RequestIpAddress = ipAddress ?? string.Empty
        };

        token.TokenId = await tokenRepo.InsertTokenAsync(token).ConfigureAwait(false);
        return token;
    }

    /// <summary>
    /// Promotes the row a redeemed token was protecting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A confirmed comment enters the moderation queue - it does
    /// NOT go live. A confirmed rating starts counting towards the aggregates. A confirmed
    /// subscription flips <c>Subscriber.IsConfirmed</c>, which is what makes the address eligible
    /// to receive an issue. Every branch is idempotent, so redeeming a token that somehow reaches
    /// here twice cannot double-apply anything.</para>
    /// <para><b>Side Effects:</b> Updates at most one comment, rating or subscriber row.</para>
    /// </remarks>
    /// <param name="consumed">The redeemed token.</param>
    /// <returns>A task that completes when the promotion has been applied.</returns>
    private async Task PromoteTargetAsync(EmailVerificationToken consumed)
    {
        if (consumed.TargetId is not > 0)
            return;

        var targetId = consumed.TargetId.Value;
        if (string.Equals(consumed.Purpose, EmailVerificationPurpose.Comment, StringComparison.OrdinalIgnoreCase))
        {
            await blogCommentRepo.MarkEmailVerifiedAsync(targetId).ConfigureAwait(false);

            // MarkCommentEmailVerified always parks the comment in the queue. [BRD-38] lets the
            // owner turn moderation off, and in that case a confirmed comment must publish now -
            // otherwise it would sit in a queue nobody is watching.
            if (!await IsModerationRequiredAsync().ConfigureAwait(false))
            {
                await blogCommentRepo
                    .SetModerationStatusAsync(targetId, CommentModerationStatus.Approved)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (string.Equals(consumed.Purpose, EmailVerificationPurpose.Rating, StringComparison.OrdinalIgnoreCase))
        {
            await postRatingRepo.MarkEmailVerifiedAsync(targetId).ConfigureAwait(false);
            return;
        }

        if (string.Equals(consumed.Purpose, EmailVerificationPurpose.Subscription, StringComparison.OrdinalIgnoreCase))
        {
            PromoteSubscriber(targetId);
        }
    }

    /// <summary>
    /// Marks a pending newsletter subscriber as confirmed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subscribe form writes the row with
    /// <c>IsConfirmed = false</c> and relies on this flip; without it a subscriber could never
    /// become confirmed and would never receive an issue, so the double opt-in loop was open.
    /// A failure here must NOT fail the whole confirmation - the token is already spent and the
    /// address is already in the verified registry, so throwing would strand the visitor on an
    /// error page with no way to retry.</para>
    /// <para><b>Flow:</b> update the row, log anything that goes wrong.</para>
    /// <para><b>Side Effects:</b> Sets <c>Subscriber.IsConfirmed</c> to true for one row.</para>
    /// </remarks>
    /// <param name="subscriberId">The pending subscriber row id.</param>
    private void PromoteSubscriber(long subscriberId)
    {
        try
        {
            subscriberRepo.UpdateStatus(subscriberId, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redeemed a subscription token but could not confirm subscriber {SubscriberId}",
                subscriberId);
        }
    }

    /// <summary>
    /// Builds the absolute confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The base URL comes from configuration because the link has
    /// to work from an email client, where relative URLs are meaningless.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="tokenValue">The random token.</param>
    /// <returns>The absolute <c>/verify/{token}</c> URL.</returns>
    private string BuildVerificationUrl(string tokenValue)
    {
        var baseUrl = configuration[BaseUrlConfigKey];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogWarning("{ConfigKey} is not configured; the verification link will be relative", BaseUrlConfigKey);
            return $"/verify/{tokenValue}";
        }

        return $"{baseUrl.TrimEnd('/')}/verify/{tokenValue}";
    }

    /// <summary>
    /// Generates a URL-safe random token value.
    /// </summary>
    /// <returns>The token string.</returns>
    private static string BuildTokenValue()
    {
        var buffer = RandomNumberGenerator.GetBytes(TokenByteLength);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
