using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Uploads, validates and manages blog media across seven upload categories.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the rules that make an upload acceptable — per-category size limits
/// and allowed formats — and records the resulting metadata. Since REQ-FN-042 it no longer knows
/// where bytes physically land: every write, delete and read goes through
/// <see cref="IFileStorage"/>, so the same code serves local disk, a network share or an object
/// store depending on site settings.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Validate the file against the category's size and format constraints.</item>
///   <item>Generate a collision-proof name and resolve the configured storage provider.</item>
///   <item>Write the bytes through the provider and persist a <c>BlogImage</c> row holding the
///     provider's public URL.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IBlogImageRepo"/> for metadata,
/// <see cref="IFileStorageFactory"/> for the configured backend.</para>
///
/// <para><b>Usage:</b> Registered scoped. Callers should surface the validation message from
/// <see cref="ValidateImageAsync"/> before attempting an upload rather than catching the
/// exception thrown by <see cref="UploadImageAsync"/>.</para>
/// </remarks>
public class BlogImageService : IBlogImageService
{
    private readonly IBlogImageRepo imageRepo;
    private readonly IFileStorageFactory fileStorageFactory;
    private readonly ILogger<BlogImageService> logger;

    /// <summary>
    /// Root folder, relative to the storage backend, that all uploads live under.
    /// </summary>
    private const string UploadRootFolder = "uploads";

    /// <summary>
    /// Category constraints defining max size and allowed formats per category.
    /// </summary>
    private static readonly Dictionary<string, CategoryConstraints> CategoryConstraintMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["profiles"] = new CategoryConstraints(2 * 1024 * 1024, new[] { "jpg", "jpeg", "png", "webp" }),
            ["logos"] = new CategoryConstraints(500 * 1024, new[] { "jpg", "jpeg", "png", "svg", "webp" }),
            ["awards"] = new CategoryConstraints(500 * 1024, new[] { "jpg", "jpeg", "png", "svg", "webp" }),
            ["icons"] = new CategoryConstraints(200 * 1024, new[] { "png", "svg", "webp" }),
            ["blog"] = new CategoryConstraints(5 * 1024 * 1024, new[] { "jpg", "jpeg", "png", "gif", "webp" }),
            ["cv"] = new CategoryConstraints(10 * 1024 * 1024, new[] { "pdf" }),
            ["general"] = new CategoryConstraints(5 * 1024 * 1024, new[] { "jpg", "jpeg", "png", "gif", "webp" })
        };

    /// <summary>
    /// MIME type mappings for common file extensions.
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["gif"] = "image/gif",
        ["webp"] = "image/webp",
        ["svg"] = "image/svg+xml",
        ["pdf"] = "application/pdf"
    };

    /// <summary>
    /// Creates the image service over its repository and storage factory.
    /// </summary>
    /// <param name="imageRepo">Persistence for image metadata.</param>
    /// <param name="fileStorageFactory">Resolves the storage backend selected in site settings.</param>
    /// <param name="logger">Structured logger for upload and delete failures.</param>
    public BlogImageService(
        IBlogImageRepo imageRepo,
        IFileStorageFactory fileStorageFactory,
        ILogger<BlogImageService> logger)
    {
        this.imageRepo = imageRepo ?? throw new ArgumentNullException(nameof(imageRepo));
        this.fileStorageFactory = fileStorageFactory ?? throw new ArgumentNullException(nameof(fileStorageFactory));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category)
    {
        if (file == null)
            return Task.FromResult<(bool IsValid, string? Error)>((false, "No file provided."));

        if (string.IsNullOrWhiteSpace(category))
            return Task.FromResult<(bool IsValid, string? Error)>((false, "Category is required."));

        var normalizedCategory = category.ToLowerInvariant().Trim();
        if (!CategoryConstraintMap.TryGetValue(normalizedCategory, out var constraints))
        {
            return Task.FromResult<(bool IsValid, string? Error)>((false,
                $"Invalid category '{category}'. Valid categories: {string.Join(", ", CategoryConstraintMap.Keys)}."));
        }

        return Task.FromResult<(bool IsValid, string? Error)>(ValidateAgainstConstraints(file, normalizedCategory, constraints));
    }

    /// <inheritdoc />
    public async Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid user ID.", nameof(userId));

        var validation = await ValidateImageAsync(file, category).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Error);
        }

        var normalizedCategory = category.ToLowerInvariant().Trim();
        var extension = GetFileExtension(file.Name);
        var relativePath =
            $"{UploadRootFolder}/{normalizedCategory}/{BuildFileName(normalizedCategory, userId, extension)}";

        return await StoreAsync(file, normalizedCategory, userId, extension, relativePath).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteImageAsync(long imageId, long userId)
    {
        if (imageId <= 0 || userId <= 0)
        {
            logger.LogWarning("Delete rejected for image {ImageId} and user {UserId}", imageId, userId);
            return false;
        }

        try
        {
            var image = imageRepo.GetSingle(imageId);
            if (image == null || image.UserID != userId)
            {
                logger.LogWarning("User {UserId} may not delete image {ImageId}", userId, imageId);
                return false;
            }

            await RemoveStoredFileAsync(image.ImagePath).ConfigureAwait(false);
            await DeleteMetadataAsync(imageId).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete image {ImageId} for user {UserId}", imageId, userId);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null)
    {
        try
        {
            var normalizedCategory = category?.ToLowerInvariant().Trim() ?? "general";
            var filtered = imageRepo.GetAll()
                .Where(image => string.Equals(image.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase));

            if (userId.HasValue && userId.Value > 0)
            {
                filtered = filtered.Where(image => image.UserID == userId.Value);
            }

            return Task.FromResult(filtered.OrderByDescending(image => image.CreatedTime).AsEnumerable());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get images by category {Category}", category);
            return Task.FromResult(Enumerable.Empty<BlogImage>());
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<BlogImage>> GetImagesByUserAsync(long userId)
    {
        if (userId <= 0)
        {
            return Task.FromResult(Enumerable.Empty<BlogImage>());
        }

        try
        {
            return Task.FromResult(imageRepo.GetAllById(userId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get images for user {UserId}", userId);
            return Task.FromResult(Enumerable.Empty<BlogImage>());
        }
    }

    /// <inheritdoc />
    public Task<BlogImage?> GetImageAsync(long imageId)
    {
        if (imageId <= 0)
        {
            return Task.FromResult<BlogImage?>(null);
        }

        try
        {
            return Task.FromResult<BlogImage?>(imageRepo.GetSingle(imageId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get image {ImageId}", imageId);
            return Task.FromResult<BlogImage?>(null);
        }
    }

    /// <inheritdoc />
    public string GetImageUrl(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return string.Empty;

        return imagePath.StartsWith('/') || imagePath.Contains("://") ? imagePath : $"/{imagePath}";
    }

    /// <summary>
    /// Writes the upload through the configured storage backend and records its metadata.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Storage and metadata must not drift apart, so a failure after
    /// the bytes land removes the stored file before the exception propagates.</para>
    /// <para><b>Flow:</b> Resolve the backend, copy the browser stream, insert the row, roll the
    /// file back on failure.</para>
    /// <para><b>Side Effects:</b> Writes one file and one database row.</para>
    /// </remarks>
    /// <param name="file">The browser file being uploaded.</param>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="userId">Owner of the upload.</param>
    /// <param name="extension">Lower-case file extension without the dot.</param>
    /// <param name="relativePath">Storage-relative destination path.</param>
    /// <returns>The persisted image record including its generated identifier.</returns>
    private async Task<BlogImage> StoreAsync(
        IBrowserFile file, string category, long userId, string extension, string relativePath)
    {
        var storage = await fileStorageFactory.GetStorageAsync().ConfigureAwait(false);
        var mimeType = GetMimeType(extension);

        await using var source = file.OpenReadStream(maxAllowedSize: GetMaxSizeForCategory(category));
        var stored = await storage
            .SaveAsync(source, relativePath, mimeType, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            return PersistMetadata(file, category, userId, mimeType, stored);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metadata insert failed for {RelativePath}; removing stored file", relativePath);
            await storage.DeleteAsync(relativePath, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Inserts the metadata row describing a stored file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>ImagePath</c> holds the provider's public URL so existing
    /// rendering code needs no change when the backend changes.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogImage</c> row.</para>
    /// </remarks>
    /// <param name="file">The uploaded file, used for its original name.</param>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="userId">Owner of the upload.</param>
    /// <param name="mimeType">Resolved MIME type.</param>
    /// <param name="stored">The storage backend's description of the written file.</param>
    /// <returns>The image record with its generated identifier populated.</returns>
    private BlogImage PersistMetadata(
        IBrowserFile file, string category, long userId, string mimeType, FileStorageResult stored)
    {
        var blogImage = new BlogImage
        {
            ImageName = file.Name,
            ImagePath = stored.PublicUrl,
            Size = (int)file.Size,
            CreatedTime = DateTime.UtcNow,
            UserID = userId,
            Category = category,
            MimeType = mimeType
        };

        blogImage.BlogImageID = imageRepo.InsertToGetId(blogImage);
        logger.LogInformation(
            "Image {ImageId} stored at {PublicUrl} via {Provider}",
            blogImage.BlogImageID, stored.PublicUrl, stored.ProviderName);
        return blogImage;
    }

    /// <summary>
    /// Removes a stored file, tolerating one that has already gone.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing file must not block removal of its orphaned metadata
    /// row — media lost to a redeploy without a mounted volume is a known failure mode.</para>
    /// <para><b>Side Effects:</b> Removes the file from the configured backend.</para>
    /// </remarks>
    /// <param name="imagePath">The stored public URL or relative path.</param>
    private async Task RemoveStoredFileAsync(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        var storage = await fileStorageFactory.GetStorageAsync().ConfigureAwait(false);
        var removed = await storage.DeleteAsync(imagePath, CancellationToken.None).ConfigureAwait(false);
        if (!removed)
        {
            logger.LogWarning("Stored file {ImagePath} was already absent", imagePath);
        }
    }

    /// <summary>
    /// Deletes an image's metadata row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>IBlogImageRepo</c> carries no delete member, so the removal
    /// is issued as a parameterised statement over the repository's connection.</para>
    /// <para><b>Side Effects:</b> Removes one <c>BlogImage</c> row.</para>
    /// </remarks>
    /// <param name="imageId">Identifier of the row to remove.</param>
    private async Task DeleteMetadataAsync(long imageId)
    {
        using var connection = imageRepo.GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ImageId", imageId);
        await connection
            .ExecuteAsync("DELETE FROM BlogImage WHERE BlogImageId = @ImageId", parameters)
            .ConfigureAwait(false);
        logger.LogInformation("Deleted image record {ImageId}", imageId);
    }

    /// <summary>
    /// Applies a category's size and format constraints to a candidate file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Format is checked before size so the user sees the more
    /// actionable message first.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="file">The candidate file.</param>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="constraints">The category's limits.</param>
    /// <returns>Validity and, when invalid, the message to show the user.</returns>
    private static (bool IsValid, string? Error) ValidateAgainstConstraints(
        IBrowserFile file, string category, CategoryConstraints constraints)
    {
        var extension = GetFileExtension(file.Name);
        if (string.IsNullOrEmpty(extension))
        {
            return (false, "File must have a valid extension.");
        }

        if (!constraints.AllowedFormats.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"File format '{extension}' is not allowed for category '{category}'. " +
                           $"Allowed formats: {string.Join(", ", constraints.AllowedFormats)}.");
        }

        if (file.Size > constraints.MaxSize)
        {
            return (false, $"File size ({FormatFileSize(file.Size)}) exceeds maximum allowed size " +
                           $"({FormatFileSize(constraints.MaxSize)}) for category '{category}'.");
        }

        return (true, null);
    }

    /// <summary>
    /// Builds a collision-proof file name for an upload.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category, owner, timestamp and a GUID fragment together make
    /// two concurrent uploads of the same original name impossible to collide.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="userId">Owner of the upload.</param>
    /// <param name="extension">Lower-case extension without the dot.</param>
    /// <returns>The generated file name.</returns>
    private static string BuildFileName(string category, long userId, string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var unique = Guid.NewGuid().ToString("N")[..8];
        return $"{category}-{userId}-{timestamp}-{unique}.{extension}";
    }

    /// <summary>
    /// Extracts the lower-case extension from a file name.
    /// </summary>
    /// <param name="fileName">The original file name.</param>
    /// <returns>The extension without its dot, or an empty string.</returns>
    private static string GetFileExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var lastDot = fileName.LastIndexOf('.');
        if (lastDot < 0 || lastDot == fileName.Length - 1)
            return string.Empty;

        return fileName[(lastDot + 1)..].ToLowerInvariant();
    }

    /// <summary>
    /// Maps a file extension to its MIME type.
    /// </summary>
    /// <param name="extension">Lower-case extension without the dot.</param>
    /// <returns>The MIME type, or the generic binary type.</returns>
    private static string GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "application/octet-stream";

        return MimeTypeMap.TryGetValue(extension, out var mimeType) ? mimeType : "application/octet-stream";
    }

    /// <summary>
    /// Returns the maximum allowed upload size for a category.
    /// </summary>
    /// <param name="category">The normalised upload category.</param>
    /// <returns>The limit in bytes, falling back to the general category's limit.</returns>
    private static long GetMaxSizeForCategory(string category)
    {
        return CategoryConstraintMap.TryGetValue(category, out var constraints)
            ? constraints.MaxSize
            : CategoryConstraintMap["general"].MaxSize;
    }

    /// <summary>
    /// Renders a byte count as a human-readable size.
    /// </summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>A short, user-facing size string.</returns>
    private static string FormatFileSize(long bytes)
    {
        const long OneKilobyte = 1024;
        const long OneMegabyte = OneKilobyte * 1024;

        return bytes switch
        {
            >= OneMegabyte => $"{bytes / (double)OneMegabyte:F1} MB",
            >= OneKilobyte => $"{bytes / (double)OneKilobyte:F1} KB",
            _ => $"{bytes} bytes"
        };
    }

    /// <summary>
    /// Size and format limits for one upload category.
    /// </summary>
    /// <param name="MaxSize">Maximum accepted size in bytes.</param>
    /// <param name="AllowedFormats">Extensions accepted for this category.</param>
    private record CategoryConstraints(long MaxSize, string[] AllowedFormats);
}
