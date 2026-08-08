using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using TrBlazeUI.Components.Badge;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Administration list of application users with role and activation management.
/// </summary>
public partial class UsersList
{
    /// <summary>User repository backing the list and the update operations.</summary>
    [Inject]
    public IBlogUserRepo BlogUserRepo { get; set; } = default!;

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

    /// <summary>True while the change-role dialog is open.</summary>
    public bool ShowRoleDialog { get; set; }

    /// <summary>User whose role is being edited.</summary>
    public AppUser? UserToEdit { get; set; }

    /// <summary>Role picked in the change-role dialog.</summary>
    public string? SelectedRole { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadUsers();
    }

    private Task LoadUsers()
    {
        try
        {
            ObjectList = BlogUserRepo.GetAll()?.ToList() ?? new List<AppUser>();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading users: {ex.Message}";
            IsError = true;
        }
        return Task.CompletedTask;
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

    /// <summary>Opens the change-role dialog for a user.</summary>
    /// <param name="user">User to edit.</param>
    public void ShowEditRoleDialog(AppUser user)
    {
        UserToEdit = user;
        SelectedRole = user.UserRole ?? "Reader";
        ShowRoleDialog = true;
    }

    /// <summary>Closes the change-role dialog without saving.</summary>
    public void CancelRoleEdit()
    {
        ShowRoleDialog = false;
        UserToEdit = null;
        SelectedRole = null;
    }

    /// <summary>Persists the role picked in the change-role dialog.</summary>
    public async Task SaveRoleChange()
    {
        if (UserToEdit == null || string.IsNullOrEmpty(SelectedRole))
        {
            CancelRoleEdit();
            return;
        }

        try
        {
            IsProcessing = true;
            UserToEdit.UserRole = SelectedRole;
            BlogUserRepo.Update(UserToEdit);
            StatusMessage = $"Role updated for {UserToEdit.FirstName} {UserToEdit.LastName}";
            IsError = false;
            await LoadUsers();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating role: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
            ShowRoleDialog = false;
            UserToEdit = null;
        }
    }

    /// <summary>Activates or deactivates a user account.</summary>
    /// <param name="user">User to toggle.</param>
    public async Task ToggleUserStatus(AppUser user)
    {
        try
        {
            IsProcessing = true;
            user.IsConfirmed = !user.IsConfirmed;
            BlogUserRepo.Update(user);
            StatusMessage = user.IsConfirmed
                ? $"{user.FirstName} {user.LastName} has been activated"
                : $"{user.FirstName} {user.LastName} has been deactivated";
            IsError = false;
            await LoadUsers();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating user status: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
