using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BlogUI.Components;

/// <summary>
/// Code-behind for the ImagePicker component.
/// Provides image selection from library and upload functionality with category-based validation.
/// </summary>
public partial class ImagePicker : ComponentBase
{
    /// <summary>
    /// The image category for filtering and validation.
    /// Valid categories: profiles, logos, awards, icons, blog, cv, general
    /// </summary>
    [Parameter]
    public string Category { get; set; } = "general";

    /// <summary>
    /// The currently selected image path.
    /// </summary>
    [Parameter]
    public string? SelectedImagePath { get; set; }

    /// <summary>
    /// Event callback for two-way binding of SelectedImagePath.
    /// </summary>
    [Parameter]
    public EventCallback<string?> SelectedImagePathChanged { get; set; }

    /// <summary>
    /// The user ID for filtering and uploading images.
    /// </summary>
    [Parameter]
    public long UserId { get; set; }

    /// <summary>
    /// Injected BlogImageService for image operations.
    /// </summary>
    [Inject]
    public IBlogImageService ImageService { get; set; } = default!;

    // Modal visibility states
    protected bool ShowGalleryModal { get; set; }
    protected bool ShowUploadModal { get; set; }

    // Gallery state
    protected bool IsLoadingGallery { get; set; }
    protected IEnumerable<BlogImage>? GalleryImages { get; set; }

    // Upload state
    protected IBrowserFile? SelectedFile { get; set; }
    protected bool IsUploading { get; set; }
    protected string? UploadError { get; set; }

    /// <summary>
    /// Category constraints for file validation display.
    /// </summary>
    private static readonly Dictionary<string, (string MaxSize, string Formats)> CategoryInfo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profiles"] = ("2MB", "jpg, jpeg, png, webp"),
        ["logos"] = ("500KB", "jpg, jpeg, png, svg, webp"),
        ["awards"] = ("500KB", "jpg, jpeg, png, svg, webp"),
        ["icons"] = ("200KB", "png, svg, webp"),
        ["blog"] = ("5MB", "jpg, jpeg, png, gif, webp"),
        ["cv"] = ("10MB", "pdf"),
        ["general"] = ("5MB", "jpg, jpeg, png, gif, webp")
    };

    /// <summary>
    /// File type mappings for the file input accept attribute.
    /// </summary>
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

    /// <summary>
    /// Opens the gallery modal and loads images.
    /// </summary>
    protected async Task OpenGallery()
    {
        ShowGalleryModal = true;
        IsLoadingGallery = true;
        GalleryImages = null;

        try
        {
            GalleryImages = await ImageService.GetImagesByCategoryAsync(Category, UserId);
        }
        catch (Exception)
        {
            // Handle error silently - empty gallery will be shown
            GalleryImages = Enumerable.Empty<BlogImage>();
        }
        finally
        {
            IsLoadingGallery = false;
        }
    }

    /// <summary>
    /// Closes the gallery modal.
    /// </summary>
    protected void CloseGallery()
    {
        ShowGalleryModal = false;
        GalleryImages = null;
    }

    /// <summary>
    /// Selects an image from the gallery.
    /// </summary>
    protected async Task SelectImage(string imagePath)
    {
        SelectedImagePath = imagePath;
        await SelectedImagePathChanged.InvokeAsync(imagePath);
        CloseGallery();
    }

    /// <summary>
    /// Opens the upload modal.
    /// </summary>
    protected void OpenUpload()
    {
        ShowUploadModal = true;
        SelectedFile = null;
        UploadError = null;
        IsUploading = false;
    }

    /// <summary>
    /// Closes the upload modal.
    /// </summary>
    protected void CloseUpload()
    {
        ShowUploadModal = false;
        SelectedFile = null;
        UploadError = null;
        IsUploading = false;
    }

    /// <summary>
    /// Handles file selection from the file input.
    /// </summary>
    protected async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        UploadError = null;
        SelectedFile = e.File;

        // Validate the file immediately
        var validation = await ImageService.ValidateImageAsync(SelectedFile, Category);
        if (!validation.IsValid)
        {
            UploadError = validation.Error;
            SelectedFile = null;
        }
    }

    /// <summary>
    /// Uploads the selected file.
    /// </summary>
    protected async Task UploadFile()
    {
        if (SelectedFile == null || UserId <= 0)
        {
            UploadError = "Please select a file to upload.";
            return;
        }

        IsUploading = true;
        UploadError = null;

        try
        {
            var uploadedImage = await ImageService.UploadImageAsync(SelectedFile, Category, UserId);
            SelectedImagePath = uploadedImage.ImagePath;
            await SelectedImagePathChanged.InvokeAsync(uploadedImage.ImagePath);
            CloseUpload();
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
        }
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    protected async Task ClearSelection()
    {
        SelectedImagePath = null;
        await SelectedImagePathChanged.InvokeAsync(null);
    }

    /// <summary>
    /// Gets the constraint text for the current category.
    /// </summary>
    protected string GetCategoryConstraintsText()
    {
        var normalizedCategory = Category?.ToLowerInvariant() ?? "general";
        if (CategoryInfo.TryGetValue(normalizedCategory, out var info))
        {
            return $"Max {info.MaxSize}, formats: {info.Formats}";
        }
        return "Max 5MB, formats: jpg, jpeg, png, gif, webp";
    }

    /// <summary>
    /// Gets the accepted file types for the file input.
    /// </summary>
    protected string GetAcceptedFileTypes()
    {
        var normalizedCategory = Category?.ToLowerInvariant() ?? "general";
        if (AcceptTypes.TryGetValue(normalizedCategory, out var types))
        {
            return types;
        }
        return "image/jpeg,image/png,image/gif,image/webp";
    }

    /// <summary>
    /// Formats file size to human-readable string.
    /// </summary>
    protected static string FormatFileSize(long bytes)
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
}
