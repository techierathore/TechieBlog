using BlogModels;
using Microsoft.Extensions.Logging;
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
    /// original id, subscription date and unsubscribe token; anything else is inserted. A missing
    /// name defaults to the local part of the address, so the greeting in a newsletter is never
    /// blank.</para>
    /// <para><b>Flow:</b> require an address → normalise → validate the format → branch on the
    /// existing row → insert.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>Subscriber</c> row, or updates one row's active
    /// flag. Sends no email — the caller decides whether confirmation is required.</para>
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

            // Reactivate inactive subscription
            if (existing != null && !existing.IsActive)
            {
                subscriberRepo.UpdateStatus(existing.SubscriberId, true);
                existing.IsActive = true;
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

            // Reactivate inactive subscription
            if (existing != null && !existing.IsActive)
            {
                await subscriberRepo.UpdateStatusAsync(existing.SubscriberId, true, cancellationToken).ConfigureAwait(false);
                existing.IsActive = true;
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
