namespace BlogModels;

/// <summary>
/// An uploaded image asset in the media library.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One row of <c>BlogImage</c> — the metadata record for a file held by the
/// configured storage provider. The bytes live in storage; only the path and descriptive fields are
/// stored here.</para>
///
/// <para><b>Code Flow:</b> Written by <c>BlogEngine</c>'s image service after an
/// <see cref="Interfaces.IFileStorage"/> implementation has accepted the upload and returned a path;
/// read back by the media picker and by post rendering.</para>
///
/// <para><b>Dependencies:</b> The <c>BlogImage</c> table in
/// <c>PostgresScripts/001-CreateTables.sql</c>, extended with the descriptive columns by
/// <c>012-ResumeAndImageManagement.sql</c>.</para>
///
/// <para><b>Usage:</b> A data carrier. Deleting a row does not delete the underlying file — the
/// storage provider is cleaned up separately, so an orphaned blob is the expected failure mode of a
/// half-finished delete.</para>
/// </remarks>
public class BlogImage
{
    /// <summary>
    /// Surrogate key (<c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long BlogImageID { get; set; }

    /// <summary>
    /// The original filename as uploaded, kept for display in the media library. Not the storage
    /// name — the provider may rename the file to avoid collisions; <see cref="ImagePath"/> is the
    /// authoritative locator.
    /// </summary>
    public string ImageName    { get; set; } = string.Empty;

    /// <summary>
    /// Where the file actually lives, as returned by the storage provider. Required. Its
    /// interpretation is provider-dependent — a site-relative path for local disk, a key or URL for
    /// a remote provider — so never assume it is resolvable against the web root.
    /// </summary>
    public string ImagePath    { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes. Zero when the size was not recorded at upload time.
    /// </summary>
    public int Size    { get; set; }

    /// <summary>
    /// When the file was uploaded. Required; server-local time.
    /// </summary>
    public DateTime CreatedTime    { get; set; }

    /// <summary>
    /// The <c>BlogUser</c> who uploaded the file. Required by the foreign key.
    /// </summary>
    public long UserID { get; set; }

    /// <summary>
    /// Library grouping used to filter the media picker, defaulting to <c>"general"</c> both here
    /// and in the column. Free text with no lookup table, matched by exact string.
    /// </summary>
    public string Category { get; set; } = "general";

    /// <summary>
    /// Accessible alternative text emitted as the <c>alt</c> attribute. Null means no text was
    /// supplied, which is an accessibility gap on any image that carries meaning — render an empty
    /// <c>alt</c> for decorative images rather than omitting the attribute.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// The file's media type, e.g. <c>image/png</c>. Recorded at upload; null on rows predating
    /// migration 012.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Pixel width, recorded at upload so layouts can reserve space without loading the file. Null
    /// when the dimensions were not probed.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Pixel height. Same caveat as <see cref="Width"/> — the pair is populated together or not at
    /// all.
    /// </summary>
    public int? Height { get; set; }
}
