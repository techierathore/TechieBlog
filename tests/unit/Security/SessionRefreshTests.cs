using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Tests for session expiry and renewal in <see cref="AuthSvc"/> (REQ-FN-008, BRD-6).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A session used to have two expiry values that disagreed with each other
/// and neither was ever read, so a token never expired, the refresh path had nothing to do, and
/// nothing called it. These tests pin the repaired behaviour from both ends: an access token past
/// its <c>exp</c> claim is refused, the same value redeemed through
/// <see cref="AuthSvc.RefreshSessionAsync"/> produces a <i>different</i> token that is accepted, and
/// a session past its refresh window cannot be revived by either route.</para>
/// <para><b>Dependencies:</b> xUnit and NSubstitute; no database and no host. The session
/// repository is substituted with a one-row store so a rotation can be observed as the update it
/// really is — the SQL that persists it is proved against live PostgreSQL in the smoke run.</para>
/// <para><b>On the two tests that sleep:</b> a JWT <c>exp</c> claim has whole-second resolution and
/// the token handler refuses to stamp an expiry that is not strictly after the issue time, so the
/// shortest token that can exist lives one second. The two tests that need a genuinely expired
/// token therefore issue one and wait it out rather than faking a clock — a little over a second
/// each, and what they prove is the real claim being read rather than a substitute's opinion. Every
/// other test here runs against an ordinary lifetime, because the refresh path deliberately does
/// not care whether the token it is handed has expired; the session row is what it checks.</para>
/// </remarks>
public class SessionRefreshTests
{
    private const string SamplePassword = "Str0ngPassword";
    private const string SampleEmail = "session@techieblog.test";
    private const long SampleUserId = 21L;

    private readonly IBlogUserRepo userRepo = Substitute.For<IBlogUserRepo>();
    private readonly IUserCredentialRepo credentialRepo = Substitute.For<IUserCredentialRepo>();
    private readonly IUserLoginRepository loginRepo = Substitute.For<IUserLoginRepository>();
    private readonly IPasswordResetTokenRepo tokenRepo = Substitute.For<IPasswordResetTokenRepo>();
    private readonly ILoginLogRepo loginLogRepo = Substitute.For<ILoginLogRepo>();
    private readonly ILoginThrottle loginThrottle = Substitute.For<ILoginThrottle>();
    private readonly IEmailService emailService = Substitute.For<IEmailService>();

    private UserLogin? storedSession;

    /// <summary>
    /// Arranges the account and turns the substituted session repository into a one-row store, so
    /// an insert, a token lookup and an in-place rotation all see the same row.
    /// </summary>
    public SessionRefreshTests()
    {
        TestAppSecrets.EnsureInitialised();

        credentialRepo.GetByEmailAsync(SampleEmail, Arg.Any<CancellationToken>()).Returns(new UserCredential
        {
            UserId = SampleUserId,
            EmailId = SampleEmail,
            LoginPass = PasswordHasher.HashPassword(SamplePassword),
            UserRole = "Author",
            MustChangePassword = false
        });

        userRepo.GetSingleAsync(SampleUserId, Arg.Any<CancellationToken>()).Returns(new AppUser
        {
            UserId = SampleUserId,
            FirstName = "Session",
            LastName = "Member",
            EmailId = SampleEmail,
            UserRole = "Author"
        });

        loginRepo
            .When(repo => repo.InsertAsync(Arg.Any<UserLogin>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                storedSession = call.ArgAt<UserLogin>(0);
                storedSession.LoginId = 91;
            });

        loginRepo
            .When(repo => repo.UpdateAsync(Arg.Any<UserLogin>(), Arg.Any<CancellationToken>()))
            .Do(call => storedSession = call.ArgAt<UserLogin>(0));

        loginRepo
            .GetUserByTokenAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => MatchStoredSession(call.ArgAt<long>(0), call.ArgAt<string>(1)));
    }

    /// <summary>
    /// The simple name <c>BlogSvc</c> binds to the post service type, not to a namespace, in a file
    /// that also uses <see cref="SvcUtils"/> (REQ-NFR-032).
    /// </summary>
    /// <remarks>
    /// <para><b>Why this test exists:</b> <c>SvcUtils</c> used to declare <c>namespace BlogSvc</c>,
    /// which put a namespace named <c>BlogSvc</c> in the global namespace. Simple-name lookup finds
    /// enclosing-namespace members before it consults <c>using</c> directives, so in any file that
    /// imported this helper the bare name <c>BlogSvc</c> resolved to that namespace and
    /// <c>typeof(BlogSvc)</c> below would not compile (CS0118, "is a namespace but is used like a
    /// type"). Every reference to the post service had to be written out in full.</para>
    /// <para><b>What it proves:</b> this is a compile-time assertion first and a runtime one second
    /// — the file uses both <c>BlogSvc</c> and <c>SvcUtils</c> unqualified, so it only builds while
    /// the collision is absent. The two <see cref="Type.FullName"/> checks then pin <i>which</i>
    /// types those names reached, so the test cannot be satisfied by a namespace being renamed to
    /// something else that happens to compile.</para>
    /// </remarks>
    [Fact]
    public void BlogSvcNameResolvesToTheServiceTypeNotANamespace()
    {
        Assert.Equal("BlogEngine.Services.BlogSvc", typeof(BlogSvc).FullName);
        Assert.Equal("BlogEngine.Common.SvcUtils", typeof(SvcUtils).FullName);
    }

    /// <summary>
    /// The access token's expiry comes from the configured policy rather than from a literal, which
    /// is what lets a deployment — or a smoke run — decide how long a token lives.
    /// </summary>
    [Fact]
    public async Task SignInStampsConfiguredAccessTokenLifetime()
    {
        var policy = new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7));

        var session = await SignInAsync(policy);

        var expiry = SvcUtils.GetTokenExpiryUtc(session.JwToken);
        Assert.NotNull(expiry);
        Assert.InRange(
            expiry.Value,
            DateTime.UtcNow.AddMinutes(29),
            DateTime.UtcNow.AddMinutes(31));
    }

    /// <summary>
    /// The recorded session carries the refresh window, not the access-token lifetime — the two are
    /// different clocks and confusing them is what made the original implementation incoherent.
    /// </summary>
    [Fact]
    public async Task SignInRecordsRefreshWindowOnTheSessionRow()
    {
        await SignInAsync(new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7)));

        Assert.NotNull(storedSession);
        Assert.InRange(
            storedSession.ExipryDate,
            DateTime.UtcNow.AddDays(6),
            DateTime.UtcNow.AddDays(8));
    }

    /// <summary>
    /// An access token past its <c>exp</c> claim no longer resolves a user, even though its session
    /// row is still present and still marked valid. Before REQ-FN-008 the claim was never read, so
    /// this returned the user forever.
    /// </summary>
    [Fact]
    public async Task ExpiredAccessTokenIsRefused()
    {
        var session = await SignInAndWaitForExpiryAsync();

        var resolved = await BuildService(ShortestUsable())
            .GetUserByTokenAsync(new SvcData { JwToken = session.JwToken });

        Assert.Null(resolved);
    }

    /// <summary>
    /// The same expired token, redeemed through the refresh path, produces a replacement token —
    /// this is the user-visible behaviour the requirement asks for, and the one the verifier found
    /// could never occur because nothing reached it.
    /// </summary>
    [Fact]
    public async Task RefreshIssuesReplacementTokenForExpiredAccessToken()
    {
        var session = await SignInAndWaitForExpiryAsync();

        var refreshed = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });

        Assert.NotNull(refreshed);
        Assert.NotEqual(session.JwToken, refreshed.JwToken);
    }

    /// <summary>
    /// Two tokens minted for the same account inside the same second are still different values.
    /// Every claim other than the unique token id is identical at that resolution — including the
    /// whole-second expiry — so without one a "rotation" would rewrite the session row with the
    /// string it already held and the replaced token would keep working.
    /// </summary>
    [Fact]
    public async Task EveryIssuedTokenIsDistinct()
    {
        var session = await SignInAsync(NormalPolicy());

        var firstRefresh = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });
        var secondRefresh = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = firstRefresh!.JwToken });

        Assert.NotNull(secondRefresh);
        Assert.Equal(3, new HashSet<string>(StringComparer.Ordinal)
        {
            session.JwToken,
            firstRefresh.JwToken,
            secondRefresh.JwToken
        }.Count);
    }

    /// <summary>
    /// A renewal rewrites the existing session row rather than inserting a second one, so the
    /// replaced token stops working immediately and the sessions table does not grow by one row per
    /// renewal.
    /// </summary>
    [Fact]
    public async Task RefreshRewritesTheSameSessionRow()
    {
        var session = await SignInAsync(NormalPolicy());

        var refreshed = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });

        Assert.NotNull(refreshed);
        Assert.NotNull(storedSession);
        Assert.Equal(91, storedSession.LoginId);
        Assert.Equal(refreshed.JwToken, storedSession.LoginToken);
        await loginRepo.Received(1).UpdateAsync(Arg.Any<UserLogin>(), Arg.Any<CancellationToken>());
        await loginRepo.Received(1).InsertAsync(Arg.Any<UserLogin>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The refresh window slides forward on every renewal, which is what lets an actively used
    /// session outlive the window without a second sign-in.
    /// </summary>
    [Fact]
    public async Task RefreshSlidesTheRefreshWindowForward()
    {
        var policy = NormalPolicy();
        var session = await SignInAsync(policy);
        storedSession!.ExipryDate = DateTime.UtcNow.AddMinutes(5);

        await BuildService(policy).RefreshSessionAsync(new SvcData { JwToken = session.JwToken });

        Assert.InRange(
            storedSession.ExipryDate,
            DateTime.UtcNow.AddDays(6),
            DateTime.UtcNow.AddDays(8));
    }

    /// <summary>
    /// The token a renewal hands back is accepted by the ordinary session lookup, so the user
    /// carries on rather than being sent to the sign-in screen — the whole point of the feature.
    /// </summary>
    [Fact]
    public async Task RefreshedTokenIsAcceptedForTheNextRequest()
    {
        var session = await SignInAsync(NormalPolicy());
        var renewalPolicy = new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7));

        var refreshed = await BuildService(renewalPolicy)
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });
        var resolved = await BuildService(renewalPolicy)
            .GetUserByTokenAsync(new SvcData { JwToken = refreshed!.JwToken });

        Assert.NotNull(resolved);
        Assert.Equal(refreshed.JwToken, resolved.JwToken);
    }

    /// <summary>
    /// The token that was replaced stops working the instant the renewal succeeds, so a copied or
    /// cached value cannot keep a second session alive alongside the real one.
    /// </summary>
    [Fact]
    public async Task ReplacedTokenStopsWorkingAfterRefresh()
    {
        var session = await SignInAsync(NormalPolicy());
        var renewalPolicy = new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7));

        await BuildService(renewalPolicy).RefreshSessionAsync(new SvcData { JwToken = session.JwToken });
        var resolved = await BuildService(renewalPolicy)
            .GetUserByTokenAsync(new SvcData { JwToken = session.JwToken });

        Assert.Null(resolved);
    }

    /// <summary>
    /// A session past its refresh window cannot be renewed: the window is what bounds how long a
    /// stolen or abandoned token remains useful, so an expired one must end the session rather than
    /// slide.
    /// </summary>
    [Fact]
    public async Task RefreshIsRefusedPastTheRefreshWindow()
    {
        var session = await SignInAsync(NormalPolicy());
        storedSession!.ExipryDate = DateTime.UtcNow.AddMinutes(-1);

        var refreshed = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });

        Assert.Null(refreshed);
    }

    /// <summary>
    /// A token that was revoked or signed out — its row no longer matching — cannot be revived by
    /// the refresh path. The <c>UserLogin</c> lookup remains the only integrity check in the chain
    /// and the refresh path does not get to skip it.
    /// </summary>
    [Fact]
    public async Task RefreshIsRefusedWhenTheSessionIsNotIssued()
    {
        var session = await SignInAsync(NormalPolicy());
        storedSession = null;

        var refreshed = await BuildService(NormalPolicy())
            .RefreshSessionAsync(new SvcData { JwToken = session.JwToken });

        Assert.Null(refreshed);
    }

    /// <summary>
    /// Whatever the browser happened to have in local storage reaches this method, so an unreadable
    /// value is refused rather than thrown out of — a stale or corrupted slot must land the visitor
    /// on the sign-in screen, not on an error page.
    /// </summary>
    [Fact]
    public async Task RefreshIsRefusedForAnUnreadableToken()
    {
        var refreshed = await BuildService(SessionPolicy.Default)
            .RefreshSessionAsync(new SvcData { JwToken = "not-a-token" });

        Assert.Null(refreshed);
    }

    /// <summary>
    /// An empty envelope is refused without a database round trip, because a signed-out browser
    /// presenting nothing is the ordinary case rather than an error.
    /// </summary>
    [Fact]
    public async Task RefreshIsRefusedForAnEmptyToken()
    {
        var refreshed = await BuildService(SessionPolicy.Default)
            .RefreshSessionAsync(new SvcData { JwToken = string.Empty });

        Assert.Null(refreshed);
        await loginRepo.DidNotReceive()
            .GetUserByTokenAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpired access token whose session row has outlived its window is refused by the
    /// ordinary lookup too, so the window bounds the session regardless of which clock is checked
    /// first.
    /// </summary>
    [Fact]
    public async Task SessionPastItsWindowIsRefusedByTheTokenLookup()
    {
        var policy = new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7));
        var session = await SignInAsync(policy);
        storedSession!.ExipryDate = DateTime.UtcNow.AddMinutes(-1);

        var resolved = await BuildService(policy)
            .GetUserByTokenAsync(new SvcData { JwToken = session.JwToken });

        Assert.Null(resolved);
    }

    /// <summary>
    /// A host that configures nothing gets the shipped durations, so the feature works out of the
    /// box on a clone-and-run checkout.
    /// </summary>
    [Fact]
    public void PolicyFallsBackToShippedDurations()
    {
        var policy = SessionPolicy.FromConfiguration(BuildConfiguration(null, null));

        Assert.Equal(TimeSpan.FromMinutes(SessionPolicy.DefaultAccessTokenMinutes), policy.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(SessionPolicy.DefaultRefreshWindowDays), policy.RefreshWindow);
    }

    /// <summary>
    /// Both durations are read from configuration, and the access-token lifetime accepts a fraction
    /// of a minute so a smoke run can watch a token expire for real.
    /// </summary>
    [Fact]
    public void PolicyReadsConfiguredDurations()
    {
        var policy = SessionPolicy.FromConfiguration(BuildConfiguration("0.25", "3"));

        Assert.Equal(TimeSpan.FromSeconds(15), policy.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(3), policy.RefreshWindow);
    }

    /// <summary>
    /// A typo in an operational knob falls back to the default rather than stopping the host: the
    /// value it falls back to is the safe one, and an unstartable host is a worse outcome than an
    /// ignored setting.
    /// </summary>
    [Fact]
    public void PolicyIgnoresUnusableValues()
    {
        var policy = SessionPolicy.FromConfiguration(BuildConfiguration("sixty", "-4"));

        Assert.Equal(TimeSpan.FromMinutes(SessionPolicy.DefaultAccessTokenMinutes), policy.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(SessionPolicy.DefaultRefreshWindowDays), policy.RefreshWindow);
    }

    /// <summary>
    /// A refresh window shorter than the access-token lifetime is refused: a token would outlive
    /// the session authorising its own reissue, so every refresh would be refused exactly when it
    /// became necessary.
    /// </summary>
    [Fact]
    public void PolicyRejectsAWindowShorterThanTheTokenLifetime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromHours(2), TimeSpan.FromHours(1)));
    }

    /// <summary>
    /// A sub-second lifetime is raised to the shortest a JWT can express rather than being passed
    /// through. The token handler refuses an expiry that is not strictly after the issue time, so
    /// the alternative is not a very short session — it is every sign-in in the product throwing.
    /// </summary>
    [Fact]
    public void PolicyRaisesASubSecondLifetimeToTheMinimum()
    {
        var policy = SessionPolicy.FromConfiguration(BuildConfiguration("0.001", "3"));

        Assert.Equal(SessionPolicy.MinimumAccessTokenLifetime, policy.AccessTokenLifetime);
    }

    /// <summary>
    /// A sub-second lifetime asked for in code is a mistake rather than a configuration typo, so it
    /// is refused loudly instead of being quietly corrected.
    /// </summary>
    [Fact]
    public void PolicyRejectsASubSecondLifetimeInCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SessionPolicy(TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1)));
    }

    /// <summary>
    /// Signs the sample account in and returns the issued session envelope.
    /// </summary>
    /// <param name="policy">The durations the session is issued under.</param>
    /// <returns>The envelope carrying the issued token.</returns>
    private async Task<SvcData> SignInAsync(SessionPolicy policy)
    {
        var envelope = new SvcData
        {
            LoginEmail = AppEncrypt.EncryptText(SampleEmail),
            LoginPass = AppEncrypt.EncryptText(SamplePassword)
        };

        var session = await BuildService(policy).AppLoginAsync(envelope);
        Assert.NotNull(session);
        return session;
    }

    /// <summary>
    /// Builds the service under test over the substituted repositories.
    /// </summary>
    /// <param name="policy">The session durations to run under.</param>
    /// <returns>A ready-to-use <see cref="AuthSvc"/>.</returns>
    private AuthSvc BuildService(SessionPolicy policy)
    {
        return new AuthSvc(
            userRepo,
            credentialRepo,
            loginRepo,
            tokenRepo,
            loginLogRepo,
            loginThrottle,
            emailService,
            NullLogger<AuthSvc>.Instance,
            policy);
    }

    /// <summary>
    /// Reproduces the repository's three-way match: owner, exact token and valid status.
    /// </summary>
    /// <param name="userId">The owner the caller claims.</param>
    /// <param name="token">The token string presented.</param>
    /// <returns>The stored session when it matches; otherwise <c>null</c>.</returns>
    private UserLogin? MatchStoredSession(long userId, string token)
    {
        if (storedSession == null)
            return null;

        var matches = storedSession.UserId == userId
            && string.Equals(storedSession.LoginToken, token, StringComparison.Ordinal)
            && storedSession.TokenStatus == TokenStatus.ValidToken.ToString();

        return matches ? storedSession : null;
    }

    /// <summary>
    /// Signs in under the shortest usable lifetime and waits until the issued token has expired.
    /// </summary>
    /// <remarks>
    /// The wait is a little longer than the lifetime because the <c>exp</c> claim is truncated to a
    /// whole second, which can round the real expiry up by up to one second beyond the nominal one.
    /// </remarks>
    /// <returns>The envelope carrying the now-expired token.</returns>
    private async Task<SvcData> SignInAndWaitForExpiryAsync()
    {
        var session = await SignInAsync(ShortestUsable());
        await Task.Delay(SessionPolicy.MinimumAccessTokenLifetime + TimeSpan.FromMilliseconds(400));
        return session;
    }

    /// <summary>
    /// A policy with an ordinary, unexpired access-token lifetime.
    /// </summary>
    /// <returns>The policy.</returns>
    private static SessionPolicy NormalPolicy()
    {
        return new SessionPolicy(TimeSpan.FromMinutes(30), TimeSpan.FromDays(7));
    }

    /// <summary>
    /// A policy at the shortest access-token lifetime a JWT can express.
    /// </summary>
    /// <returns>The policy.</returns>
    private static SessionPolicy ShortestUsable()
    {
        return new SessionPolicy(SessionPolicy.MinimumAccessTokenLifetime, TimeSpan.FromDays(7));
    }

    /// <summary>
    /// Builds an in-memory configuration carrying the two optional session settings.
    /// </summary>
    /// <param name="accessMinutes">Raw value for the access-token lifetime, or <c>null</c> to omit it.</param>
    /// <param name="refreshDays">Raw value for the refresh window, or <c>null</c> to omit it.</param>
    /// <returns>The configuration to read.</returns>
    private static IConfiguration BuildConfiguration(string? accessMinutes, string? refreshDays)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SessionPolicy.AccessTokenMinutesPath] = accessMinutes,
                [SessionPolicy.RefreshWindowDaysPath] = refreshDays
            })
            .Build();
    }
}
