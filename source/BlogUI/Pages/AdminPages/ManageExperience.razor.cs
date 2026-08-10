using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for ManageExperience.razor.
/// Handles CRUD operations for user work experience entries.
/// </summary>
public partial class ManageExperience : ComponentBase
{
    #region Injected Services

    [Inject]
    public IUserEventRepo EventRepo { get; set; } = default!;

    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    [Inject]
    public NavigationManager NavManager { get; set; } = default!;

    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    #endregion

    #region Parameters

    /// <summary>
    /// Event ID for edit mode. If provided, loads existing experience for editing.
    /// </summary>
    [Parameter]
    public long EventId { get; set; }

    #endregion

    #region State Properties

    private const string ExperienceEventType = "Experience";

    // Page state
    private string PageTitle => IsEditMode ? "Edit Experience" : "Manage Experience";
    private bool IsLoading { get; set; } = true;
    private bool IsSaving { get; set; }
    private bool ShowForm { get; set; }
    private bool ShowDeleteDialog { get; set; }
    private string? StatusMessage { get; set; }
    private bool IsError { get; set; }

    // Edit mode — route-level: the page was deep-linked as /admin/experience/{id}.
    private bool IsEditMode => EventId > 0;

    /// <summary>
    /// Whether the dialog is editing a persisted row rather than composing a new one.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="IsEditMode"/>, which only reports the ROUTE. Editing from the list
    /// happens in place with no navigation, so the route says "add" while the dialog is editing.
    /// </remarks>
    private bool IsExistingEntry => CurrentEvent.EventID > 0;

    // Auth state
    private ClaimsPrincipal? LoggedInUser;
    private long CurrentUserId { get; set; }
    private bool IsAdmin { get; set; }
    private long? SelectedUserId { get; set; }

    // Data
    private List<UserEvent>? ExperienceList { get; set; }
    private UserEvent CurrentEvent { get; set; } = new();
    private UserEvent? EventToDelete { get; set; }
    private List<AppUser>? AllUsers { get; set; }

    // Date handling (DatePicker binds DateTime?)
    private DateTime? StartDateValue
    {
        get => CurrentEvent.StartDate;
        set => CurrentEvent.StartDate = value;
    }

    private DateTime? EndDateValue
    {
        get => CurrentEvent.EventDate == default ? null : CurrentEvent.EventDate;
        set => CurrentEvent.EventDate = value ?? DateTime.Today;
    }

    /// <summary>
    /// Nullable projection of <see cref="UserEvent.LogoIconPath"/> for the company-logo
    /// <c>ImagePicker</c> (REQ-UI-037), whose bound value is <see cref="string"/>?.
    /// </summary>
    /// <remarks>
    /// Business Logic: the picker clears a selection by pushing <c>null</c>, but
    /// <see cref="UserEvent.LogoIconPath"/> is non-nullable, so a cleared logo is stored as
    /// <see cref="string.Empty"/> - the same value the column already uses for "no logo".
    /// Side Effects: none beyond the field assignment.
    /// </remarks>
    private string? LogoIconPath
    {
        get => string.IsNullOrEmpty(CurrentEvent.LogoIconPath) ? null : CurrentEvent.LogoIconPath;
        set => CurrentEvent.LogoIconPath = value ?? string.Empty;
    }

    /// <summary>
    /// Non-null projection of <see cref="UserEvent.LogoIconPath"/> for the manual path Input,
    /// kept alongside the picker so both controls drive one property.
    /// </summary>
    private string LogoIconPathText
    {
        get => CurrentEvent.LogoIconPath ?? string.Empty;
        set => CurrentEvent.LogoIconPath = value ?? string.Empty;
    }

    /// <summary>
    /// String projection of <see cref="UserEvent.DisplayOrder"/> for the numeric Input,
    /// whose Value parameter is a string.
    /// </summary>
    private string DisplayOrderText
    {
        get => CurrentEvent.DisplayOrder.ToString();
        set => CurrentEvent.DisplayOrder = int.TryParse(value, out var order) && order >= 0 ? order : 0;
    }

    /// <summary>
    /// Gets the effective user ID based on admin selection or current user.
    /// </summary>
    private long EffectiveUserId => IsAdmin && SelectedUserId.HasValue
        ? SelectedUserId.Value
        : CurrentUserId;

    #endregion

    #region Lifecycle Methods

    protected override async Task OnInitializedAsync()
    {
        await LoadAuthState();
        await LoadData();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (IsEditMode)
        {
            await LoadEventForEdit();
        }
    }

    #endregion

    #region Data Loading

    private async Task LoadAuthState()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        LoggedInUser = authState.User;

        // Get current user ID
        var userIdClaim = LoggedInUser.FindFirst(ClaimTypes.PrimarySid);
        if (userIdClaim != null && long.TryParse(userIdClaim.Value, out var userId))
        {
            CurrentUserId = userId;
        }

        // Check if user is Admin
        IsAdmin = LoggedInUser.IsInRole(AppRoles.Admin);

        // Load all users for admin dropdown
        if (IsAdmin)
        {
            try
            {
                AllUsers = UserRepo.GetAll()?.ToList() ?? new List<AppUser>();
            }
            catch
            {
                AllUsers = new List<AppUser>();
            }
        }
    }

    private async Task LoadData()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            ExperienceList = EventRepo.GetByUserAndType(EffectiveUserId, ExperienceEventType)?.ToList()
                ?? new List<UserEvent>();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading experience: {ex.Message}";
            IsError = true;
            ExperienceList = new List<UserEvent>();
        }
        finally
        {
            IsLoading = false;
        }

        await Task.CompletedTask;
    }

    private async Task LoadEventForEdit()
    {
        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var eventData = EventRepo.GetSingle(EventId);
            if (eventData == null)
            {
                StatusMessage = "Experience entry not found.";
                IsError = true;
                CurrentEvent = new UserEvent();
            }
            else if (eventData.UserID != CurrentUserId && !IsAdmin)
            {
                StatusMessage = "You do not have permission to edit this experience.";
                IsError = true;
                CurrentEvent = new UserEvent();
            }
            else
            {
                CurrentEvent = eventData;
                ShowForm = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading experience: {ex.Message}";
            IsError = true;
            CurrentEvent = new UserEvent();
        }
        finally
        {
            IsLoading = false;
        }

        await Task.CompletedTask;
    }

    private async Task OnUserChanged()
    {
        await LoadData();
    }

    /// <summary>
    /// Handles the admin user picker selecting a different author.
    /// </summary>
    /// <param name="value">The selected user id, or an empty string for "my experience".</param>
    private async Task OnSelectedUserChanged(string value)
    {
        SelectedUserId = string.IsNullOrEmpty(value) ? null : long.Parse(value);
        await OnUserChanged();
    }

    #endregion

    #region Form Actions

    private void ShowAddForm()
    {
        CurrentEvent = new UserEvent
        {
            UserID = EffectiveUserId,
            EventType = ExperienceEventType,
            EventDate = DateTime.Today,
            DisplayOrder = (ExperienceList?.Count ?? 0) + 1
        };
        ShowForm = true;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Opens an existing entry in the add/edit dialog.
    /// </summary>
    /// <param name="eventId">The identifier of the entry to edit.</param>
    /// <remarks>
    /// Business Logic: edits happen IN PLACE rather than by navigating to
    /// <c>/admin/experience/{id}</c>. The navigation form left the page unusable after a save -
    /// returning to <c>/admin/experience</c> reuses the same component instance, so
    /// <see cref="EventId"/> reset to 0 while <c>ShowForm</c> stayed true and the dialog hung open
    /// over a stale list. The <c>{EventId:long}</c> route still works for deep links via
    /// <see cref="OnParametersSetAsync"/>.
    /// Side Effects: opens the dialog; clears any status message.
    /// </remarks>
    private void EditExperience(long eventId)
    {
        var entry = ExperienceList?.FirstOrDefault(item => item.EventID == eventId);
        if (entry is null)
        {
            NavManager.NavigateTo($"/admin/experience/{eventId}");
            return;
        }

        // Edit a COPY: binding the list item itself would let a cancelled edit leave its
        // half-typed values on the card behind the dialog.
        CurrentEvent = new UserEvent
        {
            EventID = entry.EventID,
            UserID = entry.UserID,
            EventType = entry.EventType,
            EventTitle = entry.EventTitle,
            SessionTitle = entry.SessionTitle,
            LogoIconPath = entry.LogoIconPath,
            EventUrl = entry.EventUrl,
            EventDate = entry.EventDate,
            StartDate = entry.StartDate,
            Description = entry.Description,
            DisplayOrder = entry.DisplayOrder,
            IsCurrent = entry.IsCurrent
        };
        StatusMessage = string.Empty;
        IsError = false;
        ShowForm = true;
    }

    /// <summary>
    /// Keeps the add/edit dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    private void OnFormOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelEdit();
        }
    }

    /// <summary>
    /// Closes the add/edit dialog and discards the in-progress entry.
    /// </summary>
    /// <remarks>
    /// Business Logic: the dialog state is ALWAYS reset, including on the deep-linked
    /// <c>/admin/experience/{id}</c> route. Navigating without resetting left <c>ShowForm</c> true,
    /// and because the destination reuses the same component instance the dialog stayed on screen.
    /// Side Effects: navigates back to the list route when the page was deep-linked.
    /// </remarks>
    private void CancelEdit()
    {
        ShowForm = false;
        CurrentEvent = new UserEvent();
        StatusMessage = string.Empty;

        if (IsEditMode)
        {
            NavManager.NavigateTo("/admin/experience");
        }
    }

    private async Task SaveExperience()
    {
        if (IsSaving) return;

        // Validation
        if (string.IsNullOrWhiteSpace(CurrentEvent.SessionTitle))
        {
            StatusMessage = "Role/Position is required.";
            IsError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentEvent.EventTitle))
        {
            StatusMessage = "Company Name is required.";
            IsError = true;
            return;
        }

        IsSaving = true;
        StatusMessage = string.Empty;

        try
        {
            // Set event type
            CurrentEvent.EventType = ExperienceEventType;

            // Ensure user ID is set
            if (CurrentEvent.UserID == 0)
            {
                CurrentEvent.UserID = EffectiveUserId;
            }

            // Set end date to today if current position
            if (CurrentEvent.IsCurrent)
            {
                CurrentEvent.EventDate = DateTime.Today;
            }

            if (CurrentEvent.EventID > 0)
            {
                // Update existing
                EventRepo.Update(CurrentEvent);
                StatusMessage = "Experience updated successfully.";
            }
            else
            {
                // Insert new
                var newId = EventRepo.InsertToGetId(CurrentEvent);
                CurrentEvent.EventID = newId;
                StatusMessage = "Experience added successfully.";
            }

            IsError = false;

            // Always close the dialog and refresh the list; only the deep-linked
            // /admin/experience/{id} route additionally returns to the list URL.
            ShowForm = false;
            CurrentEvent = new UserEvent();
            await LoadData();

            if (IsEditMode)
            {
                NavManager.NavigateTo("/admin/experience");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving experience: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    #endregion

    #region Delete Actions

    private void ConfirmDelete(UserEvent experience)
    {
        EventToDelete = experience;
        ShowDeleteDialog = true;
    }

    private void CancelDelete()
    {
        EventToDelete = null;
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

    private async Task DeleteExperience()
    {
        if (EventToDelete == null) return;

        try
        {
            EventRepo.Delete(EventToDelete.EventID);
            StatusMessage = "Experience deleted successfully.";
            IsError = false;
            await LoadData();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting experience: {ex.Message}";
            IsError = true;
        }
        finally
        {
            EventToDelete = null;
            ShowDeleteDialog = false;
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Formats the date range for display.
    /// </summary>
    private string FormatDateRange(DateTime? startDate, DateTime endDate, bool isCurrent)
    {
        var start = startDate?.ToString("MMM yyyy") ?? "Unknown";
        var end = isCurrent ? "Present" : endDate.ToString("MMM yyyy");
        return $"{start} - {end}";
    }

    /// <summary>
    /// Formats the description with basic markdown-like processing.
    /// Converts lines starting with - or * to bullet points.
    /// </summary>
    private string FormatDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        // Escape HTML
        description = System.Net.WebUtility.HtmlEncode(description);

        // Convert bullet points
        var lines = description.Split('\n');
        var hasBullets = lines.Any(l => l.TrimStart().StartsWith("-") || l.TrimStart().StartsWith("*"));

        if (hasBullets)
        {
            var result = new System.Text.StringBuilder();
            var inList = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("-") || trimmed.StartsWith("*"))
                {
                    if (!inList)
                    {
                        result.Append("<ul>");
                        inList = true;
                    }
                    var content = trimmed.Substring(1).Trim();
                    result.Append($"<li>{content}</li>");
                }
                else
                {
                    if (inList)
                    {
                        result.Append("</ul>");
                        inList = false;
                    }
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        result.Append($"<p>{trimmed}</p>");
                    }
                }
            }

            if (inList)
            {
                result.Append("</ul>");
            }

            return result.ToString();
        }

        // No bullets, just convert line breaks
        return description.Replace("\n", "<br />");
    }

    #endregion
}
