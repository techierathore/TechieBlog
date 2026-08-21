namespace BlogModels.Models;

/// <summary>
/// A headline figure in the resume's statistics strip — "15+ Years of Experience", "200 Talks".
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One row of <c>UserStats</c> (migration
/// <c>012-ResumeAndImageManagement.sql</c>). Deliberately a label/value text pair rather than typed
/// columns, so the author controls the exact wording and formatting of both halves.</para>
///
/// <para><b>Code Flow:</b> Read and written by <c>BlogEngine.DbAccess.UserStatsRepo</c> through
/// <c>IUserStatsRepo</c>; rendered in <see cref="DisplayOrder"/> sequence by the resume page.</para>
///
/// <para><b>Dependencies:</b> The <c>UserStats</c> table and its foreign key to <c>BlogUser</c>.</para>
///
/// <para><b>Usage:</b> Nothing here is computed — these are hand-entered numbers that go stale
/// silently. Do not derive site metrics from them; the analytics tables are the source of truth for
/// anything measured.</para>
/// </remarks>
public class UserStat
{
    /// <summary>
    /// Surrogate key (<c>BIGSERIAL</c>). Zero until the row is inserted.
    /// </summary>
    public long StatId { get; set; }

    /// <summary>
    /// Owning <c>BlogUser</c>. Required by the foreign key.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The caption beneath the figure, e.g. "Years of Experience". Required, at most 100
    /// characters.
    /// </summary>
    public string StatLabel { get; set; } = string.Empty;

    /// <summary>
    /// The figure itself, held as text (<c>VARCHAR(50)</c>) so decorated values such as "15+",
    /// "~200" or "3.5M" survive intact. Required. Never parse this as a number.
    /// </summary>
    public string StatValue { get; set; } = string.Empty;

    /// <summary>
    /// Optional grouping key for the stat. Uncategorised rows are null and render in the default
    /// strip.
    /// </summary>
    public string? StatCategory { get; set; }

    /// <summary>
    /// Sort position in the strip, ascending. Defaults to <c>0</c>.
    /// </summary>
    public int DisplayOrder { get; set; }
}
