using Microsoft.AspNetCore.Components.Forms;
using BlogModels;

namespace BlogModels.Interfaces;

/// <summary>
/// Business contract for uploading, listing and removing media-library images.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Holds the rules that decide whether an upload is acceptable — per-category
/// size ceilings and format allow-lists — and coordinates the two stores an image lives in: the bytes
/// in the configured file-storage provider and the metadata row in <c>BlogImage</c>. Callers get one
/// operation per intent and never see either store directly.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Validate — <see cref="ValidateImageAsync"/> checks the file against the category's
///         constraints and returns a message the UI can show. It is safe to call on its own, and
///         <see cref="UploadImageAsync"/> repeats it, so a caller cannot bypass it by skipping this
///         step.</item>
///   <item>Store — <see cref="UploadImageAsync"/> writes the bytes through the storage provider, then
///         persists the metadata row pointing at the provider's public URL.</item>
///   <item>Browse — <see cref="GetImagesByCategoryAsync"/> and <see cref="GetImagesByUserAsync"/> list
///         the library; <see cref="GetImageAsync"/> resolves one row;
///         <see cref="GetImageUrl"/> turns a stored path into something an <c>&lt;img&gt;</c> can
///         use.</item>
///   <item>Remove — <see cref="DeleteImageAsync"/> checks ownership, deletes the stored file, then the
///         metadata row.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.Services.BlogImageService</c> over
/// <c>IBlogImageRepo</c> for metadata and <c>IFileStorageFactory</c> for the bytes.</para>
///
/// <para><b>Usage — the failure convention is split, and callers must know which half they are in.</b>
/// <see cref="UploadImageAsync"/> is the only member that <i>throws</i>: a rejected file or a bad user
/// id surfaces as an exception, so the upload page must wrap the call. Every other member swallows its
/// failures and reports them as an empty sequence, <c>null</c>, or <c>false</c>. That is deliberate —
/// a media-library grid should not blow up a page — but it means "no images" and "the query failed"
/// are indistinguishable to a caller, and so are "you may not delete this", "it does not exist" and
/// "the delete failed". Treat a <c>false</c> from <see cref="DeleteImageAsync"/> as "not removed",
/// nothing more precise. Note also that this contract carries no <c>CancellationToken</c> on any
/// member, unlike the repositories beneath it (REQ-NFR-026), so an upload the user has navigated away
/// from still runs to completion.</para>
///
/// <para><b>Story:</b> Stream F - BlogImageService Implementation</para>
/// </remarks>
public interface IBlogImageService
{
    /// <summary>
    /// Uploads an image file to the server and creates a database record.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Re-runs <see cref="ValidateImageAsync"/> before touching either
    /// store, so validation cannot be skipped by calling this member directly. The stored file name is
    /// generated, never taken from the upload, which is what keeps a hostile file name out of the
    /// storage path.</para>
    /// <para><b>Flow:</b> reject a non-positive user id → validate → store the bytes → persist the
    /// metadata row → return it. A failure after the bytes are written removes them again before
    /// rethrowing, so a failed upload leaves no orphan.</para>
    /// <para><b>Side Effects:</b> Writes one object to the storage provider and one row to
    /// <c>BlogImage</c>. The two are not transactional together.</para>
    /// </remarks>
    /// <param name="file">The browser file to upload.</param>
    /// <param name="category">The image category (profiles, logos, awards, icons, blog, cv, general);
    /// matched case-insensitively. Each category carries its own size ceiling and format allow-list.</param>
    /// <param name="userId">The ID of the user uploading the image; must be greater than zero. It is
    /// recorded as the owner and is what <see cref="DeleteImageAsync"/> later checks against.</param>
    /// <returns>The persisted record with its generated identifier and public <c>ImagePath</c>
    /// populated. Never <c>null</c> — this member reports failure by throwing.</returns>
    /// <exception cref="ArgumentException">The user id is not greater than zero.</exception>
    /// <exception cref="InvalidOperationException">The file failed validation; the message is the same
    /// text <see cref="ValidateImageAsync"/> would have returned and is safe to show the user.</exception>
    Task<BlogImage> UploadImageAsync(IBrowserFile file, string category, long userId);

    /// <summary>
    /// Deletes an image from disk and database.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ownership is enforced here rather than by the caller — an image
    /// may only be removed by the user who uploaded it.</para>
    /// <para><b>Flow:</b> reject non-positive ids → load the row → compare owners → delete the stored
    /// file → delete the metadata row.</para>
    /// <para><b>Side Effects:</b> Removes one stored object and one <c>BlogImage</c> row. The file goes
    /// first, so an interrupted delete leaves a row pointing at missing bytes rather than an
    /// unreferenced file.</para>
    /// </remarks>
    /// <param name="imageId">The ID of the image to delete.</param>
    /// <param name="userId">The ID of the user requesting deletion; must match the recorded owner.</param>
    /// <returns><c>true</c> only when both the file and the row were removed. <c>false</c> covers every
    /// other case indiscriminately — unknown image, wrong owner, invalid id, or an error — so a caller
    /// cannot distinguish "denied" from "failed" and must not report one as the other.</returns>
    Task<bool> DeleteImageAsync(long imageId, long userId);

    /// <summary>
    /// Gets all images in a category, optionally filtered by user.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category matching is case-insensitive. A <c>null</c> or blank
    /// category is not an error — it falls back to <c>general</c>, so a caller that forgets the argument
    /// gets a plausible-looking list rather than a failure.</para>
    /// <para><b>Side Effects:</b> None — read-only.</para>
    /// </remarks>
    /// <param name="category">The image category to filter by; <c>null</c> is treated as
    /// <c>general</c>.</param>
    /// <param name="userId">Optional owner filter. <c>null</c> or a non-positive value means "any
    /// owner" rather than "no owner".</param>
    /// <returns>The matching images, newest <c>CreatedTime</c> first; an empty sequence — never
    /// <c>null</c> — when nothing matches <i>or</i> when the read failed. The two are not
    /// distinguishable.</returns>
    Task<IEnumerable<BlogImage>> GetImagesByCategoryAsync(string category, long? userId = null);

    /// <summary>
    /// Gets all images uploaded by a specific user.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only.</para>
    /// </remarks>
    /// <param name="userId">The user ID; a non-positive value yields an empty sequence rather than an
    /// error.</param>
    /// <returns>The images owned by the user; an empty sequence — never <c>null</c> — when they own
    /// none <i>or</i> when the read failed. The two are not distinguishable. No ordering is
    /// guaranteed.</returns>
    Task<IEnumerable<BlogImage>> GetImagesByUserAsync(long userId);

    /// <summary>
    /// Gets a single image by ID.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No ownership check — any caller may read any image's metadata, so
    /// a caller that needs one must apply it itself.</para>
    /// <para><b>Side Effects:</b> None — read-only.</para>
    /// </remarks>
    /// <param name="imageId">The image ID; a non-positive value yields <c>null</c>.</param>
    /// <returns>The image, or <c>null</c> when the identifier is unknown, non-positive, <i>or</i> the
    /// read failed. The three are not distinguishable.</returns>
    Task<BlogImage?> GetImageAsync(long imageId);

    /// <summary>
    /// Converts a relative image path to a full URL path.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Pure string work — it never touches the storage provider, so it
    /// cannot tell whether the image exists. A path that is already absolute or already carries a scheme
    /// is returned unchanged, which is what lets local-disk and cloud-hosted images share one call
    /// site.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="imagePath">The path stored in <c>BlogImage.ImagePath</c>.</param>
    /// <returns>A path an <c>&lt;img&gt;</c> can use; the empty string when the input is <c>null</c> or
    /// blank, so a missing image renders as nothing rather than as a broken relative link.</returns>
    string GetImageUrl(string imagePath);

    /// <summary>
    /// Validates an image file against category constraints before upload.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Checks presence, a recognised category, an allowed extension and
    /// the category's size ceiling, in that order, and stops at the first failure — so
    /// <c>Error</c> names one problem, not all of them. Judgement is by declared extension and declared
    /// size; the bytes are never inspected, so this is an input-validation gate, not a malware
    /// check.</para>
    /// <para><b>Flow:</b> null file → missing category → unknown category → extension → size.</para>
    /// <para><b>Side Effects:</b> None — nothing is read or written.</para>
    /// </remarks>
    /// <param name="file">The browser file to validate; <c>null</c> is a normal invalid input, not an
    /// exception.</param>
    /// <param name="category">The target category; matched case-insensitively. An unrecognised value
    /// fails validation and the message lists the valid ones.</param>
    /// <returns><c>IsValid</c> true with a <c>null</c> <c>Error</c>, or false with a message that is
    /// written for the end user and is safe to display verbatim. Never throws.</returns>
    Task<(bool IsValid, string? Error)> ValidateImageAsync(IBrowserFile file, string category);
}
