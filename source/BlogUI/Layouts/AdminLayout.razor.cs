using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Layouts;

/// <summary>
/// State and behaviour for the administration shell layout.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> supplies the signed-in identity shown in the topbar account
/// menu, the sign-out action, and the grouped navigation model rendered by the
/// TrBlazeUI sidebar.</para>
/// <para><b>Dependencies:</b> <see cref="AuthenticationStateProvider"/> (as
/// <see cref="CustomAuthStateProvider"/>) and <see cref="NavigationManager"/>.</para>
/// <para><b>UAT-021 / UAT-022:</b> also supplies the sidebar brand mark's site title and logo,
/// re-read on every <see cref="ISiteSettingsService.SettingsChanged"/> notification because this
/// layout persists for the life of the circuit — see <see cref="OnSettingsChanged"/>.</para>
/// </remarks>
public partial class AdminLayout : IDisposable
{
    /// <summary>
    /// Authentication state provider used to sign the current user out.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>
    /// Navigation manager used for post-logout and profile redirects.
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>
    /// Cascading authentication state supplied by <c>CascadingAuthenticationState</c>.
    /// </summary>
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    /// <summary>
    /// The site's public identity (title, logo) shown in the sidebar brand mark (UAT-021 /
    /// UAT-022). Defaults to the built-in fallback so the very first render — before
    /// <see cref="OnInitializedAsync"/> completes — shows real content rather than a blank mark.
    /// </summary>
    public SiteIdentity Identity { get; private set; } = new("TechieBlog", string.Empty, string.Empty);

    /// <summary>
    /// Display name of the signed-in user, shown in the account menu.
    /// </summary>
    public string CurrentUserName { get; private set; } = "Guest";

    /// <summary>
    /// Role of the signed-in user, shown beneath the name in the account menu.
    /// </summary>
    public string CurrentUserRole { get; private set; } = "Reader";

    /// <summary>
    /// Up-to-two-letter monogram rendered in the topbar avatar fallback.
    /// </summary>
    public string CurrentUserInitials => BuildInitials(CurrentUserName);

    /// <summary>
    /// One navigable destination in the admin sidebar.
    /// </summary>
    /// <param name="Label">Visible menu text, also used as the collapsed-rail tooltip.</param>
    /// <param name="Href">Destination route.</param>
    /// <param name="Icon">Lucide icon name.</param>
    /// <param name="Policy">Authorization policy that must pass for the entry to render.</param>
    /// <param name="TestId">Stable <c>data-testid</c> naming the entry by intent.</param>
    public sealed record NavEntry(string Label, string Href, string Icon, string Policy, string TestId);

    /// <summary>
    /// A titled cluster of related destinations in the admin sidebar.
    /// </summary>
    /// <param name="Label">Group heading.</param>
    /// <param name="Entries">Destinations belonging to the group.</param>
    public sealed record NavGroup(string Label, IReadOnlyList<NavEntry> Entries);

    /// <summary>
    /// The admin navigation model: Content, Taxonomy, Media, Resume, Audience, System.
    /// </summary>
    /// <remarks>
    /// <para>Each entry carries the same policy that guards its page, so the menu never
    /// offers a destination that would bounce the user to <c>/access-denied</c>.</para>
    /// <para><b>REQ-FN-009 audit (2026-08-07):</b> four entries had drifted away from their
    /// page attribute and were corrected against the BRD screen tables — Series is
    /// <c>AuthorOrAbove</c> (BRD §F-SERIES), Categories and Tags are <c>AdminOnly</c>
    /// (BRD §F-TAX) and Images is <c>AdminOnly</c> (BRD §F-MEDIA). The Dashboard entry,
    /// declared in the markup, moved to <c>EditorOrAbove</c> for the same reason.</para>
    /// </remarks>
    public static readonly IReadOnlyList<NavGroup> NavGroups = new List<NavGroup>
    {
        new("Content", new List<NavEntry>
        {
            new("Posts", "/BlogsList", "file-text", AppPolicies.AuthorOrAbove, "nav-posts"),
            new("Series", "/admin/series", "layers", AppPolicies.AuthorOrAbove, "nav-series"),
            new("Comments", "/CommentsList", "message-square", AppPolicies.EditorOrAbove, "nav-comments")
        }),
        new("Taxonomy", new List<NavEntry>
        {
            new("Categories", "/admin/categories", "folder", AppPolicies.AdminOnly, "nav-categories"),
            new("Tags", "/admin/tags", "tag", AppPolicies.AdminOnly, "nav-tags")
        }),
        new("Media", new List<NavEntry>
        {
            new("Images", "/admin/images", "image", AppPolicies.AdminOnly, "nav-images")
        }),
        new("Resume", new List<NavEntry>
        {
            new("My Profile", "/admin/profile", "user", AppPolicies.AuthorOrAbove, "nav-profile"),
            new("Experience", "/admin/experience", "briefcase", AppPolicies.AuthorOrAbove, "nav-experience"),
            new("Skills", "/admin/skills", "zap", AppPolicies.AuthorOrAbove, "nav-skills"),
            new("Awards", "/admin/awards", "award", AppPolicies.AuthorOrAbove, "nav-awards"),
            new("Statistics", "/admin/stats", "trending-up", AppPolicies.AuthorOrAbove, "nav-stats"),
            new("Speaking", "/admin/speaking", "mic", AppPolicies.EditorOrAbove, "nav-speaking")
        }),
        new("Audience", new List<NavEntry>
        {
            new("Users", "/users", "users", AppPolicies.AdminOnly, "nav-users"),
            new("Subscribers", "/admin/subscribers", "mail", AppPolicies.AdminOnly, "nav-subscribers"),
            new("Newsletter", "/admin/newsletter", "send", AppPolicies.AdminOnly, "nav-newsletter"),
            new("Analytics", "/admin/analytics", "bar-chart-3", AppPolicies.EditorOrAbove, "nav-analytics")
        }),
        new("System", new List<NavEntry>
        {
            new("Settings", "/settings", "settings", AppPolicies.AdminOnly, "nav-settings")
        })
    };

    /// <summary>
    /// The subset of <see cref="NavGroups"/> the signed-in role can actually use.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> a group whose every entry is refused renders as a bare
    /// heading with nothing under it — an Author saw empty "Taxonomy", "Media", "Audience" and
    /// "System" labels once those entries were corrected to <c>AdminOnly</c>. Filtering the model
    /// by role removes the heading with its entries (REQ-FN-009, BRD-9: hide what the role
    /// cannot use).</para>
    /// </remarks>
    public IReadOnlyList<NavGroup> VisibleGroups { get; private set; } = new List<NavGroup>();

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Identity = await SiteSettingsService.GetSiteIdentityAsync();

        // This layout persists for the life of the circuit — client-side navigation does not
        // recreate it — so without this subscription an administrator who saves a new title or
        // logo on /settings would not see their own sidebar update until a hard refresh
        // (UAT-021 §2).
        SiteSettingsService.SettingsChanged += OnSettingsChanged;

        if (AuthStateTask is null)
        {
            return;
        }

        var authState = await AuthStateTask;
        if (authState.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        CurrentUserName = authState.User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
        CurrentUserRole = authState.User.FindFirst(ClaimTypes.Role)?.Value ?? "Reader";
        VisibleGroups = BuildVisibleGroups(CurrentUserRole);
    }

    /// <summary>
    /// Refreshes the rendered brand identity when a save is published elsewhere in the app.
    /// </summary>
    /// <remarks>
    /// <see cref="ISiteSettingsService.SettingsChanged"/> is raised on the saving circuit's
    /// thread, which is not necessarily this component's renderer, so the update is marshalled
    /// through <see cref="ComponentBase.InvokeAsync(Func{Task})"/>.
    /// </remarks>
    private void OnSettingsChanged(object? sender, SiteSettings settings)
    {
        _ = InvokeAsync(async () =>
        {
            Identity = await SiteSettingsService.GetSiteIdentityAsync();
            StateHasChanged();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The settings service is a singleton; an unsubscribed handler here would keep every
        // disposed circuit's AdminLayout alive for the life of the process.
        SiteSettingsService.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>
    /// Navigates to the signed-in user's own resume profile page.
    /// </summary>
    public void NavigateToProfile()
    {
        NavigationManager.NavigateTo("/admin/profile");
    }

    /// <summary>
    /// Signs the current user out and returns them to the public home page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Destroys the persisted session — both local-storage tokens and
    /// the session cookie — then forces a full reload of <c>/</c>, so no stale circuit state
    /// survives and the reloaded app re-reads the now-empty storage as anonymous.</para>
    ///
    /// <para><b>Clearing and notifying are deliberately NOT done together here, and swapping this
    /// back to <c>MarkUserAsLoggedOut</c> will silently break the destination.</b> That method
    /// publishes the anonymous principal synchronously, which re-renders the admin route the user is
    /// still standing on; <c>AuthorizeRouteView</c> then falls into <c>&lt;NotAuthorized&gt;</c> and
    /// <c>RedirectToLogin</c> force-navigates to <c>/login?returnUrl=%2Fadmin</c> before the line
    /// below ever runs. The summary above said "returns them to the public home page" while the
    /// application actually sent them to the login screen with a return link back into admin — for
    /// long enough that the <c>NavigateTo</c> here was simply dead code nobody had noticed (UAT-018).
    /// <see cref="CustomAuthStateProvider.ClearPersistedSessionAsync"/> does the security-relevant
    /// half without the notification, and the forced reload supplies the anonymous state.</para>
    ///
    /// <para><b>Side Effects:</b> Clears browser session state and reloads the application at the
    /// public home page.</para>
    /// </remarks>
    /// <returns>A task that completes once the session has been cleared and navigation started.</returns>
    public async Task LogoutAsync()
    {
        await ((CustomAuthStateProvider)AuthStateProvider).ClearPersistedSessionAsync();
        NavigationManager.NavigateTo("/", forceLoad: true);
    }

    /// <summary>
    /// Filters the navigation model down to what a role may open.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> keeps an entry when <see cref="AppPolicies.IsSatisfiedBy"/>
    /// grants the entry's policy to the role — the same map <c>Program.cs</c> registers the
    /// policies from — then drops any group left with no entries.</para>
    /// <para><b>Flow:</b> per group → filter entries → discard empty groups.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="roleName">The signed-in user's role.</param>
    /// <returns>Groups containing only the entries the role is authorised for.</returns>
    private static IReadOnlyList<NavGroup> BuildVisibleGroups(string roleName)
    {
        var visible = new List<NavGroup>();

        foreach (var group in NavGroups)
        {
            var entries = group.Entries
                .Where(entry => AppPolicies.IsSatisfiedBy(entry.Policy, roleName))
                .ToList();

            if (entries.Count > 0)
            {
                visible.Add(new NavGroup(group.Label, entries));
            }
        }

        return visible;
    }

    /// <summary>
    /// Derives an avatar monogram from a display name.
    /// </summary>
    /// <param name="name">The user's display name.</param>
    /// <returns>One or two uppercase initials, or "?" when no name is available.</returns>
    private static string BuildInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return parts[0][..1].ToUpperInvariant();
        }

        return string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant();
    }
}
