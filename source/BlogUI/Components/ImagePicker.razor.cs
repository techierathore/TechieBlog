using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using TrBlazeUI.Components.FileUpload;

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
    /// Files currently staged in the upload dropzone.
    /// </summary>
    protected IReadOnlyList<FileUploadItem>? PendingFiles { get; set; }

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
    /// Keeps the gallery dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    protected void OnGalleryOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CloseGallery();
        }
    }

    /// <summary>
    /// Builds the CSS classes for a gallery tile, highlighting the current selection.
    /// </summary>
    /// <param name="imagePath">The tile's image path.</param>
    /// <returns>The tile's Tailwind class list.</returns>
    protected string GetGalleryTileClass(string imagePath)
    {
        var border = imagePath == SelectedImagePath ? "border-primary" : "border-transparent";
        return $"aspect-square h-auto w-full overflow-hidden rounded-lg border-2 p-0 {border}";
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
        PendingFiles = Array.Empty<FileUploadItem>();
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
        PendingFiles = Array.Empty<FileUploadItem>();
        UploadError = null;
        IsUploading = false;
    }

    /// <summary>
    /// Keeps the upload dialog state in sync when it is dismissed by Escape or an outside click.
    /// </summary>
    /// <param name="isOpen">The dialog's requested open state.</param>
    protected void OnUploadOpenChanged(bool isOpen)
    {
        if (!isOpen)
        {
            CloseUpload();
        }
    }

    /// <summary>
    /// Handles file selection from the upload dropzone and validates the chosen file.
    /// </summary>
    /// <param name="files">Files staged by the dropzone.</param>
    protected async Task OnFilesChanged(IReadOnlyList<FileUploadItem> files)
    {
        PendingFiles = files;
        UploadError = null;
        SelectedFile = files.FirstOrDefault()?.File;

        if (SelectedFile is null)
        {
            return;
        }

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
    /// Gets the constraint text for the current category, taken from the service that enforces it
    /// (REQ-FN-025).
    /// </summary>
    /// <returns>Text such as <c>Max 2 MB, formats: jpg, jpeg, png, webp</c>.</returns>
    protected string GetCategoryConstraintsText()
    {
        return ImageService.GetCategoryRule(Category).ConstraintsText;
    }

    /// <summary>
    /// Gets the accepted file types for the file input, derived from the same allow-list the server
    /// validates against.
    /// </summary>
    /// <returns>A comma-separated MIME list for the file input.</returns>
    protected string GetAcceptedFileTypes()
    {
        return ImageService.GetCategoryRule(Category).AcceptAttribute;
    }

    /// <summary>
    /// Gets the client-side size ceiling handed to the dropzone for the current category
    /// (REQ-FN-025).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unset, <c>FileUpload</c> advertises and enforces its own 10 MB
    /// default, which contradicts the constraint line this component renders right beside it and
    /// lets through files the server will refuse.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The category's maximum upload size in bytes.</returns>
    protected long GetMaxUploadSize()
    {
        return ImageService.GetCategoryRule(Category).MaxSizeBytes;
    }

    /// <summary>
    /// Surfaces a file the dropzone itself refused, so the error panel names the same limit the
    /// component advertised (REQ-FN-025).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A file rejected by the dropzone never reaches
    /// <see cref="OnFilesChanged"/>, so without this the dialog would show nothing at all. The text
    /// is the component's own, built from the ceiling supplied here, and carries no exception
    /// text.</para>
    /// <para><b>Side Effects:</b> Sets <see cref="UploadError"/>; nothing is uploaded.</para>
    /// </remarks>
    /// <param name="error">The dropzone's validation failure.</param>
    protected void OnDropzoneValidationError(FileValidationError error)
    {
        SelectedFile = null;
        UploadError = error.Message;
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
