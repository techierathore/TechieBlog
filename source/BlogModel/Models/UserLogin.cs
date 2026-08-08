namespace BlogModels;

/// <summary>
/// An issued session token and its lifetime — one row of the <c>userlogins</c> table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists the JWT handed to a user at sign-in so a token can be revoked or
/// its status inspected server-side, which a self-contained JWT cannot support on its own.</para>
///
/// <para><b>Code Flow:</b> Written by <c>BlogEngine.DbAccess.UserLoginRepo</c> after
/// <c>AuthSvc</c> mints a token; read back during token validation. Column names map
/// case-insensitively onto these property names — the migration
/// <c>PostgresScripts/006-FixUserLoginTable.sql</c> was written specifically to match this class.</para>
///
/// <para><b>Dependencies:</b> The <c>userlogins</c> table; a foreign key to <c>bloguser</c>.</para>
///
/// <para><b>Usage:</b> A data carrier only. Expiry is not enforced by the database — a row keeps
/// whatever <see cref="TokenStatus"/> it was last written with, so a token past
/// <see cref="ExipryDate"/> still reads as <c>ValidToken</c> unless something updates it.</para>
/// </remarks>
public class UserLogin
{
	/// <summary>
	/// Surrogate key of the session row (<c>loginid</c>).
	/// </summary>
	/// <remarks>
	/// Declared <see cref="int"/> although the column is <c>BIGSERIAL</c>; the two disagree, and the
	/// value will overflow once the sequence passes <see cref="int.MaxValue"/>.
	/// </remarks>
	public int LoginId { get; set; }

	/// <summary>
	/// The <c>bloguser</c> the token was issued to. Required by the foreign key — never zero on a
	/// persisted row.
	/// </summary>
	public long UserId { get; set; }

	/// <summary>
	/// When the sign-in occurred. Defaults to <c>CURRENT_TIMESTAMP</c> server-side; server-local
	/// time, not UTC.
	/// </summary>
	public DateTime LoginDate { get; set; }

	/// <summary>
	/// The signed JWT itself, stored verbatim in an unbounded <c>TEXT</c> column and indexed for
	/// lookup. Treat as a bearer credential: never log it and never render it.
	/// </summary>
	public string LoginToken { get; set; } = string.Empty;

	/// <summary>
	/// Lifecycle state of the token, persisted as the string name of a <see cref="BlogModels.TokenStatus"/>
	/// member (the column is <c>VARCHAR(50)</c>, defaulting to <c>ValidToken</c>). Stored as text
	/// rather than as the enum, so nothing prevents an unrecognised value from being written —
	/// compare against <c>TokenStatus.X.ToString()</c>, which is what <c>AuthSvc</c> does.
	/// </summary>
	public string TokenStatus { get; set; } = string.Empty;

	/// <summary>
	/// When the token stops being valid.
	/// </summary>
	/// <remarks>
	/// The name misspells "Expiry", and the misspelling reaches all the way into the schema — the
	/// column is literally <c>exiprydate</c>. Renaming the property therefore requires a migration;
	/// it is not a safe local rename.
	/// </remarks>
	public DateTime ExipryDate { get; set; }

	/// <summary>
	/// When the token was signed. Distinct from <see cref="LoginDate"/> because a refresh reissues
	/// a token without a new interactive sign-in.
	/// </summary>
	public DateTime IssueDate { get; set; }
}

/// <summary>
/// Lifecycle states a session token can be in.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the values stored in the <c>tokenstatus</c> column of
/// <c>userlogins</c> and <c>svctoken</c>.</para>
///
/// <para><b>Code Flow:</b> Persisted by name, not by ordinal — <c>AuthSvc</c> writes
/// <c>TokenStatus.ValidToken.ToString()</c>. Reordering the members is therefore harmless, but
/// renaming one silently orphans every existing row that holds the old name.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Only <see cref="ValidToken"/> is ever written today; the other three are
/// declared for a revoke/expire sweep that does not yet exist. Do not assume a token is genuinely
/// live merely because its row says <see cref="ValidToken"/> — check the expiry date too.</para>
/// </remarks>
public enum TokenStatus
{
	/// <summary>The token is live. The only value written by the current code.</summary>
	ValidToken,

	/// <summary>The token failed validation — wrong signature or malformed. Never written today.</summary>
	InValidToken,

	/// <summary>The token is past its expiry. Never written today; expiry is checked, not recorded.</summary>
	ExpiredToken,

	/// <summary>The token was revoked before expiry, e.g. by an explicit sign-out. Never written today.</summary>
	InActiveToken
}
