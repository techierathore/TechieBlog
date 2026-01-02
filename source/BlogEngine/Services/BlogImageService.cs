using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service for comprehensive image upload and management operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for uploading, validating, and managing blog images.</para>
/// <para><b>Dependencies:</b> IBlogImageRepo for data access, IWebHostEnvironment for file paths.</para>
/// <para><b>Story:</b> Stream F - BlogImageService Implementation</para>
/// </remarks>
public class BlogImageService : IBlogImageService
{
    private readonly IBlogImageRepo _imageRepo;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BlogImageService> _logger;

    /// <summary>
    /// Category constraints defining max size and allowed formats per category.
    /// </summary>
    private static readonly Dictionary<string, CategoryConstraints> _categoryConstraints = new(StringComparer.OrdinalIgnoreCase)
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
    private static readonly Dictionary<string, string> _mimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["gif"] = "image/gif",
        ["webp"] = "image/webp",
        ["svg"] = "image/svg+xml",
        ["pdf"] = "application/pdf"
    };

    public BlogImageService(
        IBlogImageRepo imageRepo,
        IWebHostEnvironment environment,
        ILogger<BlogImageService> logger)
    {
        _imageRepo = imageRepo ?? throw new ArgumentNullException(nameof(imageRepo));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category)
    {
        if (file == null)
            return (false, "No file provided.");

        if (string.IsNullOrWhiteSpace(category))
            return (false, "Category is required.");

        // Normalize category
        var normalizedCategory = category.ToLowerInvariant().Trim();

        // Check if category exists
        if (!_categoryConstraints.TryGetValue(normalizedCategory, out var constraints))
        {
            return (false, $"Invalid category '{category}'. Valid categories: {string.Join(", ", _categoryConstraints.Keys)}.");
        }

        // Get file extension
        var extension = GetFileExtension(file.Name);
        if (string.IsNullOrEmpty(extension))
        {
            return (false, "File must have a valid extension.");
        }

        // Check allowed formats
        if (!constraints.AllowedFormats.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"File format '{extension}' is not allowed for category '{normalizedCategory}'. Allowed formats: {string.Join(", ", constraints.AllowedFormats)}.");
        }

        // Check file size
        if (file.Size > constraints.MaxSize)
        {
            var maxSizeFormatted = FormatFileSize(constraints.MaxSize);
            var fileSizeFormatted = FormatFileSize(file.Size);
            return (false, $"File size ({fileSizeFormatted}) exceeds maximum allowed size ({maxSizeFormatted}) for category '{normalizedCategory}'.");
        }

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId)
    {
        if (userId <= 0)
            throw new ArgumentException("Invalid user ID.", nameof(userId));

        // Validate the file
        var validation = await ValidateImageAsync(file, category);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Error);
        }

        var normalizedCategory = category.ToLowerInvariant().Trim();
        var extension = GetFileExtension(file.Name);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var guid = Guid.NewGuid().ToString("N")[..8]; // First 8 chars of GUID

        // Generate filename: {category}_{userId}_{timestamp}_{guid}.{ext}
        var fileName = $"{normalizedCategory}_{userId}_{timestamp}_{guid}.{extension}";

        // Build paths
        var uploadFolder = Path.Combine(_environment.WebRootPath, "uploads", normalizedCategory);
        var filePath = Path.Combine(uploadFolder, fileName);
        var relativePath = $"/uploads/{normalizedCategory}/{fileName}";

        try
        {
            // Ensure directory exists
            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
                _logger.LogInformation("Created upload directory: {Directory}", uploadFolder);
            }

            // Read and save file
            await using var inputStream = file.OpenReadStream(maxAllowedSize: GetMaxSizeForCategory(normalizedCategory));
            await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await inputStream.CopyToAsync(outputStream);

            _logger.LogInformation("File saved to disk: {FilePath}", filePath);

            // Create database record
            var blogImage = new BlogImage
            {
                ImageName = file.Name,
                ImagePath = relativePath,
                Size = (int)file.Size,
                CreatedTime = DateTime.UtcNow,
                UserID = userId,
                Category = normalizedCategory,
                MimeType = GetMimeType(extension)
            };

            var imageId = _imageRepo.InsertToGetId(blogImage);
            blogImage.BlogImageID = imageId;

            _logger.LogInformation("Image record created with ID {ImageId} for user {UserId}", imageId, userId);

            return blogImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload image for user {UserId}, category {Category}", userId, normalizedCategory);

            // Clean up file if it was created
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    _logger.LogInformation("Cleaned up file after failed upload: {FilePath}", filePath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to clean up file: {FilePath}", filePath);
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteImageAsync(long imageId, long userId)
    {
        if (imageId <= 0)
        {
            _logger.LogWarning("Invalid image ID: {ImageId}", imageId);
            return false;
        }

        if (userId <= 0)
        {
            _logger.LogWarning("Invalid user ID: {UserId}", userId);
            return false;
        }

        try
        {
            // Get the image record
            var image = _imageRepo.GetSingle(imageId);
            if (image == null)
            {
                _logger.LogWarning("Image not found: {ImageId}", imageId);
                return false;
            }

            // Check ownership (only owner can delete their images)
            // Note: Admin check should be done at controller/page level if needed
            if (image.UserID != userId)
            {
                _logger.LogWarning("User {UserId} attempted to delete image {ImageId} owned by user {OwnerId}",
                    userId, imageId, image.UserID);
                return false;
            }

            // Build physical file path
            var filePath = Path.Combine(_environment.WebRootPath, image.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            // Delete physical file
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("Deleted file: {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("File not found on disk: {FilePath}", filePath);
            }

            // Delete database record using the existing generic repository pattern
            // Since IBlogImageRepo doesn't have a Delete method, we need to use raw SQL
            using var conn = _imageRepo.GetOpenConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM blogimage WHERE blogimageid = @ImageId";
            var param = cmd.CreateParameter();
            param.ParameterName = "@ImageId";
            param.Value = imageId;
            cmd.Parameters.Add(param);
            await Task.Run(() => cmd.ExecuteNonQuery());

            _logger.LogInformation("Deleted image record: {ImageId}", imageId);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete image {ImageId} for user {UserId}", imageId, userId);
            return false;
        }
    }

    /// <inheritdoc />
    public Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null)
    {
        try
        {
            var normalizedCategory = category?.ToLowerInvariant().Trim() ?? "general";

            // Get all images and filter by category
            var allImages = _imageRepo.GetAll();

            var filtered = allImages
                .Where(img => string.Equals(img.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase));

            if (userId.HasValue && userId.Value > 0)
            {
                filtered = filtered.Where(img => img.UserID == userId.Value);
            }

            return Task.FromResult(filtered.OrderByDescending(img => img.CreatedTime).AsEnumerable());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get images by category {Category}", category);
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
            // GetAllById returns images by user ID
            var images = _imageRepo.GetAllById(userId);
            return Task.FromResult(images);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get images for user {UserId}", userId);
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
            var image = _imageRepo.GetSingle(imageId);
            return Task.FromResult<BlogImage?>(image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get image {ImageId}", imageId);
            return Task.FromResult<BlogImage?>(null);
        }
    }

    /// <inheritdoc />
    public string GetImageUrl(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return string.Empty;

        // If already starts with /, return as-is (it's already a URL path)
        if (imagePath.StartsWith('/'))
            return imagePath;

        // Otherwise, prepend /
        return $"/{imagePath}";
    }

    #region Private Helper Methods

    /// <summary>
    /// Extracts the file extension from a filename.
    /// </summary>
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
    /// Gets the MIME type for a file extension.
    /// </summary>
    private static string GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "application/octet-stream";

        return _mimeTypes.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }

    /// <summary>
    /// Gets the maximum allowed file size for a category.
    /// </summary>
    private static long GetMaxSizeForCategory(string category)
    {
        return _categoryConstraints.TryGetValue(category, out var constraints)
            ? constraints.MaxSize
            : _categoryConstraints["general"].MaxSize;
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
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

    #endregion

    #region Private Types

    /// <summary>
    /// Represents constraints for an image category.
    /// </summary>
    private record CategoryConstraints(long MaxSize, string[] AllowedFormats);

    #endregion
}
