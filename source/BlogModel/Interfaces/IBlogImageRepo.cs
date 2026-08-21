using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for uploaded images.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists image metadata for the media library. Binary content lives in the
/// configured file storage, not in the database, so the generic CRUD surface is nearly sufficient.</para>
/// <para><b>Code Flow:</b> <c>BlogImageService</c> validates and stores the file, then persists the
/// metadata row through the inherited <c>InsertToGetIdAsync</c>; the media library lists rows through
/// the inherited reads and removes one through <see cref="DeleteAsync"/> after deleting the stored
/// file.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogImageRepo</c>.</para>
///
/// <para><b>Usage:</b> This contract governs metadata only — it neither reads nor writes the stored
/// bytes. The two are not transactional together, so a caller that deletes a row must delete the file
/// as well (and in that order: an orphaned file is recoverable, a row pointing at a missing file
/// renders as a broken image). Ownership is not enforced here either; the service checks it before
/// calling.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> the whole read/write surface is inherited from
/// <see cref="IGenericRepository{TEntity}"/>, whose <c>…Async</c> members <c>BlogImageRepo</c> now
/// overrides with genuine async Dapper. The one member declared here, <see cref="DeleteAsync"/>,
/// closes a gap the conversion exposed: the media library's delete had no repository member at all,
/// so <c>BlogImageService</c> was reaching through <c>GetOpenConnection()</c> to issue the statement
/// itself. That is both a module-boundary violation — SQL in a service — and, inside an <c>async</c>
/// method, the blocking-connection trap this requirement exists to remove.</para>
/// </remarks>
public interface IBlogImageRepo : IGenericRepository<BlogImage>
{
    /// <summary>
    /// Removes one image's metadata row without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an identifier that no longer exists affects no rows and
    /// is treated as a no-op rather than an error, so a double submit — or a retry after the stored
    /// file has already gone — is harmless.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → delete by key.</para>
    /// <para><b>Side Effects:</b> Removes at most one <c>BlogImage</c> row. The stored file is the
    /// caller's responsibility; this member touches metadata only.</para>
    /// </remarks>
    /// <param name="imageId">Identifier of the row to remove.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns><c>true</c> when a row was removed.</returns>
    Task<bool> DeleteAsync(long imageId, CancellationToken cancellationToken = default);
}
