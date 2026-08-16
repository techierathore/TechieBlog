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
/// <para><b>Purpose:</b> Enforces the rules that make an upload acceptable — per-category size limits
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
///
/// <para><b>The limits are no longer written here (REQ-FN-025, 2026-08-11).</b> This class used to
/// hold its own <c>CategoryConstraintMap</c> and its own MIME table, and <c>ManageImages</c> and
/// <c>ImagePicker</c> each held a third and fourth copy for display — so the upload dialog could
/// advertise "Max 2MB" from one table while the dropzone advertised "Max size: 10 MB" from the
/// component's untouched default, and an administrator dropping a 5 MB avatar was accepted by the
/// client and rejected here. The values now live once, in
/// <see cref="ImageCategoryRules"/> in <c>BlogModels</c>, which every screen reads through
/// <see cref="GetCategoryRule"/>. This service remains the authority: it re-validates on
/// <see cref="UploadImageAsync"/> against the same table, so a bypassed client changes nothing.</para>
///
/// <para><b>A storage failure is now audible (REQ-NFR-040).</b> When the uploads directory was not
/// writable by the container's user, <c>StoreAsync</c> let the <c>UnauthorizedAccessException</c>
/// escape unlogged: the container stayed Up, <c>/healthz</c> stayed 200 Healthy, the startup line
/// still announced "Uploaded media served from /app/uploads", and the container log carried zero
/// <c>[ERR]</c>, <c>[WRN]</c> or <c>[FTL]</c> entries. The administrator saw only the caller's
/// generic "An error occurred while uploading the file. Please try again." and an operator grepping
/// the log found nothing at all. The failure was — and remains — transactionally clean: no partial
/// file, no orphaned <c>blogimage</c> row. <b>Only the observability was broken.</b></para>
///
/// <para><b>The split this fixes is the REQ-NFR-033 split.</b> <see cref="StoreAsync"/> now catches
/// every I/O failure, logs it at Error with the storage provider, the target relative path and the
/// exception itself — whose own text carries the absolute server path — and rethrows a curated
/// <see cref="InvalidOperationException"/>. The log gets the path and the exception; the
/// administrator gets <see cref="StorageUnwritableMessage"/> or
/// <see cref="StorageFailureMessage"/>, which name the <i>class</i> of problem without disclosing a
/// server path or exception text. Both calling components (<c>ImagePicker</c> and
/// <c>ManageImages</c>) already render the message of an <c>InvalidOperationException</c> and fall
/// back to their generic sentence for anything else, so the distinction reaches the screen with no
/// change on their side.</para>
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
    /// Message shown when the storage backend refused the write for a permissions reason
    /// (REQ-NFR-040).
    /// </summary>
    /// <remarks>
    /// <para>This is the message whose absence made the original defect invisible. It has to say
    /// something an administrator can act on — "the server cannot write there", which is a hosting
    /// problem, not something a retry will fix — while disclosing neither the absolute path nor the
    /// exception text (REQ-NFR-033). The path and the exception are in the Error log line raised
    /// immediately before this is thrown.</para>
    /// <para>Deliberately distinct from <see cref="StorageFailureMessage"/>: an operator told
    /// "please try again" will retry forever against a directory that will never become writable,
    /// which is exactly what happened here.</para>
    /// </remarks>
    private const string StorageUnwritableMessage =
        "The server cannot write to its upload location, so the file was not saved. " +
        "Retrying will not help — the uploads directory needs to be made writable by the " +
        "application. Ask an administrator to check the server log for the details.";

    /// <summary>
    /// Message shown when the storage backend failed for a non-permissions I/O reason
    /// (REQ-NFR-040). See <see cref="StorageUnwritableMessage"/>.
    /// </summary>
    /// <remarks>
    /// A full disk, a severed network share or a transient object-store error. Unlike a permissions
    /// failure this one may genuinely clear, so the message invites a retry — but it still names the
    /// server as the source, so the administrator does not assume their file was at fault.
    /// </remarks>
    private const string StorageFailureMessage =
        "The server could not save the file to its storage location. " +
        "Please try again; if it keeps failing, ask an administrator to check the server log.";

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
    public ImageCategoryRule GetCategoryRule(string category)
    {
        return ImageCategoryRules.For(category);
    }

    /// <inheritdoc />
    public Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category)
    {
        if (file == null)
            return Task.FromResult<(bool IsValid, string? Error)>((false, "No file provided."));

        if (string.IsNullOrWhiteSpace(category))
            return Task.FromResult<(bool IsValid, string? Error)>((false, "Category is required."));

        if (!ImageCategoryRules.TryGet(category, out var rule))
        {
            return Task.FromResult<(bool IsValid, string? Error)>((false,
                $"Invalid category '{category}'. Valid categories: {string.Join(", ", ImageCategoryRules.Categories)}."));
        }

        return Task.FromResult<(bool IsValid, string? Error)>(ValidateAgainstConstraints(file, rule));
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

        var normalizedCategory = ImageCategoryRules.Normalise(category);
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
    /// <exception cref="InvalidOperationException">
    /// The bytes could not be written. The underlying exception and the target path have already
    /// been logged at Error (REQ-NFR-040); the message carried here is curated and safe to render.
    /// </exception>
    private async Task<BlogImage> StoreAsync(
        IBrowserFile file, string category, long userId, string extension, string relativePath,
        string? altText)
    {
        var storage = await fileStorageFactory.GetStorageAsync().ConfigureAwait(false);
        var mimeType = ImageCategoryRules.MimeTypeFor(extension);

        await using var buffer = await BufferUploadAsync(file, category).ConfigureAwait(false);
        var dimensions = ReadDimensions(buffer, file.Name);

        var stored = await WriteBytesAsync(storage, buffer, relativePath, mimeType, userId)
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
    /// Writes the buffered upload through the storage backend, making any failure audible
    /// (REQ-NFR-040).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the whole of REQ-NFR-040. The write used to be a bare
    /// <c>await storage.SaveAsync(...)</c>, so an <c>UnauthorizedAccessException</c> raised because
    /// the uploads directory was not writable by the container's user travelled all the way to the
    /// page's <c>catch (Exception)</c>, which set a generic sentence and logged nothing. The
    /// container stayed Up and the log stayed clean, so there was no signal anywhere that uploads
    /// were dead.</para>
    /// <para><b>Two failure classes, two messages, because they demand different actions.</b> A
    /// permissions refusal will never clear on its own — telling the administrator to retry sends
    /// them into a loop — so it maps to <see cref="StorageUnwritableMessage"/>. Any other I/O
    /// failure (a full disk, a dropped network share, an object-store timeout) may well clear, and
    /// maps to <see cref="StorageFailureMessage"/>.</para>
    /// <para><b>Flow:</b> write → on failure log at Error with the provider, the storage-relative
    /// target path, the uploader and the exception → rethrow as a curated
    /// <see cref="InvalidOperationException"/> the calling page already knows how to render.</para>
    /// <para><b>Side Effects:</b> Writes one file. On failure, emits exactly one <c>[ERR]</c> line —
    /// which is the observable this requirement is measured by — and no file is left behind, because
    /// the backend never completed the write.</para>
    /// <para><b>The absolute path is in the log, never in the message.</b> The relative path and the
    /// provider name are logged as structured fields, and the exception carries the absolute server
    /// path in its own text; none of that reaches the administrator's screen (REQ-NFR-033).</para>
    /// </remarks>
    /// <param name="storage">The resolved storage backend.</param>
    /// <param name="buffer">The rewound upload buffer.</param>
    /// <param name="relativePath">Storage-relative destination path.</param>
    /// <param name="mimeType">Resolved MIME type recorded with the file.</param>
    /// <param name="userId">Owner of the upload, logged for correlation.</param>
    /// <returns>The storage backend's description of the written file.</returns>
    /// <exception cref="InvalidOperationException">The write failed; see the Error log line.</exception>
    private async Task<FileStorageResult> WriteBytesAsync(
        IFileStorage storage, Stream buffer, string relativePath, string mimeType, long userId)
    {
        try
        {
            return await storage
                .SaveAsync(buffer, relativePath, mimeType, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(
                ex,
                "Upload REFUSED: {Provider} storage cannot write {RelativePath} for user {UserId}. " +
                "The upload location is not writable by the account this process runs as — " +
                "check the directory's ownership and mode",
                storage.ProviderName, relativePath, userId);
            throw new InvalidOperationException(StorageUnwritableMessage, ex);
        }
        catch (IOException ex)
        {
            logger.LogError(
                ex,
                "Upload FAILED: {Provider} storage could not write {RelativePath} for user {UserId}",
                storage.ProviderName, relativePath, userId);
            throw new InvalidOperationException(StorageFailureMessage, ex);
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
        await using (var source = file.OpenReadStream(maxAllowedSize: ImageCategoryRules.For(category).MaxSizeBytes))
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
    /// <param name="rule">The category's authoritative limits.</param>
    /// <returns>Validity and, when invalid, the message to show the user.</returns>
    private static (bool IsValid, string? Error) ValidateAgainstConstraints(
        IBrowserFile file, ImageCategoryRule rule)
    {
        var extension = GetFileExtension(file.Name);
        if (string.IsNullOrEmpty(extension))
        {
            return (false, "File must have a valid extension.");
        }

        if (!rule.AllowsFormat(extension))
        {
            return (false, rule.BuildFormatMessage(extension));
        }

        if (file.Size > rule.MaxSizeBytes)
        {
            return (false, rule.BuildOversizeMessage(file.Size));
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

}
