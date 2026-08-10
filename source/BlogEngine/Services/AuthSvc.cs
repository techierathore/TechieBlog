using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using BlogSvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlogEngine.Services;

/// <summary>
/// Authentication and credential-management service.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns sign-in, staff-account creation, password change and password
/// reset. Passwords are stored as PBKDF2 hashes (REQ-NFR-002), sign-in attempts are throttled
/// per account (REQ-NFR-005) and reset tokens are persisted in the database (REQ-NFR-019).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The host's <c>IAuthService</c> adapter decrypts the transport envelope and calls in.</item>
///   <item>Credential reads and writes go through <see cref="IUserCredentialRepo"/> so the stored
///     hash never travels with the profile projections.</item>
///   <item>A successful sign-in issues a JWT and records a <c>UserLogin</c> row.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IBlogUserRepo"/>, <see cref="IUserCredentialRepo"/>,
/// <see cref="IUserLoginRepository"/>, <see cref="IPasswordResetTokenRepo"/>,
/// <see cref="ILoginThrottle"/>, <see cref="IEmailService"/> and <see cref="ILogger{T}"/>.</para>
///
/// <para><b>REQ-FN-006 (BRD-1 retired / BRD-3 rev):</b> the public self-service signup path
/// (<c>AppSignUp</c> / <c>RegisterUser</c>) has been removed. Accounts are created by an
/// administrator through <see cref="CreateStaffAccountAsync"/>, which still enforces
/// <see cref="PasswordValidator"/>, as does <see cref="ResetPasswordAsync"/> (BRD-5, BRD-10).</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member of this service is asynchronous and
/// every repository call it makes goes to the repositories' <c>…Async</c> surface, so the login,
/// reset and profile paths no longer park a thread-pool thread for the whole database round trip.
/// There is deliberately no blocking twin and no <c>.Result</c> anywhere in the chain: blocking on
/// a task inside a Blazor Server circuit is a deadlock risk, and a single blocking call left in the
/// middle of the chain reinstates exactly the stall this requirement removes.</para>
///
/// <para><b>Session model, and its current limitation.</b> A successful sign-in issues a JWT
/// (<see cref="GenerateJWToken"/>) whose HMAC-SHA256 signing key comes from the secret store, never
/// from a source literal (REQ-NFR-027). <b>That signature is never verified on the way back in.</b>
/// <see cref="GetUserByTokenAsync"/> reads the subject id with
/// <c>SvcUtils.GetUserIDFromToken</c>, which calls <c>JwtSecurityTokenHandler.ReadJwtToken</c> —
/// a decode, not a validation: it checks neither the signature, nor the expiry, nor the issuer.
/// What actually makes a session valid here is the database: the exact token string must still be
/// present in <c>UserLogin</c> for that user id, which is why revocation works and why a forged
/// token does not simply walk in. The consequence to understand before changing anything in this
/// area is that <b>the JWT is a session handle, not a bearer credential</b> — its integrity comes
/// entirely from the <c>UserLogin</c> lookup, so any future code path that trusts a claim out of
/// this token (a role, an email) without re-reading the row is trusting unverified input. Closing
/// the gap means calling <c>ValidateToken</c> with the same key and asserting the claims, and is
/// tracked separately; it is stated here rather than left implicit because a reader who assumes
/// the signature is checked will write exactly that bug.</para>
///
/// <para><b>Session lifetime and renewal (REQ-FN-008, BRD-6).</b> A session has two clocks, both
/// named by <see cref="SessionPolicy"/> and both now enforced by
/// <see cref="GetUserByTokenAsync"/>: the access token's own <c>exp</c> claim, and the refresh
/// window stored on the <c>UserLogin</c> row. When the first runs out
/// <see cref="RefreshSessionAsync"/> rewrites the row with a replacement token and slides the
/// window; when the second runs out the session is over and only a sign-in creates a new one. There
/// is no separate refresh-token artefact and none should be added — REQ-FN-052 deleted the
/// <c>svctoken</c> table as dead code and <c>UserLogin</c> is the single store of interactive
/// session state.</para>
///
/// <para><b>Usage:</b> Registered as a transient service by <c>BlogSvcInitializer</c>.</para>
/// </remarks>
public class AuthSvc
{
    private readonly IBlogUserRepo userRepo;
    private readonly IUserCredentialRepo credentialRepo;
    private readonly ILogger<AuthSvc> appLogger;
    private readonly IUserLoginRepository loginRepo;
    private readonly IPasswordResetTokenRepo tokenRepo;
    private readonly ILoginThrottle loginThrottle;
    private readonly IEmailService emailService;
    private readonly ILoginLogRepo loginLogRepo;
    private readonly SessionPolicy sessionPolicy;

    /// <summary>
    /// Initialises the authentication service.
    /// </summary>
    /// <param name="userRepo">User profile repository.</param>
    /// <param name="credentialRepo">Credential repository used for hash reads and rotations.</param>
    /// <param name="userLogins">Issued-token repository.</param>
    /// <param name="tokenRepo">Persisted password-reset token repository.</param>
    /// <param name="loginLogRepo">Sign-in audit trail repository (REQ-FN-051).</param>
    /// <param name="loginThrottle">Per-account failed-login throttle.</param>
    /// <param name="emailService">Outbound email service.</param>
    /// <param name="logger">Logger for authentication events.</param>
    /// <param name="sessionPolicy">The two session durations (REQ-FN-008). Optional so a test can
    /// construct the service without one; the container always supplies the configured instance.</param>
    public AuthSvc(
        IBlogUserRepo userRepo,
        IUserCredentialRepo credentialRepo,
        IUserLoginRepository userLogins,
        IPasswordResetTokenRepo tokenRepo,
        ILoginLogRepo loginLogRepo,
        ILoginThrottle loginThrottle,
        IEmailService emailService,
        ILogger<AuthSvc> logger,
        SessionPolicy? sessionPolicy = null)
    {
        this.userRepo = userRepo;
        this.credentialRepo = credentialRepo;
        this.loginRepo = userLogins;
        this.tokenRepo = tokenRepo;
        this.loginLogRepo = loginLogRepo;
        this.loginThrottle = loginThrottle;
        this.emailService = emailService;
        this.appLogger = logger;
        this.sessionPolicy = sessionPolicy ?? SessionPolicy.Default;
    }

    /// <summary>
    /// Authenticates a user from the encrypted transport envelope supplied by the UI.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The account is throttled first (REQ-NFR-005), then the stored
    /// credential is fetched and the password verified with PBKDF2 (REQ-NFR-002). A legacy MD5 or
    /// plaintext credential still authenticates but is transparently re-hashed, so no account is
    /// stranded by the algorithm change.</para>
    /// <para><b>Flow:</b> decrypt → throttle check → load credential → verify → upgrade hash if
    /// needed → load profile → issue token.</para>
    /// <para><b>Side Effects:</b> May rewrite the stored password hash; records a
    /// <c>UserLogin</c> row; writes one <c>LoginLog</c> audit row per attempt; updates throttle
    /// counters; logs the outcome.</para>
    /// <para><b>Audit (REQ-FN-051):</b> every arm of this method — refused by the throttle, wrong
    /// password, unknown address, success — writes exactly one audit row carrying the outcome and
    /// the address that was tried, so a burst of failures against one address is visible in
    /// <c>LoginLog</c> as a run. When the address matches no account the row is written with a
    /// <c>null</c> user id and the typed address, which is what lets an investigation tell
    /// "someone guessing at a real account" apart from "someone guessing at addresses". The
    /// attempted <i>password</i> is never recorded anywhere — not in the audit row, not in the
    /// log — and must never be added.</para>
    /// <para><b>Forced password change (REQ-NFR-023):</b> a successful sign-in carries
    /// <c>AppUser.MustChangePassword</c> out to the caller, copied from the credential row by
    /// <see cref="AuthenticateAsync"/>. The flag does not block the sign-in here — the session is
    /// issued and the token is valid — so <b>enforcement is the caller's job</b>: the UI is what
    /// redirects to <c>/change-password</c>. Only <see cref="ChangePasswordAsync"/> and
    /// <see cref="ResetPasswordAsync"/> clear it; every seeded account ships with it set.</para>
    /// </remarks>
    /// <param name="loginData">Envelope carrying the encrypted email and password.</param>
    /// <param name="cancellationToken">Cancels the sign-in.</param>
    /// <returns>The encrypted user envelope on success; <c>null</c> when authentication fails.</returns>
    public async Task<SvcData?> AppLoginAsync(SvcData loginData, CancellationToken cancellationToken = default)
    {
        try
        {
            var email = AppEncrypt.DecryptText(loginData.LoginEmail)?.Trim();
            var password = AppEncrypt.DecryptText(loginData.LoginPass);
            var throttleKey = email?.ToLowerInvariant() ?? string.Empty;

            if (loginThrottle.IsBlocked(throttleKey, out var retryAfter))
            {
                appLogger.LogWarning(
                    "Login refused for {Email}: account locked for another {RetrySeconds} s",
                    email, (int)retryAfter.TotalSeconds);
                await RecordAttemptAsync(null, email, false, loginData, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var attempt = await AuthenticateAsync(email, password, cancellationToken).ConfigureAwait(false);
            if (attempt.User == null)
            {
                var failures = loginThrottle.RegisterFailure(throttleKey);
                appLogger.LogWarning("Failed login attempt {FailureCount} for {Email}", failures, email);
                await RecordAttemptAsync(attempt.CandidateUserId, email, false, loginData, cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            loginThrottle.RegisterSuccess(throttleKey);
            await RecordAttemptAsync(attempt.User.UserId, email, true, loginData, cancellationToken)
                .ConfigureAwait(false);
            return await IssueSessionAsync(attempt.User, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error during login");
            throw;
        }
    }

    /// <summary>
    /// Writes one row of the sign-in audit trail (REQ-FN-051).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The audit trail is the only evidence a brute-force attempt
    /// leaves behind, so it records the outcome as well as the attempt: <paramref name="succeeded"/>
    /// is written verbatim and the attempted address is written even when it matches no account —
    /// that combination is what makes a run of failures legible. <paramref name="userId"/> is
    /// <c>null</c> for an address with no account, which the nullable foreign key allows.</para>
    /// <para><b>Flow:</b> build the row → insert → swallow and log any failure.</para>
    /// <para><b>Side Effects:</b> Adds one <c>LoginLog</c> row.</para>
    /// <para><b>Security:</b> the attempted password is deliberately not a parameter here and must
    /// never become one. Only the address, the outcome and the client metadata are auditable.</para>
    /// <para><b>Failure policy:</b> an audit write that fails is logged as an error and does not
    /// fail the sign-in. Refusing a valid login because the audit table is unavailable would turn a
    /// logging outage into an outage of the product; the error log is the compensating signal.</para>
    /// </remarks>
    /// <param name="userId">The account the attempt resolved to, or <c>null</c> when unknown.</param>
    /// <param name="attemptedEmail">The address that was typed into the sign-in form.</param>
    /// <param name="succeeded">Whether the attempt authenticated.</param>
    /// <param name="loginData">The request envelope, carrying the best-effort client metadata.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes once the row has been attempted.</returns>
    private async Task RecordAttemptAsync(
        long? userId,
        string? attemptedEmail,
        bool succeeded,
        SvcData loginData,
        CancellationToken cancellationToken)
    {
        try
        {
            await loginLogRepo.InsertAsync(
                new LoginLog
                {
                    LoginUserId = userId,
                    AttemptedEmail = attemptedEmail ?? string.Empty,
                    Success = succeeded,
                    ClientIP = loginData.ClientIP,
                    UserAgent = loginData.ClientUserAgent,
                    LoginDateTime = DateTime.UtcNow
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            appLogger.LogError(
                ex, "Could not record the sign-in audit row for {Email} (success {Success})",
                attemptedEmail, succeeded);
        }
    }

    /// <summary>
    /// Verifies credentials and returns the authenticated user profile.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads only the credential columns for the verification, then
    /// loads the full profile once the password is known to be correct. A stale hash format is
    /// upgraded in place before the profile is returned.</para>
    /// <para><b>Flow:</b> load credential → verify → re-hash when required → load profile.</para>
    /// <para><b>Side Effects:</b> May update the stored password hash.</para>
    /// <para><b>Forced password change:</b> the profile row is not the authority on
    /// <c>MustChangePassword</c> — the credential row is, because that is what
    /// <c>UpdatePasswordHashAsync</c> writes. The flag is therefore copied from the credential onto
    /// the returned <see cref="AppUser"/> on the last line, overwriting whatever the profile
    /// projection happened to carry. Removing that copy would let a user who must change their
    /// password sail past the redirect.</para>
    /// <para><b>Audit (REQ-FN-051):</b> a refused attempt still reports the account the address
    /// resolved to, when it resolved to one, so the audit row can be attributed. A wrong password
    /// against a real account and an attempt against an address with no account are different
    /// events to an investigation, and the trail has to be able to tell them apart. The distinction
    /// is never surfaced to the caller — <see cref="LoginAttempt.User"/> is <c>null</c> either way,
    /// so the response cannot be used to enumerate registered addresses.</para>
    /// </remarks>
    /// <param name="email">The login email address.</param>
    /// <param name="password">The plaintext password supplied by the user.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The outcome of the attempt; its user is <c>null</c> when the credentials do not match.</returns>
    private async Task<LoginAttempt> AuthenticateAsync(string? email, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return LoginAttempt.Refused(null);

        var credential = await credentialRepo.GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        if (credential == null)
            return LoginAttempt.Refused(null);

        var outcome = PasswordHasher.Verify(password, credential.LoginPass);
        if (outcome == PasswordVerifyResult.Failed)
            return LoginAttempt.Refused(credential.UserId);

        if (outcome == PasswordVerifyResult.SuccessNeedsRehash)
            await UpgradeStoredHashAsync(credential, password, cancellationToken).ConfigureAwait(false);

        var user = await userRepo.GetSingleAsync(credential.UserId, cancellationToken).ConfigureAwait(false);
        if (user == null)
            return LoginAttempt.Refused(credential.UserId);

        user.MustChangePassword = credential.MustChangePassword;
        return new LoginAttempt(user, credential.UserId);
    }

    /// <summary>
    /// The result of one credential check: the authenticated user, plus the account the attempt was
    /// aimed at even when it failed.
    /// </summary>
    /// <remarks>
    /// Exists so the audit trail can attribute a failed attempt (REQ-FN-051) without a second
    /// lookup and without leaking account existence to the caller.
    /// </remarks>
    /// <param name="User">The authenticated user, or <c>null</c> when the attempt was refused.</param>
    /// <param name="CandidateUserId">The account the address resolved to, or <c>null</c> when it
    /// matched no account.</param>
    private readonly record struct LoginAttempt(AppUser? User, long? CandidateUserId)
    {
        /// <summary>
        /// Builds a refused attempt.
        /// </summary>
        /// <param name="candidateUserId">The account the address resolved to, if any.</param>
        /// <returns>An attempt carrying no authenticated user.</returns>
        public static LoginAttempt Refused(long? candidateUserId)
        {
            return new LoginAttempt(null, candidateUserId);
        }
    }

    /// <summary>
    /// Replaces a legacy or under-strength stored hash with a current PBKDF2 hash.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Runs only after the password has already been proven correct,
    /// which is the one moment the plaintext is available for re-hashing. Failure to persist the
    /// upgrade must not fail the login, so it is logged and swallowed.</para>
    /// <para><b>Flow:</b> hash → persist → log.</para>
    /// <para><b>Side Effects:</b> Updates <c>BlogUser.LoginPass</c>.</para>
    /// </remarks>
    /// <param name="credential">The credential being upgraded.</param>
    /// <param name="password">The verified plaintext password.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the upgrade has been attempted.</returns>
    private async Task UpgradeStoredHashAsync(UserCredential credential, string password, CancellationToken cancellationToken)
    {
        try
        {
            await credentialRepo.UpdatePasswordHashAsync(
                credential.UserId,
                PasswordHasher.HashPassword(password),
                credential.MustChangePassword,
                cancellationToken).ConfigureAwait(false);

            appLogger.LogInformation(
                "Password hash upgraded to {Algorithm} for user {UserId}",
                PasswordHasher.HashPrefix, credential.UserId);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Could not upgrade password hash for user {UserId}", credential.UserId);
        }
    }

    /// <summary>
    /// Issues a JWT for an authenticated user and records the login.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is stored alongside its validity window so it can be
    /// revoked, and the encrypted user envelope is what the UI persists in local storage.</para>
    /// <para><b>Flow:</b> generate token → insert <c>UserLogin</c> → serialise and encrypt.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>UserLogin</c> row.</para>
    /// <para><b>Two clocks, one origin (REQ-FN-008).</b> The row's <c>ExipryDate</c> is the end of
    /// the <i>refresh window</i> — how long <see cref="RefreshSessionAsync"/> may keep reissuing —
    /// while the token's own <c>exp</c> claim is the much shorter access-token lifetime. Both are
    /// measured from the same UTC instant stamped here. They were previously written from
    /// <c>DateTime.Today</c>, i.e. local midnight, which made the stored window depend on the host's
    /// time zone and on how late in the day the user signed in; every comparison in this service is
    /// against <see cref="DateTime.UtcNow"/>, so the write side is UTC too.</para>
    /// </remarks>
    /// <param name="user">The authenticated user.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The encrypted envelope returned to the UI.</returns>
    private async Task<SvcData> IssueSessionAsync(AppUser user, CancellationToken cancellationToken)
    {
        var jwToken = GenerateJWToken(user);
        var issuedAt = DateTime.UtcNow;
        await loginRepo.InsertAsync(
            new UserLogin
            {
                LoginToken = jwToken,
                IssueDate = issuedAt,
                LoginDate = issuedAt,
                ExipryDate = issuedAt.Add(sessionPolicy.RefreshWindow),
                TokenStatus = TokenStatus.ValidToken.ToString(),
                UserId = user.UserId
            },
            cancellationToken).ConfigureAwait(false);

        user.AccessToken = jwToken;
        user.RefreshToken = jwToken;
        return new SvcData
        {
            ComplexData = AppEncrypt.EncryptText(JsonSerializer.Serialize(user)),
            JwToken = jwToken
        };
    }

    /// <summary>
    /// Resolves the user behind a previously issued access token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token must still be recorded against the user in
    /// <c>UserLogin</c>, so a revoked token stops working even before it expires. That row is the
    /// authority on whether a session is live — not the token itself.</para>
    /// <para><b>Flow:</b> read subject id from the token → confirm it is still issued → confirm
    /// neither clock has run out → load profile → encrypt envelope.</para>
    /// <para><b>Side Effects:</b> None. This method neither refreshes the token nor extends the
    /// <c>UserLogin</c> expiry, so calling it repeatedly does not keep a session alive.</para>
    /// <para><b>Expiry is enforced here, and the caller is expected to refresh (REQ-FN-008).</b>
    /// Two clocks are checked, and either running out yields <c>null</c>: the token's own
    /// <c>exp</c> claim (the access-token lifetime) and the row's <c>ExipryDate</c> (the refresh
    /// window). Neither was consulted before this requirement landed, so a session simply never
    /// ended. The distinction matters to whoever handles the <c>null</c>: an expired <i>access
    /// token</i> is recoverable by calling <see cref="RefreshSessionAsync"/> with the same value,
    /// while an expired <i>window</i> is not and means a fresh sign-in. Callers that cannot tell
    /// the two apart should simply attempt the refresh and treat its own <c>null</c> as the end of
    /// the session — which is exactly what <c>CustomAuthStateProvider</c> does.</para>
    /// <para><b>Security — the signature is NOT verified here.</b>
    /// <c>SvcUtils.GetUserIDFromToken</c> <i>decodes</i> the JWT
    /// (<c>JwtSecurityTokenHandler.ReadJwtToken</c>); it does not validate the HMAC, the expiry or
    /// the issuer. The only thing standing between a hand-crafted token and a session is the
    /// <c>UserLogin</c> lookup on the next line, which requires the exact token string to have been
    /// issued to that user id. Two rules follow for anyone editing this method: the repository
    /// lookup must never be made conditional or cached away, and no claim carried by
    /// <paramref name="tokenData"/> — role, email, name — may be trusted. Everything returned to
    /// the caller is re-read from <c>BlogUser</c> for exactly that reason.</para>
    /// </remarks>
    /// <param name="tokenData">Envelope carrying the JWT.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <returns>The encrypted user envelope, or <c>null</c> when the token is not valid.</returns>
    public async Task<SvcData?> GetUserByTokenAsync(SvcData tokenData, CancellationToken cancellationToken = default)
    {
        var userId = SvcUtils.GetUserIDFromToken(tokenData.JwToken);
        var validatedToken = await loginRepo
            .GetUserByTokenAsync(userId, tokenData.JwToken, cancellationToken).ConfigureAwait(false);
        if (validatedToken == null)
            return null;

        if (IsRefreshWindowOver(validatedToken))
        {
            appLogger.LogInformation(
                "Session {LoginId} for user {UserId} is past its refresh window; a new sign-in is required",
                validatedToken.LoginId, userId);
            return null;
        }

        if (IsAccessTokenExpired(tokenData.JwToken))
        {
            appLogger.LogInformation(
                "Access token for user {UserId} has expired; the caller must refresh session {LoginId}",
                userId, validatedToken.LoginId);
            return null;
        }

        var validatedUser = await userRepo.GetSingleAsync(userId, cancellationToken).ConfigureAwait(false);
        if (validatedUser == null)
            return null;

        validatedUser.AccessToken = tokenData.JwToken;
        validatedUser.RefreshToken = tokenData.JwToken;
        return new SvcData
        {
            ComplexData = AppEncrypt.EncryptText(JsonSerializer.Serialize(validatedUser)),
            JwToken = tokenData.JwToken
        };
    }

    /// <summary>
    /// Renews a session whose access token has expired, without asking for the password again
    /// (REQ-FN-008, BRD-6).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The presented token must still be the token recorded against
    /// its own subject in <c>UserLogin</c> with status <c>ValidToken</c>, and that row must still
    /// be inside its refresh window. Only then is a replacement access token minted. The old token
    /// value is <b>overwritten</b> on the same row rather than a second row being inserted, which
    /// has three consequences worth stating: the replaced token stops working immediately (there is
    /// no window in which two tokens authorise the same session), a revoked or signed-out session
    /// can never be revived, and the sessions table does not grow by one row per renewal.</para>
    /// <para><b>Flow:</b> read subject id → match the row → refresh-window check → load profile →
    /// mint a replacement → rewrite the row, sliding the window → encrypt envelope.</para>
    /// <para><b>Side Effects:</b> Updates one <c>UserLogin</c> row — its token, its dates and its
    /// window — and writes one information-level log line naming the session that was renewed.</para>
    /// <para><b>Why the expired token is accepted as its own refresh token.</b> This product has no
    /// separate refresh-token artefact and deliberately does not grow one: the sibling requirement
    /// REQ-FN-052 deleted the <c>svctoken</c> table as dead code, and <c>UserLogin</c> is the only
    /// store of interactive session state. The security property a refresh token normally provides
    /// — "possession of this proves the session was not merely observed" — is provided here by the
    /// row itself: the exact string must be present, the status must be <c>ValidToken</c>, and the
    /// row is rewritten on use. What this design does <i>not</i> provide is a credential that
    /// survives the access token being stolen; that is a real limitation and the reason the refresh
    /// window is bounded rather than perpetual.</para>
    /// <para><b>The window slides.</b> Each renewal moves <c>ExipryDate</c> forward by
    /// <see cref="SessionPolicy.RefreshWindow"/> from now, so a user who keeps using the site is
    /// never signed out and one who stops is signed out a window later. There is deliberately no
    /// absolute cap on total session age; adding one means recording the original sign-in instant
    /// separately, since <c>IssueDate</c> is rewritten here.</para>
    /// <para><b>Security:</b> the same unverified-signature caveat as
    /// <see cref="GetUserByTokenAsync"/> applies. Nothing is trusted out of the presented token
    /// except the subject id used as a lookup key, and the returned profile is re-read from
    /// <c>BlogUser</c>. An unreadable token is refused rather than throwing, because this method is
    /// reached with whatever a browser happened to have in local storage.</para>
    /// </remarks>
    /// <param name="refreshData">Envelope carrying the token to redeem.</param>
    /// <param name="cancellationToken">Cancels the lookups and the update.</param>
    /// <returns>The encrypted user envelope carrying the replacement token, or <c>null</c> when the
    /// session cannot be renewed and the user must sign in again.</returns>
    public async Task<SvcData?> RefreshSessionAsync(SvcData refreshData, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshData?.JwToken))
            return null;

        long userId;
        try
        {
            userId = SvcUtils.GetUserIDFromToken(refreshData.JwToken);
        }
        catch (Exception ex)
        {
            appLogger.LogWarning(ex, "Session refresh refused: the presented value is not a readable token");
            return null;
        }

        var session = await loginRepo
            .GetUserByTokenAsync(userId, refreshData.JwToken, cancellationToken).ConfigureAwait(false);
        if (session == null)
        {
            appLogger.LogInformation(
                "Session refresh refused for user {UserId}: the token is not a live session", userId);
            return null;
        }

        if (IsRefreshWindowOver(session))
        {
            appLogger.LogInformation(
                "Session refresh refused for user {UserId}: session {LoginId} is past its refresh window",
                userId, session.LoginId);
            return null;
        }

        var sessionUser = await userRepo.GetSingleAsync(userId, cancellationToken).ConfigureAwait(false);
        if (sessionUser == null)
        {
            appLogger.LogInformation(
                "Session refresh refused for user {UserId}: the account no longer exists", userId);
            return null;
        }

        var renewedToken = await RotateSessionTokenAsync(session, sessionUser, cancellationToken)
            .ConfigureAwait(false);

        sessionUser.AccessToken = renewedToken;
        sessionUser.RefreshToken = renewedToken;
        return new SvcData
        {
            ComplexData = AppEncrypt.EncryptText(JsonSerializer.Serialize(sessionUser)),
            JwToken = renewedToken
        };
    }

    /// <summary>
    /// Mints a replacement access token and writes it over the session row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Everything time-related on the row is re-stamped from one UTC
    /// instant, and the status is re-asserted so a row can never be renewed into an ambiguous
    /// state. The update is matched on <c>LoginId</c>, so the row identity survives the rotation and
    /// an operator watching <c>userlogins</c> sees one session being renewed rather than a new
    /// session appearing.</para>
    /// <para><b>Flow:</b> mint → stamp the row → update → log.</para>
    /// <para><b>Side Effects:</b> Updates one <c>UserLogin</c> row; logs the renewal.</para>
    /// </remarks>
    /// <param name="session">The session row being renewed.</param>
    /// <param name="sessionUser">The account the session belongs to, used to build the claims.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The replacement token.</returns>
    private async Task<string> RotateSessionTokenAsync(
        UserLogin session,
        AppUser sessionUser,
        CancellationToken cancellationToken)
    {
        var renewedToken = GenerateJWToken(sessionUser);
        var renewedAt = DateTime.UtcNow;

        session.LoginToken = renewedToken;
        session.LoginDate = renewedAt;
        session.IssueDate = renewedAt;
        session.ExipryDate = renewedAt.Add(sessionPolicy.RefreshWindow);
        session.TokenStatus = TokenStatus.ValidToken.ToString();
        await loginRepo.UpdateAsync(session, cancellationToken).ConfigureAwait(false);

        appLogger.LogInformation(
            "Session refreshed for user {UserId}: session {LoginId} reissued for {AccessMinutes} minutes, " +
            "refresh window now ends {WindowEnd:u}",
            sessionUser.UserId,
            session.LoginId,
            sessionPolicy.AccessTokenLifetime.TotalMinutes,
            session.ExipryDate);

        return renewedToken;
    }

    /// <summary>
    /// Reports whether a session has outlived the window in which it may be renewed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row's <c>ExipryDate</c> is stored as a
    /// <c>TIMESTAMP</c> without time zone and is written from <see cref="DateTime.UtcNow"/>, so it
    /// is compared against UTC. Rows written before REQ-FN-008 carry a local-midnight value, which
    /// can be out by the host's UTC offset — hours on a window measured in days, and only ever in
    /// the direction of ending the session slightly early.</para>
    /// <para><b>Flow:</b> compare.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="session">The session row to test.</param>
    /// <returns><c>true</c> when the session can no longer be renewed.</returns>
    private static bool IsRefreshWindowOver(UserLogin session)
    {
        return session.ExipryDate < DateTime.UtcNow;
    }

    /// <summary>
    /// Reports whether an access token has passed its <c>exp</c> claim.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A token carrying no expiry is treated as unexpired — see
    /// <c>SvcUtils.GetTokenExpiryUtc</c> on why this fails open. An unreadable token is treated as
    /// expired: it cannot be honoured, and reporting it as expired routes the caller to the refresh
    /// path, which refuses it for the same reason without throwing.</para>
    /// <para><b>Flow:</b> read the claim → compare against now.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="jwToken">The token presented by the caller.</param>
    /// <returns><c>true</c> when the token may no longer be used without a refresh.</returns>
    private static bool IsAccessTokenExpired(string jwToken)
    {
        try
        {
            var expiresAtUtc = SvcUtils.GetTokenExpiryUtc(jwToken);
            return expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Builds the signed JWT carried by an authenticated session.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The signing key is read from <see cref="AppSecrets"/>, which
    /// resolves it from the user-secret store or the environment and refuses to start without it
    /// (REQ-NFR-027). It was previously a literal in this file; a signing key committed to source
    /// is a signing key every reader of the repository can mint tokens with, so the literal was
    /// removed rather than rotated. The bytes are taken as UTF-8, not ASCII: ASCII replaces every
    /// byte above <c>0x7F</c> with <c>'?'</c>, which would silently collapse the key space of a
    /// randomly generated key.</para>
    /// <para><b>Flow:</b> read the key → build the claim set (user id, name, email, role) → sign
    /// with HMAC-SHA256 → serialise.</para>
    /// <para><b>Side Effects:</b> None — this method only builds a string. The session becomes real
    /// when <see cref="IssueSessionAsync"/> writes the <c>UserLogin</c> row.</para>
    /// <para><b>Two expiries, and how they now relate (REQ-FN-008).</b> The <c>exp</c> claim
    /// stamped here is the <i>access-token lifetime</i> from <see cref="SessionPolicy"/> — an hour
    /// by default — and the <c>UserLogin</c> row <see cref="IssueSessionAsync"/> writes carries the
    /// much longer <i>refresh window</i>. Both are now read on the way back in, so the claim is the
    /// thing that expires and the row is the thing that authorises the reissue. The pair used to be
    /// 15 days against 2 with neither ever consulted, which is why sessions never ended and the
    /// refresh path had nothing to do; do not restore a hard-coded value here, change the policy.</para>
    /// <para><b>Security:</b> the claims are copied into the token for convenience only. Because
    /// nothing verifies them on the way back in, no authorization decision anywhere in the product
    /// may be made from a claim read out of this token. That includes <c>exp</c>: it is honoured
    /// only for a token already matched against its <c>UserLogin</c> row.</para>
    /// </remarks>
    /// <param name="loggedInUser">The authenticated user.</param>
    /// <returns>The serialised JWT.</returns>
    private string GenerateJWToken(AppUser loggedInUser)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        // REQ-NFR-027: the signing key comes from configuration, never from a literal in source.
        // UTF-8 rather than ASCII so a randomly generated key keeps all of its entropy - ASCII
        // replaces every byte above 0x7F with '?', silently collapsing the key space.
        var key = Encoding.UTF8.GetBytes(AppSecrets.JwtSigningKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.PrimarySid, Convert.ToString(loggedInUser.UserId)),
                new Claim(ClaimTypes.Name, loggedInUser.FullName),
                new Claim(ClaimTypes.Email, loggedInUser.EmailId),
                new Claim(ClaimTypes.Role, loggedInUser.UserRole),
                // REQ-FN-008: without a unique id, two tokens minted for the same user inside the
                // same second are byte-identical, because every other claim - including the
                // whole-second exp - is identical. A refresh would then "rotate" the session to the
                // value it already had: the row would be rewritten with the same string, the old
                // token would keep working, and nothing downstream could tell a renewal from a
                // no-op. This claim is what makes each issued token distinguishable.
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            }),
            Expires = DateTime.UtcNow.Add(sessionPolicy.AccessTokenLifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        return tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));
    }

    /// <summary>
    /// Creates a staff account on behalf of an administrator (BRD-10, REQ-FN-006).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the only account-creation path left after public
    /// signup was retired. It enforces <see cref="PasswordValidator"/>, rejects duplicate email
    /// addresses, stores a PBKDF2 hash and marks the account so the new member must choose their
    /// own password at first sign-in.</para>
    /// <para><b>Flow:</b> validate inputs → validate password strength → check uniqueness →
    /// hash → insert → set the forced-change flag.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogUser</c> row; logs the creation.</para>
    /// </remarks>
    /// <param name="displayName">The member's display name; split into first and last name.</param>
    /// <param name="email">The member's email address, used as the login name.</param>
    /// <param name="password">The initial password, which the member must change at first login.</param>
    /// <param name="role">One of the <see cref="AppRoles"/> constants.</param>
    /// <param name="cancellationToken">Cancels the creation.</param>
    /// <returns>The created user, or a failure describing why creation was refused.</returns>
    public async Task<Result<AppUser>> CreateStaffAccountAsync(
        string displayName,
        string email,
        string password,
        string role,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validation = await ValidateStaffAccountRequestAsync(displayName, email, password, cancellationToken)
                .ConfigureAwait(false);
            if (validation.IsFailure)
                return Result<AppUser>.Failure(validation.ErrorMessage);

            var newUser = BuildStaffUser(displayName, email, password, role);
            var userId = await userRepo.InsertToGetIdAsync(newUser, cancellationToken).ConfigureAwait(false);
            if (userId <= 0)
                return Result<AppUser>.Failure("Account creation failed. Please try again.");

            newUser.UserId = userId;
            await credentialRepo.UpdatePasswordHashAsync(userId, newUser.LoginPass, true, cancellationToken)
                .ConfigureAwait(false);

            appLogger.LogInformation("Staff account created for {Email} with role {Role}", email, newUser.UserRole);
            return Result<AppUser>.Success(newUser);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error creating staff account for {Email}", email);
            return Result<AppUser>.Failure("An error occurred while creating the account.");
        }
    }

    /// <summary>
    /// Validates an administrator's request to create a staff account.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Name and email are mandatory, the password must satisfy
    /// <see cref="PasswordValidator"/> (BRD-10) and the email must not already be in use. The
    /// cheap in-memory checks run before the uniqueness query, so a blank form never reaches the
    /// database.</para>
    /// <para><b>Flow:</b> required fields → password strength → uniqueness.</para>
    /// <para><b>Side Effects:</b> Reads one user row.</para>
    /// </remarks>
    /// <param name="displayName">The member's display name.</param>
    /// <param name="email">The member's email address.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="cancellationToken">Cancels the uniqueness lookup.</param>
    /// <returns>Success, or a failure describing the first problem found.</returns>
    private async Task<Result> ValidateStaffAccountRequestAsync(
        string displayName,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure("Display name is required");

        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure("Email is required");

        var passwordResult = PasswordValidator.Validate(password);
        if (!passwordResult.IsValid)
            return Result.Failure(passwordResult.ErrorMessage);

        var existing = await userRepo
            .GetUserByEmailAsync(email.ToLowerInvariant().Trim(), cancellationToken).ConfigureAwait(false);
        if (existing != null)
            return Result.Failure("An account with this email already exists");

        return Result.Success();
    }

    /// <summary>
    /// Builds the <see cref="AppUser"/> to insert for a new staff account.
    /// </summary>
    /// <param name="displayName">The member's display name.</param>
    /// <param name="email">The member's email address.</param>
    /// <param name="password">The initial password.</param>
    /// <param name="role">The requested role, defaulting to <see cref="AppRoles.Reader"/>.</param>
    /// <returns>The populated, unsaved user.</returns>
    private static AppUser BuildStaffUser(string displayName, string email, string password, string role)
    {
        var nameParts = displayName.Trim().Split(' ', 2);
        return new AppUser
        {
            FirstName = nameParts[0],
            LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
            EmailId = email.ToLowerInvariant().Trim(),
            LoginPass = PasswordHasher.HashPassword(password),
            UserRole = string.IsNullOrWhiteSpace(role) ? AppRoles.Reader : role,
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = DateTime.UtcNow,
            IsConfirmed = true,
            MustChangePassword = true
        };
    }

    /// <summary>
    /// Requests a password reset for the given email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Always reports success so the endpoint cannot be used to
    /// enumerate registered addresses. The issued token is persisted (REQ-NFR-019) with a
    /// 24-hour expiry, so the emailed link survives a restart and works on any instance.</para>
    /// <para><b>Flow:</b> validate → look up user → generate token → persist → email the link.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>PasswordResetToken</c> row and sends an email.</para>
    /// </remarks>
    /// <param name="email">Email address to send the reset link to.</param>
    /// <param name="baseUrl">Base URL used to build an absolute reset link.</param>
    /// <param name="cancellationToken">Cancels the lookup and the insert.</param>
    /// <returns>The issued token (for development logging), or a failure message.</returns>
    public async Task<Result<string>> RequestPasswordResetAsync(
        string email,
        string baseUrl = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                return Result<string>.Failure("Email is required");

            var user = await userRepo
                .GetUserByEmailAsync(email.ToLowerInvariant().Trim(), cancellationToken).ConfigureAwait(false);
            if (user == null)
            {
                appLogger.LogInformation("Password reset requested for unknown address {Email}", email);
                // Deliberately reported as success with no token so the response cannot be used
                // to discover which addresses have accounts (account-enumeration defence).
                return Result<string>.Success(string.Empty);
            }

            var token = GenerateSecureToken();
            await PersistResetTokenAsync(user.UserId, token, cancellationToken).ConfigureAwait(false);
            await emailService.SendPasswordResetEmail(email, BuildResetUrl(baseUrl, token)).ConfigureAwait(false);

            appLogger.LogInformation("Password reset token created for user {UserId}", user.UserId);
            return Result<string>.Success(token);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error requesting password reset for {Email}", email);
            return Result<string>.Failure("An error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Persists a newly issued reset token with a 24-hour expiry.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The window is fixed at 24 hours and stamped in UTC so the link's
    /// lifetime does not depend on the database server's time zone. The repository normalises the
    /// <see cref="DateTimeKind"/> before binding — without that the stored function resolves to no
    /// overload at all and the insert fails with <c>42883</c>, invisibly, because this flow reports
    /// the same generic message either way (REQ-NFR-026).</para>
    /// <para><b>Flow:</b> build the token row → insert.</para>
    /// <para><b>Side Effects:</b> Adds one <c>PasswordResetToken</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user the token belongs to.</param>
    /// <param name="token">The opaque token string.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    private async Task PersistResetTokenAsync(long userId, string token, CancellationToken cancellationToken)
    {
        await tokenRepo.InsertAsync(
            new PasswordResetToken
            {
                UserId = userId,
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsUsed = false
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the reset link mailed to the user.
    /// </summary>
    /// <param name="baseUrl">Configured base URL; may be empty for a relative link.</param>
    /// <param name="token">The issued token.</param>
    /// <returns>The reset URL.</returns>
    private static string BuildResetUrl(string baseUrl, string token)
    {
        return string.IsNullOrEmpty(baseUrl)
            ? $"/reset-password/{token}"
            : $"{baseUrl.TrimEnd('/')}/reset-password/{token}";
    }

    /// <summary>
    /// Resets a user's password using a valid reset token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token must exist, be unexpired and unused; the new
    /// password must satisfy <see cref="PasswordValidator"/> (BRD-5). The new password is stored
    /// as a PBKDF2 hash and the forced-change flag is cleared, because the user has just chosen
    /// their own password.</para>
    /// <para><b>Flow:</b> validate token → validate strength → rotate hash → consume token.</para>
    /// <para><b>Side Effects:</b> Updates the credential row and marks the token used.</para>
    /// </remarks>
    /// <param name="token">The reset token from the email link.</param>
    /// <param name="newPassword">The new password to set.</param>
    /// <param name="cancellationToken">Cancels the lookups and the writes.</param>
    /// <returns>Success, or a failure describing why the reset was refused.</returns>
    public async Task<Result<bool>> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validation = await ValidateResetTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (validation.IsFailure)
                return validation;

            var passwordResult = PasswordValidator.Validate(newPassword);
            if (!passwordResult.IsValid)
                return Result<bool>.Failure(passwordResult.ErrorMessage);

            var resetToken = await tokenRepo.GetByTokenAsync(token, cancellationToken).ConfigureAwait(false);
            if (resetToken == null)
                return Result<bool>.Failure("This reset link is no longer valid. Please request a new one.");

            await credentialRepo.UpdatePasswordHashAsync(
                resetToken.UserId, PasswordHasher.HashPassword(newPassword), false, cancellationToken)
                .ConfigureAwait(false);
            await tokenRepo.MarkUsedAsync(resetToken.TokenId, cancellationToken).ConfigureAwait(false);

            appLogger.LogInformation("Password reset completed for user {UserId}", resetToken.UserId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error resetting password with token");
            return Result<bool>.Failure("An error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Validates a reset token without consuming it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Distinguishes unknown, expired and already-used tokens so the
    /// user gets an accurate message and knows whether to request a new link.</para>
    /// <para><b>Flow:</b> guard → load → expiry check → used check.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="token">The token to validate.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Success when the token can still be redeemed; otherwise a failure message.</returns>
    public async Task<Result<bool>> ValidateResetTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result<bool>.Failure("Invalid reset link");

        var resetToken = await tokenRepo.GetByTokenAsync(token, cancellationToken).ConfigureAwait(false);
        if (resetToken == null)
            return Result<bool>.Failure("Invalid reset link. Please request a new password reset.");

        if (resetToken.ExpiresAt < DateTime.UtcNow)
            return Result<bool>.Failure("This reset link has expired. Please request a new password reset.");

        if (resetToken.IsUsed)
            return Result<bool>.Failure("This reset link has already been used. Please request a new password reset.");

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Generates a cryptographically secure, URL-safe reset token.
    /// </summary>
    /// <param name="byteCount">Entropy in bytes; 32 bytes gives a 256-bit token.</param>
    /// <returns>The URL-safe base64 token.</returns>
    private static string GenerateSecureToken(int byteCount = 32)
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", string.Empty);
    }

    /// <summary>
    /// Gets a user's profile by their identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Read-only lookup used by profile screens; failures are logged
    /// and surfaced as <c>null</c> rather than propagating to the UI.</para>
    /// <para><b>Flow:</b> load → return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The user if found; otherwise <c>null</c>.</returns>
    public async Task<AppUser?> GetUserProfileAsync(long userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await userRepo.GetSingleAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error getting user profile for {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Updates a user's profile information.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> First and last name are mandatory; the social and biography
    /// fields are optional and stored trimmed, never null, because the update function does not
    /// accept nulls.</para>
    /// <para><b>Flow:</b> validate → load → apply → save.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="firstName">First name.</param>
    /// <param name="lastName">Last name.</param>
    /// <param name="profileDescription">Biography.</param>
    /// <param name="twitterUrl">Twitter profile URL.</param>
    /// <param name="linkedInUrl">LinkedIn profile URL.</param>
    /// <param name="gitHubUrl">GitHub profile URL.</param>
    /// <param name="cancellationToken">Cancels the load and the save.</param>
    /// <returns>Success, or a failure describing the problem.</returns>
    public async Task<Result<bool>> UpdateProfileAsync(
        long userId,
        string firstName,
        string lastName,
        string profileDescription,
        string twitterUrl,
        string linkedInUrl,
        string gitHubUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                return Result<bool>.Failure("First name and last name are required");

            var user = await userRepo.GetSingleAsync(userId, cancellationToken).ConfigureAwait(false);
            if (user == null)
                return Result<bool>.Failure("User not found");

            ApplyProfileFields(user, firstName, lastName, profileDescription, twitterUrl, linkedInUrl, gitHubUrl);
            await userRepo.UpdateAsync(user, cancellationToken).ConfigureAwait(false);

            appLogger.LogInformation("Profile updated for user {UserId}", userId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error updating profile for user {UserId}", userId);
            return Result<bool>.Failure("An error occurred while updating your profile.");
        }
    }

    /// <summary>
    /// Copies the editable profile fields onto a loaded user.
    /// </summary>
    /// <param name="user">The user to mutate.</param>
    /// <param name="firstName">First name.</param>
    /// <param name="lastName">Last name.</param>
    /// <param name="profileDescription">Biography.</param>
    /// <param name="twitterUrl">Twitter profile URL.</param>
    /// <param name="linkedInUrl">LinkedIn profile URL.</param>
    /// <param name="gitHubUrl">GitHub profile URL.</param>
    private static void ApplyProfileFields(
        AppUser user,
        string firstName,
        string lastName,
        string profileDescription,
        string twitterUrl,
        string linkedInUrl,
        string gitHubUrl)
    {
        user.FirstName = firstName.Trim();
        user.LastName = lastName.Trim();
        user.ProfileDescription = profileDescription?.Trim() ?? string.Empty;
        user.TwitterUrl = twitterUrl?.Trim() ?? string.Empty;
        user.LinkedInUrl = linkedInUrl?.Trim() ?? string.Empty;
        user.GitHubUrl = gitHubUrl?.Trim() ?? string.Empty;
        user.UpdatedOn = DateTime.UtcNow;
    }

    /// <summary>
    /// Changes a signed-in user's own password.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The current password is verified against the stored PBKDF2
    /// hash before anything changes, the replacement must satisfy
    /// <see cref="PasswordValidator"/>, and the forced-change flag is cleared — this is the flow
    /// that satisfies the "must change at first login" requirement (REQ-NFR-023).</para>
    /// <para><b>Flow:</b> validate inputs → load credential → verify current → validate new →
    /// rotate hash.</para>
    /// <para><b>Side Effects:</b> Updates the credential row.</para>
    /// </remarks>
    /// <param name="userId">The signed-in user's identifier.</param>
    /// <param name="currentPassword">The current password, for confirmation.</param>
    /// <param name="newPassword">The replacement password.</param>
    /// <param name="cancellationToken">Cancels the load and the rotation.</param>
    /// <returns>Success, or a failure describing why the change was refused.</returns>
    public async Task<Result<bool>> ChangePasswordAsync(
        long userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
                return Result<bool>.Failure("Current and new passwords are required");

            var credential = await credentialRepo.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (credential == null)
                return Result<bool>.Failure("User not found");

            if (PasswordHasher.Verify(currentPassword, credential.LoginPass) == PasswordVerifyResult.Failed)
                return Result<bool>.Failure("Current password is incorrect");

            var validationResult = PasswordValidator.Validate(newPassword);
            if (!validationResult.IsValid)
                return Result<bool>.Failure(validationResult.ErrorMessage);

            await credentialRepo.UpdatePasswordHashAsync(
                userId, PasswordHasher.HashPassword(newPassword), false, cancellationToken).ConfigureAwait(false);

            appLogger.LogInformation("Password changed for user {UserId}", userId);
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Error changing password for user {UserId}", userId);
            return Result<bool>.Failure("An error occurred while changing your password.");
        }
    }
}
