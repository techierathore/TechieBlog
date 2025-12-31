using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.AdminPages
{
    public partial class UsersList
    {
        [Inject]
        public IBlogUserRepo BlogUserRepo { get; set; }

        public List<AppUser> ObjectList { get; set; }
        public List<AppUser> FilteredList { get; set; }
        public string RoleFilter { get; set; } = "all";
        public string SearchTerm { get; set; } = "";
        public string StatusMessage { get; set; }
        public bool IsError { get; set; }
        public bool IsProcessing { get; set; }

        // Counts
        public int AdminCount => ObjectList?.Count(u => u.UserRole?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true) ?? 0;
        public int EditorCount => ObjectList?.Count(u => u.UserRole?.Equals("Editor", StringComparison.OrdinalIgnoreCase) == true) ?? 0;
        public int ReaderCount => ObjectList?.Count(u => u.UserRole?.Equals("Reader", StringComparison.OrdinalIgnoreCase) == true) ?? 0;

        // Role Dialog
        public bool ShowRoleDialog { get; set; }
        public AppUser UserToEdit { get; set; }
        public string SelectedRole { get; set; }

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

        public void SetFilter(string filter)
        {
            RoleFilter = filter;
            ApplyFilter();
        }

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

        public void ClearFilters()
        {
            RoleFilter = "all";
            SearchTerm = "";
            ApplyFilter();
        }

        public string GetRoleBadgeClass(string role)
        {
            return role?.ToLower() switch
            {
                "admin" => "badge--admin",
                "editor" => "badge--editor",
                "author" => "badge--author",
                _ => ""
            };
        }

        public void ShowEditRoleDialog(AppUser user)
        {
            UserToEdit = user;
            SelectedRole = user.UserRole ?? "Reader";
            ShowRoleDialog = true;
        }

        public void CancelRoleEdit()
        {
            ShowRoleDialog = false;
            UserToEdit = null;
            SelectedRole = null;
        }

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
}
