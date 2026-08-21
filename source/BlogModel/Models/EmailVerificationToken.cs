namespace BlogModels;

/// <summary>
/// A persisted, single-use, time-limited double opt-in token.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the "confirm your email address" step for anonymous comments,
/// ratings and newsletter subscriptions. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A submission is stored in a pending state and a token row is inserted with
///   <see cref="ExpiresOn"/> 24 hours out.</item>
///   <item>The token travels to the visitor inside a <c>/verify/{token}</c> link.</item>
///   <item>Consuming the link flips <see cref="IsUsed"/> and stamps <see cref="ConsumedOn"/>
///   inside a single SQL statement, so the link works exactly once.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Persisted by <c>EmailVerificationTokenRepo</c> against the
/// <c>EmailVerificationToken</c> table created in migration script 014. Unlike the legacy
/// in-memory <c>PasswordResetTokenRepo</c>, these tokens SURVIVE an application restart -
/// that is the whole point of the requirement.</para>
///
/// <para><b>Usage:</b> Never expose <see cref="Token"/> anywhere except the emailed link.</para>
///
/// <para><b>An instance is not the same as the row.</b> Redemption goes through the stored function
/// <c>ConsumeEmailVerificationToken</c>, whose projection omits <see cref="RequestIpAddress"/> - so a
/// token returned by the consume path always has an empty IP even though the column holds one. Read
/// the row through the repository's plain lookup if you need the forensic field.</para>
///
/// <para><b>Time.</b> Application writes normalise to UTC, but the expiry test inside the stored
/// function compares against the <i>database server's</i> <c>CURRENT_TIMESTAMP</c>, and that is also
/// what stamps <see cref="ConsumedOn"/>. The two agree only while the database runs on UTC; on a
/// server with a local time zone the effective validity window shifts by its offset.</para>
/// </remarks>
public class EmailVerificationToken
{
    /// <summary>
    /// Surrogate primary key (<c>TokenId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Internal only - it is <see cref="Token"/>, never this, that appears in the emailed link. A
    /// sequential id in a URL would be guessable, which is precisely what the random token avoids.
    /// </remarks>
    public long TokenId { get; set; }

    /// <summary>
    /// The URL-safe random secret handed to the recipient (<c>Token VARCHAR(128) NOT NULL</c>,
    /// unique index <c>IdxEmailVerificationTokenToken</c>).
    /// </summary>
    /// <remarks>
    /// This value <i>is</i> the credential: whoever holds it can confirm the address, so it must be
    /// generated from a cryptographic source and never derived from the email, the target id or a
    /// timestamp. The unique index makes a collision an insert failure rather than a silent
    /// cross-wiring of two pending submissions.
    /// <para><b>Exposure:</b> the emailed link and nothing else. Never render it on a page, never log
    /// it, never include it in an error message or a redirect that could land in a referrer header.
    /// Look it up by exact value - the index is on the raw column, unlike the case-insensitive one on
    /// <see cref="Email"/>.</para>
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// The address being confirmed (<c>Email VARCHAR(320) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Indexed case-insensitively (<c>LOWER(Email)</c>), matching how the rest of the double opt-in
    /// flow treats an address, so <c>A@b.com</c> confirms <c>a@b.com</c>. On successful redemption
    /// this address joins the <c>VerifiedEmail</c> registry and later submissions from it skip
    /// confirmation entirely - which makes a mis-set value here a permanent grant, not a one-off.
    /// <para><b>Exposure:</b> personal data. Admin surfaces and the outbound mail only.</para>
    /// </remarks>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// What the confirmation unlocks (<c>Purpose VARCHAR(30) NOT NULL</c>) - one of the
    /// <see cref="EmailVerificationPurpose"/> values.
    /// </summary>
    /// <remarks>
    /// The switch that decides which promotion routine runs when the token is consumed, so it must be
    /// consistent with <see cref="TargetId"/>: a purpose of <c>Comment</c> with a rating's id would
    /// promote the wrong row. Free text with no check constraint, so a misspelling matches no branch
    /// and the token confirms nothing while still being marked used. Always assign from
    /// <see cref="EmailVerificationPurpose"/> and compare case-insensitively.
    /// </remarks>
    public string Purpose { get; set; } = string.Empty;

    /// <summary>
    /// The row awaiting confirmation - a comment id, rating id or subscriber id, depending on
    /// <see cref="Purpose"/> (<c>TargetId BIGINT</c>, nullable).
    /// </summary>
    /// <remarks>
    /// A polymorphic reference with no foreign key: nothing in the schema ties it to a table, and
    /// nothing stops it pointing at a row that has since been deleted. That is deliberate - if a
    /// verification mail cannot be sent the pending comment is deleted, leaving any stray token
    /// harmlessly dangling. Interpret it only together with <see cref="Purpose"/>.
    /// </remarks>
    public long? TargetId { get; set; }

    /// <summary>
    /// The display name supplied with the submission, echoed into the email
    /// (<c>DisplayName VARCHAR(150)</c>).
    /// </summary>
    /// <remarks>
    /// Untrusted visitor input that is inserted into an outbound message, so it must be encoded for
    /// the body it lands in - HTML-encoded for an HTML body - and must never be interpolated into a
    /// header, where a newline would be header injection. Purely cosmetic: it makes the mail read as
    /// addressed to a person and affects nothing about validity.
    /// </remarks>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// When the token was issued (<c>IssuedOn TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP</c>).
    /// </summary>
    /// <remarks>
    /// Audit information; no check keys off it. <see cref="ExpiresOn"/> is stored independently
    /// rather than computed from this at read time, so editing one does not move the other - the
    /// 24-hour gap is a convention of the issuing code, not an invariant.
    /// </remarks>
    public DateTime IssuedOn { get; set; }

    /// <summary>
    /// The instant after which the token is worthless (<c>ExpiresOn TIMESTAMP NOT NULL</c>, indexed).
    /// </summary>
    /// <remarks>
    /// Set 24 hours after issue. Enforcement is in the database, inside the same atomic statement
    /// that redeems the token (<c>ExpiresOn &gt; CURRENT_TIMESTAMP</c>), so an expired link simply
    /// returns no row - a caller cannot skip the check by testing this property itself. Note the time
    /// base caveat on the type.
    /// <para>The index exists so expired rows can be swept cheaply; nothing deletes them
    /// automatically, so the table grows until something does.</para>
    /// </remarks>
    public DateTime ExpiresOn { get; set; }

    /// <summary>
    /// When the token was redeemed; null while unused (<c>ConsumedOn TIMESTAMP</c>).
    /// </summary>
    /// <remarks>
    /// Stamped by the database with its own <c>CURRENT_TIMESTAMP</c> during redemption, not by the
    /// application - which is why the repository's update statement deliberately leaves this column
    /// out. Audit information only; <see cref="IsUsed"/> is what makes the token spent.
    /// </remarks>
    public DateTime? ConsumedOn { get; set; }

    /// <summary>
    /// Whether the token has already been redeemed
    /// (<c>IsUsed BOOLEAN NOT NULL DEFAULT FALSE</c>).
    /// </summary>
    /// <remarks>
    /// The single-use gate. It is flipped by the same data-modifying statement that reads the row, so
    /// two concurrent clicks on the same link cannot both succeed - one redeems it and the other
    /// finds nothing. Never test this property and then act on the result: a read-then-write pair
    /// reopens exactly the race the atomic statement closes, which is why a plain lookup exists only
    /// to tell a visitor "this link has already been used".
    /// </remarks>
    public bool IsUsed { get; set; }

    /// <summary>
    /// The IP address the original submission came from, for abuse forensics
    /// (<c>RequestIpAddress VARCHAR(45)</c>).
    /// </summary>
    /// <remarks>
    /// Recorded at issue time - it is where the <i>submission</i> came from, not where the link was
    /// later clicked, so it will not tell you who redeemed the token. Empty on any instance returned
    /// by the consume path, whose projection omits the column; see the remarks on the type.
    /// <para><b>Exposure:</b> personal data; admin surfaces only.</para>
    /// </remarks>
    public string RequestIpAddress { get; set; } = string.Empty;
}
