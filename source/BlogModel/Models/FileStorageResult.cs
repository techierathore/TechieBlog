namespace BlogModels.Models;

/// <summary>
/// Describes a file after it has been committed to a storage backend.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives callers everything they need to record and serve a stored file
/// without knowing which backend accepted it — the relative key for later deletion, the public
/// URL for rendering, and the byte count actually written.</para>
///
/// <para><b>Code Flow:</b> Returned by <c>IFileStorage.SaveAsync</c>; <c>BlogImageService</c>
/// copies <see cref="PublicUrl"/> into the <c>BlogImage</c> row it persists.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Always store <see cref="RelativePath"/> alongside the public URL — it is
/// the only value that stays valid if the public base URL is later reconfigured.</para>
///
/// <para><b>It only ever describes a success.</b> Every provider builds this at the end of a write
/// that completed; a failed save reports through the call's failure channel, never through a
/// half-filled instance. So a non-null result means the bytes are committed — but it is a snapshot
/// taken at that moment, not a live handle. Nothing revalidates it afterwards, and nothing deletes
/// the file if the database row that was meant to record it is never written; an orphaned blob is
/// the expected result of a half-finished upload.</para>
/// </remarks>
public class FileStorageResult
{
    /// <summary>
    /// Backend-relative key the file was written under, using forward slashes,
    /// for example <c>uploads/blog/blog-1-abc.png</c>.
    /// </summary>
    /// <remarks>
    /// The durable identity of the file and the only value a delete can be issued against. It is
    /// normalised to forward slashes by every provider, so it is comparable across backends even
    /// where the underlying filesystem uses another separator — never rebuild it with
    /// <c>Path.Combine</c>, which would reintroduce a platform separator.
    /// <para>Provider-relative, not web-root-relative: it is meaningful only in combination with
    /// <see cref="ProviderName"/> and that provider's configured root, so the same key can address
    /// different files under two different configurations.</para>
    /// </remarks>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// URL a browser can use to fetch the file.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="RelativePath"/> and the provider's configured public base at the
    /// moment of the write, then copied into the <c>BlogImage</c> row — so it is a cached value, and
    /// re-pointing the base URL invalidates every stored copy without touching the files. That is why
    /// the relative key must be stored beside it.
    /// <para><b>Exposure:</b> unauthenticated. Anything written here is served to whoever knows the
    /// URL, with no permission check in front of it; the path is the only secret, and it is not a
    /// good one. Never place a private document behind a merely unguessable path.</para>
    /// </remarks>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// Number of bytes written.
    /// </summary>
    /// <remarks>
    /// Measured by the provider from what it actually committed, not taken from the upload's declared
    /// length, so it is the value to record and to compare against a size quota. It is a
    /// <see cref="long"/> here while <c>BlogImage.Size</c> is an <see cref="int"/> — a file above two
    /// gigabytes cannot round-trip through that column.
    /// </remarks>
    public long SizeInBytes { get; set; }

    /// <summary>
    /// Name of the provider that accepted the write, from <see cref="StorageProviderNames"/> —
    /// <c>Local</c>, <c>Network</c> or <c>Cloud</c>.
    /// </summary>
    /// <remarks>
    /// Records which backend a file actually landed in, which matters when the configuration changes:
    /// a file written under <c>Local</c> is not reachable through a later <c>Cloud</c> configuration,
    /// and <see cref="RelativePath"/> alone cannot tell you that. Compare it against the constants
    /// rather than against literals — the factory matches provider names case-insensitively, so
    /// casing here is not guaranteed to match a literal you write.
    /// </remarks>
    public string ProviderName { get; set; } = string.Empty;
}
