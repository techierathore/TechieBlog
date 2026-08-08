using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using System.Security.Claims;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using TrBlazeUI.Components.FileUpload;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the ManageImages admin page.
/// Provides comprehensive image management with category filtering, upload, and delete functionality.
/// </summary>
public partial class ManageImages : ComponentBase
{
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

    // Category constraints for validation display
    private static readonly Dictionary<string, (string MaxSize, string Formats)> CategoryConstraints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profiles"] = ("2MB", "jpg, jpeg, png, webp"),
        ["logos"] = ("500KB", "jpg, jpeg, png, svg, webp"),
        ["awards"] = ("500KB", "jpg, jpeg, png, svg, webp"),
        ["icons"] = ("200KB", "png, svg, webp"),
        ["blog"] = ("5MB", "jpg, jpeg, png, gif, webp"),
        ["cv"] = ("10MB", "pdf"),
        ["general"] = ("5MB", "jpg, jpeg, png, gif, webp")
    };

    // File type mappings for input accept attribute
    private static readonly Dictionary<string, string> AcceptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profiles"] = "image/jpeg,image/png,image/webp",
        ["logos"] = "image/jpeg,image/png,image/svg+xml,image/webp",
        ["awards"] = "image/jpeg,image/png,image/svg+xml,image/webp",
        ["icons"] = "image/png,image/svg+xml,image/webp",
        ["blog"] = "image/jpeg,image/png,image/gif,image/webp",
        ["cv"] = "application/pdf",
        ["general"] = "image/jpeg,image/png,image/gif,image/webp"
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

    private Task LoadUserList()
    {
        try
        {
            var users = UserRepo.GetAll();
            UserList = users?.ToDictionary(
                u => u.UserId,
                u => !string.IsNullOrWhiteSpace(u.FullName) ? u.FullName : u.EmailId ?? $"User {u.UserId}"
            );
        }
        catch
        {
            UserList = new Dictionary<long, string>();
        }
        return Task.CompletedTask;
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
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load images: {ex.Message}";
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
        PendingFiles = null;
        UploadError = null;
        IsUploading = false;
    }

    public void CloseUploadDialog()
    {
        ShowUploadDialog = false;
        SelectedFile = null;
        PendingFiles = null;
        UploadError = null;
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
            var uploadedImage = await ImageService.UploadImageAsync(SelectedFile, UploadCategory, CurrentUserId);
            StatusMessage = $"Image '{uploadedImage.ImageName}' uploaded successfully.";
            IsError = false;
            CloseUploadDialog();

            // Refresh if we uploaded to the currently selected category
            if (UploadCategory == SelectedCategory)
            {
                await LoadImages();
            }
        }
        catch (InvalidOperationException ex)
        {
            UploadError = ex.Message;
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

    public string GetCategoryConstraintsText(string category)
    {
        var normalizedCategory = category?.ToLowerInvariant() ?? "general";
        if (CategoryConstraints.TryGetValue(normalizedCategory, out var info))
        {
            return $"Max {info.MaxSize}, formats: {info.Formats}";
        }
        return "Max 5MB, formats: jpg, jpeg, png, gif, webp";
    }

    public string GetAcceptedFileTypes(string category)
    {
        var normalizedCategory = category?.ToLowerInvariant() ?? "general";
        if (AcceptTypes.TryGetValue(normalizedCategory, out var types))
        {
            return types;
        }
        return "image/jpeg,image/png,image/gif,image/webp";
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
        catch (Exception ex)
        {
            StatusMessage = $"Error deleting image: {ex.Message}";
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
