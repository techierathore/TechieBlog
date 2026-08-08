namespace BlogModels.Models;

/// <summary>
/// One skill entry in the resume's skills grid.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One row of <c>UserSkills</c> (migration
/// <c>012-ResumeAndImageManagement.sql</c>). Skills are displayed grouped by
/// <see cref="Category"/>, so the category string is what drives the layout, not just a label.</para>
///
/// <para><b>Code Flow:</b> Read and written by <c>BlogEngine.DbAccess.UserSkillsRepo</c> through
/// <c>IUserSkillsRepo</c>; the resume page groups the returned rows by <see cref="Category"/> and
/// orders each group by <see cref="DisplayOrder"/>.</para>
///
/// <para><b>Dependencies:</b> The <c>UserSkills</c> table, its foreign key to <c>BlogUser</c>, and
/// the <c>IdxUserSkillsCategory</c> index that supports the grouping.</para>
///
/// <para><b>Usage:</b> A data carrier. Author-supplied text bound for a public page — escape on
/// render.</para>
/// </remarks>
public class UserSkill
{
    /// <summary>
    /// Surrogate key (<c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long SkillId { get; set; }

    /// <summary>
    /// Owning <c>BlogUser</c>. Required by the foreign key.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Grouping heading the skill appears under — "Languages", "Frameworks", "Databases" and so on.
    /// Required, at most 100 characters. There is no lookup table constraining the value, so it is
    /// matched by exact string: two rows differing only in case or spacing render as two separate
    /// headings.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the skill. Required, at most 150 characters.
    /// </summary>
    public string SkillName { get; set; } = string.Empty;

    /// <summary>
    /// Optional site-relative path to a technology logo. Null renders the skill as text only.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Sort position <i>within</i> the skill's <see cref="Category"/>, ascending — it does not order
    /// the categories themselves. Defaults to <c>0</c>.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// When the row was created; set by the database default. Audit metadata, not rendered.
    /// </summary>
    public DateTime CreatedOn { get; set; }
}
