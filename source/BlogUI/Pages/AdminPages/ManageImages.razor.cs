using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Security.Claims;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using TrBlazeUI.Components.FileUpload;

using BlogUI.Common;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the ManageImages admin page.
/// Provides comprehensive image management with category filtering, upload, and delete functionality.
/// </summary>
public partial class ManageImages : ComponentBase
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
        "Could not load the media library. Please try again later.";

    private const string DeleteFailureMessage =
        "Could not delete the image. Please try again later.";
    [Inject]
    public IBlogImageService ImageService { get; set; } = default!;

    [Inject]
    public NavigationManager NavManager { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    // Category definitions
    public static readonly Dictionary<string, string> Categories = new()
    {
        ["profiles"] = "Profiles",
        ["logos"] = "Logos",
        ["awards"] = "Awards",
        ["icons"] = "Icons",
        ["blog"] = "Blog",
        ["cv"] = "CV",
        ["general"] = "General"
    };

    // State properties
    public string SelectedCategory { get; set; } = "profiles";
    public long SelectedUserId { get; set; } = 0;
    public bool IsLoading { get; set; }
    public string? StatusMessage { get; set; }
    public bool IsError { get; set; }

    // Image list
    private IEnumerable<BlogImage> AllImages { get; set; } = Enumerable.Empty<BlogImage>();
    public IEnumerable<BlogImage> FilteredImages => AllImages;
    public int TotalImages => FilteredImages.Count();

    // Pagination
    public int CurrentPage { get; set; } = 1;
    public int PageSize { get; set; } = 12;
    public int TotalPages => (int)Math.Ceiling((double)TotalImages / PageSize);
    public IEnumerable<BlogImage> PagedImages => FilteredImages
        .Skip((CurrentPage - 1) * PageSize)
        .Take(PageSize);

    // User filter list
    public Dictionary<long, string>? UserList { get; set; }

    // Current user info
    private ClaimsPrincipal? LoggedInUser;
    private long CurrentUserId;

    // Upload dialog state
    public bool ShowUploadDialog { get; set; }
    public string UploadCategory { get; set; } = "profiles";
    public IBrowserFile? SelectedFile { get; set; }
    public bool IsUploading { get; set; }
    public string? UploadError { get; set; }

    /// <summary>
    /// Accessible alternative text typed for the staged upload [REQ-FN-026]. Blank is allowed — the
    /// image service then derives a readable phrase from the file name, so <c>BlogImage.AltText</c>
    /// is never persisted as NULL.
    /// </summary>
    public string? UploadAltText { get; set; }

    /// <summary>
    /// Files currently staged in the upload dropzone.
    /// </summary>
    public IReadOnlyList<FileUploadItem>? PendingFiles { get; set; }

    // Delete dialog state
    public bool ShowDeleteDialog { get; set; }
    public BlogImage? ImageToDelete { get; set; }
    public bool IsDeleting { get; set; }

    protected override async Task OnInitializedAsync()
    {
        LoggedInUser = (await AuthStateTask).User;

        // Get current user ID
        var userIdClaim = LoggedInUser?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.PrimarySid)?.Value;
        CurrentUserId = long.TryParse(userIdClaim, out long id) ? id : 0;

        // Load user list for admin filter
        await LoadUserList();

        // Load images
        await LoadImages();
    }

    private async Task LoadUserList()
    {
        try
        {
            var users = await UserRepo.GetAllAsync();
            UserList = users?.ToDictionary(
                u => u.UserId,
                u => !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : u.EmailId ?? $"User {u.UserId}"
            );
        }
        catch
        {
            UserList = new Dictionary<long, string>();
        }
    }

    public async Task LoadImages()
    {
        IsLoading = true;
        StatusMessage = null;
        StateHasChanged();

        try
        {
            // Get images by category, optionally filtered by user
            long? userFilter = SelectedUserId > 0 ? SelectedUserId : null;
            AllImages = await ImageService.GetImagesByCategoryAsync(SelectedCategory, userFilter);
            CurrentPage = 1; // Reset to first page on reload
        }
        catch (Exception)
        {
            StatusMessage = LoadFailureMessage;
            IsError = true;
            AllImages = Enumerable.Empty<BlogImage>();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    public async Task SelectCategory(string category)
    {
        SelectedCategory = category;
        await LoadImages();
    }

    /// <summary>
    /// Handles the category tab strip switching to a different category.
    /// </summary>
    /// <param name="category">The newly selected category key.</param>
    public async Task OnCategoryChanged(string category)
    {
        if (string.IsNullOrEmpty(category) || category == SelectedCategory)
        {
            return;
        }

        await SelectCategory(category);
    }

    /// <summary>
    /// Handles the owner filter selecting a different user.
    /// </summary>
    /// <param name="value">The selected user id as text; "0" means all users.</param>
    public async Task OnUserFilterChanged(string value)
    {
        SelectedUserId = long.TryParse(value, out var userId) ? userId : 0;
        await LoadImages();
    }

    public string GetCategoryDisplayName(string category)
    {
        return Categories.TryGetValue(category, out var name) ? name : category;
    }

    #region Pagination

    public void GoToPage(int page)
    {
        if (page >= 1 && page <= TotalPages)
        {
            CurrentPage = page;
        }
    }

    public void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    public void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
        }
    }

    #endregion

    #region Upload Dialog

    public void OpenUploadDialog()
    {
        ShowUploadDialog = true;
        UploadCategory = SelectedCategory;
        SelectedFile = null;
        PendingFiles = Array.Empty<FileUploadItem>();
        UploadError = null;
        UploadAltText = null;
        IsUploading = false;
    }

    public void CloseUploadDialog()
    {
        ShowUploadDialog = false;
        SelectedFile = null;
        PendingFiles = Array.Empty<FileUploadItem>();
        UploadError = null;
        UploadAltText = null;
        IsUploading = false;
    }

    /// <summary>
    /// Keeps the upload dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    public void OnUploadDialogOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CloseUploadDialog();
        }
    }

    /// <summary>
    /// Handles files staged in the upload dropzone and validates the chosen file.
    /// </summary>
    /// <param name="files">Files staged by the dropzone.</param>
    public async Task OnFilesChanged(IReadOnlyList<FileUploadItem> files)
    {
        PendingFiles = files;
        UploadError = null;
        SelectedFile = files.FirstOrDefault()?.File;

        if (SelectedFile is null)
        {
            return;
        }

        var validation = await ImageService.ValidateImageAsync(SelectedFile, UploadCategory);
        if (!validation.IsValid)
        {
            UploadError = validation.Error;
            SelectedFile = null;
        }
    }

    public async Task UploadFile()
    {
        if (SelectedFile == null || CurrentUserId <= 0)
        {
            UploadError = "Please select a file to upload.";
            return;
        }

        IsUploading = true;
        UploadError = null;
        StateHasChanged();

        try
        {
            var uploadedImage = await ImageService.UploadImageAsync(
                SelectedFile, UploadCategory, CurrentUserId, UploadAltText);
            StatusMessage = $"Image '{uploadedImage.ImageName}' uploaded successfully.";
            IsError = false;
            CloseUploadDialog();

            // Refresh if we uploaded to the currently selected category
            if (UploadCategory == SelectedCategory)
            {
                await LoadImages();
            }
        }
        catch (InvalidOperationException curated)
        {
            // The variable is named `curated`, not `ex`, deliberately: BlogImageService authors this
            // message and it is always one of its own constants — a category validation rule, or the
            // REQ-NFR-040 storage-failure sentence that distinguishes "the server cannot write here"
            // from a retry-able failure. It carries no exception text and no server path, which is
            // what makes surfacing it compatible with REQ-NFR-033. Every other exception falls to
            // the generic branch below.
            UploadError = curated.Message;
        }
        catch (Exception)
        {
            UploadError = "An error occurred while uploading the file. Please try again.";
        }
        finally
        {
            IsUploading = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// The sentence advertising the selected category's limits, taken from the service that
    /// enforces them (REQ-FN-025).
    /// </summary>
    /// <param name="category">The category currently chosen in the dialog.</param>
    /// <returns>Text such as <c>Max 2 MB, formats: jpg, jpeg, png, webp</c>.</returns>
    public string GetCategoryConstraintsText(string category)
    {
        return ImageService.GetCategoryRule(category).ConstraintsText;
    }

    /// <summary>
    /// The <c>accept</c> filter for the selected category, derived from the same allow-list the
    /// server validates against.
    /// </summary>
    /// <param name="category">The category currently chosen in the dialog.</param>
    /// <returns>A comma-separated MIME list for the file input.</returns>
    public string GetAcceptedFileTypes(string category)
    {
        return ImageService.GetCategoryRule(category).AcceptAttribute;
    }

    /// <summary>
    /// The client-side size ceiling handed to the dropzone for the selected category
    /// (REQ-FN-025).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Without this the <c>FileUpload</c> component keeps its own 10 MB
    /// default, prints "Max size: 10 MB" directly beneath a caption saying "Max 2 MB", and accepts a
    /// file the server is certain to reject. Feeding it the category's real ceiling makes the
    /// dropzone advertise and enforce the same number as the caption and the service.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="category">The category currently chosen in the dialog.</param>
    /// <returns>The category's maximum upload size in bytes.</returns>
    public long GetMaxUploadSize(string category)
    {
        return ImageService.GetCategoryRule(category).MaxSizeBytes;
    }

    /// <summary>
    /// Re-arms the dialog when the uploader picks a different category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every advertised limit is a function of the category, so a
    /// change has to clear whatever was staged under the previous one — a 4 MB blog image left in
    /// the dropzone while the category moves to <c>profiles</c> would sit there under a caption that
    /// no longer describes it.</para>
    /// <para><b>Flow:</b> record the new category → drop the staged file and any stale error.</para>
    /// <para><b>Side Effects:</b> Clears the staged upload; the dropzone re-renders empty with the
    /// new ceiling, accept filter and caption.</para>
    /// </remarks>
    /// <param name="category">The newly selected category key.</param>
    public void OnUploadCategoryChanged(string category)
    {
        UploadCategory = string.IsNullOrWhiteSpace(category) ? UploadCategory : category;
        SelectedFile = null;
        PendingFiles = Array.Empty<FileUploadItem>();
        UploadError = null;
    }

    /// <summary>
    /// Surfaces a file the dropzone itself refused, so the page's error panel names the same limit
    /// the dropzone advertised (REQ-FN-025).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The component rejects an oversize or wrong-typed file before
    /// <see cref="OnFilesChanged"/> ever fires, so without this the staged-file panel simply stays
    /// empty and the administrator is told nothing. The message is the component's own, built from
    /// the ceiling this page supplied — it carries no exception text, so REQ-NFR-033 is
    /// unaffected.</para>
    /// <para><b>Side Effects:</b> Sets <see cref="UploadError"/>; nothing is uploaded.</para>
    /// </remarks>
    /// <param name="error">The dropzone's validation failure.</param>
    public void OnDropzoneValidationError(FileValidationError error)
    {
        SelectedFile = null;
        UploadError = error.Message;
    }

    #endregion

    #region Delete Dialog

    public void ShowDeleteConfirmation(BlogImage image)
    {
        ImageToDelete = image;
        ShowDeleteDialog = true;
        IsDeleting = false;
    }

    public void CancelDelete()
    {
        ImageToDelete = null;
        ShowDeleteDialog = false;
        IsDeleting = false;
    }

    /// <summary>
    /// Keeps the delete confirmation state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    public void OnDeleteOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CancelDelete();
        }
    }

    public async Task ConfirmDelete()
    {
        if (ImageToDelete == null) return;

        IsDeleting = true;
        StateHasChanged();

        try
        {
            // For admin, we can delete any image - pass the image owner's ID
            var result = await ImageService.DeleteImageAsync(ImageToDelete.BlogImageID, ImageToDelete.UserID);

            if (result)
            {
                StatusMessage = $"Image '{ImageToDelete.ImageName}' deleted successfully.";
                IsError = false;
                await LoadImages();
            }
            else
            {
                StatusMessage = "Failed to delete the image. Please try again.";
                IsError = true;
            }
        }
        catch (Exception)
        {
            StatusMessage = DeleteFailureMessage;
            IsError = true;
        }
        finally
        {
            ImageToDelete = null;
            ShowDeleteDialog = false;
            IsDeleting = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Copy URL

    public async Task CopyImageUrl(BlogImage image)
    {
        try
        {
            var fullUrl = ImageService.GetImageUrl(image.ImagePath);
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", fullUrl);
            StatusMessage = "Image URL copied to clipboard.";
            IsError = false;
            StateHasChanged();

            // Auto-clear message after 3 seconds
            await Task.Delay(3000);
            if (StatusMessage == "Image URL copied to clipboard.")
            {
                StatusMessage = null;
                StateHasChanged();
            }
        }
        catch
        {
            StatusMessage = "Failed to copy URL to clipboard.";
            IsError = true;
            StateHasChanged();
        }
    }

    #endregion

    #region Helpers

    public static string FormatFileSize(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;

        return bytes switch
        {
            >= MB => $"{bytes / (double)MB:F1} MB",
            >= KB => $"{bytes / (double)KB:F1} KB",
            _ => $"{bytes} bytes"
        };
    }

    public static string TruncateFileName(string? fileName, int maxLength = 20)
    {
        if (string.IsNullOrEmpty(fileName)) return "Unnamed";
        if (fileName.Length <= maxLength) return fileName;

        var extension = Path.GetExtension(fileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        var availableLength = maxLength - extension.Length - 3; // 3 for "..."
        if (availableLength < 5) return fileName[..maxLength] + "...";

        return nameWithoutExt[..availableLength] + "..." + extension;
    }

    #endregion
}
