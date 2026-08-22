using Blazored.LocalStorage;
using BlogModels;
using BlogModels.Interfaces;
using BlogUI;
using Microsoft.AspNetCore.Components.Authorization;
using NSubstitute;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests pinning the separation between destroying a session and announcing it (UAT-018).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>AdminLayout.LogoutAsync</c> is documented as returning the visitor to the
/// public home page, and for a long time it did not. It called <c>MarkUserAsLoggedOut</c>, which
/// publishes the anonymous principal <b>synchronously</b>; that re-rendered the admin route the user
/// was still standing on, <c>AuthorizeRouteView</c> fell into <c>&lt;NotAuthorized&gt;</c>, and
/// <c>RedirectToLogin</c> force-navigated to <c>/login?returnUrl=%2Fadmin</c> before the caller's own
/// <c>NavigateTo("/")</c> could run. The navigation on the next line was dead code, and nothing
/// failed — the destination was simply wrong, quietly, for as long as nobody looked.</para>
///
/// <para><b>What these tests defend:</b> the fix works only because clearing and notifying are now
/// separable. Re-merging them — the obvious "simplification" for a future reader who sees two methods
/// that look alike — silently restores the wrong destination without breaking a build or a UI test
/// that only checks the user ended up signed out. That is exactly the shape of regression this
/// repository has been bitten by before, so it is pinned here rather than left to a smoke run.</para>
///
/// <para><b>Dependencies:</b> NSubstitute for local storage and the auth service. No browser, no
/// database — these assert the provider's contract, not the navigation, because the navigation is a
/// consequence of the contract.</para>
/// </remarks>
public class LogoutStateTests
{
    /// <summary>
    /// Clearing the persisted session must NOT publish an authentication-state change.
    /// </summary>
    /// <remarks>
    /// This is the whole fix in one assertion. The notification is what triggers the redirect race;
    /// a caller that is about to force a full reload does not want it, and must be able to opt out.
    /// </remarks>
    [Fact]
    public async Task ClearingPersistedSessionDoesNotNotify()
    {
        var provider = BuildProvider(out var storage);
        var notified = false;
        provider.AuthenticationStateChanged += _ => notified = true;

        await provider.ClearPersistedSessionAsync();

        Assert.False(
            notified,
            "ClearPersistedSessionAsync must not raise AuthenticationStateChanged. Publishing the " +
            "anonymous principal re-renders the protected page the user is still on, which sends " +
            "RedirectToLogin to /login?returnUrl=... and overrides the caller's own navigation " +
            "(UAT-018).");

        await storage.Received(1).RemoveItemAsync(AppConstants.AccessKey);
        await storage.Received(1).RemoveItemAsync(AppConstants.RefreshKey);
    }

    /// <summary>
    /// The notifying overload must still notify, because a caller staying on the circuit needs the
    /// UI to react.
    /// </summary>
    /// <remarks>
    /// The positive control for the test above: if BOTH methods went quiet the first assertion would
    /// pass for the wrong reason, and any screen relying on the state change would stop updating.
    /// </remarks>
    [Fact]
    public async Task MarkingUserLoggedOutStillNotifies()
    {
        var provider = BuildProvider(out _);
        var notified = false;
        provider.AuthenticationStateChanged += _ => notified = true;

        await provider.MarkUserAsLoggedOut();

        Assert.True(
            notified,
            "MarkUserAsLoggedOut must keep raising AuthenticationStateChanged — a caller that stays " +
            "on the current circuit depends on it to re-render as anonymous.");
    }

    /// <summary>
    /// Both paths must destroy the same persisted state, so choosing the quiet one cannot be a
    /// security downgrade.
    /// </summary>
    /// <remarks>
    /// The reason to separate them was the redirect, never the clearing. If the quiet path ever
    /// cleared less, "log out" would leave a usable token behind on the exact route that now uses it.
    /// </remarks>
    [Fact]
    public async Task BothLogoutPathsDestroyTheSamePersistedState()
    {
        var quiet = BuildProvider(out var quietStorage);
        await quiet.ClearPersistedSessionAsync();

        var loud = BuildProvider(out var loudStorage);
        await loud.MarkUserAsLoggedOut();

        foreach (var key in new[] { AppConstants.AccessKey, AppConstants.RefreshKey })
        {
            await quietStorage.Received(1).RemoveItemAsync(key);
            await loudStorage.Received(1).RemoveItemAsync(key);
        }
    }

    /// <summary>
    /// Builds a provider over substituted dependencies.
    /// </summary>
    /// <param name="storage">The substituted local storage, for call assertions.</param>
    /// <returns>The provider under test.</returns>
    private static CustomAuthStateProvider BuildProvider(out ILocalStorageService storage)
    {
        storage = Substitute.For<ILocalStorageService>();
        return new CustomAuthStateProvider(storage, Substitute.For<IAuthService>());
    }
}
