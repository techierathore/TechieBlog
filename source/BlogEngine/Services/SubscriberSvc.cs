using BlogModels;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace BlogEngine.Services;

/// <summary>
/// Newsletter subscriber list management: subscribe, unsubscribe, admin listing and export.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the subscriber <i>roster</i> — who is on the list and whether they
/// are active. It is deliberately narrower than <c>NewsletterSvc</c>, which owns issues and
/// dispatch; the two meet only at the <c>Subscriber</c> table.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="Subscribe"/> normalises and validates the address, then either reactivates a
///     lapsed row, refuses a duplicate, or inserts a new subscriber.</item>
///   <item><see cref="Unsubscribe"/> and <see cref="UpdateSubscriberStatus"/> flip
///     <c>IsActive</c>; rows are never deleted, so an address that resubscribes keeps its original
///     <c>SubscribedOn</c> and its unsubscribe token.</item>
///   <item>The read methods back the admin subscriber list, the audience estimate on the newsletter
///     composer and the CSV export.</item>
/// </list>
///
/// <para><b>Soft delete is the rule here.</b> Nothing in this class removes a row. Unsubscribing
/// sets <c>IsActive = false</c>, which is what <c>INewsletterRepo.GetRecipientsAsync</c> filters
/// on, so an unsubscribed address stops receiving mail without the site losing its record that the
/// address once opted out. Hard-deleting instead would let the same address be re-added silently by
/// a later import.</para>
///
/// <para><b>[REQ-FN-059] 2026-08-10 — consent is recorded, not overwritten.</b> Flipping the
/// mailability bit is no longer the whole story. A withdrawal goes through
/// <see cref="UnsubscribeByTokenAsync"/>, which stamps <c>UnsubscribedOn</c> and burns the link
/// while leaving <c>ConfirmedOn</c> alone, so the row afterwards proves both that the address
/// consented and that it later withdrew — the previous behaviour erased the first fact and left an
/// opted-out address indistinguishable from one that never confirmed. A re-consent goes through
/// <c>ISubscriberRepo.RecordConsentAsync</c>, which stamps the new consent instant and re-issues the
/// unsubscribe link. The administrative paths here still use the plain status flip; the database
/// trigger <c>TrgSubscriberConsentChange</c> stamps the consent columns for them, so no write
/// anywhere in the solution can erase the record even if it predates this requirement.</para>
///
/// <para><b>[REQ-FN-060] 2026-08-11 — the mailed link is now scoped to one issue.</b> REQ-FN-059
/// built the token lifecycle but nothing called <see cref="IssueUnsubscribeTokenAsync"/>, so the
/// single row-level token still shipped in every issue an address received. A send now calls
/// <see cref="IssueTokenForNewsletterAsync"/> per recipient, which ADDS a row to the
/// <c>UnsubscribeToken</c> table keyed to that (subscriber, issue) pair. Two consequences to hold
/// on to: a subscriber legitimately holds several live tokens at once, and a link in an OLDER issue
/// still unsubscribes them — refusing that click would be a compliance failure, not a tightened
/// credential. Rotation therefore happens on re-consent only, enforced by
/// <c>IsTokenSuperseded</c>. The full reasoning, including why the literal "the newest send
/// supersedes the older ones" reading was rejected, is in the header of
/// <c>027-PerIssueUnsubscribeToken.sql</c>.</para>
///
/// <para><b>Two subscription paths exist, and only one of them is double opt-in.</b>
/// <see cref="SubscribePendingAsync"/> is the double opt-in path and is the ONLY one an
/// anonymous, publicly reachable form may use: it writes <c>IsConfirmed = false</c> and mails a
/// confirmation link through <see cref="IEmailVerificationService"/>. <see cref="Subscribe"/>
/// auto-confirms (<c>IsConfirmed = true</c> at insert) and is therefore <b>administrative only</b>
/// — an auto-confirming public form lets one visitor subscribe a stranger's address without their
/// consent.</para>
///
/// <para><b>[REQ-UI-056] 2026-08-09 — the sidebar form used to call <see cref="Subscribe"/>.</b>
/// <c>BlogSidebar</c> is rendered by <c>MainLayout</c> on every public page, so the single
/// anonymous surface with the widest reach in the whole site was writing confirmed subscribers with
/// no captcha and no consent check. It now goes through <see cref="SubscribePendingAsync"/> behind
/// a <c>CaptchaWidget</c>, exactly like <c>NewsletterSubscribeCard</c>.</para>
///
/// <para><b>Error contract:</b> every mutation returns <c>Result</c> — an expected failure such as
/// "already subscribed" or "not found" is <i>returned</i>, and an unexpected one is caught, logged
/// with the address, and converted into a safe message. Reads never throw: a failure logs and
/// yields an empty sequence, because a broken sidebar must not take a blog page down with it.</para>
///
/// <para><b>Dependencies:</b> <see cref="ISubscriberRepo"/> and
/// <see cref="ILogger{TCategoryName}"/>. Synchronous throughout — the repository surface it sits on
/// has not been converted (REQ-NFR-026), which is why <c>BlogSidebar</c> wraps
/// <see cref="Subscribe"/> in <c>Task.Run</c> rather than awaiting it.</para>
///
/// <para><b>Async surface (REQ-NFR-026 stage 3):</b> every member below exists twice — a legacy
/// blocking member and an <c>…Async</c> twin carrying a <see cref="CancellationToken"/> as its last
/// parameter. Each twin sits immediately after the synchronous member it mirrors, routes every read
/// and write through the matching <c>ISubscriberRepo…Async</c> member with the token flowed in and
/// <c>ConfigureAwait(false)</c>, and preserves that member's behaviour exactly — same filters, same
/// ordering, same swallow-and-log on reads, same <c>Result</c> failure strings on writes.
/// <b>Call the async member.</b> The synchronous surface is retained only until the last Blazor call
/// site migrates and is <b>pending deletion in stage 4</b>; nothing new should be written against
/// it. <c>Result</c>/<c>Result&lt;T&gt;</c> is unchanged by the conversion — it simply travels
/// inside a task, because <c>Result</c> models the expected-failure axis and <c>Task</c> models the
/// completion axis.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>.
/// <see cref="Subscribe"/> and <see cref="Unsubscribe"/> are reachable anonymously from public
/// pages; every other member is administrative and its callers sit behind
/// <c>AppPolicies.AdminOnly</c>. This class performs <b>no</b> authorization of its own — the
/// calling page is responsible for the policy check, so never expose
/// <see cref="GetAllSubscribers"/> or <see cref="GetSubscribersForExport"/> from an unauthenticated
/// surface: both return every subscriber's email address.</para>
/// </remarks>
public class SubscriberSvc
{
    private readonly ISubscriberRepo subscriberRepo;
    private readonly ILogger<SubscriberSvc> logger;
    private readonly IEmailVerificationService emailVerification;

    /// <summary>
    /// Accepts anything with a single unspaced local part, an <c>@</c> and a dotted domain.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive. A strict RFC 5322 pattern rejects valid addresses and still cannot
    /// prove an address is deliverable; the confirmation mail is the only real test. The pattern has
    /// no nested quantifier, so it cannot backtrack catastrophically on hostile input.
    /// </remarks>
    private static readonly Regex EmailRegex = new Regex(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// How long an unsubscribe token stays valid once its issuance has been recorded: 400 days.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately long. An unsubscribe link must still work for someone who opens a mail
    /// weeks or months later — a short expiry recreates the very harm this requirement is about, a
    /// mailing nobody can get off. 400 days is longer than any plausible "I finally read that
    /// email" gap for a periodic newsletter, and it matches the 400-day cap browsers now impose on
    /// cookie lifetimes, which is the closest widely accepted precedent for how long a bearer
    /// credential held by a user should stay live.</para>
    /// <para>The security comes from ROTATION and BURNING, not from the clock: a redeemed token is
    /// burned in the same statement that records the withdrawal, any re-consent re-issues the token,
    /// and <see cref="IssueUnsubscribeTokenAsync"/> lets the send path re-issue per mailing so a
    /// live subscriber's clock restarts with every issue. The expiry is the backstop for a token
    /// that stopped being re-issued because nothing is being sent to that address any more.</para>
    /// <para>A token whose <c>UnsubscribeTokenIssuedOn</c> is <c>null</c> — every token that
    /// predates REQ-FN-059 — is NOT subject to this and never expires. Those tokens are already
    /// sitting in delivered mail and cannot be recalled, so expiring them could only strand a
    /// subscriber. They are still burnable, and the first rotation puts them on the clock.</para>
    /// </remarks>
    public const int UnsubscribeTokenLifetimeDays = 400;

    /// <summary>
    /// The single wording returned for a blank, unknown, burned or expired unsubscribe token.
    /// </summary>
    /// <remarks>
    /// One message for every failure mode on purpose: a caller probing the route must not be able
    /// to tell a token that belongs to a real subscriber from one that does not, so the four cases
    /// are indistinguishable from outside. They are distinguished in the log, which is not
    /// reachable by the prober.
    /// </remarks>
    private const string InvalidLinkMessage = "This unsubscribe link is not valid.";

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriberSvc"/> class.
    /// </summary>
    /// <param name="subscriberRepo">Subscriber data access.</param>
    /// <param name="logger">Logger for subscription lifecycle and failures.</param>
    /// <param name="emailVerification">
    /// Issues and mails the double opt-in confirmation link. Optional so the administrative
    /// members of this class stay constructible without the engagement stack; when it is absent
    /// <see cref="SubscribePendingAsync"/> refuses rather than falling back to the auto-confirming
    /// path, because silently degrading a consent gate is worse than a temporary outage.
    /// </param>
    public SubscriberSvc(
        ISubscriberRepo subscriberRepo,
        ILogger<SubscriberSvc> logger,
        IEmailVerificationService emailVerification = null)
    {
        this.subscriberRepo = subscriberRepo;
        this.logger = logger;
        this.emailVerification = emailVerification;
    }

    /// <summary>
    /// Registers an address as a PENDING subscriber and mails its confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The double opt-in signup path (BRD-98 / REQ-FN-048), and the
    /// only path a publicly reachable form may use. The row is written with
    /// <c>IsConfirmed = false</c> and <c>IsActive = false</c>, so the address receives nothing at
    /// all until the mailed link is redeemed on <c>/verify/{token}</c>. An address that is already
    /// confirmed is reported as such and is never mailed a second link; an address that is still
    /// pending re-uses its EXISTING row, so a visitor who lost the first email is not duplicated in
    /// the subscriber table.</para>
    /// <para><b>Flow:</b> require the verification service → normalise → validate the format →
    /// short-circuit an already-confirmed address → reuse or insert the pending row → issue the
    /// token and send the mail.</para>
    /// <para><b>Side Effects:</b> May insert one <c>Subscriber</c> row; issues one verification
    /// token row and sends one email.</para>
    /// <para><b>Caller responsibility:</b> this method performs no human check. Every anonymous
    /// caller must have satisfied a <c>CaptchaWidget</c> challenge first — see REQ-UI-056.</para>
    /// </remarks>
    /// <param name="email">Address to subscribe. Case and surrounding whitespace are ignored.</param>
    /// <param name="name">Display name; defaults to the address's local part when omitted.</param>
    /// <param name="cancellationToken">
    /// Cancels the subscriber lookup. Added by REQ-NFR-026 stage 3 with a default, so every existing
    /// caller keeps compiling unchanged. Cancellation is only observed up to the point the
    /// verification token is issued — once <see cref="IEmailVerificationService.IssueAsync"/> is
    /// entered the mail is on its way and the operation runs to completion.
    /// </param>
    /// <returns>
    /// Success carrying <c>true</c> when a confirmation link was sent, or <c>false</c> when the
    /// address was already confirmed and nothing was sent; a failure when the address is missing,
    /// malformed, or the row or the token could not be written.
    /// </returns>
    public async Task<Result<bool>> SubscribePendingAsync(
        string email, string name = "", CancellationToken cancellationToken = default)
    {
        if (emailVerification == null)
        {
            logger.LogError("Double opt-in subscribe attempted without IEmailVerificationService");
            return Result<bool>.Failure("Subscriptions are unavailable right now. Please try again later.");
        }

        if (string.IsNullOrWhiteSpace(email))
            return Result<bool>.Failure("Email address is required.");

        email = email.Trim().ToLower();

        if (!IsValidEmail(email))
            return Result<bool>.Failure("Please enter a valid email address.");

        try
        {
            var existing = await subscriberRepo.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.IsConfirmed)
                return Result<bool>.Success(false);

            var subscriberId = existing?.SubscriberId ?? InsertPending(email, name);
            var issued = await emailVerification
                .IssueAsync(email, DeriveName(email, name), EmailVerificationPurpose.Subscription, subscriberId, string.Empty)
                .ConfigureAwait(false);

            if (issued.IsFailure)
                return Result<bool>.Failure(issued.ErrorMessage ?? "We could not start the subscription. Please try again.");

            logger.LogInformation("Pending subscription created for {Email}", email);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start double opt-in subscription for {Email}", email);
            return Result<bool>.Failure("We could not complete the subscription. Please try again.");
        }
    }

    /// <summary>
    /// Writes the unconfirmed subscriber row that the confirmation link will promote.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>IsConfirmed</c> and <c>IsActive</c> are both false and stay
    /// false until the mailed link is redeemed — that is what makes the opt-in double.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>Subscriber</c> row.</para>
    /// </remarks>
    /// <param name="email">The already-normalised address.</param>
    /// <param name="name">Display name supplied by the caller; may be empty.</param>
    /// <returns>The new subscriber id.</returns>
    private long InsertPending(string email, string name)
    {
        return subscriberRepo.InsertToGetId(new Subscriber
        {
            Email = email,
            Name = DeriveName(email, name),
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = false,
            IsActive = false,
            Preferences = string.Empty
        });
    }

    /// <summary>
    /// Falls back to the local part of the address when no display name was supplied.
    /// </summary>
    /// <param name="email">The already-normalised address.</param>
    /// <param name="name">Display name supplied by the caller; may be empty.</param>
    /// <returns>A non-empty display name.</returns>
    private static string DeriveName(string email, string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }

    /// <summary>
    /// Adds an address to the subscriber list, or reactivates it if it lapsed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address is lower-cased and trimmed first, so
    /// <c>Foo@Example.com</c> and <c>foo@example.com</c> are one subscriber rather than two. Three
    /// outcomes follow: an already-active address is <i>refused</i> (a duplicate row would mean two
    /// copies of every issue); an inactive address is <i>reactivated</i> in place, keeping its
    /// original id and subscription date but receiving a FRESH unsubscribe token; anything else is
    /// inserted. A missing name defaults to the local part of the address, so the greeting in a
    /// newsletter is never blank.</para>
    /// <para><b>Flow:</b> require an address → normalise → validate the format → branch on the
    /// existing row → insert.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>Subscriber</c> row, or records a re-consent on one
    /// (stamping <c>ConfirmedOn</c> and rotating the unsubscribe token). Sends no email — the
    /// caller decides whether confirmation is required.</para>
    /// <para><b>Behaviourally identical to <see cref="SubscribeAsync"/> [REQ-FN-059].</b> The two
    /// overloads must write the same consent record; the reactivation branch here calls
    /// <c>ISubscriberRepo.RecordConsent</c>, the blocking twin of the <c>RecordConsentAsync</c> the
    /// async overload uses, over the same SQL constant. Before 2026-08-10 this overload flipped
    /// <c>IsConfirmed</c> alone, so which overload a caller happened to reach decided whether the
    /// consent instant was stamped and whether the subscriber was left holding the burned token
    /// that had removed them.</para>
    /// <para><b>ADMINISTRATIVE ONLY — never call this from an anonymous surface.</b> The new row is
    /// written with <c>IsConfirmed = true</c>, so this path is single opt-in and a visitor can
    /// subscribe an address they do not own. It also discloses, through its distinct "already
    /// subscribed" message, whether a given address is on the list. Every public form must use
    /// <see cref="SubscribePendingAsync"/> behind a captcha instead. [REQ-UI-056]</para>
    /// </remarks>
    /// <param name="email">Address to subscribe. Case and surrounding whitespace are ignored.</param>
    /// <param name="name">Display name; defaults to the address's local part when omitted.</param>
    /// <returns>
    /// Success carrying the new or reactivated subscriber; a failure when the address is missing,
    /// malformed, already active, or could not be written.
    /// </returns>
    public Result<Subscriber> Subscribe(string email, string name = "")
    {
        // Validate email
        if (string.IsNullOrWhiteSpace(email))
            return Result<Subscriber>.Failure("Email address is required.");

        email = email.Trim().ToLower();

        if (!IsValidEmail(email))
            return Result<Subscriber>.Failure("Please enter a valid email address.");

        // Check for existing subscription
        if (subscriberRepo.EmailExists(email))
        {
            var existing = subscriberRepo.GetByEmail(email);
            if (existing != null && existing.IsActive)
                return Result<Subscriber>.Failure("This email is already subscribed.");

            // Reactivate inactive subscription. [REQ-FN-059] This is a re-consent, so it goes
            // through RecordConsent rather than the bare status flip, exactly as SubscribeAsync
            // does: the consent instant is stamped and the subscriber is handed a fresh, unburned
            // unsubscribe link, because the one they were holding may well be the burned link that
            // took them off the list. Until 2026-08-10 this overload did only UpdateStatus(id,
            // true), so the SAME reactivation wrote a DIFFERENT consent record depending on which
            // overload the caller reached — and the address came back holding a token the
            // repository had already refused once.
            if (existing != null && !existing.IsActive)
            {
                var reissuedToken = GenerateUnsubscribeToken();
                subscriberRepo.RecordConsent(existing.SubscriberId, reissuedToken);

                existing.IsActive = true;
                existing.IsConfirmed = true;
                existing.ConfirmedOn = DateTime.UtcNow;
                existing.UnsubscribeToken = reissuedToken;
                existing.UnsubscribeTokenIssuedOn = existing.ConfirmedOn;
                existing.UnsubscribeTokenUsedOn = null;
                logger.LogInformation("Reactivated subscription for {Email}", email);
                return Result<Subscriber>.Success(existing);
            }
        }

        try
        {
            var subscriber = new Subscriber
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim(),
                SubscribedOn = DateTime.UtcNow,
                IsConfirmed = true, // Auto-confirm for now (no double opt-in)
                IsActive = true
            };

            subscriber.SubscriberId = subscriberRepo.InsertToGetId(subscriber);
            logger.LogInformation("New subscription created for {Email}", email);
            return Result<Subscriber>.Success(subscriber);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create subscription for {Email}", email);
            return Result<Subscriber>.Failure("Failed to subscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Adds an address to the subscriber list, or reactivates it if it lapsed, without blocking the
    /// calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="Subscribe"/> (REQ-NFR-026 stage 3)
    /// and behaviourally identical to it. The address is lower-cased and trimmed first, so
    /// <c>Foo@Example.com</c> and <c>foo@example.com</c> are one subscriber rather than two. Three
    /// outcomes follow: an already-active address is <i>refused</i> (a duplicate row would mean two
    /// copies of every issue); an inactive address is <i>reactivated</i> in place, keeping its
    /// original id, subscription date and unsubscribe token; anything else is inserted. A missing
    /// name defaults to the local part of the address, so the greeting in a newsletter is never
    /// blank.</para>
    /// <para><b>Flow:</b> require an address → normalise → validate the format → await the existence
    /// check and branch on the existing row → await the insert.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>Subscriber</c> row, or updates one row's active flag.
    /// Sends no email — the caller decides whether confirmation is required.</para>
    /// <para><b>Error contract:</b> matches the synchronous twin exactly, including the fact that the
    /// existence check and the reactivation sit <i>outside</i> the <c>try</c>: a data-access failure
    /// there faults the returned task rather than coming back as a failed <c>Result</c>. Only the
    /// insert is converted into "Failed to subscribe. Please try again later.".</para>
    /// <para><b>ADMINISTRATIVE ONLY — never call this from an anonymous surface.</b> The new row is
    /// written with <c>IsConfirmed = true</c>, so this path is single opt-in and a visitor can
    /// subscribe an address they do not own. Every public form must use
    /// <see cref="SubscribePendingAsync"/> behind a captcha instead. [REQ-UI-056]</para>
    /// </remarks>
    /// <param name="email">Address to subscribe. Case and surrounding whitespace are ignored.</param>
    /// <param name="name">Display name; defaults to the address's local part when omitted.</param>
    /// <param name="cancellationToken">Cancels the lookups and the insert.</param>
    /// <returns>
    /// Success carrying the new or reactivated subscriber; a failure when the address is missing,
    /// malformed, already active, or could not be written.
    /// </returns>
    public async Task<Result<Subscriber>> SubscribeAsync(
        string email, string name = "", CancellationToken cancellationToken = default)
    {
        // Validate email
        if (string.IsNullOrWhiteSpace(email))
            return Result<Subscriber>.Failure("Email address is required.");

        email = email.Trim().ToLower();

        if (!IsValidEmail(email))
            return Result<Subscriber>.Failure("Please enter a valid email address.");

        // Check for existing subscription
        if (await subscriberRepo.EmailExistsAsync(email, cancellationToken).ConfigureAwait(false))
        {
            var existing = await subscriberRepo.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
            if (existing != null && existing.IsActive)
                return Result<Subscriber>.Failure("This email is already subscribed.");

            // Reactivate inactive subscription. [REQ-FN-059] This is a re-consent, so it goes
            // through RecordConsentAsync rather than the bare status flip: the consent instant is
            // stamped and the subscriber is handed a fresh, unburned unsubscribe link, because the
            // one they were holding may well be the burned link that took them off the list.
            if (existing != null && !existing.IsActive)
            {
                var reissuedToken = GenerateUnsubscribeToken();
                await subscriberRepo
                    .RecordConsentAsync(existing.SubscriberId, reissuedToken, cancellationToken)
                    .ConfigureAwait(false);

                existing.IsActive = true;
                existing.IsConfirmed = true;
                existing.ConfirmedOn = DateTime.UtcNow;
                existing.UnsubscribeToken = reissuedToken;
                existing.UnsubscribeTokenIssuedOn = existing.ConfirmedOn;
                existing.UnsubscribeTokenUsedOn = null;
                logger.LogInformation("Reactivated subscription for {Email}", email);
                return Result<Subscriber>.Success(existing);
            }
        }

        try
        {
            var subscriber = new Subscriber
            {
                Email = email,
                Name = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name.Trim(),
                SubscribedOn = DateTime.UtcNow,
                IsConfirmed = true, // Auto-confirm for now (no double opt-in)
                IsActive = true
            };

            subscriber.SubscriberId = await subscriberRepo
                .InsertToGetIdAsync(subscriber, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("New subscription created for {Email}", email);
            return Result<Subscriber>.Success(subscriber);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create subscription for {Email}", email);
            return Result<Subscriber>.Failure("Failed to subscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Marks an address inactive so it stops receiving newsletters.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A soft opt-out — the row survives with <c>IsActive = false</c>,
    /// which is the flag the dispatch audience query filters on. An unknown address is reported as a
    /// failure rather than treated as success, because the admin screen that calls this needs to
    /// distinguish "removed" from "was never there".</para>
    /// <para><b>Flow:</b> require an address → look it up → flip the active flag.</para>
    /// <para><b>Side Effects:</b> Updates one <c>Subscriber</c> row; logs the opt-out.</para>
    /// <para><b>Note:</b> this is the <i>administrative</i> opt-out, keyed by address. The
    /// one-click unsubscribe link in a newsletter goes through
    /// <c>NewsletterSvc.UnsubscribeAsync</c> instead, which resolves an opaque token so the URL
    /// never carries an email address and cannot be used to unsubscribe a stranger by guessing
    /// theirs.</para>
    /// </remarks>
    /// <param name="email">Address to deactivate; surrounding whitespace is ignored.</param>
    /// <returns>Success, or a failure when the address is missing, unknown or could not be saved.</returns>
    public Result Unsubscribe(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure("Email address is required.");

        var subscriber = subscriberRepo.GetByEmail(email.Trim());
        if (subscriber == null)
            return Result.Failure("Email not found in subscribers list.");

        try
        {
            subscriberRepo.UpdateStatus(subscriber.SubscriberId, false);
            logger.LogInformation("Unsubscribed {Email}", email);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unsubscribe {Email}", email);
            return Result.Failure("Failed to unsubscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Marks an address inactive so it stops receiving newsletters, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="Unsubscribe"/> (REQ-NFR-026
    /// stage 3) and behaviourally identical to it. A soft opt-out — the row survives with
    /// <c>IsActive = false</c>, which is the flag the dispatch audience query filters on. An unknown
    /// address is reported as a failure rather than treated as success, because the admin screen that
    /// calls this needs to distinguish "removed" from "was never there". Note that the lookup uses
    /// the trimmed address <i>without</i> lower-casing it, exactly as the synchronous twin does; the
    /// repository matches case-insensitively, so the two agree.</para>
    /// <para><b>Flow:</b> require an address → await the lookup → await the flag flip.</para>
    /// <para><b>Side Effects:</b> Updates one <c>Subscriber</c> row; logs the opt-out.</para>
    /// <para><b>Error contract:</b> matches the synchronous twin exactly, including the fact that the
    /// lookup sits <i>outside</i> the <c>try</c>: a data-access failure there faults the returned task
    /// rather than coming back as a failed <c>Result</c>.</para>
    /// <para><b>Note:</b> this is the <i>administrative</i> opt-out, keyed by address. The one-click
    /// unsubscribe link in a newsletter goes through <c>NewsletterSvc.UnsubscribeAsync</c> instead,
    /// which resolves an opaque token so the URL never carries an email address.</para>
    /// </remarks>
    /// <param name="email">Address to deactivate; surrounding whitespace is ignored.</param>
    /// <param name="cancellationToken">Cancels the lookup and the update.</param>
    /// <returns>Success, or a failure when the address is missing, unknown or could not be saved.</returns>
    public async Task<Result> UnsubscribeAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure("Email address is required.");

        var subscriber = await subscriberRepo.GetByEmailAsync(email.Trim(), cancellationToken).ConfigureAwait(false);
        if (subscriber == null)
            return Result.Failure("Email not found in subscribers list.");

        try
        {
            await subscriberRepo.UpdateStatusAsync(subscriber.SubscriberId, false, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Unsubscribed {Email}", email);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to unsubscribe {Email}", email);
            return Result.Failure("Failed to unsubscribe. Please try again later.");
        }
    }

    /// <summary>
    /// Honours a one-click unsubscribe link, recording the withdrawal instead of erasing consent.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-059] The consent-aware replacement for the token path
    /// that used to live on <c>NewsletterSvc.UnsubscribeAsync</c>, which flipped
    /// <c>IsConfirmed</c> to false and so destroyed the record that the address had ever opted in.
    /// A redemption now writes <c>UnsubscribedOn</c> alongside the flag and leaves
    /// <c>ConfirmedOn</c> in place, so the row afterwards shows both when consent was given and when
    /// it was withdrawn — and an opted-out address is no longer indistinguishable from one that
    /// never confirmed. The token is burned in the same statement, so one link performs at most one
    /// state change.</para>
    /// <para><b>The token is the authorisation.</b> The link is followed from a mail client with no
    /// session, so an unguessable per-subscriber token stands in for an identity. That is why the
    /// URL carries a token and not an address: an address would let anyone unsubscribe a
    /// stranger.</para>
    /// <para><b>[REQ-FN-060] Two token stores, tried in that order.</b> A link mailed since
    /// migration 027 is a PER-ISSUE token living in the <c>UnsubscribeToken</c> table, scoped to the
    /// one issue it travelled in; a link mailed before that is a ROW-LEVEL token in
    /// <c>Subscriber.UnsubscribeToken</c>. The table is consulted first and the column is the
    /// fallback, because every link already sitting in a subscriber's inbox is a row-level one and
    /// must keep working. From the caller's point of view nothing changes — same outcomes, same
    /// wording, same failure modes — and every rule below applies identically to both, because the
    /// per-issue lookup projects the token row's issuance and burn stamps onto the same three
    /// properties the row-level lookup fills.</para>
    /// <para><b>An older issue's link still works.</b> That is the deliberate consequence of the
    /// design chosen in <c>027-PerIssueUnsubscribeToken.sql</c>: a subscriber who receives issue #1
    /// and issue #2 and then clicks Unsubscribe in the OLDER mail is unsubscribed, not refused.
    /// Narrowing the credential's blast radius must not cost anyone their opt-out — refusing a
    /// genuine withdrawal is a compliance failure, not a usability wrinkle. What the newer issue
    /// does NOT do is invalidate the older token; what a re-consent does is invalidate every token
    /// issued before it, which is the rotation this requirement family promises.</para>
    /// <para><b>Flow:</b> require a token → resolve it against the per-issue table, then the
    /// row-level column → report an already-withdrawn subscriber without writing → refuse a burned
    /// token → refuse a PER-ISSUE token superseded by a later re-consent → refuse an expired token
    /// → record the withdrawal through whichever store resolved it.</para>
    /// <para><b>Side Effects:</b> On the <see cref="UnsubscribeOutcome.Unsubscribed"/> path only:
    /// one row is updated, the address stops being mailable, the withdrawal is stamped and the token
    /// is burned. Logs the subscriber id — never the address and never the token.</para>
    /// <para><b>Compatibility with the flow smoked under REQ-FN-032:</b> unchanged from the caller's
    /// point of view. The same three outcomes come back in the same <c>Result</c> shape, an unknown
    /// token still fails with the same vague wording, and re-opening a link that already did its
    /// work still reports <see cref="UnsubscribeOutcome.AlreadyUnsubscribed"/> rather than an error.
    /// One case behaves better than before: a subscriber who was still PENDING when they followed
    /// the link now has that decision RECORDED as a withdrawal, so a future re-confirmation sweep
    /// leaves them alone, where previously it was silently ignored.</para>
    /// <para><b>Result contract:</b> a blank token, an unknown token, a burned token, a superseded
    /// token and an expired token all fail with <see cref="InvalidLinkMessage"/>, so the route
    /// cannot be used to test whether a guessed token belongs to a real subscriber. An infrastructure failure is reported
    /// differently on purpose — telling a reader their link is invalid when the database is down
    /// would send them away for good.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The opaque token from the unsubscribe URL.</param>
    /// <param name="cancellationToken">Cancels the lookup and the update.</param>
    /// <returns>
    /// Success carrying <see cref="UnsubscribeOutcome.Unsubscribed"/> when this request opted the
    /// address out, or <see cref="UnsubscribeOutcome.AlreadyUnsubscribed"/> when it was already off
    /// the list; a failure when the token is blank, unknown, burned, expired, or could not be
    /// processed.
    /// </returns>
    public async Task<Result<UnsubscribeOutcome>> UnsubscribeByTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unsubscribeToken))
            return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);

        try
        {
            var token = unsubscribeToken.Trim();

            // [REQ-FN-060] The per-issue table first, the row-level column second. A token mailed
            // since migration 027 lives in the table; every link already sitting in someone's inbox
            // from an earlier issue is a row-level one, and must keep resolving.
            var subscriber = await subscriberRepo
                .GetByNewsletterTokenAsync(token, cancellationToken)
                .ConfigureAwait(false);
            var isIssueScoped = subscriber != null;

            subscriber ??= await subscriberRepo
                .GetByUnsubscribeTokenAsync(token, cancellationToken)
                .ConfigureAwait(false);

            if (subscriber == null)
            {
                logger.LogWarning("An unsubscribe link was opened with a token that matches no subscriber");
                return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);
            }

            // Already withdrawn. Report it without writing, so re-opening the same link is a no-op
            // rather than an error and the recorded withdrawal instant is not moved.
            if (subscriber.ConsentState == SubscriberConsentState.Withdrawn)
            {
                logger.LogInformation(
                    "Subscriber {SubscriberId} re-opened an unsubscribe link and was already opted out",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Success(UnsubscribeOutcome.AlreadyUnsubscribed);
            }

            // Burned, yet the subscriber is not withdrawn: the link was spent and a later re-consent
            // should have rotated it. A spent link must not be able to opt an address out a second
            // time after its owner deliberately came back.
            if (subscriber.UnsubscribeTokenUsedOn.HasValue)
            {
                logger.LogWarning(
                    "A burned unsubscribe token was replayed for subscriber {SubscriberId}",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);
            }

            // [REQ-FN-060] Superseded by a later re-consent. PER-ISSUE TOKENS ONLY, and the
            // qualifier matters: a row-level token rotates by being PHYSICALLY OVERWRITTEN, so an
            // old one simply stops resolving and needs no staleness rule — whereas the token table
            // keeps every row, and something has to decide which of a subscriber's several live
            // tokens the current consent covers. Applying the rule to the row-level path as well
            // would also refuse a perfectly good token whenever its issuance happened to predate a
            // consent instant, which is representable and legitimate on that column.
            if (isIssueScoped && IsTokenSuperseded(subscriber))
            {
                logger.LogWarning(
                    "An unsubscribe token predating the current consent was presented for subscriber {SubscriberId}",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);
            }

            if (IsTokenExpired(subscriber))
            {
                logger.LogWarning(
                    "An expired unsubscribe token was presented for subscriber {SubscriberId}",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);
            }

            // A per-issue redemption burns the token ROW; a row-level one burns the COLUMN. Both
            // record the withdrawal in the same statement, and both leave ConfirmedOn alone.
            var recorded = isIssueScoped
                ? await subscriberRepo
                    .RedeemNewsletterTokenAsync(subscriber.UnsubscribeToken, cancellationToken)
                    .ConfigureAwait(false)
                : await subscriberRepo
                    .RecordWithdrawalAsync(subscriber.SubscriberId, cancellationToken)
                    .ConfigureAwait(false);

            if (!recorded)
            {
                // A concurrent redemption of the same link won the guarded UPDATE. The address is
                // off the list either way, so this is a success, not a failure.
                logger.LogInformation(
                    "A concurrent redemption had already burned the link for subscriber {SubscriberId}",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Success(UnsubscribeOutcome.AlreadyUnsubscribed);
            }

            logger.LogInformation(
                "Subscriber {SubscriberId} withdrew consent via an unsubscribe link", subscriber.SubscriberId);
            return Result<UnsubscribeOutcome>.Success(UnsubscribeOutcome.Unsubscribed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process an unsubscribe request");
            return Result<UnsubscribeOutcome>.Failure(
                "The unsubscribe request could not be processed just now. Please try the link again shortly.");
        }
    }

    /// <summary>
    /// Issues a fresh unsubscribe token for a subscriber and returns it for use in one mailing.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-059] This is what makes the token scoped rather than
    /// permanent. Calling it immediately before composing a message means the link in that message
    /// is the only live one, the link in every earlier issue stops working, and the 400-day expiry
    /// clock restarts — so a subscriber who is still being mailed can never be holding a link that
    /// has aged out.</para>
    /// <para><b>Flow:</b> generate 256 bits of cryptographic randomness → install it, stamping the
    /// issuance and clearing any burn → hand the caller the value to put in the URL.</para>
    /// <para><b>Side Effects:</b> The subscriber's previous unsubscribe link stops resolving. The
    /// caller MUST use the returned value rather than a token it read earlier, or it will mail a
    /// dead link. Consent columns are untouched — re-issuing a link is not a consent decision.</para>
    /// <para><b>[REQ-FN-060] This is NOT what the send path calls.</b> It replaces the subscriber's
    /// single row-level token, which means every earlier issue's link dies the moment a newer issue
    /// goes out — and a subscriber who opens last week's mail on Saturday would be told their link
    /// is invalid while they are still on the list. A send therefore calls
    /// <see cref="IssueTokenForNewsletterAsync"/> instead, which ADDS a token scoped to that issue
    /// and leaves the earlier ones working. This member survives for administrative re-issuance —
    /// "give this subscriber one fresh link and kill the old one", which is a deliberate act with a
    /// human behind it, not something that should happen silently on a schedule.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber the message is addressed to.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Success carrying the new token; a failure when the subscriber is unknown or the
    /// token could not be written. A caller that cannot get a fresh token should fall back to the
    /// stored one rather than skipping the unsubscribe link.</returns>
    public async Task<Result<string>> IssueUnsubscribeTokenAsync(
        long subscriberId, CancellationToken cancellationToken = default)
    {
        try
        {
            var token = GenerateUnsubscribeToken();
            var rotated = await subscriberRepo
                .RotateUnsubscribeTokenAsync(subscriberId, token, cancellationToken)
                .ConfigureAwait(false);

            if (!rotated)
                return Result<string>.Failure("Could not issue an unsubscribe token.");

            return Result<string>.Success(token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to issue an unsubscribe token for subscriber {Id}", subscriberId);
            return Result<string>.Failure("Could not issue an unsubscribe token.");
        }
    }

    /// <summary>
    /// Issues an unsubscribe token scoped to ONE newsletter issue, for use in that issue's message.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-060] This is what makes a mailed unsubscribe link a
    /// per-send credential instead of a permanent one. It is called once per recipient per send,
    /// immediately before the message is composed, and the token it returns is the only one that
    /// issue carries. The credential's blast radius is therefore one issue rather than "every mail
    /// ever sent to this address" — the property this requirement is about.</para>
    /// <para><b>It ADDS a token; it does not rotate one.</b> Every token this subscriber was issued
    /// for an earlier issue stays live until it is used, expires, or is superseded by a re-consent.
    /// That is a deliberate choice over the literal "the newest send invalidates the older ones"
    /// reading, and the reasoning is recorded in full in the header of
    /// <c>027-PerIssueUnsubscribeToken.sql</c>: a subscriber who receives two issues and then clicks
    /// Unsubscribe in the OLDER one must be unsubscribed. Silently refusing that click is a
    /// CAN-SPAM-shaped failure, strictly worse than the over-broad credential it would be fixing.
    /// Rotation still happens, on re-consent only — the event this requirement's own title
    /// names.</para>
    /// <para><b>Flow:</b> generate 256 bits of cryptographic randomness → record it against the
    /// (subscriber, issue) pair → hand the caller the value to put in the URL.</para>
    /// <para><b>Side Effects:</b> Adds one <c>UnsubscribeToken</c> row. Nothing on the subscriber
    /// row changes: the row-level token, the consent record and the mailability bit are all
    /// untouched, because issuing a link is not a consent decision.</para>
    /// <para><b>Failure is not fatal to the send.</b> A caller that cannot get a per-issue token
    /// must fall back to the subscriber's row-level token rather than mailing a message with no
    /// unsubscribe link — a coarser credential is a smaller harm than a mailing nobody can get off.
    /// <c>NewsletterSvc.DeliverAsync</c> does exactly that.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber the message is addressed to.</param>
    /// <param name="newsletterId">The issue being sent; the scope of the returned token.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>Success carrying the new per-issue token; a failure when the identifiers are not
    /// usable or the token could not be recorded.</returns>
    public async Task<Result<string>> IssueTokenForNewsletterAsync(
        long subscriberId, long newsletterId, CancellationToken cancellationToken = default)
    {
        if (subscriberId <= 0 || newsletterId <= 0)
            return Result<string>.Failure("Could not issue an unsubscribe token.");

        try
        {
            var token = GenerateUnsubscribeToken();
            var issued = await subscriberRepo
                .IssueTokenForNewsletterAsync(subscriberId, newsletterId, token, cancellationToken)
                .ConfigureAwait(false);

            if (!issued)
                return Result<string>.Failure("Could not issue an unsubscribe token.");

            return Result<string>.Success(token);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to issue a per-issue unsubscribe token for subscriber {Id} on newsletter {NewsletterId}",
                subscriberId,
                newsletterId);
            return Result<string>.Failure("Could not issue an unsubscribe token.");
        }
    }

    /// <summary>
    /// Generates an opaque unsubscribe token: 256 bits of cryptographic randomness, lower-case hex.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is a bearer credential, so it comes from
    /// <see cref="RandomNumberGenerator"/> rather than from <c>md5(random())</c> as the SQL column
    /// default does — a value produced by a predictable PRNG is guessable in principle, and this one
    /// authorises a state change on a stranger's row. 64 hex characters is exactly the width of the
    /// <c>VARCHAR(64)</c> column.</para>
    /// <para><b>Side Effects:</b> None; pure apart from consuming entropy.</para>
    /// </remarks>
    /// <returns>A 64-character lower-case hexadecimal token.</returns>
    public static string GenerateUnsubscribeToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Tests whether an unsubscribe token has outlived <see cref="UnsubscribeTokenLifetimeDays"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A token with no recorded issuance never expires. That is not an
    /// oversight: those tokens were mailed before REQ-FN-059 and cannot be recalled, so putting them
    /// on a clock could only ever leave a subscriber with no working way off the list — the exact
    /// failure this requirement exists to prevent. They are still burnable, and the first rotation
    /// puts them on the clock.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber whose token was presented.</param>
    /// <returns>True when the token carries an issuance stamp older than the lifetime.</returns>
    private static bool IsTokenExpired(Subscriber subscriber)
    {
        if (!subscriber.UnsubscribeTokenIssuedOn.HasValue)
            return false;

        return subscriber.UnsubscribeTokenIssuedOn.Value.AddDays(UnsubscribeTokenLifetimeDays)
               < DateTime.UtcNow;
    }

    /// <summary>
    /// Tests whether a token was issued under a consent the subscriber has since re-given.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-060] This is how "unsubscribe tokens rotate on
    /// re-consent" is enforced once a subscriber can hold several live tokens at once. Rather than
    /// hunting down and revoking every outstanding row when someone resubscribes — a write that any
    /// future consent path could forget to make — a token is simply stale if it predates the current
    /// <c>ConfirmedOn</c>. Re-consent moves that instant forward and invalidates every token issued
    /// under the previous consent in one stroke, on the read side, where no writer can bypass
    /// it.</para>
    /// <para><b>Why it matters:</b> without this rule an old link that leaked out of an archived
    /// mailbox could opt an address out again <i>after</i> its owner deliberately came back. That is
    /// the same harm REQ-FN-059 burned tokens to prevent, reappearing through the extra tokens
    /// REQ-FN-060 hands out.</para>
    /// <para><b>Applies to per-issue tokens only.</b> A row-level token rotates by being physically
    /// overwritten in its column, so an old one stops resolving on its own and needs no staleness
    /// rule; the token TABLE keeps every row, which is what creates the question this answers.
    /// Applying it to the row-level path as well would refuse a live token whenever its issuance
    /// merely predated a consent instant — representable and legitimate on that column, and
    /// refusing it would strand a subscriber holding a working link.</para>
    /// <para>The comparison is strict, so a token issued in the same instant as the consent — which
    /// is exactly what <c>RecordConsentAsync</c> and <c>TrgSubscriberConsentChange</c> write — is
    /// live rather than born stale. A token with no recorded issuance is never superseded, for the
    /// reason <see cref="IsTokenExpired"/> gives: it cannot be recalled from delivered mail, so
    /// refusing it could only strand a subscriber.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber whose token was presented, carrying that token's
    /// issuance stamp and the subscriber's own consent instant.</param>
    /// <returns>True when the token was issued strictly before the current consent.</returns>
    private static bool IsTokenSuperseded(Subscriber subscriber)
    {
        if (!subscriber.UnsubscribeTokenIssuedOn.HasValue || !subscriber.ConfirmedOn.HasValue)
            return false;

        return subscriber.UnsubscribeTokenIssuedOn.Value < subscriber.ConfirmedOn.Value;
    }

    /// <summary>
    /// Lists every subscriber, active or not, for the admin roster.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unfiltered by design — the admin list shows opt-outs too, so an
    /// administrator can see that an address unsubscribed rather than being left wondering where it
    /// went.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> returns personal data (every subscriber's email address). The
    /// caller must already sit behind <c>AppPolicies.AdminOnly</c>; this method checks
    /// nothing.</para>
    /// </remarks>
    /// <returns>Every subscriber; an empty sequence if the read failed.</returns>
    public IEnumerable<Subscriber> GetAllSubscribers()
    {
        try
        {
            return subscriberRepo.GetAll();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all subscribers");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Lists every subscriber, active or not, for the admin roster, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetAllSubscribers"/> (REQ-NFR-026
    /// stage 3) and behaviourally identical to it. Unfiltered by design — the admin list shows
    /// opt-outs too, so an administrator can see that an address unsubscribed rather than being left
    /// wondering where it went.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> returns personal data (every subscriber's email address). The
    /// caller must already sit behind <c>AppPolicies.AdminOnly</c>; this method checks nothing.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every subscriber; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<Subscriber>> GetAllSubscribersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await subscriberRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all subscribers");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Lists subscribers on one side of the active/opted-out split.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs the admin list's status filter. Passing <c>true</c> gives
    /// the set that would actually receive an issue sent to everyone.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> administrative; see <see cref="GetAllSubscribers"/>.</para>
    /// </remarks>
    /// <param name="isActive">True for current subscribers, false for opt-outs.</param>
    /// <returns>The matching subscribers; an empty sequence if the read failed.</returns>
    public IEnumerable<Subscriber> GetSubscribersByStatus(bool isActive)
    {
        try
        {
            return subscriberRepo.GetByStatus(isActive);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscribers by status");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Lists subscribers on one side of the active/opted-out split, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetSubscribersByStatus"/>
    /// (REQ-NFR-026 stage 3) and behaviourally identical to it. Backs the admin list's status filter.
    /// Passing <c>true</c> gives the set that would actually receive an issue sent to everyone. The
    /// filter is applied against <c>IsConfirmed</c> in SQL, so a row with a NULL <c>IsConfirmed</c>
    /// matches neither value and the two calls do not partition the list — that is the repository's
    /// documented behaviour and is deliberately not compensated for here.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> administrative; see <see cref="GetAllSubscribersAsync"/>.</para>
    /// </remarks>
    /// <param name="isActive">True for current subscribers, false for opt-outs.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching subscribers; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<Subscriber>> GetSubscribersByStatusAsync(
        bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            return await subscriberRepo.GetByStatusAsync(isActive, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscribers by status");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Finds subscribers whose address matches a search fragment.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty query means "no filter" and returns the whole roster,
    /// which is what makes the admin search box behave sensibly when the user clears it.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> administrative; see <see cref="GetAllSubscribers"/>.</para>
    /// </remarks>
    /// <param name="query">Fragment to match against the email address; empty returns everything.</param>
    /// <returns>The matching subscribers; an empty sequence if the read failed.</returns>
    public IEnumerable<Subscriber> SearchSubscribers(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return subscriberRepo.GetAll();
            return subscriberRepo.SearchByEmail(query);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching subscribers with query: {Query}", query);
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Finds subscribers whose address matches a search fragment, without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="SearchSubscribers"/> (REQ-NFR-026
    /// stage 3) and behaviourally identical to it. An empty query means "no filter" and returns the
    /// whole roster, which is what makes the admin search box behave sensibly when the user clears
    /// it. A non-empty query goes to the repository's capped <c>ILIKE</c> search, so at most fifty
    /// rows come back.</para>
    /// <para><b>Flow:</b> branch on the blank query → await the matching repository read → log and
    /// degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> administrative; see <see cref="GetAllSubscribersAsync"/>.</para>
    /// </remarks>
    /// <param name="query">Fragment to match against the email address; empty returns everything.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching subscribers; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<Subscriber>> SearchSubscribersAsync(
        string query, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return await subscriberRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);

            return await subscriberRepo.SearchByEmailAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching subscribers with query: {Query}", query);
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Sets a subscriber's active flag from the admin roster.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The id-keyed counterpart of <see cref="Unsubscribe"/>, and the
    /// only way to put a lapsed subscriber back on the list without them acting themselves.
    /// Existence is confirmed first so a stale row id reports "not found" rather than silently
    /// updating nothing.</para>
    /// <para><b>Flow:</b> load the row → reject when absent → write the new flag.</para>
    /// <para><b>Side Effects:</b> Updates one <c>Subscriber</c> row; logs the change with the id and
    /// the new status, which is the audit trail for a re-activation.</para>
    /// <para><b>Authorization:</b> administrative; the calling page must already have enforced
    /// <c>AppPolicies.AdminOnly</c>. Re-activating an address the owner opted out of is a consent
    /// decision, so it should never be reachable anonymously.</para>
    /// </remarks>
    /// <param name="subscriberId">Identifier of the subscriber to change.</param>
    /// <param name="isActive">True to put the address back on the list, false to opt it out.</param>
    /// <returns>Success, or a failure when the subscriber is unknown or the write failed.</returns>
    public Result UpdateSubscriberStatus(long subscriberId, bool isActive)
    {
        try
        {
            var subscriber = subscriberRepo.GetSingle(subscriberId);
            if (subscriber == null)
                return Result.Failure("Subscriber not found.");

            subscriberRepo.UpdateStatus(subscriberId, isActive);
            logger.LogInformation("Updated subscriber {Id} status to {Status}", subscriberId, isActive);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating subscriber status for ID {Id}", subscriberId);
            return Result.Failure("Failed to update subscriber status.");
        }
    }

    /// <summary>
    /// Sets a subscriber's active flag from the admin roster, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="UpdateSubscriberStatus"/>
    /// (REQ-NFR-026 stage 3) and behaviourally identical to it. The id-keyed counterpart of
    /// <see cref="UnsubscribeAsync"/>, and the only way to put a lapsed subscriber back on the list
    /// without them acting themselves. Existence is confirmed first so a stale row id reports "not
    /// found" rather than silently updating nothing.</para>
    /// <para><b>Flow:</b> await the row load → reject when absent → await the flag write.</para>
    /// <para><b>Side Effects:</b> Updates one <c>Subscriber</c> row; logs the change with the id and
    /// the new status, which is the audit trail for a re-activation.</para>
    /// <para><b>Error contract:</b> matches the synchronous twin exactly — here the load <i>is</i>
    /// inside the <c>try</c>, so a failing lookup comes back as "Failed to update subscriber
    /// status." rather than faulting the task.</para>
    /// <para><b>Authorization:</b> administrative; the calling page must already have enforced
    /// <c>AppPolicies.AdminOnly</c>. Re-activating an address the owner opted out of is a consent
    /// decision, so it should never be reachable anonymously.</para>
    /// </remarks>
    /// <param name="subscriberId">Identifier of the subscriber to change.</param>
    /// <param name="isActive">True to put the address back on the list, false to opt it out.</param>
    /// <param name="cancellationToken">Cancels the lookup and the update.</param>
    /// <returns>Success, or a failure when the subscriber is unknown or the write failed.</returns>
    public async Task<Result> UpdateSubscriberStatusAsync(
        long subscriberId, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            var subscriber = await subscriberRepo.GetSingleAsync(subscriberId, cancellationToken).ConfigureAwait(false);
            if (subscriber == null)
                return Result.Failure("Subscriber not found.");

            await subscriberRepo.UpdateStatusAsync(subscriberId, isActive, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Updated subscriber {Id} status to {Status}", subscriberId, isActive);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating subscriber status for ID {Id}", subscriberId);
            return Result.Failure("Failed to update subscriber status.");
        }
    }

    /// <summary>
    /// Gets the roster head-count, split into total and currently active.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Feeds the dashboard tile and the newsletter composer's audience
    /// estimate. The estimate is advisory only — the real audience is resolved at send time by
    /// <c>INewsletterRepo.GetRecipientsAsync</c>, which also applies the selected audience
    /// filter.</para>
    /// <para><b>Side Effects:</b> None beyond logging. Two separate count queries, so the two
    /// numbers are not a consistent snapshot; treat a momentary <c>Active &gt; Total</c> skew as
    /// impossible in practice but never assert on it.</para>
    /// </remarks>
    /// <returns>Total and active subscriber counts; <c>(0, 0)</c> if the read failed.</returns>
    public (int Total, int Active) GetSubscriberStats()
    {
        try
        {
            return (subscriberRepo.GetTotalCount(), subscriberRepo.GetActiveCount());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscriber stats");
            return (0, 0);
        }
    }

    /// <summary>
    /// Gets the roster head-count, split into total and currently active, without blocking the
    /// calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetSubscriberStats"/> (REQ-NFR-026
    /// stage 3) and behaviourally identical to it. Feeds the dashboard tile and the newsletter
    /// composer's audience estimate. The estimate is advisory only — the real audience is resolved at
    /// send time by <c>INewsletterRepo.GetRecipientsAsync</c>, which also applies the selected
    /// audience filter.</para>
    /// <para><b>Flow:</b> await the total count → await the active count → return the pair, or
    /// <c>(0, 0)</c> after logging a failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging. The two counts are awaited <i>sequentially</i>
    /// — in the same order the synchronous twin evaluates them, and never concurrently, because the
    /// repository owns a single connection factory and is not safe for two overlapping reads. They
    /// are therefore still two separate queries and not a consistent snapshot; treat a momentary
    /// <c>Active &gt; Total</c> skew as impossible in practice but never assert on it.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels either count query.</param>
    /// <returns>Total and active subscriber counts; <c>(0, 0)</c> if the read failed.</returns>
    public async Task<(int Total, int Active)> GetSubscriberStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var total = await subscriberRepo.GetTotalCountAsync(cancellationToken).ConfigureAwait(false);
            var active = await subscriberRepo.GetActiveCountAsync(cancellationToken).ConfigureAwait(false);
            return (total, active);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscriber stats");
            return (0, 0);
        }
    }

    /// <summary>
    /// Gets the active subscribers for the CSV export.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Active only. An export is normally taken to seed another mail
    /// system, and carrying opted-out addresses across would resurrect an opt-out the subscriber
    /// already exercised.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> this produces a downloadable file of personal data — the highest
    /// disclosure risk in this class. The calling page must be behind
    /// <c>AppPolicies.AdminOnly</c>.</para>
    /// </remarks>
    /// <returns>Active subscribers; an empty sequence if the read failed.</returns>
    public IEnumerable<Subscriber> GetSubscribersForExport()
    {
        try
        {
            return subscriberRepo.GetActiveSubscribers();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscribers for export");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Gets the active subscribers for the CSV export, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The async twin of <see cref="GetSubscribersForExport"/>
    /// (REQ-NFR-026 stage 3) and behaviourally identical to it. Active only. An export is normally
    /// taken to seed another mail system, and carrying opted-out addresses across would resurrect an
    /// opt-out the subscriber already exercised. "Active" here is the repository's strict reading —
    /// <c>IsConfirmed</c> explicitly true — so a row with a NULL <c>IsConfirmed</c> is excluded even
    /// though the admin grid reports it as active.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade to an empty sequence on failure.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Authorization:</b> this produces a downloadable file of personal data — the highest
    /// disclosure risk in this class. The calling page must be behind
    /// <c>AppPolicies.AdminOnly</c>.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Active subscribers; an empty sequence if the read failed.</returns>
    public async Task<IEnumerable<Subscriber>> GetSubscribersForExportAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await subscriberRepo.GetActiveSubscribersAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting subscribers for export");
            return Enumerable.Empty<Subscriber>();
        }
    }

    /// <summary>
    /// Tests whether an address is shaped like an email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A shape check, not a deliverability check — see
    /// <see cref="EmailRegex"/> for why the pattern is intentionally loose.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="email">The already-normalised candidate address.</param>
    /// <returns>True when the address could plausibly be delivered to.</returns>
    private bool IsValidEmail(string email)
    {
        return !string.IsNullOrWhiteSpace(email) && EmailRegex.IsMatch(email);
    }
}
