using System.Globalization;
using BlogModels;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Administration screen for the owner's speaking engagements — the data behind
/// <c>/speaker-profile</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Add, edit and delete <c>UserEvents</c> rows of type
/// <see cref="EventTypes.Speaking"/>. Before this screen existed the only way to correct a session
/// title was a SQL statement.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Resolve the site owner — these rows belong to whoever the public pages are built from,
///   not to whoever happens to be signed in.</item>
///   <item>Load that owner's speaking rows through <see cref="IUserEventRepo"/>.</item>
///   <item>A save writes one row and reloads the list, so the grid always shows persisted truth
///   rather than what was merely submitted.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IUserEventRepo"/> for the rows,
/// <see cref="IBlogUserRepo"/> to resolve the owner.</para>
///
/// <para><b>Past and Future are not editable fields.</b> Which public table a row lands in is
/// derived from its date (<see cref="UserEvent.IsUpcoming"/>) — migration 031 records why there is
/// no stored flag. The form therefore shows the destination as live feedback on the date, and only
/// offers the registration field while the date is in the future.</para>
/// </remarks>
public partial class ManageSpeaking
{
    /// <summary>Characters of the details column shown in the grid before it is elided.</summary>
    private const int DetailsPreviewLength = 90;

    private const string LoadFailureMessage =
        "Could not load your speaking engagements. Please try again later.";

    private const string SaveFailureMessage =
        "Could not save the engagement. Please try again later.";

    private const string DeleteFailureMessage =
        "Could not delete the engagement. Please try again later.";

    private const string OwnerMissingMessage =
        "No site owner is configured, so there is no profile to attach engagements to.";

    private List<UserEvent> engagements = [];
    private AppUser? siteOwner;
    private bool isLoading = true;
    private bool isSaving;
    private string? statusMessage;
    private bool isError;

    private bool showEditDialog;
    private bool showDeleteDialog;
    private bool isNewEngagement;
    private string? validationMessage;
    private UserEvent? rowBeingEdited;
    private UserEvent? rowToDelete;

    // Bound as TEXT, not DateTime?, because TrBlazeUI's Input binds a string — the same shape
    // AnalyticsDashboard uses for its range pickers. FormDate below is the parsed view of it, so a
    // half-typed or cleared date reads as "no date" instead of throwing on every keystroke.
    private string formDateText = string.Empty;
    private string formEventTitle = string.Empty;
    private string formEventUrl = string.Empty;
    private string formSessionTitle = string.Empty;
    private string formDescription = string.Empty;
    private string formRegistrationUrl = string.Empty;

    /// <summary>Repository owning the speaking rows.</summary>
    [Inject]
    public IUserEventRepo EventRepo { get; set; } = default!;

    /// <summary>Repository used to resolve the site owner these rows belong to.</summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    /// <summary>
    /// The date currently in the form, or null when the field is empty or not yet a valid date.
    /// </summary>
    /// <remarks>
    /// Parsed with the round-trip <c>yyyy-MM-dd</c> shape an <c>&lt;input type="date"&gt;</c> posts,
    /// and with <see cref="CultureInfo.InvariantCulture"/> — a browser sends ISO regardless of the
    /// viewer's locale, so parsing under the server's culture would misread the value on any host
    /// whose culture is not ISO-ordered.
    /// </remarks>
    private DateTime? FormDate =>
        DateTime.TryParseExact(
            formDateText,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;

    /// <summary>True when the date currently in the form puts the row in Future Sessions.</summary>
    private bool FormIsUpcoming => FormDate.HasValue && FormDate.Value.Date >= DateTime.Today;

    /// <summary>Live description of which public table the current date will file the row under.</summary>
    private string BucketHint => FormDate.HasValue
        ? FormIsUpcoming
            ? "This date is upcoming — the row will appear under Future Sessions."
            : "This date has passed — the row will appear under Past Sessions."
        : "Without a date the row is treated as past.";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadEngagementsAsync();
    }

    /// <summary>
    /// Loads the site owner and that owner's speaking engagements.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ordered newest-first, which is how the public Past Sessions table
    /// reads and therefore the order the owner is used to seeing them in. Upcoming rows sort to the
    /// top naturally, since their dates are the largest.</para>
    /// <para><b>Side Effects:</b> Populates the grid; a failure leaves it empty and shows a curated
    /// message rather than an exception (REQ-NFR-033).</para>
    /// </remarks>
    private async Task LoadEngagementsAsync()
    {
        isLoading = true;

        try
        {
            siteOwner = await UserRepo.GetSiteOwnerAsync();

            if (siteOwner is null)
            {
                statusMessage = OwnerMissingMessage;
                isError = true;
                engagements = [];
                return;
            }

            engagements = (await EventRepo.GetByUserAndTypeAsync(siteOwner.UserId, EventTypes.Speaking))
                ?.OrderByDescending(row => row.EventDate)
                .ToList() ?? [];
        }
        catch (Exception)
        {
            statusMessage = LoadFailureMessage;
            isError = true;
            engagements = [];
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>Shortens the details column so one long abstract cannot dominate the grid.</summary>
    /// <param name="value">The stored description.</param>
    /// <returns>The value, elided when longer than the preview length.</returns>
    private static string Truncate(string value) =>
        value.Length <= DetailsPreviewLength ? value : value[..DetailsPreviewLength].TrimEnd() + "…";

    /// <summary>Opens the dialog for a new engagement.</summary>
    private void ShowAddDialog()
    {
        isNewEngagement = true;
        rowBeingEdited = null;
        formDateText = string.Empty;
        formEventTitle = string.Empty;
        formEventUrl = string.Empty;
        formSessionTitle = string.Empty;
        formDescription = string.Empty;
        formRegistrationUrl = string.Empty;
        validationMessage = null;
        showEditDialog = true;
    }

    /// <summary>Opens the dialog populated from an existing engagement.</summary>
    /// <param name="row">The engagement to edit.</param>
    private void ShowEditDialog(UserEvent row)
    {
        isNewEngagement = false;
        rowBeingEdited = row;
        formDateText = row.EventDate == default
            ? string.Empty
            : row.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        formEventTitle = row.EventTitle ?? string.Empty;
        formEventUrl = row.EventUrl ?? string.Empty;
        formSessionTitle = row.SessionTitle ?? string.Empty;
        formDescription = row.Description ?? string.Empty;
        formRegistrationUrl = row.RegistrationUrl ?? string.Empty;
        validationMessage = null;
        showEditDialog = true;
    }

    /// <summary>Closes the add/edit dialog without saving.</summary>
    private void CancelEdit()
    {
        showEditDialog = false;
        rowBeingEdited = null;
        validationMessage = null;
    }

    /// <summary>
    /// Validates and persists the engagement in the dialog.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An engagement must carry at least an event title or a session
    /// title — a row with neither renders as two dashes and a date, which is not a record of
    /// anything. URLs are checked for being absolute http(s), because the public page emits them as
    /// an <c>href</c> and a relative value there would resolve against this site and 404.</para>
    /// <para><b>Registration links are cleared on a past row</b> rather than quietly kept: the field
    /// is hidden once the date moves into the past, so a retained value would be invisible here and
    /// unrenderable on the public page — a value nobody can see or remove.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one <c>UserEvents</c> row and reloads the
    /// grid.</para>
    /// </remarks>
    private async Task SaveEngagement()
    {
        validationMessage = Validate();
        if (validationMessage != null)
        {
            return;
        }

        if (siteOwner is null)
        {
            statusMessage = OwnerMissingMessage;
            isError = true;
            showEditDialog = false;
            return;
        }

        try
        {
            isSaving = true;

            var row = rowBeingEdited ?? new UserEvent();
            row.EventType = EventTypes.Speaking;
            row.UserID = siteOwner.UserId;
            row.EventDate = FormDate ?? default;
            row.EventTitle = formEventTitle.Trim();
            row.EventUrl = formEventUrl.Trim();
            row.SessionTitle = formSessionTitle.Trim();
            row.Description = string.IsNullOrWhiteSpace(formDescription) ? null : formDescription.Trim();
            row.RegistrationUrl = FormIsUpcoming && !string.IsNullOrWhiteSpace(formRegistrationUrl)
                ? formRegistrationUrl.Trim()
                : null;

            // StartDate and IsCurrent belong to Experience rows only (see EventTypes). Setting them
            // explicitly stops a row that was somehow created elsewhere from carrying stale values.
            row.StartDate = null;
            row.IsCurrent = false;

            if (isNewEngagement)
            {
                await EventRepo.InsertAsync(row);
            }
            else
            {
                await EventRepo.UpdateAsync(row);
            }

            statusMessage = isNewEngagement
                ? "Engagement added."
                : "Engagement updated.";
            isError = false;
            showEditDialog = false;
            rowBeingEdited = null;

            await LoadEngagementsAsync();
        }
        catch (Exception)
        {
            statusMessage = SaveFailureMessage;
            isError = true;
            showEditDialog = false;
            rowBeingEdited = null;
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <summary>
    /// Validates the dialog's fields.
    /// </summary>
    /// <returns>The first validation failure, or null when the form is valid.</returns>
    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(formEventTitle) && string.IsNullOrWhiteSpace(formSessionTitle))
        {
            return "Give the engagement an event title, a session title, or both.";
        }

        if (!IsAcceptableUrl(formEventUrl))
        {
            return "The event page URL must be a full http:// or https:// address.";
        }

        if (FormIsUpcoming && !IsAcceptableUrl(formRegistrationUrl))
        {
            return "The registration link must be a full http:// or https:// address.";
        }

        return null;
    }

    /// <summary>
    /// True when a URL field is either empty or an absolute http(s) address.
    /// </summary>
    /// <remarks>
    /// Empty passes — both URLs are optional. A relative or scheme-less value fails, because the
    /// public page renders these as an <c>href</c> and the browser would resolve them against this
    /// site. Restricting to http/https also refuses <c>javascript:</c>, which is the reason this
    /// checks the scheme rather than merely calling <c>Uri.TryCreate</c>.
    /// </remarks>
    /// <param name="value">The URL to check.</param>
    /// <returns>True when the value is acceptable to store.</returns>
    private static bool IsAcceptableUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Opens the delete-confirmation dialog.</summary>
    /// <param name="row">The engagement queued for deletion.</param>
    private void ShowDeleteDialog(UserEvent row)
    {
        rowToDelete = row;
        showDeleteDialog = true;
    }

    /// <summary>Closes the delete-confirmation dialog without deleting.</summary>
    private void CancelDelete()
    {
        showDeleteDialog = false;
        rowToDelete = null;
    }

    /// <summary>
    /// Deletes the confirmed engagement.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A hard delete, unlike a user account: nothing references a
    /// <c>UserEvents</c> row, so there is no integrity to preserve and no content left orphaned.</para>
    /// <para><b>Side Effects:</b> Removes one row and reloads the grid.</para>
    /// </remarks>
    private async Task ConfirmDelete()
    {
        if (rowToDelete is null)
        {
            CancelDelete();
            return;
        }

        try
        {
            isSaving = true;
            await EventRepo.DeleteAsync(rowToDelete.EventID);
            statusMessage = "Engagement deleted.";
            isError = false;
            await LoadEngagementsAsync();
        }
        catch (Exception)
        {
            statusMessage = DeleteFailureMessage;
            isError = true;
        }
        finally
        {
            isSaving = false;
            showDeleteDialog = false;
            rowToDelete = null;
        }
    }
}
