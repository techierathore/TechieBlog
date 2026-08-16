using System;
using System.Collections.Generic;

namespace BlogModels;

/// <summary>
/// The canonical editorial-status literals stored in <c>BlogSeries.Status</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogSeries.Status</c> is free text with no lookup table, so the same
/// literal has to be spelled identically in the seed migration, the admin editor's status picker,
/// the admin list's filter tabs and the public series page. It was not: the database and
/// <c>019-SampleData.sql</c> stored <c>"Completed"</c> while the C# side compared against
/// <c>"Complete"</c>, so a completed series rendered as "In Progress" and its filter tab counted
/// zero (REQ-UI-024). Holding the literals here turns that class of typo into a compile error.</para>
///
/// <para><b>Code Flow:</b> purely declarative. <see cref="BlogSeries.Status"/> defaults to
/// <see cref="InProgress"/>; <see cref="BlogSeries.IsComplete"/> delegates to
/// <see cref="IsCompleted"/>; <c>SeriesSvc.CreateSeries</c> / <c>UpdateSeries</c> (and their async
/// twins) run every inbound value through <see cref="Normalize"/> before it reaches the repository,
/// so nothing but a canonical literal can be persisted.</para>
///
/// <para><b>Dependencies:</b> None — this is the bottom of the graph. The database side is
/// <c>Status VARCHAR(50) DEFAULT 'In Progress'</c> from <c>007-FixBlogSeriesAndPostTag.sql</c>,
/// normalised and constrained to these two values by <c>029-NormalizeSeriesStatus.sql</c>.</para>
///
/// <para><b>Usage:</b> Never write a status literal anywhere else — bind pickers to
/// <see cref="All"/>, test completion with <see cref="IsCompleted"/> and sanitise anything that
/// arrives from outside with <see cref="Normalize"/>. <see cref="IsCompleted"/> deliberately still
/// recognises the legacy <c>"Complete"</c> spelling so a row written before the fix keeps rendering
/// correctly; <see cref="Normalize"/> rewrites it to <see cref="Completed"/> on the next save.</para>
/// </remarks>
public static class SeriesStatus
{
    /// <summary>
    /// Parts are still being written or published — the column default and the value a new series
    /// starts with.
    /// </summary>
    public const string InProgress = "In Progress";

    /// <summary>
    /// Every part has been published and no more are planned. This is the canonical spelling; the
    /// pre-REQ-UI-024 code compared against <c>"Complete"</c>, which no row ever stored.
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>
    /// The superseded spelling of <see cref="Completed"/>. Recognised on read so pre-fix rows still
    /// render as completed; never written.
    /// </summary>
    public const string LegacyCompleted = "Complete";

    /// <summary>
    /// Every canonical status, in the order the admin editor offers them.
    /// </summary>
    /// <remarks>
    /// Bind the status picker to this rather than repeating the literals in markup — a value that is
    /// not in this list cannot survive <see cref="Normalize"/>.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = new[] { InProgress, Completed };

    /// <summary>
    /// Reports whether a stored status means "the series is finished".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> True for <see cref="Completed"/> and for the legacy
    /// <see cref="LegacyCompleted"/> spelling; anything else — including null, blank and
    /// <see cref="InProgress"/> — is false. The comparison trims surrounding whitespace and ignores
    /// case, so a hand-edited row is not silently mis-rendered the way the original ordinal,
    /// case-sensitive equality was.</para>
    /// <para><b>Flow:</b> null/blank guard → trim → ordinal case-insensitive compare against the two
    /// accepted spellings.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="status">The stored status value, which may be null or blank.</param>
    /// <returns><c>true</c> when the series is complete; otherwise <c>false</c>.</returns>
    public static bool IsCompleted(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var trimmed = status.Trim();
        return string.Equals(trimmed, Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, LegacyCompleted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Maps any inbound status value onto one of the two canonical literals.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Anything <see cref="IsCompleted"/> accepts becomes
    /// <see cref="Completed"/>; everything else — including null, blank and unrecognised text —
    /// becomes <see cref="InProgress"/>, which is the honest default for a series whose completion
    /// was never asserted. Call it on the write path so the database can only ever hold a value the
    /// read path understands.</para>
    /// <para><b>Flow:</b> completion test → return the matching canonical constant.</para>
    /// <para><b>Side Effects:</b> None; pure. It does not mutate the argument.</para>
    /// </remarks>
    /// <param name="status">The candidate status, which may be null, blank or misspelled.</param>
    /// <returns><see cref="Completed"/> or <see cref="InProgress"/> — never anything else.</returns>
    public static string Normalize(string? status) => IsCompleted(status) ? Completed : InProgress;
}
