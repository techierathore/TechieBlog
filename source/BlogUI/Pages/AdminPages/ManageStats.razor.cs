using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

using BlogUI.Common;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// State and behaviour for the resume headline-statistics maintenance screen.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Closes the last gap in REQ-FN-027 (BRD-51). <c>UserStats</c> rows drive the
/// About and Community figures on <c>/resume</c> and the stat tiles on the portfolio home page, but
/// until now there was no maintenance screen, so they could only be populated with direct SQL. This
/// page supplies full create / edit / delete / reorder over those rows.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="OnInitializedAsync"/> reads the signed-in principal, defaults the target user
///         to that principal, and loads their statistics.</item>
///   <item>An Admin additionally gets a user picker, so the site owner's rows can be maintained from
///         any administrator account.</item>
///   <item>Every mutation goes through <see cref="UserStatsSvc"/> and surfaces the returned
///         <c>Result</c> as an inline alert — the page never throws at the user.</item>
///   <item>Move-up / move-down swap adjacent display orders and re-persist both rows, matching the
///         ordering affordance already used by the sibling awards and skills screens.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="UserStatsSvc"/> for all statistic access,
/// <see cref="IBlogUserRepo"/> for the admin user picker,
/// <see cref="AuthenticationStateProvider"/> for the current principal.</para>
///
/// <para><b>Usage:</b> Routed at <c>/admin/stats</c> behind the <c>AuthorOrAbove</c> policy — the
/// same gate as the sibling resume editors <c>/admin/experience</c>, <c>/admin/skills</c> and
/// <c>/admin/awards</c> — and rendered inside <c>AdminLayout</c>.</para>
/// </remarks>
public partial class ManageStats : ComponentBase
{
    /// <summary>
    /// Role name that unlocks the "maintain another user's statistics" picker.
    /// </summary>
    private const string AdminRoleName = "Admin";

    /// <summary>
    /// Category whose statistics render in the resume's community block.
    /// </summary>
    public const string CommunityCategory = "Community";

    /// <summary>
    /// Service supplying validated create, read, update, delete and reorder over statistics.
    /// </summary>
    [Inject]
    public UserStatsSvc StatsSvc { get; set; } = default!;

    /// <summary>
    /// User repository backing the admin-only "whose statistics" picker.
    /// </summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    /// <summary>
    /// Supplies the signed-in principal whose identifier and role drive the page.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>
    /// Whether the statistics list is still loading.
    /// </summary>
    public bool IsLoading { get; set; } = true;

    /// <summary>
    /// Whether the signed-in principal holds the Admin role.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Identifier of the user whose statistics are currently shown.
    /// </summary>
    public long SelectedUserId { get; set; }

    /// <summary>
    /// Every user, populated only for an Admin so another user's statistics can be maintained.
    /// </summary>
    public List<AppUser>? AllUsers { get; set; }

    /// <summary>
    /// The loaded statistics for <see cref="SelectedUserId"/>.
    /// </summary>
    public List<UserStat> AllStats { get; set; } = new();

    /// <summary>
    /// Display name of the user whose statistics are shown.
    /// </summary>
    /// <remarks>
    /// Kept for logging and messages. It no longer feeds a badge beside the picker: the trigger
    /// itself resolves the name from the selected item's registered text.
    /// </remarks>
    public string SelectedUserName
    {
        get
        {
            var user = AllUsers?.FirstOrDefault(u => u.UserId == SelectedUserId);
            return user is null ? $"User {SelectedUserId}" : $"{user.FirstName} {user.LastName}";
        }
    }

    /// <summary>
    /// The loaded statistics in display order, as the list renders them.
    /// </summary>
    public IReadOnlyList<UserStat> OrderedStats => AllStats.OrderBy(s => s.DisplayOrder).ToList();

    /// <summary>
    /// Inline success or failure message, shown when non-empty.
    /// </summary>
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Whether <see cref="StatusMessage"/> describes a failure.
    /// </summary>
    public bool IsError { get; set; }

    /// <summary>
    /// Whether the add/edit dialog is open.
    /// </summary>
    public bool ShowStatDialog { get; set; }

    /// <summary>
    /// Whether the dialog is editing an existing statistic rather than adding one.
    /// </summary>
    public bool IsEditMode { get; set; }

    /// <summary>
    /// Identifier of the statistic being edited, or zero when adding.
    /// </summary>
    public long EditingStatId { get; set; }

    /// <summary>
    /// Form field: the statistic's headline value.
    /// </summary>
    public string FormStatValue { get; set; } = string.Empty;

    /// <summary>
    /// Form field: the statistic's descriptive label.
    /// </summary>
    public string FormStatLabel { get; set; } = string.Empty;

    /// <summary>
    /// Form field: the statistic's optional grouping category.
    /// </summary>
    public string FormStatCategory { get; set; } = string.Empty;

    /// <summary>
    /// Whether the delete confirmation dialog is open.
    /// </summary>
    public bool ShowDeleteDialog { get; set; }

    /// <summary>
    /// The statistic awaiting delete confirmation.
    /// </summary>
    public UserStat? StatToDelete { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadCurrentUserAsync();
        await LoadStatsAsync();
    }

    /// <summary>
    /// Reads the signed-in principal and prepares the admin user picker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The page defaults to maintaining the caller's own statistics.
    /// An Admin additionally receives the full user list so the site owner's rows are reachable from
    /// any administrator account.</para>
    /// <para><b>Side Effects:</b> Sets <see cref="SelectedUserId"/>, <see cref="IsAdmin"/> and
    /// <see cref="AllUsers"/>.</para>
    /// </remarks>
    /// <returns>A task completing once the principal has been read.</returns>
    private async Task LoadCurrentUserAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        var userIdClaim = authState.User.FindFirst(ClaimTypes.PrimarySid);
        var roleClaim = authState.User.FindFirst(ClaimTypes.Role);

        if (userIdClaim is not null && long.TryParse(userIdClaim.Value, out var userId))
        {
            SelectedUserId = userId;
        }

        IsAdmin = string.Equals(roleClaim?.Value, AdminRoleName, StringComparison.OrdinalIgnoreCase);
        if (IsAdmin)
        {
            AllUsers = (await UserRepo.GetAllAsync())?.ToList() ?? new List<AppUser>();
        }
    }

    /// <summary>
    /// Reloads the statistics belonging to <see cref="SelectedUserId"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The service already degrades a read failure to an empty list, so
    /// this method only has to clear the loading flag.</para>
    /// <para><b>Side Effects:</b> Replaces <see cref="AllStats"/>.</para>
    /// </remarks>
    /// <returns>A task that completes when the statistics have been read.</returns>
    private async Task LoadStatsAsync()
    {
        IsLoading = true;
        AllStats = (await StatsSvc.GetStatsForUserAsync(SelectedUserId)).ToList();
        IsLoading = false;
    }

    /// <summary>
    /// Handles the admin user picker selecting a different user.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Changes <see cref="SelectedUserId"/> and reloads the list.</para>
    /// </remarks>
    /// <param name="value">The selected user identifier as text.</param>
    public async Task OnSelectedUserChangedAsync(string value)
    {
        if (!long.TryParse(value, out var userId))
        {
            return;
        }

        SelectedUserId = userId;
        StatusMessage = null;
        await LoadStatsAsync();
    }

    /// <summary>
    /// Opens the dialog ready to add a new statistic.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Clears the form and opens the dialog.</para></remarks>
    public void ShowAddStatDialog()
    {
        IsEditMode = false;
        EditingStatId = 0;
        ClearForm();
        ShowStatDialog = true;
    }

    /// <summary>
    /// Opens the dialog populated from an existing statistic.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Fills the form and opens the dialog.</para></remarks>
    /// <param name="stat">The statistic to edit.</param>
    public void StartEditStat(UserStat stat)
    {
        IsEditMode = true;
        EditingStatId = stat.StatId;
        FormStatValue = stat.StatValue ?? string.Empty;
        FormStatLabel = stat.StatLabel ?? string.Empty;
        FormStatCategory = stat.StatCategory ?? string.Empty;
        ShowStatDialog = true;
    }

    /// <summary>
    /// Closes the add/edit dialog and discards the form.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Closes the dialog and clears the form.</para></remarks>
    public void CancelStatDialog()
    {
        ShowStatDialog = false;
        IsEditMode = false;
        EditingStatId = 0;
        ClearForm();
    }

    /// <summary>
    /// Keeps dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Delegates to <see cref="CancelStatDialog"/> on close.</para></remarks>
    /// <param name="isOpen">The dialog's requested open state.</param>
    public void OnStatDialogOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelStatDialog();
        }
    }

    /// <summary>
    /// Persists the dialog's contents as a new or updated statistic.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A new statistic is appended after the highest existing display
    /// order so it lands at the end of the resume block. Validation lives in
    /// <see cref="UserStatsSvc"/>, so the page only relays the returned message.</para>
    /// <para><b>Flow:</b> Build the entity, call <c>SaveStat</c>, report, reload.</para>
    /// <para><b>Side Effects:</b> Writes one <c>UserStats</c> row.</para>
    /// </remarks>
    /// <returns>A task that completes when the statistic has been saved.</returns>
    public async Task SaveStatAsync()
    {
        var stat = BuildStatFromForm();
        var result = await StatsSvc.SaveStatAsync(stat);

        if (result.IsFailure)
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
            return;
        }

        StatusMessage = IsEditMode
            ? $"Statistic '{stat.StatLabel}' updated successfully."
            : $"Statistic '{stat.StatLabel}' added successfully.";
        IsError = false;

        CancelStatDialog();
        await LoadStatsAsync();
    }

    /// <summary>
    /// Opens the delete confirmation for a statistic.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Opens the confirmation dialog.</para></remarks>
    /// <param name="stat">The statistic proposed for deletion.</param>
    public void ConfirmDeleteStat(UserStat stat)
    {
        StatToDelete = stat;
        ShowDeleteDialog = true;
    }

    /// <summary>
    /// Abandons a pending delete.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Closes the confirmation dialog.</para></remarks>
    public void CancelDelete()
    {
        StatToDelete = null;
        ShowDeleteDialog = false;
    }

    /// <summary>
    /// Keeps delete-confirmation state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Delegates to <see cref="CancelDelete"/> on close.</para></remarks>
    /// <param name="isOpen">The dialog's requested open state.</param>
    public void OnDeleteOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelDelete();
        }
    }

    /// <summary>
    /// Deletes the confirmed statistic.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A statistic that has already gone is reported rather than
    /// silently ignored, so two admins editing at once see what happened.</para>
    /// <para><b>Side Effects:</b> Removes one <c>UserStats</c> row and reloads the list.</para>
    /// </remarks>
    /// <returns>A task that completes when the statistic has been deleted.</returns>
    public async Task DeleteStatAsync()
    {
        if (StatToDelete is null)
        {
            return;
        }

        var label = StatToDelete.StatLabel;
        var result = await StatsSvc.DeleteStatAsync(StatToDelete.StatId);

        StatusMessage = result.IsSuccess
            ? $"Statistic '{label}' deleted successfully."
            : result.ErrorMessage;
        IsError = result.IsFailure;

        CancelDelete();
        await LoadStatsAsync();
    }

    /// <summary>
    /// Determines whether a statistic is already first in display order.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> None.</para></remarks>
    /// <param name="stat">The statistic being tested.</param>
    /// <returns><c>true</c> when it cannot move up.</returns>
    public bool IsFirstStat(UserStat stat) => OrderedStats.FirstOrDefault()?.StatId == stat.StatId;

    /// <summary>
    /// Determines whether a statistic is already last in display order.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> None.</para></remarks>
    /// <param name="stat">The statistic being tested.</param>
    /// <returns><c>true</c> when it cannot move down.</returns>
    public bool IsLastStat(UserStat stat) => OrderedStats.LastOrDefault()?.StatId == stat.StatId;

    /// <summary>
    /// Moves a statistic one position earlier in display order.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Rewrites display order for the whole list.</para></remarks>
    /// <param name="stat">The statistic to move.</param>
    public Task MoveStatUpAsync(UserStat stat) => MoveStatAsync(stat, -1);

    /// <summary>
    /// Moves a statistic one position later in display order.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Rewrites display order for the whole list.</para></remarks>
    /// <param name="stat">The statistic to move.</param>
    public Task MoveStatDownAsync(UserStat stat) => MoveStatAsync(stat, 1);

    /// <summary>
    /// Reorders the list by moving one statistic by the given offset.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The reordered identifier sequence is handed to
    /// <c>UserStatsSvc.ReorderStats</c>, which renumbers display order from zero. Renumbering the
    /// whole list rather than swapping two values keeps the sequence dense even when the seeded rows
    /// share an order number, which the seed data does.</para>
    /// <para><b>Side Effects:</b> Updates one row per statistic and reloads the list.</para>
    /// </remarks>
    /// <param name="stat">The statistic to move.</param>
    /// <param name="offset">-1 to move earlier, +1 to move later.</param>
    private async Task MoveStatAsync(UserStat stat, int offset)
    {
        var ordered = OrderedStats.ToList();
        var currentIndex = ordered.FindIndex(s => s.StatId == stat.StatId);
        var targetIndex = currentIndex + offset;

        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ordered.Count)
        {
            return;
        }

        ordered.RemoveAt(currentIndex);
        ordered.Insert(targetIndex, stat);

        var result = await StatsSvc.ReorderStatsAsync(SelectedUserId, ordered.Select(s => s.StatId).ToList());
        if (result.IsFailure)
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
        }

        await LoadStatsAsync();
    }

    /// <summary>
    /// Projects the dialog form onto a <see cref="UserStat"/> ready to persist.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> In edit mode the existing identifier is carried through so the
    /// service updates rather than inserts; in add mode the row is appended after the current
    /// maximum display order.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The statistic described by the form.</returns>
    private UserStat BuildStatFromForm()
    {
        var nextOrder = AllStats.Count == 0 ? 0 : AllStats.Max(s => s.DisplayOrder) + 1;

        return new UserStat
        {
            StatId = IsEditMode ? EditingStatId : 0,
            UserId = SelectedUserId,
            StatValue = FormStatValue?.Trim() ?? string.Empty,
            StatLabel = FormStatLabel?.Trim() ?? string.Empty,
            StatCategory = string.IsNullOrWhiteSpace(FormStatCategory) ? null : FormStatCategory.Trim(),
            DisplayOrder = IsEditMode
                ? AllStats.FirstOrDefault(s => s.StatId == EditingStatId)?.DisplayOrder ?? nextOrder
                : nextOrder
        };
    }

    /// <summary>
    /// Empties the dialog form fields.
    /// </summary>
    /// <remarks><para><b>Side Effects:</b> Resets the three form properties.</para></remarks>
    private void ClearForm()
    {
        FormStatValue = string.Empty;
        FormStatLabel = string.Empty;
        FormStatCategory = string.Empty;
    }
}
