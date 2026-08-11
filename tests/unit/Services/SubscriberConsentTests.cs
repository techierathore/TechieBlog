using System.Reflection;
using BlogEngine.DbAccess;
using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Unit tests for the subscriber consent record and the unsubscribe-token lifecycle. [REQ-FN-059]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Two defects are pinned here. First, <c>Subscriber.IsConfirmed</c> carried
/// both "never completed double opt-in" and "explicitly opted out", so unsubscribing ERASED the
/// proof of consent instead of recording a withdrawal and an opted-out address became
/// indistinguishable from one that never confirmed. Second, the unsubscribe token was never rotated
/// and never burned, so the same value shipped in every issue a subscriber ever received — a bearer
/// credential with unlimited lifetime.</para>
///
/// <para><b>What is checked:</b> the four-way consent state derived from the new columns; that a
/// redemption records the withdrawal and leaves the confirmation instant alone; that a burned token
/// cannot be replayed and an expired one is refused; that a legacy token with no recorded issuance
/// is NOT expirable, because expiring links already sitting in delivered mail would recreate the
/// original harm; and that every rejection returns the same wording so the route cannot be used as
/// a membership oracle.</para>
///
/// <para><b>What is deliberately NOT checked here:</b> that the database actually preserves
/// <c>ConfirmedOn</c> across a withdrawal. That is a property of the UPDATE statement, so
/// <see cref="WithdrawalStatementNeverWritesConfirmedOn"/> pins the statement itself and the
/// psql evidence in the cluster smoke proves the end-to-end behaviour against real rows.</para>
///
/// <para><b>Dependencies:</b> xUnit and NSubstitute over <c>SubscriberSvc</c>. No database, no
/// host. Note the trap documented by <c>SubstituteBridgeTrapTests</c>: a substitute intercepts a
/// default interface implementation rather than falling through to it, so the async members the
/// service calls are stubbed directly.</para>
/// </remarks>
public class SubscriberConsentTests
{
    private static readonly DateTime SignedUp = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Builds a service over a substitute repository, with the repository exposed to the caller.
    /// </summary>
    /// <param name="subscriberRepo">Receives the substitute the service was built on.</param>
    /// <returns>The service under test.</returns>
    private static SubscriberSvc BuildService(out ISubscriberRepo subscriberRepo)
    {
        subscriberRepo = Substitute.For<ISubscriberRepo>();
        return new SubscriberSvc(subscriberRepo, NullLogger<SubscriberSvc>.Instance);
    }

    /// <summary>
    /// Builds a confirmed subscriber holding a token issued at a given moment.
    /// </summary>
    /// <param name="issuedOn">When the token was issued; <c>null</c> models a legacy token.</param>
    /// <returns>A confirmed subscriber row.</returns>
    private static Subscriber ConfirmedSubscriber(DateTime? issuedOn)
    {
        return new Subscriber
        {
            SubscriberId = 42,
            Email = "reader@example.com",
            SubscribedOn = SignedUp,
            IsConfirmed = true,
            IsActive = true,
            ConfirmedOn = SignedUp,
            UnsubscribeToken = new string('a', 64),
            UnsubscribeTokenIssuedOn = issuedOn
        };
    }

    /// <summary>
    /// A row that signed up but never redeemed its opt-in link reports Pending, not Withdrawn: no
    /// consent was ever given and no decision to leave was ever made.
    /// </summary>
    [Fact]
    public void NeverConfirmedSubscriberIsPending()
    {
        var subscriber = new Subscriber { SubscribedOn = SignedUp };

        Assert.Equal(SubscriberConsentState.Pending, subscriber.ConsentState);
    }

    /// <summary>
    /// A row carrying a confirmation instant and no withdrawal reports Confirmed.
    /// </summary>
    [Fact]
    public void ConfirmedSubscriberIsConfirmed()
    {
        var subscriber = ConfirmedSubscriber(SignedUp);

        Assert.Equal(SubscriberConsentState.Confirmed, subscriber.ConsentState);
    }

    /// <summary>
    /// The defect this requirement exists for: a row that confirmed and then unsubscribed reports
    /// Withdrawn AND still carries its confirmation instant, so the proof of consent survives the
    /// opt-out and the two states are distinguishable from one another.
    /// </summary>
    [Fact]
    public void WithdrawnSubscriberIsDistinctFromNeverConfirmed()
    {
        var withdrawn = ConfirmedSubscriber(SignedUp);
        withdrawn.IsConfirmed = false;
        withdrawn.UnsubscribedOn = SignedUp.AddDays(30);

        var neverConfirmed = new Subscriber { SubscribedOn = SignedUp };

        Assert.Equal(SubscriberConsentState.Withdrawn, withdrawn.ConsentState);
        Assert.Equal(SubscriberConsentState.Pending, neverConfirmed.ConsentState);
        Assert.NotNull(withdrawn.ConfirmedOn);
    }

    /// <summary>
    /// A resubscribe writes a newer confirmation instant rather than clearing the withdrawal, so the
    /// row reports Confirmed again while still recording that the address once left.
    /// </summary>
    [Fact]
    public void ResubscribeKeepsTheWithdrawalOnRecord()
    {
        var subscriber = ConfirmedSubscriber(SignedUp);
        subscriber.UnsubscribedOn = SignedUp.AddDays(30);
        subscriber.ConfirmedOn = SignedUp.AddDays(60);

        Assert.Equal(SubscriberConsentState.Confirmed, subscriber.ConsentState);
        Assert.Equal(SignedUp.AddDays(30), subscriber.UnsubscribedOn);
    }

    /// <summary>
    /// When the two instants are equal the state is Withdrawn: for a consent question the safe
    /// direction of error is "do not mail".
    /// </summary>
    [Fact]
    public void SimultaneousConsentAndWithdrawalResolvesToWithdrawn()
    {
        var subscriber = ConfirmedSubscriber(SignedUp);
        subscriber.UnsubscribedOn = subscriber.ConfirmedOn;

        Assert.Equal(SubscriberConsentState.Withdrawn, subscriber.ConsentState);
    }

    /// <summary>
    /// A pre-migration row whose unconfirmed state could not be interpreted reports Unknown rather
    /// than being silently promoted to consented or demoted to withdrawn.
    /// </summary>
    [Fact]
    public void UninterpretableLegacyRowIsUnknown()
    {
        var subscriber = new Subscriber { SubscribedOn = SignedUp, IsConsentUnknown = true };

        Assert.Equal(SubscriberConsentState.Unknown, subscriber.ConsentState);
    }

    /// <summary>
    /// A valid, unexpired, unburned token opts the subscriber out and records the withdrawal
    /// through the consent-aware repository member rather than the bare status flip.
    /// </summary>
    [Fact]
    public async Task ValidTokenRecordsTheWithdrawal()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);
        subscriberRepo.RecordWithdrawalAsync(subscriber.SubscriberId, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
        await subscriberRepo.Received(1).RecordWithdrawalAsync(42L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A token whose recorded issuance is older than the 400-day lifetime is refused and writes
    /// nothing, which is what makes the token expirable rather than a permanent credential.
    /// </summary>
    [Fact]
    public async Task ExpiredTokenIsRejected()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(
            DateTime.UtcNow.AddDays(-(SubscriberSvc.UnsubscribeTokenLifetimeDays + 1)));
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        await subscriberRepo.DidNotReceive().RecordWithdrawalAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A token issued one day inside the lifetime is still honoured, so someone who opens a mail
    /// more than a year later can still get off the list.
    /// </summary>
    [Fact]
    public async Task TokenInsideTheLifetimeIsAccepted()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(
            DateTime.UtcNow.AddDays(-(SubscriberSvc.UnsubscribeTokenLifetimeDays - 1)));
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);
        subscriberRepo.RecordWithdrawalAsync(subscriber.SubscriberId, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
    }

    /// <summary>
    /// A token with no recorded issuance never expires. Those tokens were mailed before this
    /// requirement existed and cannot be recalled, so expiring them could only ever strand a
    /// subscriber with no working way off the list.
    /// </summary>
    [Fact]
    public async Task LegacyTokenWithNoRecordedIssuanceNeverExpires()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(issuedOn: null);
        subscriber.SubscribedOn = DateTime.UtcNow.AddYears(-5);
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);
        subscriberRepo.RecordWithdrawalAsync(subscriber.SubscriberId, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
    }

    /// <summary>
    /// Replaying a burned token against a subscriber who has since re-consented is refused: a spent
    /// link must not be able to opt an address out a second time after its owner came back.
    /// </summary>
    [Fact]
    public async Task BurnedTokenCannotBeReplayedAfterReconsent()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        subscriber.UnsubscribedOn = DateTime.UtcNow.AddDays(-8);
        subscriber.ConfirmedOn = DateTime.UtcNow.AddDays(-2);
        subscriber.UnsubscribeTokenUsedOn = DateTime.UtcNow.AddDays(-8);
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        await subscriberRepo.DidNotReceive().RecordWithdrawalAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Re-opening the link of a subscriber who is already withdrawn stays a no-op success, exactly
    /// as it did before this change, so the flow smoked under REQ-FN-032 is not broken and the
    /// recorded withdrawal instant is not moved.
    /// </summary>
    [Fact]
    public async Task AlreadyWithdrawnSubscriberIsReportedWithoutWriting()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        subscriber.IsConfirmed = false;
        subscriber.UnsubscribedOn = DateTime.UtcNow.AddDays(-1);
        subscriber.UnsubscribeTokenUsedOn = subscriber.UnsubscribedOn;
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.AlreadyUnsubscribed, result.Data);
        await subscriberRepo.DidNotReceive().RecordWithdrawalAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A subscriber who was still pending when they followed the link has that decision RECORDED as
    /// a withdrawal, so a later re-confirmation sweep leaves them alone instead of mailing someone
    /// who already said no.
    /// </summary>
    [Fact]
    public async Task PendingSubscriberFollowingTheLinkIsRecordedAsWithdrawn()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = new Subscriber
        {
            SubscriberId = 43,
            SubscribedOn = SignedUp,
            IsConfirmed = false,
            UnsubscribeToken = new string('b', 64)
        };
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);
        subscriberRepo.RecordWithdrawalAsync(43L, Arg.Any<CancellationToken>()).Returns(true);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await subscriberRepo.Received(1).RecordWithdrawalAsync(43L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Losing the guarded UPDATE to a concurrent redemption is a success, not a failure: the address
    /// is off the list either way and the reader must not be told their link failed.
    /// </summary>
    [Fact]
    public async Task ConcurrentRedemptionIsReportedAsAlreadyUnsubscribed()
    {
        var service = BuildService(out var subscriberRepo);
        var subscriber = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        subscriberRepo.GetByUnsubscribeTokenAsync(subscriber.UnsubscribeToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);
        subscriberRepo.RecordWithdrawalAsync(subscriber.SubscriberId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await service.UnsubscribeByTokenAsync(
            subscriber.UnsubscribeToken, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.AlreadyUnsubscribed, result.Data);
    }

    /// <summary>
    /// A blank, unknown, expired and burned token all fail with the SAME wording, so the route
    /// cannot be used to test whether a guessed token belongs to a real subscriber.
    /// </summary>
    [Fact]
    public async Task EveryRejectionReturnsTheSameWording()
    {
        var service = BuildService(out var subscriberRepo);

        var expired = ConfirmedSubscriber(
            DateTime.UtcNow.AddDays(-(SubscriberSvc.UnsubscribeTokenLifetimeDays + 1)));
        subscriberRepo.GetByUnsubscribeTokenAsync("expired", Arg.Any<CancellationToken>()).Returns(expired);

        var burned = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        burned.UnsubscribeTokenUsedOn = DateTime.UtcNow.AddDays(-3);
        subscriberRepo.GetByUnsubscribeTokenAsync("burned", Arg.Any<CancellationToken>()).Returns(burned);

        subscriberRepo.GetByUnsubscribeTokenAsync("unknown", Arg.Any<CancellationToken>())
            .Returns((Subscriber?)null);

        var blankResult = await service.UnsubscribeByTokenAsync("  ", TestContext.Current.CancellationToken);
        var unknownResult = await service.UnsubscribeByTokenAsync("unknown", TestContext.Current.CancellationToken);
        var expiredResult = await service.UnsubscribeByTokenAsync("expired", TestContext.Current.CancellationToken);
        var burnedResult = await service.UnsubscribeByTokenAsync("burned", TestContext.Current.CancellationToken);

        Assert.True(blankResult.IsFailure);
        Assert.Equal(blankResult.ErrorMessage, unknownResult.ErrorMessage);
        Assert.Equal(blankResult.ErrorMessage, expiredResult.ErrorMessage);
        Assert.Equal(blankResult.ErrorMessage, burnedResult.ErrorMessage);
    }

    /// <summary>
    /// Reactivating a lapsed subscriber re-issues the unsubscribe link, because the token they are
    /// holding is the burned one that took them off the list.
    /// </summary>
    [Fact]
    public async Task ReactivationReissuesTheUnsubscribeToken()
    {
        var service = BuildService(out var subscriberRepo);
        var lapsed = ConfirmedSubscriber(DateTime.UtcNow.AddDays(-10));
        lapsed.IsConfirmed = false;
        lapsed.IsActive = false;
        lapsed.UnsubscribedOn = DateTime.UtcNow.AddDays(-2);
        lapsed.UnsubscribeTokenUsedOn = lapsed.UnsubscribedOn;
        subscriberRepo.EmailExistsAsync(lapsed.Email, Arg.Any<CancellationToken>()).Returns(true);
        subscriberRepo.GetByEmailAsync(lapsed.Email, Arg.Any<CancellationToken>()).Returns(lapsed);

        var result = await service.SubscribeAsync(
            lapsed.Email, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data.UnsubscribeTokenUsedOn);
        Assert.Equal(64, result.Data.UnsubscribeToken.Length);
        await subscriberRepo.Received(1).RecordConsentAsync(
            lapsed.SubscriberId, Arg.Is<string>(token => token.Length == 64), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Issuing a token for a mailing installs a fresh value and hands it back, so the send path can
    /// scope the link to one issue instead of reusing a permanent one.
    /// </summary>
    [Fact]
    public async Task IssuingATokenRotatesItAndReturnsTheNewValue()
    {
        var service = BuildService(out var subscriberRepo);
        subscriberRepo.RotateUnsubscribeTokenAsync(7L, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await service.IssueUnsubscribeTokenAsync(7L, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(64, result.Data.Length);
        await subscriberRepo.Received(1).RotateUnsubscribeTokenAsync(
            7L, result.Data, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Generated tokens are 64 lower-case hexadecimal characters — the exact width of the
    /// VARCHAR(64) column — and two calls never collide.
    /// </summary>
    [Fact]
    public void GeneratedTokensAreSixtyFourHexCharactersAndUnique()
    {
        var first = SubscriberSvc.GenerateUnsubscribeToken();
        var second = SubscriberSvc.GenerateUnsubscribeToken();

        Assert.Equal(64, first.Length);
        Assert.All(first, character => Assert.Contains(character, "0123456789abcdef"));
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The withdrawal statement never writes ConfirmedOn. That single property is what stops an
    /// unsubscribe from erasing the proof of consent, and it lives in a SQL constant where no
    /// behavioural test over a fake repository can see it.
    /// </summary>
    [Fact]
    public void WithdrawalStatementNeverWritesConfirmedOn()
    {
        var statement = (string)typeof(SubscriberRepo)
            .GetField("RecordWithdrawalSql", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

        Assert.Contains("UnsubscribedOn", statement, StringComparison.Ordinal);
        Assert.Contains("UnsubscribeTokenUsedOn", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmedOn = ", statement, StringComparison.Ordinal);
    }
}
