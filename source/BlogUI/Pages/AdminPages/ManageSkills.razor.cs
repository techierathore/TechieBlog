using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ManageSkills.razor.
/// Handles CRUD operations for user skills grouped by category.
/// </summary>
public partial class ManageSkills
{
    [Inject]
    public IUserSkillsRepo SkillsRepo { get; set; } = default!;

    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    [Inject]
    public NavigationManager NavManager { get; set; } = default!;

    /// <summary>
    /// Sentinel value used by the category select to mean "create a new category".
    /// </summary>
    private const string NewCategorySentinel = "__new__";

    // State
    private bool IsLoading = true;
    private bool IsAdmin = false;
    private long CurrentUserId;
    private long SelectedUserId;
    private List<AppUser>? AllUsers;
    private List<UserSkill> AllSkills = new();
    private IEnumerable<IGrouping<string, UserSkill>>? GroupedSkills;
    private HashSet<string> CollapsedCategories = new();
    private List<string> ExistingCategories = new();

    // Status messages
    private string? StatusMessage;
    private bool IsError = false;

    // Add Skill Dialog
    private bool ShowAddDialog = false;
    private string NewSkillName = string.Empty;
    private string NewSkillCategory = string.Empty;
    private string NewCategoryName = string.Empty;

    // Edit Skill
    private long EditingSkillId = 0;
    private string EditSkillName = string.Empty;
    private string EditSkillCategory = string.Empty;

    // Delete Skill Dialog
    private bool ShowDeleteDialog = false;
    private UserSkill? SkillToDelete;

    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentUser();
        await LoadSkills();
    }

    private async Task LoadCurrentUser()
    {
        try
        {
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid);
            var roleClaim = authState.User.FindFirst(ClaimTypes.Role);

            if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
            {
                CurrentUserId = userId;
                SelectedUserId = userId;
            }

            // Check if user is admin
            IsAdmin = roleClaim?.Value?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

            if (IsAdmin)
            {
                // Load all users for admin dropdown
                AllUsers = UserRepo.GetAll()?.ToList() ?? new List<AppUser>();
            }
        }
        catch
        {
            CurrentUserId = 0;
            SelectedUserId = 0;
        }
    }

    private Task LoadSkills()
    {
        IsLoading = true;

        try
        {
            if (SelectedUserId > 0)
            {
                AllSkills = SkillsRepo.GetByUserId(SelectedUserId).ToList();
                GroupedSkills = AllSkills
                    .GroupBy(s => s.Category ?? "Uncategorized")
                    .OrderBy(g => g.Key);

                // Build list of existing categories
                ExistingCategories = AllSkills
                    .Select(s => s.Category ?? "Uncategorized")
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                // If no categories exist, add some defaults
                if (!ExistingCategories.Any())
                {
                    ExistingCategories = new List<string>
                    {
                        "AI/Emerging",
                        "Cloud/SaaS",
                        "Development",
                        "Database",
                        "DevOps",
                        "Soft Skills"
                    };
                }
            }
            else
            {
                AllSkills = new List<UserSkill>();
                GroupedSkills = Enumerable.Empty<IGrouping<string, UserSkill>>();
            }

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading skills: {ex.Message}";
            IsError = true;
            AllSkills = new List<UserSkill>();
            GroupedSkills = Enumerable.Empty<IGrouping<string, UserSkill>>();
        }
        finally
        {
            IsLoading = false;
        }

        return Task.CompletedTask;
    }

    private async Task OnUserSelectionChanged(long userId)
    {
        SelectedUserId = userId;
        await LoadSkills();
        StateHasChanged();
    }

    /// <summary>
    /// Handles the admin user picker selecting a different author.
    /// </summary>
    /// <param name="value">The selected user id as text.</param>
    private async Task OnSelectedUserChanged(string value)
    {
        if (long.TryParse(value, out var userId))
        {
            await OnUserSelectionChanged(userId);
        }
    }

    private void ToggleCategory(string category)
    {
        if (CollapsedCategories.Contains(category))
        {
            CollapsedCategories.Remove(category);
        }
        else
        {
            CollapsedCategories.Add(category);
        }
    }

    #region Add Skill

    private void ShowAddSkillDialog()
    {
        NewSkillName = string.Empty;
        NewSkillCategory = ExistingCategories.FirstOrDefault() ?? string.Empty;
        NewCategoryName = string.Empty;
        ShowAddDialog = true;
    }

    private void ShowAddSkillToCategory(string category)
    {
        NewSkillName = string.Empty;
        NewSkillCategory = category;
        NewCategoryName = string.Empty;
        ShowAddDialog = true;
    }

    private void CancelAddSkill()
    {
        ShowAddDialog = false;
        NewSkillName = string.Empty;
        NewSkillCategory = string.Empty;
        NewCategoryName = string.Empty;
    }

    /// <summary>
    /// Keeps the add-skill dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    private void OnAddDialogOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelAddSkill();
        }
    }

    private async Task SaveNewSkill()
    {
        if (string.IsNullOrWhiteSpace(NewSkillName))
        {
            StatusMessage = "Skill name is required.";
            IsError = true;
            return;
        }

        var category = NewSkillCategory == NewCategorySentinel ? NewCategoryName : NewSkillCategory;

        if (string.IsNullOrWhiteSpace(category))
        {
            StatusMessage = "Category is required.";
            IsError = true;
            return;
        }

        try
        {
            // Get max display order in category
            var categorySkills = AllSkills.Where(s => s.Category == category);
            var maxOrder = categorySkills.Any() ? categorySkills.Max(s => s.DisplayOrder) : 0;

            var newSkill = new UserSkill
            {
                UserId = SelectedUserId,
                SkillName = NewSkillName.Trim(),
                Category = category.Trim(),
                DisplayOrder = maxOrder + 1,
                CreatedOn = DateTime.UtcNow
            };

            SkillsRepo.Create(newSkill);

            StatusMessage = $"Skill '{NewSkillName}' added successfully.";
            IsError = false;
            ShowAddDialog = false;

            // Add new category to the list if it's new
            if (!ExistingCategories.Contains(category))
            {
                ExistingCategories.Add(category);
                ExistingCategories = ExistingCategories.OrderBy(c => c).ToList();
            }

            await LoadSkills();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error adding skill: {ex.Message}";
            IsError = true;
        }
    }

    #endregion

    #region Edit Skill

    private void StartEditSkill(UserSkill skill)
    {
        EditingSkillId = skill.SkillId;
        EditSkillName = skill.SkillName;
        EditSkillCategory = skill.Category ?? string.Empty;
        NewCategoryName = string.Empty;
    }

    private void CancelEdit()
    {
        EditingSkillId = 0;
        EditSkillName = string.Empty;
        EditSkillCategory = string.Empty;
        NewCategoryName = string.Empty;
    }

    private async Task SaveEditedSkill()
    {
        if (string.IsNullOrWhiteSpace(EditSkillName))
        {
            StatusMessage = "Skill name is required.";
            IsError = true;
            return;
        }

        var category = EditSkillCategory == NewCategorySentinel ? NewCategoryName : EditSkillCategory;

        if (string.IsNullOrWhiteSpace(category))
        {
            StatusMessage = "Category is required.";
            IsError = true;
            return;
        }

        try
        {
            var skill = SkillsRepo.GetById(EditingSkillId);
            if (skill == null)
            {
                StatusMessage = "Skill not found.";
                IsError = true;
                return;
            }

            skill.SkillName = EditSkillName.Trim();
            skill.Category = category.Trim();

            SkillsRepo.Update(skill);

            StatusMessage = $"Skill '{EditSkillName}' updated successfully.";
            IsError = false;

            // Add new category if needed
            if (!ExistingCategories.Contains(category))
            {
                ExistingCategories.Add(category);
                ExistingCategories = ExistingCategories.OrderBy(c => c).ToList();
            }

            CancelEdit();
            await LoadSkills();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating skill: {ex.Message}";
            IsError = true;
        }
    }

    #endregion

    #region Delete Skill

    private void ConfirmDeleteSkill(UserSkill skill)
    {
        SkillToDelete = skill;
        ShowDeleteDialog = true;
    }

    private void CancelDelete()
    {
        SkillToDelete = null;
        ShowDeleteDialog = false;
    }

    /// <summary>
    /// Keeps the delete confirmation state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    private void OnDeleteOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelDelete();
        }
    }

    private async Task DeleteSkill()
    {
        if (SkillToDelete == null) return;

        try
        {
            SkillsRepo.Delete(SkillToDelete.SkillId);

            StatusMessage = $"Skill '{SkillToDelete.SkillName}' deleted successfully.";
            IsError = false;

            ShowDeleteDialog = false;
            SkillToDelete = null;

            await LoadSkills();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting skill: {ex.Message}";
            IsError = true;
            ShowDeleteDialog = false;
        }
    }

    #endregion

    #region Reorder Skills

    private bool IsFirstInCategory(UserSkill skill)
    {
        var categorySkills = AllSkills
            .Where(s => s.Category == skill.Category)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        return categorySkills.FirstOrDefault()?.SkillId == skill.SkillId;
    }

    private bool IsLastInCategory(UserSkill skill)
    {
        var categorySkills = AllSkills
            .Where(s => s.Category == skill.Category)
            .OrderBy(s => s.DisplayOrder)
            .ToList();

        return categorySkills.LastOrDefault()?.SkillId == skill.SkillId;
    }

    private async Task MoveSkillUp(UserSkill skill)
    {
        await SwapSkillOrder(skill, -1);
    }

    private async Task MoveSkillDown(UserSkill skill)
    {
        await SwapSkillOrder(skill, 1);
    }

    private async Task SwapSkillOrder(UserSkill skill, int direction)
    {
        try
        {
            var categorySkills = AllSkills
                .Where(s => s.Category == skill.Category)
                .OrderBy(s => s.DisplayOrder)
                .ToList();

            var currentIndex = categorySkills.FindIndex(s => s.SkillId == skill.SkillId);
            var targetIndex = currentIndex + direction;

            if (targetIndex < 0 || targetIndex >= categorySkills.Count)
            {
                return; // Can't move further
            }

            var targetSkill = categorySkills[targetIndex];

            // Swap display orders
            var tempOrder = skill.DisplayOrder;
            skill.DisplayOrder = targetSkill.DisplayOrder;
            targetSkill.DisplayOrder = tempOrder;

            SkillsRepo.Update(skill);
            SkillsRepo.Update(targetSkill);

            await LoadSkills();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error reordering skill: {ex.Message}";
            IsError = true;
        }
    }

    #endregion
}
