using System.Security.Claims;
using BlogModels;
using BlogModels.Models;
using BlogUI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TrBlazeUI.Components.Badge;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Administration list of application users with role and activation management.
/// </summary>
public partial class UsersList
{

    /// <summary>
    /// Curated messages shown when an unexpected failure is caught on this page (REQ-NFR-033).
    /// </summary>
    /// <remarks>
    /// <para>These assignments previously interpolated <c>ex.Message</c>. The page is gated by
    /// <c>AppPolicies.AdminOnly</c>, which was the defence offered for the disclosure, but an
    /// exception's text is not written for an audience and routinely carries a SQL fragment, a
    /// table name or a file-system path — none of which an administrator can act on and all of
    /// which end up in a screenshot pasted into a ticket.</para>
    /// <para>The engine service beneath every one of these calls already logs the exception with
    /// its own context through <c>ILogger&lt;T&gt;</c>, where the host's
    /// <c>CorrelationIdMiddleware</c> has stamped the request's correlation id onto the event
    /// (REQ-NFR-015), so nothing is lost by curating here. This page injects no logger of its own;
    /// adding one is tracked as a follow-up.</para>
    /// </remarks>
    private const string LoadFailureMessage =
        "Could not load users. Please try again later.";

    private const string StatusFailureMessage =
        "Could not update the user status. Please try again later.";

    private const string EditFailureMessage =
        "Could not save the changes. Please try again later.";

    private const string DeleteFailureMessage =
        "Could not delete the user. Please try again later.";

    /// <summary>User repository backing the list and the update operations.</summary>
    [Inject]
    public IBlogUserRepo BlogUserRepo { get; set; } = default!;

    /// <summary>
    /// Supplies the signed-in administrator, so the page can refuse to let them delete or deactivate
    /// their own account.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>All users loaded from the repository.</summary>
    public List<AppUser> ObjectList { get; set; } = new();

    /// <summary>Users remaining after the role and search filters are applied.</summary>
    public List<AppUser> FilteredList { get; set; } = new();

    /// <summary>Active role tab: all, admin, editor or reader.</summary>
    public string RoleFilter { get; set; } = "all";

    /// <summary>Free-text search term applied to name and email.</summary>
    public string SearchTerm { get; set; } = "";

    /// <summary>Feedback message rendered in the page alert.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>True when <see cref="StatusMessage"/> describes a failure.</summary>
    public bool IsError { get; set; }

    /// <summary>True while a repository update is running.</summary>
    public bool IsProcessing { get; set; }

    /// <summary>Number of users whose role is Admin.</summary>
    public int AdminCount => ObjectList?.Count(u => u.UserRole?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true) ?? 0;

    /// <summary>Number of users whose role is Editor.</summary>
    public int EditorCount => ObjectList?.Count(u => u.UserRole?.Equals("Editor", StringComparison.OrdinalIgnoreCase) == true) ?? 0;

    /// <summary>Number of users whose role is Reader.</summary>
    public int ReaderCount => ObjectList?.Count(u => u.UserRole?.Equals("Reader", StringComparison.OrdinalIgnoreCase) == true) ?? 0;

    /// <summary>True when a role tab other than "all" or a search term is active.</summary>
    public bool HasActiveFilters => RoleFilter != "all" || !string.IsNullOrEmpty(SearchTerm);

    /// <summary>Heading rendered by the empty state.</summary>
    public string EmptyTitle => HasActiveFilters ? "No users match your filters" : "No users yet";

    /// <summary>Body text rendered by the empty state.</summary>
    public string EmptyDescription => HasActiveFilters
        ? "Try a different role tab or clear the search term."
        : "Add the first account to get started.";

    /// <summary>True while the edit-user dialog is open.</summary>
    public bool ShowEditDialog { get; set; }

    /// <summary>User being edited.</summary>
    public AppUser? UserToEdit { get; set; }

    /// <summary>First name bound to the edit dialog.</summary>
    public string EditFirstName { get; set; } = string.Empty;

    /// <summary>Last name bound to the edit dialog.</summary>
    public string EditLastName { get; set; } = string.Empty;

    /// <summary>Email address bound to the edit dialog.</summary>
    public string EditEmail { get; set; } = string.Empty;

    /// <summary>Role picked in the edit dialog.</summary>
    public string? SelectedRole { get; set; }

    /// <summary>Validation message shown inside the edit dialog, or null when the form is valid.</summary>
    public string? EditValidationMessage { get; set; }

    /// <summary>True while the delete-confirmation dialog is open.</summary>
    public bool ShowDeleteDialog { get; set; }

    /// <summary>User queued for deletion, pending confirmation.</summary>
    public AppUser? UserToDelete { get; set; }

    /// <summary>
    /// The signed-in administrator's own user id, or null when it could not be resolved.
    /// </summary>
    /// <remarks>
    /// Backs the self-protection guards. When it cannot be resolved the guards fail CLOSED — an
    /// unknown current user is treated as "might be this row", so destructive actions are refused
    /// rather than allowed on an unverified identity.
    /// </remarks>
    public long? CurrentUserId { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await ResolveCurrentUser();
        await LoadUsers();
    }

    /// <summary>
    /// Resolves the signed-in administrator's user id from the authentication state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The id arrives as the <c>PrimarySid</c> claim, the same claim the
    /// other administration pages read. A missing or unparsable claim leaves
    /// <see cref="CurrentUserId"/> null, which the guards treat as "cannot prove this is not me".</para>
    /// <para><b>Side Effects:</b> Sets <see cref="CurrentUserId"/>.</para>
    /// </remarks>
    private async Task ResolveCurrentUser()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid);

            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
            {
                CurrentUserId = userId;
            }
        }
        catch (Exception)
        {
            CurrentUserId = null;
        }
    }

    private async Task LoadUsers()
    {
        try
        {
            ObjectList = (await BlogUserRepo.GetAllAsync())?.ToList() ?? new List<AppUser>();
            ApplyFilter();
        }
        catch (Exception)
        {
            StatusMessage = LoadFailureMessage;
            IsError = true;
        }
    }

    /// <summary>Switches the active role tab.</summary>
    /// <param name="filter">Role tab key.</param>
    public void SetFilter(string filter)
    {
        RoleFilter = filter;
        ApplyFilter();
    }

    /// <summary>Recomputes <see cref="FilteredList"/> from the role tab and the search term.</summary>
    public void ApplyFilter()
    {
        if (ObjectList == null)
        {
            FilteredList = new List<AppUser>();
            return;
        }

        IEnumerable<AppUser> query = ObjectList;

        // Apply role filter
        if (RoleFilter != "all")
        {
            query = query.Where(u => !string.IsNullOrEmpty(u.UserRole) &&
                u.UserRole.Equals(RoleFilter, StringComparison.OrdinalIgnoreCase));
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var term = SearchTerm.ToLower();
            query = query.Where(u =>
                (!string.IsNullOrEmpty(u.FirstName) && u.FirstName.ToLower().Contains(term)) ||
                (!string.IsNullOrEmpty(u.LastName) && u.LastName.ToLower().Contains(term)) ||
                (!string.IsNullOrEmpty(u.EmailId) && u.EmailId.ToLower().Contains(term)));
        }

        FilteredList = query.ToList();
        StateHasChanged();
    }

    /// <summary>Resets the role tab and the search term.</summary>
    public void ClearFilters()
    {
        RoleFilter = "all";
        SearchTerm = "";
        ApplyFilter();
    }

    /// <summary>Maps a role name onto a badge variant.</summary>
    /// <param name="role">Role name.</param>
    /// <returns>The badge variant to render.</returns>
    public BadgeVariant GetRoleBadgeVariant(string role)
    {
        return role?.ToLower() switch
        {
            "admin" => BadgeVariant.Destructive,
            "editor" => BadgeVariant.Default,
            "author" => BadgeVariant.Outline,
            _ => BadgeVariant.Secondary
        };
    }

    /// <summary>Builds the two-letter avatar fallback for a user.</summary>
    /// <param name="user">User to describe.</param>
    /// <returns>Up to two upper-case initials, or "?" when the name is unknown.</returns>
    public string GetInitials(AppUser user)
    {
        if (user == null)
        {
            return "?";
        }

        var first = string.IsNullOrEmpty(user.FirstName) ? string.Empty : user.FirstName.Substring(0, 1);
        var last = string.IsNullOrEmpty(user.LastName) ? string.Empty : user.LastName.Substring(0, 1);
        var initials = (first + last).ToUpperInvariant();
        return string.IsNullOrEmpty(initials) ? "?" : initials;
    }

    /// <summary>True when the row belongs to the signed-in administrator.</summary>
    /// <remarks>
    /// Fails CLOSED: an unresolved <see cref="CurrentUserId"/> answers true, so a session whose
    /// identity could not be established cannot delete or deactivate anything.
    /// </remarks>
    /// <param name="user">Row being tested.</param>
    /// <returns>True when the row is, or might be, the current administrator.</returns>
    public bool IsSelf(AppUser user) => CurrentUserId is null || user.UserId == CurrentUserId;

    /// <summary>
    /// True when deactivating or deleting this row would leave the site with no active administrator.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts the OTHER accounts that are both Admin and active. If none
    /// remain, this row is the last way into the administration area and removing it would lock
    /// everybody out of a live site with no recovery path short of a database edit.</para>
    /// </remarks>
    /// <param name="user">Row being tested.</param>
    /// <returns>True when this is the last active administrator.</returns>
    public bool IsLastActiveAdmin(AppUser user)
    {
        if (!string.Equals(user.UserRole, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ObjectList.Any(other =>
            other.UserId != user.UserId
            && other.IsConfirmed
            && string.Equals(other.UserRole, "Admin", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Explains why a row cannot be deleted, or null when it can be.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Three refusals, each of which would otherwise break the running
    /// site rather than merely inconvenience the administrator: deleting yourself ends your own
    /// session mid-action, deleting the site owner blanks the public landing page and <c>/resume</c>
    /// (both read <c>GetSiteOwner</c>), and deleting the last active administrator locks everyone out
    /// of the admin area. The database enforces the site-owner rule too — this is the half that can
    /// explain itself to a human.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="user">Row being tested.</param>
    /// <returns>The reason deletion is refused, or null when deletion is allowed.</returns>
    public string? DeleteBlockReason(AppUser user)
    {
        if (IsSelf(user))
        {
            return "You cannot delete your own account.";
        }

        if (user.IsSiteOwner)
        {
            return "The site owner cannot be deleted — the public home page and resume are built from this account.";
        }

        if (IsLastActiveAdmin(user))
        {
            return "This is the last active administrator. Promote another account first.";
        }

        return null;
    }

    /// <summary>
    /// Explains why a row's activation cannot be toggled, or null when it can be.
    /// </summary>
    /// <remarks>
    /// Deactivation carries the same lock-out risks as deletion, minus the site-owner rule: an
    /// inactive owner still renders the public pages, because those read the profile columns and not
    /// the confirmation flag. Activating an account is never blocked.
    /// </remarks>
    /// <param name="user">Row being tested.</param>
    /// <returns>The reason the toggle is refused, or null when it is allowed.</returns>
    public string? StatusBlockReason(AppUser user)
    {
        if (!user.IsConfirmed)
        {
            return null;
        }

        if (IsSelf(user))
        {
            return "You cannot deactivate your own account.";
        }

        if (IsLastActiveAdmin(user))
        {
            return "This is the last active administrator. Promote another account first.";
        }

        return null;
    }

    /// <summary>Opens the edit dialog for a user.</summary>
    /// <param name="user">User to edit.</param>
    public void ShowEditUserDialog(AppUser user)
    {
        UserToEdit = user;
        EditFirstName = user.FirstName ?? string.Empty;
        EditLastName = user.LastName ?? string.Empty;
        EditEmail = user.EmailId ?? string.Empty;
        SelectedRole = string.IsNullOrWhiteSpace(user.UserRole) ? "Reader" : user.UserRole;
        EditValidationMessage = null;
        ShowEditDialog = true;
    }

    /// <summary>Closes the edit dialog without saving.</summary>
    public void CancelUserEdit()
    {
        ShowEditDialog = false;
        UserToEdit = null;
        SelectedRole = null;
        EditValidationMessage = null;
    }

    /// <summary>
    /// Persists the name, email address and role entered in the edit dialog.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Validates in the dialog rather than on the page, so a rejected
    /// save keeps the administrator's typing instead of discarding it behind a closed dialog. The
    /// email address must stay unique — the database holds a case-insensitive unique index on it
    /// (migration 020) — so a collision is caught here and reported as a field error rather than
    /// surfacing as an unhandled constraint violation.</para>
    /// <para><b>Demoting the last administrator is refused</b> for the same reason deactivating one
    /// is: it is the last way into the admin area.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row and reloads the list.</para>
    /// </remarks>
    public async Task SaveUserChanges()
    {
        if (UserToEdit == null)
        {
            CancelUserEdit();
            return;
        }

        EditValidationMessage = ValidateEdit(UserToEdit);
        if (EditValidationMessage != null)
        {
            return;
        }

        try
        {
            IsProcessing = true;

            UserToEdit.FirstName = EditFirstName.Trim();
            UserToEdit.LastName = EditLastName.Trim();
            UserToEdit.EmailId = EditEmail.Trim();
            UserToEdit.UserRole = SelectedRole!;

            await BlogUserRepo.UpdateAsync(UserToEdit);
            StatusMessage = $"Saved changes to {UserToEdit.FirstName} {UserToEdit.LastName}";
            IsError = false;
            ShowEditDialog = false;
            UserToEdit = null;
            await LoadUsers();
        }
        catch (Exception)
        {
            StatusMessage = EditFailureMessage;
            IsError = true;
            ShowEditDialog = false;
            UserToEdit = null;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>
    /// Validates the edit dialog's fields against the row being edited.
    /// </summary>
    /// <param name="user">The row being edited.</param>
    /// <returns>The first validation failure, or null when the form is valid.</returns>
    private string? ValidateEdit(AppUser user)
    {
        if (string.IsNullOrWhiteSpace(EditFirstName) || string.IsNullOrWhiteSpace(EditLastName))
        {
            return "First name and last name are both required.";
        }

        if (string.IsNullOrWhiteSpace(EditEmail) || !EditEmail.Contains('@', StringComparison.Ordinal))
        {
            return "Enter a valid email address.";
        }

        if (string.IsNullOrWhiteSpace(SelectedRole))
        {
            return "Pick a role.";
        }

        var email = EditEmail.Trim();
        var taken = ObjectList.Any(other =>
            other.UserId != user.UserId
            && string.Equals(other.EmailId, email, StringComparison.OrdinalIgnoreCase));

        if (taken)
        {
            return "Another account already uses that email address.";
        }

        var demotingLastAdmin =
            !string.Equals(SelectedRole, "Admin", StringComparison.OrdinalIgnoreCase)
            && IsLastActiveAdmin(user);

        if (demotingLastAdmin)
        {
            return "This is the last active administrator — promote another account before changing this role.";
        }

        return null;
    }

    /// <summary>Opens the delete-confirmation dialog for a user.</summary>
    /// <param name="user">User queued for deletion.</param>
    public void ShowDeleteUserDialog(AppUser user)
    {
        UserToDelete = user;
        ShowDeleteDialog = true;
    }

    /// <summary>Closes the delete-confirmation dialog without deleting.</summary>
    public void CancelDelete()
    {
        ShowDeleteDialog = false;
        UserToDelete = null;
    }

    /// <summary>
    /// Soft-deletes the confirmed user.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Re-checks <see cref="DeleteBlockReason"/> at the point of action
    /// rather than trusting that the button was disabled — the list can have been reloaded, or the
    /// last other administrator deactivated, between the dialog opening and the click.</para>
    /// <para><b>Side Effects:</b> Flags one <c>BlogUser</c> row deleted and inactive; the account's
    /// posts and comments stay published and attributed. Reloads the list.</para>
    /// </remarks>
    public async Task ConfirmDelete()
    {
        if (UserToDelete == null)
        {
            CancelDelete();
            return;
        }

        var blocked = DeleteBlockReason(UserToDelete);
        if (blocked != null)
        {
            StatusMessage = blocked;
            IsError = true;
            CancelDelete();
            return;
        }

        var displayName = $"{UserToDelete.FirstName} {UserToDelete.LastName}".Trim();

        try
        {
            IsProcessing = true;
            var deleted = await BlogUserRepo.SoftDeleteUserAsync(UserToDelete.UserId);

            if (deleted)
            {
                StatusMessage = $"{displayName} has been deleted. Their posts and comments remain published.";
                IsError = false;
            }
            else
            {
                // The database refused — the row is the site owner, or somebody else deleted it first.
                StatusMessage = $"{displayName} could not be deleted. Refresh the list and try again.";
                IsError = true;
            }

            await LoadUsers();
        }
        catch (Exception)
        {
            StatusMessage = DeleteFailureMessage;
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
            ShowDeleteDialog = false;
            UserToDelete = null;
        }
    }

    /// <summary>Activates or deactivates a user account.</summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes through <c>SetUserActiveAsync</c>, which targets
    /// <c>IsConfirmed</c> directly. The previous implementation flipped the flag on the in-memory
    /// model and called the general <c>Update</c>, whose stored function does not carry the column —
    /// so the change was discarded, the list reloaded, and the badge silently reverted.</para>
    /// <para><b>Side Effects:</b> Updates one row's <c>IsConfirmed</c> and reloads the list.</para>
    /// </remarks>
    /// <param name="user">User to toggle.</param>
    public async Task ToggleUserStatus(AppUser user)
    {
        var blocked = StatusBlockReason(user);
        if (blocked != null)
        {
            StatusMessage = blocked;
            IsError = true;
            return;
        }

        var target = !user.IsConfirmed;

        try
        {
            IsProcessing = true;
            var updated = await BlogUserRepo.SetUserActiveAsync(user.UserId, target);

            if (updated)
            {
                StatusMessage = target
                    ? $"{user.FirstName} {user.LastName} has been activated"
                    : $"{user.FirstName} {user.LastName} has been deactivated";
                IsError = false;
            }
            else
            {
                StatusMessage = StatusFailureMessage;
                IsError = true;
            }

            await LoadUsers();
        }
        catch (Exception)
        {
            StatusMessage = StatusFailureMessage;
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
