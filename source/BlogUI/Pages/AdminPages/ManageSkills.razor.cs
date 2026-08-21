using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

using BlogUI.Common;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ManageSkills.razor.
/// Handles CRUD operations for user skills grouped by category.
/// </summary>
public partial class ManageSkills
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
        "Could not load skills. Please try again later.";

    private const string AddFailureMessage =
        "Could not add the skill. Please try again later.";

    private const string UpdateFailureMessage =
        "Could not update the skill. Please try again later.";

    private const string DeleteFailureMessage =
        "Could not delete the skill. Please try again later.";

    private const string ReorderFailureMessage =
        "Could not reorder the skills. Please try again later.";
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
                AllUsers = (await UserRepo.GetAllAsync())?.ToList() ?? new List<AppUser>();
            }
        }
        catch
        {
            CurrentUserId = 0;
            SelectedUserId = 0;
        }
    }

    private async Task LoadSkills()
    {
        IsLoading = true;

        try
        {
            if (SelectedUserId > 0)
            {
                AllSkills = (await SkillsRepo.GetByUserIdAsync(SelectedUserId)).ToList();
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
        catch (Exception)
        {
            StatusMessage = LoadFailureMessage;
            IsError = true;
            AllSkills = new List<UserSkill>();
            GroupedSkills = Enumerable.Empty<IGrouping<string, UserSkill>>();
        }
        finally
        {
            IsLoading = false;
        }
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

            await SkillsRepo.CreateAsync(newSkill);

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
        catch (Exception)
        {
            StatusMessage = AddFailureMessage;
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
            var skill = await SkillsRepo.GetByIdAsync(EditingSkillId);
            if (skill == null)
            {
                StatusMessage = "Skill not found.";
                IsError = true;
                return;
            }

            skill.SkillName = EditSkillName.Trim();
            skill.Category = category.Trim();

            await SkillsRepo.UpdateAsync(skill);

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
        catch (Exception)
        {
            StatusMessage = UpdateFailureMessage;
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
            await SkillsRepo.DeleteAsync(SkillToDelete.SkillId);

            StatusMessage = $"Skill '{SkillToDelete.SkillName}' deleted successfully.";
            IsError = false;

            ShowDeleteDialog = false;
            SkillToDelete = null;

            await LoadSkills();
        }
        catch (Exception)
        {
            StatusMessage = DeleteFailureMessage;
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

            await SkillsRepo.UpdateAsync(skill);
            await SkillsRepo.UpdateAsync(targetSkill);

            await LoadSkills();
        }
        catch (Exception)
        {
            StatusMessage = ReorderFailureMessage;
            IsError = true;
        }
    }

    #endregion
}
