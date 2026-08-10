using BlogEngine.Common;
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
    /// Leading bytes handed to <see cref="ImageDimensionReader"/>. PNG, GIF, BMP and WebP declare
    /// their size in the first 30 bytes; JPEG hides it behind however many EXIF, ICC and comment
    /// segments the camera or editor wrote, so the window has to be generous. 64 KB clears a full
    /// ICC profile and an embedded thumbnail.
    /// </summary>
    private const int HeaderProbeLength = 64 * 1024;

    /// <summary>
    /// Maximum length of <c>blogimage.alttext</c>, which is <c>VARCHAR(255)</c>.
    /// </summary>
    private const int MaxAltTextLength = 255;

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
    public async Task<BlogImage> UploadImageAsync(
        IBrowserFile file, string category, long userId, string? altText = null)
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

        return await StoreAsync(file, normalizedCategory, userId, extension, relativePath, altText)
            .ConfigureAwait(false);
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
            var image = await imageRepo.GetSingleAsync(imageId).ConfigureAwait(false);
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
    public async Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null)
    {
        try
        {
            var normalizedCategory = category?.ToLowerInvariant().Trim() ?? "general";
            var all = await imageRepo.GetAllAsync().ConfigureAwait(false);
            var filtered = all
                .Where(image => string.Equals(image.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase));

            if (userId.HasValue && userId.Value > 0)
            {
                filtered = filtered.Where(image => image.UserID == userId.Value);
            }

            return filtered.OrderByDescending(image => image.CreatedTime).AsEnumerable();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get images by category {Category}", category);
            return Enumerable.Empty<BlogImage>();
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<BlogImage>> GetImagesByUserAsync(long userId)
    {
        if (userId <= 0)
        {
            return Enumerable.Empty<BlogImage>();
        }

        try
        {
            return await imageRepo.GetAllByIdAsync(userId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get images for user {UserId}", userId);
            return Enumerable.Empty<BlogImage>();
        }
    }

    /// <inheritdoc />
    public async Task<BlogImage?> GetImageAsync(long imageId)
    {
        if (imageId <= 0)
        {
            return null;
        }

        try
        {
            return await imageRepo.GetSingleAsync(imageId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get image {ImageId}", imageId);
            return null;
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
    /// <para><b>Flow:</b> Resolve the backend, buffer the browser stream, read the pixel dimensions
    /// out of the buffered header, write the bytes, insert the row, roll the file back on failure.</para>
    /// <para><b>Side Effects:</b> Writes one file and one database row.</para>
    /// <para><b>Why the upload is buffered (REQ-FN-026).</b> An <see cref="IBrowserFile"/> stream is
    /// forward-only and single-use, so the bytes cannot be inspected for dimensions and then handed
    /// to the storage provider — one pass has to serve both. The buffer is bounded by the category's
    /// own size ceiling, which is 10 MB at its largest, so the cost is the price of the two columns
    /// the requirement asks for.</para>
    /// </remarks>
    /// <param name="file">The browser file being uploaded.</param>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="userId">Owner of the upload.</param>
    /// <param name="extension">Lower-case file extension without the dot.</param>
    /// <param name="relativePath">Storage-relative destination path.</param>
    /// <param name="altText">Alternative text supplied by the uploader, or <c>null</c>.</param>
    /// <returns>The persisted image record including its generated identifier.</returns>
    private async Task<BlogImage> StoreAsync(
        IBrowserFile file, string category, long userId, string extension, string relativePath,
        string? altText)
    {
        var storage = await fileStorageFactory.GetStorageAsync().ConfigureAwait(false);
        var mimeType = GetMimeType(extension);

        await using var buffer = await BufferUploadAsync(file, category).ConfigureAwait(false);
        var dimensions = ReadDimensions(buffer, file.Name);

        var stored = await storage
            .SaveAsync(buffer, relativePath, mimeType, CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            return PersistMetadata(file, category, userId, mimeType, stored, altText, dimensions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Metadata insert failed for {RelativePath}; removing stored file", relativePath);
            await storage.DeleteAsync(relativePath, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Copies the browser upload into a seekable in-memory buffer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The buffer is capped at the category's own limit, which
    /// <c>ValidateImageAsync</c> has already applied to the declared size — so a client that lies
    /// about its file size is stopped by <see cref="IBrowserFile.OpenReadStream"/> rather than
    /// allowed to allocate without bound.</para>
    /// <para><b>Flow:</b> open the bounded stream → copy → rewind for the reader that follows.</para>
    /// <para><b>Side Effects:</b> Allocates a buffer the size of the upload; the caller disposes it.</para>
    /// </remarks>
    /// <param name="file">The browser file being uploaded.</param>
    /// <param name="category">The normalised upload category, which sets the ceiling.</param>
    /// <returns>A rewound stream holding the complete upload.</returns>
    private static async Task<MemoryStream> BufferUploadAsync(IBrowserFile file, string category)
    {
        var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream(maxAllowedSize: GetMaxSizeForCategory(category)))
        {
            await source.CopyToAsync(buffer).ConfigureAwait(false);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>
    /// Reads an upload's pixel dimensions from its buffered header.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A format this build cannot measure — SVG, PDF — is a normal
    /// outcome and yields nulls rather than zeros, because <c>NULL</c> means "not probed" while
    /// <c>0</c> would claim the image has no size. The stream is rewound afterwards so the storage
    /// provider still receives the whole file.</para>
    /// <para><b>Flow:</b> read the leading bytes → probe → rewind.</para>
    /// <para><b>Side Effects:</b> Moves and restores the buffer's position.</para>
    /// </remarks>
    /// <param name="buffer">The rewound upload buffer.</param>
    /// <param name="fileName">Original file name, for the diagnostic log entry only.</param>
    /// <returns>The dimensions, or a pair of nulls when the format was not recognised.</returns>
    private (int? Width, int? Height) ReadDimensions(MemoryStream buffer, string fileName)
    {
        var headerLength = (int)Math.Min(buffer.Length, HeaderProbeLength);
        var header = new byte[headerLength];
        _ = buffer.Read(header, 0, headerLength);
        buffer.Position = 0;

        if (ImageDimensionReader.TryReadDimensions(header, out var width, out var height))
        {
            return (width, height);
        }

        logger.LogInformation("Dimensions of {FileName} could not be read from its header", fileName);
        return (null, null);
    }

    /// <summary>
    /// Inserts the metadata row describing a stored file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>ImagePath</c> holds the provider's public URL so existing
    /// rendering code needs no change when the backend changes.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>BlogImage</c> row.</para>
    /// <para><b>Every descriptive column is populated (REQ-FN-026).</b> This method previously set
    /// only name, path, size, time, owner, category and MIME type, which left <c>AltText</c>,
    /// <c>Width</c> and <c>Height</c> NULL on every upload — the columns existed but the record did
    /// not actually carry the attributes the requirement names, and the missing alternative text is
    /// what REQ-NFR-007's WCAG 1.1.1 obligation depends on.</para>
    /// </remarks>
    /// <param name="file">The uploaded file, used for its original name.</param>
    /// <param name="category">The normalised upload category.</param>
    /// <param name="userId">Owner of the upload.</param>
    /// <param name="mimeType">Resolved MIME type.</param>
    /// <param name="stored">The storage backend's description of the written file.</param>
    /// <param name="altText">Alternative text supplied by the uploader, or <c>null</c>.</param>
    /// <param name="dimensions">Pixel size read from the header, or a pair of nulls.</param>
    /// <returns>The image record with its generated identifier populated.</returns>
    private BlogImage PersistMetadata(
        IBrowserFile file, string category, long userId, string mimeType, FileStorageResult stored,
        string? altText, (int? Width, int? Height) dimensions)
    {
        var blogImage = new BlogImage
        {
            ImageName = file.Name,
            ImagePath = stored.PublicUrl,
            Size = (int)file.Size,
            CreatedTime = DateTime.UtcNow,
            UserID = userId,
            Category = category,
            MimeType = mimeType,
            AltText = BuildAltText(altText, file.Name),
            Width = dimensions.Width,
            Height = dimensions.Height
        };

        blogImage.BlogImageID = imageRepo.InsertToGetId(blogImage);
        logger.LogInformation(
            "Image {ImageId} stored at {PublicUrl} via {Provider} at {Width}x{Height}",
            blogImage.BlogImageID, stored.PublicUrl, stored.ProviderName,
            blogImage.Width, blogImage.Height);
        return blogImage;
    }

    /// <summary>
    /// Chooses the alternative text stored against an upload.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A typed description always wins. When the uploader supplies
    /// none, the original file name — with its extension and its separators removed — is stored
    /// rather than NULL: it is a poor description, but it is an editable starting point and a
    /// screen reader announces something meaningful instead of a generated storage name. The value
    /// is clipped to the column width so a long name cannot fail the insert.</para>
    /// <para><b>Flow:</b> trim the supplied text → fall back to the humanised file name → clip.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="suppliedAltText">Text typed by the uploader, possibly blank or <c>null</c>.</param>
    /// <param name="fileName">The original file name.</param>
    /// <returns>Alternative text no longer than the column allows; never <c>null</c> or blank.</returns>
    private static string BuildAltText(string? suppliedAltText, string fileName)
    {
        var candidate = suppliedAltText?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            candidate = HumaniseFileName(fileName);
        }

        return candidate.Length <= MaxAltTextLength ? candidate : candidate[..MaxAltTextLength];
    }

    /// <summary>
    /// Turns a file name into readable words for use as fallback alternative text.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "my-holiday_photo.JPG" reads better to a screen reader as "my
    /// holiday photo" than as its raw file name. An extension-only or empty name has nothing to
    /// humanise and falls back to a generic word rather than to an empty string, which would signal
    /// "decorative" and hide the image from assistive technology altogether.</para>
    /// <para><b>Flow:</b> drop the extension → replace separators with spaces → collapse blanks.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="fileName">The original file name.</param>
    /// <returns>A non-empty, human-readable phrase.</returns>
    private static string HumaniseFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Uploaded image";
        }

        var lastDot = fileName.LastIndexOf('.');
        var stem = lastDot > 0 ? fileName[..lastDot] : fileName;
        var words = stem.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Trim();

        return string.IsNullOrWhiteSpace(words) ? "Uploaded image" : words;
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
