using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ManageAwards.razor.
/// Handles CRUD operations for user awards.
/// </summary>
public partial class ManageAwards
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
        "Could not load awards. Please try again later.";

    private const string SaveFailureMessage =
        "Could not save the award. Please try again later.";

    private const string DeleteFailureMessage =
        "Could not delete the award. Please try again later.";

    private const string ReorderFailureMessage =
        "Could not reorder the awards. Please try again later.";
    [Inject]
    public IUserAwardsRepo AwardsRepo { get; set; } = default!;

    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    [Inject]
    public NavigationManager NavManager { get; set; } = default!;

    // State
    private bool IsLoading = true;
    private bool IsAdmin = false;
    private long CurrentUserId;
    private long SelectedUserId;
    private List<AppUser>? AllUsers;
    private List<UserAward> AllAwards = new();

    // Status messages
    private string? StatusMessage;
    private bool IsError = false;

    // Add/Edit Award Dialog
    private bool ShowAwardDialog = false;
    private bool IsEditMode = false;
    private long EditingAwardId = 0;
    private string FormAwardTitle = string.Empty;
    private string FormDescription = string.Empty;
    private string FormBadgeImagePath = string.Empty;
    private string FormAwardUrl = string.Empty;
    private string FormYearReceivedText = string.Empty;
    private string FormYearEndText = string.Empty;

    // Delete Award Dialog
    private bool ShowDeleteDialog = false;
    private UserAward? AwardToDelete;

    /// <summary>
    /// Nullable projection of <see cref="FormBadgeImagePath"/> for the badge-image
    /// <c>ImagePicker</c> (REQ-UI-039), whose bound value is <see cref="string"/>?.
    /// </summary>
    /// <remarks>
    /// Business Logic: the picker clears a selection by pushing <c>null</c>, but the form field
    /// is non-nullable, so a cleared badge is stored as <see cref="string.Empty"/> - which
    /// <see cref="SaveAward"/> already normalises to a NULL <c>BadgeImagePath</c> column.
    /// Side Effects: none beyond the field assignment.
    /// </remarks>
    private string? FormBadgeImagePathValue
    {
        get => string.IsNullOrEmpty(FormBadgeImagePath) ? null : FormBadgeImagePath;
        set => FormBadgeImagePath = value ?? string.Empty;
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentUser();
        await LoadAwards();
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

    private async Task LoadAwards()
    {
        IsLoading = true;

        try
        {
            if (SelectedUserId > 0)
            {
                AllAwards = (await AwardsRepo.GetByUserIdAsync(SelectedUserId)).ToList();
            }
            else
            {
                AllAwards = new List<UserAward>();
            }

            StatusMessage = string.Empty;
        }
        catch (Exception)
        {
            StatusMessage = LoadFailureMessage;
            IsError = true;
            AllAwards = new List<UserAward>();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OnUserSelectionChanged(long userId)
    {
        SelectedUserId = userId;
        await LoadAwards();
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

    /// <summary>
    /// Formats the year range for display (e.g., "2015-2024" or "2015-Present").
    /// </summary>
    private string FormatYearRange(string? awardYear)
    {
        if (string.IsNullOrEmpty(awardYear))
        {
            return "Unknown";
        }

        return awardYear;
    }

    #region Add/Edit Award

    private void ShowAddAwardDialog()
    {
        IsEditMode = false;
        EditingAwardId = 0;
        FormAwardTitle = string.Empty;
        FormDescription = string.Empty;
        FormBadgeImagePath = string.Empty;
        FormAwardUrl = string.Empty;
        FormYearReceivedText = string.Empty;
        FormYearEndText = string.Empty;
        ShowAwardDialog = true;
    }

    private void StartEditAward(UserAward award)
    {
        IsEditMode = true;
        EditingAwardId = award.AwardId;
        FormAwardTitle = award.AwardTitle ?? string.Empty;
        FormDescription = award.AwardDescription ?? string.Empty;
        FormBadgeImagePath = award.BadgeImagePath ?? string.Empty;
        FormAwardUrl = award.AwardUrl ?? string.Empty;
        FormYearReceivedText = award.AwardYear ?? string.Empty;
        FormYearEndText = string.Empty; // AwardYear contains the full range
        ShowAwardDialog = true;
    }

    private void CancelAwardDialog()
    {
        ShowAwardDialog = false;
        IsEditMode = false;
        EditingAwardId = 0;
        ClearForm();
    }

    /// <summary>
    /// Keeps the add/edit dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    private void OnAwardDialogOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelAwardDialog();
        }
    }

    private void ClearForm()
    {
        FormAwardTitle = string.Empty;
        FormDescription = string.Empty;
        FormBadgeImagePath = string.Empty;
        FormAwardUrl = string.Empty;
        FormYearReceivedText = string.Empty;
        FormYearEndText = string.Empty;
    }

    private async Task SaveAward()
    {
        if (string.IsNullOrWhiteSpace(FormAwardTitle))
        {
            StatusMessage = "Award title is required.";
            IsError = true;
            return;
        }

        try
        {
            // Build year range string
            var yearRange = FormYearReceivedText?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(FormYearEndText))
            {
                yearRange = $"{yearRange}-{FormYearEndText.Trim()}";
            }
            else if (!string.IsNullOrWhiteSpace(yearRange) && !yearRange.Contains("-"))
            {
                // If only start year and no end year, append "-Present" if desired
                // For now, just keep the start year
            }

            if (IsEditMode)
            {
                // Update existing award
                var award = await AwardsRepo.GetByIdAsync(EditingAwardId);
                if (award == null)
                {
                    StatusMessage = "Award not found.";
                    IsError = true;
                    return;
                }

                award.AwardTitle = FormAwardTitle.Trim();
                award.AwardDescription = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim();
                award.BadgeImagePath = string.IsNullOrWhiteSpace(FormBadgeImagePath) ? null : FormBadgeImagePath.Trim();
                award.AwardUrl = string.IsNullOrWhiteSpace(FormAwardUrl) ? null : FormAwardUrl.Trim();
                award.AwardYear = string.IsNullOrWhiteSpace(yearRange) ? null : yearRange;

                await AwardsRepo.UpdateAsync(award);

                StatusMessage = $"Award '{FormAwardTitle}' updated successfully.";
                IsError = false;
            }
            else
            {
                // Get max display order
                var maxOrder = AllAwards.Any() ? AllAwards.Max(a => a.DisplayOrder) : 0;

                var newAward = new UserAward
                {
                    UserId = SelectedUserId,
                    AwardTitle = FormAwardTitle.Trim(),
                    AwardDescription = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                    BadgeImagePath = string.IsNullOrWhiteSpace(FormBadgeImagePath) ? null : FormBadgeImagePath.Trim(),
                    AwardUrl = string.IsNullOrWhiteSpace(FormAwardUrl) ? null : FormAwardUrl.Trim(),
                    AwardYear = string.IsNullOrWhiteSpace(yearRange) ? null : yearRange,
                    DisplayOrder = maxOrder + 1,
                    CreatedOn = DateTime.UtcNow
                };

                await AwardsRepo.CreateAsync(newAward);

                StatusMessage = $"Award '{FormAwardTitle}' added successfully.";
                IsError = false;
            }

            ShowAwardDialog = false;
            IsEditMode = false;
            EditingAwardId = 0;
            ClearForm();

            await LoadAwards();
        }
        catch (Exception)
        {
            StatusMessage = SaveFailureMessage;
            IsError = true;
        }
    }

    #endregion

    #region Delete Award

    private void ConfirmDeleteAward(UserAward award)
    {
        AwardToDelete = award;
        ShowDeleteDialog = true;
    }

    private void CancelDelete()
    {
        AwardToDelete = null;
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

    private async Task DeleteAward()
    {
        if (AwardToDelete == null) return;

        try
        {
            await AwardsRepo.DeleteAsync(AwardToDelete.AwardId);

            StatusMessage = $"Award '{AwardToDelete.AwardTitle}' deleted successfully.";
            IsError = false;

            ShowDeleteDialog = false;
            AwardToDelete = null;

            await LoadAwards();
        }
        catch (Exception)
        {
            StatusMessage = DeleteFailureMessage;
            IsError = true;
            ShowDeleteDialog = false;
        }
    }

    #endregion

    #region Reorder Awards

    private bool IsFirstAward(UserAward award)
    {
        var orderedAwards = AllAwards.OrderBy(a => a.DisplayOrder).ToList();
        return orderedAwards.FirstOrDefault()?.AwardId == award.AwardId;
    }

    private bool IsLastAward(UserAward award)
    {
        var orderedAwards = AllAwards.OrderBy(a => a.DisplayOrder).ToList();
        return orderedAwards.LastOrDefault()?.AwardId == award.AwardId;
    }

    private async Task MoveAwardUp(UserAward award)
    {
        await SwapAwardOrder(award, -1);
    }

    private async Task MoveAwardDown(UserAward award)
    {
        await SwapAwardOrder(award, 1);
    }

    private async Task SwapAwardOrder(UserAward award, int direction)
    {
        try
        {
            var orderedAwards = AllAwards.OrderBy(a => a.DisplayOrder).ToList();

            var currentIndex = orderedAwards.FindIndex(a => a.AwardId == award.AwardId);
            var targetIndex = currentIndex + direction;

            if (targetIndex < 0 || targetIndex >= orderedAwards.Count)
            {
                return; // Can't move further
            }

            var targetAward = orderedAwards[targetIndex];

            // Swap display orders
            var tempOrder = award.DisplayOrder;
            award.DisplayOrder = targetAward.DisplayOrder;
            targetAward.DisplayOrder = tempOrder;

            await AwardsRepo.UpdateAsync(award);
            await AwardsRepo.UpdateAsync(targetAward);

            await LoadAwards();
        }
        catch (Exception)
        {
            StatusMessage = ReorderFailureMessage;
            IsError = true;
        }
    }

    #endregion
}
