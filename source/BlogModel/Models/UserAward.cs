namespace BlogModels.Models;

/// <summary>
/// An award, certification or achievement shown in the resume's recognition section.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One row of <c>UserAwards</c>, added by migration
/// <c>012-ResumeAndImageManagement.sql</c> to give the public resume page a credentials block.</para>
///
/// <para><b>Code Flow:</b> Read and written by <c>BlogEngine.DbAccess.UserAwardsRepo</c> through
/// <c>IUserAwardsRepo</c>; rendered by the resume page for the user whose
/// <c>AppUser.ResumeEnabled</c> is set.</para>
///
/// <para><b>Dependencies:</b> The <c>UserAwards</c> table and its foreign key to <c>BlogUser</c>.</para>
///
/// <para><b>Usage:</b> A data carrier. Everything here is author-supplied text destined for a public
/// page — escape it on render; nothing sanitises it on the way in.</para>
/// </remarks>
public class UserAward
{
    /// <summary>
    /// Surrogate key (<c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long AwardId { get; set; }

    /// <summary>
    /// Owning <c>BlogUser</c>. Required by the foreign key; awards are never shared between users.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Name of the award or certification, e.g. "Microsoft MVP". Required, at most 255 characters.
    /// </summary>
    public string AwardTitle { get; set; } = string.Empty;

    /// <summary>
    /// Optional free-text detail about the achievement. Unbounded <c>TEXT</c>; null when the author
    /// supplied only a title.
    /// </summary>
    public string? AwardDescription { get; set; }

    /// <summary>
    /// Optional site-relative path to a badge or certificate image. Null hides the badge rather than
    /// rendering a broken image.
    /// </summary>
    public string? BadgeImagePath { get; set; }

    /// <summary>
    /// Optional absolute URL where the award can be verified. Points off-site; treat as untrusted.
    /// </summary>
    public string? AwardUrl { get; set; }

    /// <summary>
    /// Year the award was received, stored as free text (<c>VARCHAR(50)</c>) rather than a number so
    /// that ranges and qualifiers such as "2019–2024" or "2021 (renewed)" can be recorded. Do not
    /// parse it as an integer.
    /// </summary>
    public string? AwardYear { get; set; }

    /// <summary>
    /// Manual sort position within the resume's award list, ascending. Defaults to <c>0</c>, so
    /// awards that were never ordered all tie and fall back to whatever order the query returns.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// When the row was created. Set by the database default; audit metadata, never shown on the
    /// resume — the date a visitor sees is <see cref="AwardYear"/>.
    /// </summary>
    public DateTime CreatedOn { get; set; }
}
