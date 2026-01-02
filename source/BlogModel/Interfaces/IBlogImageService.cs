using Microsoft.AspNetCore.Components.Forms;
using BlogModels;

namespace BlogModels.Interfaces;

/// <summary>
/// Service interface for comprehensive image upload and management operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for uploading, validating, and managing blog images.</para>
/// <para><b>Dependencies:</b> IBlogImageRepo for data access, IWebHostEnvironment for file paths.</para>
/// <para><b>Story:</b> Stream F - BlogImageService Implementation</para>
/// </remarks>
public interface IBlogImageService
{
    /// <summary>
    /// Uploads an image file to the server and creates a database record.
    /// </summary>
    /// <param name="file">The browser file to upload.</param>
    /// <param name="category">The image category (profiles, logos, awards, icons, blog, cv, general).</param>
    /// <param name="userId">The ID of the user uploading the image.</param>
    /// <returns>The created BlogImage record on success.</returns>
    Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId);

    /// <summary>
    /// Deletes an image from disk and database.
    /// </summary>
    /// <param name="imageId">The ID of the image to delete.</param>
    /// <param name="userId">The ID of the user requesting deletion (for ownership check).</param>
    /// <returns>True if deletion succeeded, false otherwise.</returns>
    Task<bool> DeleteImageAsync(long imageId, long userId);

    /// <summary>
    /// Gets all images in a category, optionally filtered by user.
    /// </summary>
    /// <param name="category">The image category to filter by.</param>
    /// <param name="userId">Optional user ID to filter by owner.</param>
    /// <returns>List of images matching the criteria.</returns>
    Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null);

    /// <summary>
    /// Gets all images uploaded by a specific user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>List of images owned by the user.</returns>
    Task<IEnumerable<BlogImage>> GetImagesByUserAsync(long userId);

    /// <summary>
    /// Gets a single image by ID.
    /// </summary>
    /// <param name="imageId">The image ID.</param>
    /// <returns>The BlogImage if found, null otherwise.</returns>
    Task<BlogImage?> GetImageAsync(long imageId);

    /// <summary>
    /// Converts a relative image path to a full URL path.
    /// </summary>
    /// <param name="imagePath">The relative path stored in the database.</param>
    /// <returns>The full URL path for the image.</returns>
    string GetImageUrl(string imagePath);

    /// <summary>
    /// Validates an image file against category constraints before upload.
    /// </summary>
    /// <param name="file">The browser file to validate.</param>
    /// <param name="category">The target category.</param>
    /// <returns>Tuple with IsValid flag and optional error message.</returns>
    Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category);
}
