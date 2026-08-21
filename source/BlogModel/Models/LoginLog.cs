namespace BlogModels;

/// <summary>
/// One row of the <c>LoginLog</c> security audit trail.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the "recent sign-in activity" view — which account signed in, from
/// which address, and when.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogEngine.DbAccess.LoginLogRepo</c>, which aliases
/// the snake-free PostgreSQL columns onto these properties (<c>logid → LoginLogId</c>,
/// <c>userid → LoginUserId</c>, <c>attemptedon → LoginDateTime</c>, <c>ipaddress → ClientIP</c>,
/// <c>attemptedemail → AttemptedEmail</c>, <c>success → Success</c>,
/// <c>useragent → UserAgent</c>).</para>
///
/// <para><b>Dependencies:</b> The <c>LoginLog</c> table in
/// <c>PostgresScripts/001-CreateTables.sql</c>.</para>
///
/// <para><b>Usage:</b> This model now mirrors every column of the table (REQ-FN-051). It used to
/// omit <see cref="AttemptedEmail"/>, <see cref="Success"/> and <see cref="UserAgent"/>, which made
/// a failed sign-in indistinguishable from a successful one through this model and left the
/// repository free to hard-code <c>success = true</c>. Both halves were repaired together: a
/// refused attempt is written with <see cref="Success"/> <c>false</c> and the address that was
/// tried, so a run of failures — a brute-force attempt — is visible in the log.</para>
///
/// <para><b>Security:</b> the attempted <i>password</i> is never carried on this type and must
/// never be added to it. Only the attempted address, the outcome and the client metadata are
/// auditable.</para>
/// </remarks>
public class LoginLog
{

    /// <summary>
    /// Surrogate key of the audit row (<c>logid</c>). Assigned by the database sequence; zero on an
    /// instance that has not been inserted yet.
    /// </summary>
    public long LoginLogId { get; set; }

    /// <summary>
    /// The <c>BlogUser</c> whose credentials were presented, or <c>null</c> when the attempt named
    /// an address that matches no account. The underlying column is a nullable foreign key, so a
    /// failed attempt against an unknown address is recorded rather than rejected by the constraint
    /// (REQ-FN-051).
    /// </summary>
    public long? LoginUserId { get; set; }

    /// <summary>
    /// The address the sign-in was attempted with (<c>attemptedemail</c>). Recorded on every
    /// attempt, successful or not, because a failed attempt that cannot be attributed to an address
    /// is useless to an abuse investigation.
    /// </summary>
    public string AttemptedEmail { get; set; } = string.Empty;

    /// <summary>
    /// Whether the attempt succeeded (<c>success</c>). <c>false</c> covers a wrong password, an
    /// unknown address and an attempt refused by the login throttle (REQ-NFR-005).
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The client's user-agent string (<c>useragent</c>, up to 500 characters). Empty when the
    /// caller could not supply one — a Blazor Server circuit has no HTTP request to read it from.
    /// </summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// When the attempt was made (<c>attemptedon</c>). Server-local time, not UTC — the column
    /// is <c>TIMESTAMP</c> without a time zone and defaults to <c>CURRENT_TIMESTAMP</c>.
    /// </summary>
    public DateTime LoginDateTime { get; set; }

    /// <summary>
    /// Remote address the attempt originated from (<c>ipaddress</c>, up to 100 characters, so it
    /// accommodates IPv6). Empty when the address could not be determined; behind a reverse proxy
    /// this is only as trustworthy as the forwarded-headers configuration.
    /// </summary>
    public string ClientIP { get; set; } = string.Empty;

    /// <summary>
    /// Intended sign-out timestamp. <b>Never populated</b> — the <c>LoginLog</c> table has no logout
    /// column, and <c>LoginLogRepo.UpdateLogOut</c> is a documented no-op. Always
    /// <see cref="DateTime.MinValue"/>; do not render it.
    /// </summary>
    public DateTime LogOutDateTime { get; set; }

    /// <summary>
    /// Given name of the user, for display alongside the audit row. Not selected by any query in
    /// <c>LoginLogRepo</c>, so it is empty unless a caller joins and fills it in itself.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Family name of the user. Same caveat as <see cref="FirstName"/> — not populated by the
    /// repository.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Display name composed from <see cref="FirstName"/> and <see cref="LastName"/>. Computed, not
    /// persisted; returns a lone space when neither source property has been filled in, which is
    /// the normal case for rows loaded through <c>LoginLogRepo</c>.
    /// </summary>
    public string FullName
    {
        get
        {
            return FirstName + " " + LastName;
        }
    }
}

/// <summary>
/// Query parameters for a Vedic-astrology report, carried over from an unrelated application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> None in TechieBlog. <c>Jatak</c> (natal chart), <c>Bhav</c> (house) and
/// <c>Planet</c> are astrology terms; there is no report feature, no corresponding table and no
/// reference to this type anywhere in <c>source/</c> or <c>tests/</c>.</para>
///
/// <para><b>Code Flow:</b> Dead. Nothing constructs, returns or consumes it.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Do not build on this type — it is a deletion candidate, tracked with the
/// other leftovers listed on <see cref="AppConstants.ImageTypeReceipt"/>. It also sits in the wrong
/// file: it has nothing to do with <see cref="LoginLog"/>.</para>
/// </remarks>
public class ReportInput
{
    /// <summary>Identifies the requesting user. Unused; see the remarks on the type.</summary>
    public long AppUserId { get; set; }

    /// <summary>Identifies the natal chart the report is drawn for. Unused.</summary>
    public long JatakUserId { get; set; }

    /// <summary>Astrological house number. Unused.</summary>
    public byte Bhav { get; set; }

    /// <summary>Planet name the rule applies to. Unused.</summary>
    public string Planet { get; set; } = string.Empty;

    /// <summary>Rule classification. Unused.</summary>
    public string RuleType { get; set; } = string.Empty;

    /// <summary>Nature classification. Unused.</summary>
    public string NatureType { get; set; } = string.Empty;

}
