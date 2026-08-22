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

    /// <summary>
    /// Group a skill with no category of its own is rendered under.
    /// </summary>
    /// <remarks>
    /// Stated once because three places have to agree on it: the grouping that builds the screen,
    /// the category list behind the picker, and the move helpers that look up a skill's neighbours.
    /// </remarks>
    private const string UncategorizedName = "Uncategorized";

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
                GroupedSkills = OrderCategories(AllSkills);

                // Build list of existing categories
                ExistingCategories = AllSkills
                    .Select(s => s.Category ?? UncategorizedName)
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

    /// <summary>
    /// Groups skills into categories and puts the CATEGORIES themselves in authored order
    /// (REQ-UI-064).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both this screen and the public <c>ResumeSkills</c> component
    /// used to order categories alphabetically. That made category position unchangeable — no
    /// sequence of the per-skill Move up / Move down buttons can carry a skill past its category
    /// boundary, so an author who wanted Languages listed before Cloud and DevOps had no move
    /// available anywhere on the screen. Owner UAT reported that as "there is no way to change the
    /// order of skills", and it was the accurate reading.</para>
    /// <para>Ordering by each category's LOWEST <c>DisplayOrder</c> makes the existing per-skill
    /// numbers the single source of truth for both levels: moving a skill to the top of its
    /// category can now move the whole category, and <see cref="MoveCategoryUp"/> renumbers whole
    /// blocks to move it deliberately. No new column and no migration — the intent was already in
    /// the data, it simply was not being read. Name remains the tie-break so equal or absent
    /// numbers still produce a stable, predictable order rather than the repository's row order.</para>
    /// <para>This method is shared with nothing: <c>ResumeSkills</c> applies the same two-key rule
    /// in its own component, and the pairing is asserted by the unit tests for this REQ so the two
    /// surfaces cannot drift into disagreeing about what an author arranged.</para>
    /// <para><b>Flow:</b> group by category → order by minimum display order → tie-break by name.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="skills">Every skill belonging to the selected user.</param>
    /// <returns>The category groups in the order they should be rendered.</returns>
    public static IEnumerable<IGrouping<string, UserSkill>> OrderCategories(IEnumerable<UserSkill> skills)
    {
        return (skills ?? Enumerable.Empty<UserSkill>())
            .GroupBy(skill => skill.Category ?? UncategorizedName)
            .OrderBy(group => group.Min(skill => skill.DisplayOrder))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The categories in render order, used by the category move buttons.
    /// </summary>
    private List<string> OrderedCategoryNames =>
        OrderCategories(AllSkills).Select(group => group.Key).ToList();

    private bool IsFirstCategory(string category)
    {
        return OrderedCategoryNames.FirstOrDefault() == category;
    }

    private bool IsLastCategory(string category)
    {
        return OrderedCategoryNames.LastOrDefault() == category;
    }

    private async Task MoveCategoryUp(string category)
    {
        await SwapCategoryOrder(category, -1);
    }

    private async Task MoveCategoryDown(string category)
    {
        await SwapCategoryOrder(category, 1);
    }

    /// <summary>
    /// Moves a whole category one position up or down and renumbers every skill to match.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category position is derived from the skills' own
    /// <c>DisplayOrder</c> values (see <see cref="OrderCategories"/>), so moving a category means
    /// rewriting those numbers rather than storing a category order somewhere new. The whole list
    /// is renumbered from 1 in the new sequence, which also repairs the duplicate or gapped values
    /// that accumulate as skills are added and deleted — a set of ties is exactly the state in
    /// which the per-skill swap silently does nothing, because it exchanges two equal numbers.</para>
    /// <para>Only rows whose number actually changed are written, so moving the last category
    /// costs two updates rather than one per skill on the screen. A failure part-way through leaves
    /// the rows that did succeed renumbered; that is safe because the ordering is total and
    /// re-derived on every load, so the worst case is a partially-applied move the operator can
    /// simply repeat.</para>
    /// <para><b>Flow:</b> resolve the current order → guard the ends → move the category →
    /// renumber every skill in the new sequence → persist the changed rows → reload.</para>
    /// <para><b>Side Effects:</b> Updates <c>UserSkills</c> rows and reloads the screen.</para>
    /// </remarks>
    /// <param name="category">The category to move.</param>
    /// <param name="direction">-1 to move up, 1 to move down.</param>
    /// <returns>A task that completes when the move has been persisted.</returns>
    private async Task SwapCategoryOrder(string category, int direction)
    {
        var categories = OrderedCategoryNames;
        var currentIndex = categories.IndexOf(category);
        var targetIndex = currentIndex + direction;

        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= categories.Count)
        {
            return;
        }

        categories.RemoveAt(currentIndex);
        categories.Insert(targetIndex, category);

        await ApplyOrder(categories, skillOrderOverride: null);
    }

    private bool IsFirstInCategory(UserSkill skill)
    {
        return SkillsIn(CategoryOf(skill)).FirstOrDefault()?.SkillId == skill.SkillId;
    }

    private bool IsLastInCategory(UserSkill skill)
    {
        return SkillsIn(CategoryOf(skill)).LastOrDefault()?.SkillId == skill.SkillId;
    }

    private async Task MoveSkillUp(UserSkill skill)
    {
        await SwapSkillOrder(skill, -1);
    }

    private async Task MoveSkillDown(UserSkill skill)
    {
        await SwapSkillOrder(skill, 1);
    }

    /// <summary>
    /// Moves one skill up or down within its own category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This used to EXCHANGE the two skills' <c>DisplayOrder</c>
    /// values, which is correct only while every value in the category is distinct. Two skills
    /// sharing a number — the state a fresh set of rows arrives in, since
    /// <c>ManageSkills</c> seeds a new skill from the category's maximum and deletions leave
    /// duplicates behind — made the exchange a no-op: the button was enabled, the click was
    /// accepted, two rows were written, and nothing moved. That is indistinguishable from "there is
    /// no way to change the order", which is what owner UAT reported. Moving the skill in a list and
    /// renumbering the whole arrangement cannot express that failure, and it repairs the ties on the
    /// way past.</para>
    /// <para><b>Flow:</b> take the category's skills in render order → guard the ends → move the
    /// skill → renumber every category with that one arrangement overridden.</para>
    /// <para><b>Side Effects:</b> Updates the <c>UserSkills</c> rows whose number changed and
    /// reloads the screen.</para>
    /// </remarks>
    /// <param name="skill">The skill to move.</param>
    /// <param name="direction">-1 to move up, 1 to move down.</param>
    /// <returns>A task that completes when the move has been persisted.</returns>
    private async Task SwapSkillOrder(UserSkill skill, int direction)
    {
        var category = CategoryOf(skill);
        var categorySkills = SkillsIn(category);

        var currentIndex = categorySkills.FindIndex(s => s.SkillId == skill.SkillId);
        var targetIndex = currentIndex + direction;

        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= categorySkills.Count)
        {
            return; // Can't move further
        }

        categorySkills.RemoveAt(currentIndex);
        categorySkills.Insert(targetIndex, skill);

        await ApplyOrder(
            OrderedCategoryNames,
            new Dictionary<string, List<UserSkill>> { [category] = categorySkills });
    }

    /// <summary>
    /// Renumbers every skill so the rendered arrangement becomes the stored one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One numbering pass serves both moves, because category order
    /// and skill order are the same data read at two levels (see <see cref="OrderCategories"/>).
    /// Numbering runs 1..N across the categories in the requested sequence, so the result is always
    /// gapless and tie-free — which is what makes the NEXT move well-defined regardless of how the
    /// rows looked before. Only rows whose number actually changed are written, so a move at the
    /// bottom of the list costs two updates rather than one per skill on screen.</para>
    /// <para>A failure part-way through leaves the already-written rows renumbered. That is safe
    /// rather than merely tolerable: the ordering is total and re-derived on every load, so the
    /// worst outcome is a half-applied move the operator repeats. It is reported through the
    /// existing reorder message rather than silently.</para>
    /// <para><b>Flow:</b> for each category in order, take its skills (or the caller's override) →
    /// assign the next number → collect the changed rows → persist → reload.</para>
    /// <para><b>Side Effects:</b> Updates <c>UserSkills</c> rows and reloads the screen.</para>
    /// </remarks>
    /// <param name="categories">Category names in the order they should be rendered.</param>
    /// <param name="skillOrderOverride">
    /// Arrangements to use instead of the stored order, keyed by category. Null keeps every
    /// category's current order, which is what a pure category move wants.
    /// </param>
    /// <returns>A task that completes when the new numbering has been persisted.</returns>
    private async Task ApplyOrder(
        List<string> categories, Dictionary<string, List<UserSkill>> skillOrderOverride)
    {
        try
        {
            var renumbered = new List<UserSkill>();
            var nextOrder = 1;

            foreach (var categoryName in categories)
            {
                var categorySkills =
                    skillOrderOverride != null && skillOrderOverride.TryGetValue(categoryName, out var overridden)
                        ? overridden
                        : SkillsIn(categoryName);

                foreach (var skill in categorySkills)
                {
                    if (skill.DisplayOrder != nextOrder)
                    {
                        skill.DisplayOrder = nextOrder;
                        renumbered.Add(skill);
                    }

                    nextOrder++;
                }
            }

            foreach (var skill in renumbered)
            {
                await SkillsRepo.UpdateAsync(skill);
            }

            await LoadSkills();
        }
        catch (Exception)
        {
            StatusMessage = ReorderFailureMessage;
            IsError = true;
        }
    }

    /// <summary>
    /// The skills of one category, in the order the screen renders them.
    /// </summary>
    /// <param name="category">The category name, already normalised by <see cref="CategoryOf"/>.</param>
    /// <returns>A new list, safe for the caller to reorder in place.</returns>
    private List<UserSkill> SkillsIn(string category)
    {
        return AllSkills
            .Where(skill => CategoryOf(skill) == category)
            .OrderBy(skill => skill.DisplayOrder)
            .ThenBy(skill => skill.SkillId)
            .ToList();
    }

    /// <summary>
    /// The category a skill is grouped under, treating a null category as one real group.
    /// </summary>
    /// <remarks>
    /// The grouping in <see cref="OrderCategories"/> maps null to
    /// <see cref="UncategorizedName"/>, so the move helpers have to agree — comparing
    /// <c>skill.Category</c> directly, as they used to, put a null-category skill in a group the
    /// screen never renders and left its move buttons unable to find their neighbours.
    /// </remarks>
    /// <param name="skill">The skill to classify.</param>
    /// <returns>The category name used for grouping.</returns>
    private static string CategoryOf(UserSkill skill)
    {
        return skill.Category ?? UncategorizedName;
    }

    #endregion
}
