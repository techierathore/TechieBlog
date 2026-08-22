namespace BlogModels.Models;

/// <summary>
/// The primary application user record — an author, editor, contributor or administrator.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries one row of the <c>BlogUser</c> table across every layer of the
/// application: authentication, profile editing, the public resume/portfolio surface and post
/// authorship attribution. It is both the persistence shape used by Dapper and the model bound by
/// the UI, so property names must match the database column names case-insensitively.</para>
///
/// <para><b>Code Flow:</b> Materialised by <c>BlogUserRepo</c> from PostgreSQL via Dapper, cached in
/// the authentication cookie's claims by <c>AuthSvc</c>, and projected onto view models by the
/// profile and resume components.</para>
///
/// <para><b>Dependencies:</b> Column parity with the <c>BlogUser</c> table defined in
/// <c>PostgresScripts/001-CreateTables.sql</c> and extended by <c>012-ResumeAndImageManagement.sql</c>
/// (username, site-owner and résumé columns) and <c>017-SecurityAndTokenPersistence.sql</c>
/// (<c>MustChangePassword</c>).</para>
///
/// <para><b>Usage:</b> Treat as a data carrier only — it holds no behaviour beyond the computed
/// <see cref="FullName"/>. Business rules belong in <c>BlogEngine.Services</c>.</para>
///
/// <para><b>Trap — an <c>AppUser</c> is only ever as complete as the query that made it.</b> The
/// repository reads users through three different shapes and they do not project the same columns:</para>
/// <list type="bullet">
///   <item><c>SELECT * FROM BlogUser</c> (get-all, paged, by-username, site-owner, authors) fills
///   every mapped property.</item>
///   <item><c>SelectBlogUserById(pUserId)</c> — the function behind <c>GetSingle</c>, and therefore
///   behind every profile load that starts from a token — returns a <b>fixed 17-column subset</b>.
///   <see cref="Username"/>, <see cref="IsSiteOwner"/>, <see cref="Title"/>, <see cref="Tagline"/>,
///   <see cref="InstagramUrl"/>, <see cref="PhoneNumber"/>, <see cref="Location"/>,
///   <see cref="CVFilePath"/> and <see cref="ResumeEnabled"/> are <b>not</b> in it, so they come back
///   as <c>null</c>/<c>false</c> no matter what the row holds. Persisting such an instance wholesale
///   would blank the real values.</item>
///   <item><c>GetLoginUser</c> returns only the eight credential columns.</item>
/// </list>
/// <para>This is not hypothetical: <c>021-LoginAuditAndForcedChange.sql</c> exists precisely because
/// <see cref="MustChangePassword"/> was missing from that projection, so a flagged user escaped the
/// forced-change screen by pressing F5 (REQ-NFR-023). Before trusting a property, check which query
/// produced the instance.</para>
///
/// <para><b>Security:</b> This type carries a password hash and, transiently, JWT material. It is a
/// persistence/service model, <b>not</b> a view model — never serialise a whole <c>AppUser</c> to the
/// browser, to an API response or into a log line. Public author surfaces must project the display
/// fields they need and leave <see cref="LoginPass"/>, <see cref="EmailId"/>,
/// <see cref="PhoneNumber"/>, <see cref="AccessToken"/> and <see cref="RefreshToken"/> behind.</para>
/// </remarks>
public class AppUser
{
    /// <summary>
    /// Surrogate primary key (<c>BlogUser.UserId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Zero on an instance that has not been inserted yet — the database assigns the value, and
    /// <c>InsertBlogUser</c> returns it. Every foreign key that names an author, series owner,
    /// uploader or event subject points here.
    /// </remarks>
    public long UserId { get; set; }

    /// <summary>
    /// Given name (<c>FirstName VARCHAR(100) NOT NULL</c>). User-supplied, therefore untrusted:
    /// encode it before rendering.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Family name (<c>LastName VARCHAR(100) NOT NULL</c>). User-supplied, therefore untrusted:
    /// encode it before rendering.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// The user's display name, composed from <see cref="FirstName"/> and <see cref="LastName"/>.
    /// </summary>
    /// <remarks>
    /// Computed and never persisted; there is no <c>FullName</c> column and Dapper ignores it because
    /// it has no setter. Always returns a non-null string because both source properties default to
    /// empty — but for a user with neither name it is a single space, not an empty string, so test it
    /// with <see cref="string.IsNullOrWhiteSpace(string)"/> rather than
    /// <see cref="string.IsNullOrEmpty(string)"/> before rendering a byline.
    /// </remarks>
    public string FullName
    {
        get
        {
            return FirstName + " " + LastName;
        }
    }

    /// <summary>
    /// Email address, which doubles as the login identifier
    /// (<c>EmailId VARCHAR(255) NOT NULL UNIQUE</c>).
    /// </summary>
    /// <remarks>
    /// Unique across the table, both by column constraint and by the <c>IdxBlogUserEmail</c> index,
    /// so an insert with a duplicate address fails at the database rather than silently creating a
    /// second account. Lookups are case-insensitive since
    /// <c>020-CaseInsensitiveEmailLookup.sql</c>, so <c>A@b.com</c> and <c>a@b.com</c> resolve to the
    /// same user — do not compare this value with <see cref="string.Equals(string, string)"/> and
    /// expect database semantics.
    /// <para><b>Exposure:</b> personal data. It may be rendered to the owning user and to
    /// administrators; it must never appear on a public author, post or comment surface.</para>
    /// </remarks>
    public string EmailId { get; set; } = string.Empty;

    /// <summary>
    /// The password verifier (<c>LoginPass VARCHAR(255) NOT NULL</c>) — never a plaintext password.
    /// </summary>
    /// <remarks>
    /// Written and compared only by <c>AuthSvc</c>; no other layer has any reason to read it.
    /// <para><b>Exposure:</b> must never leave the server — not to the browser, not into a log, not
    /// into an exception message. Because <c>SELECT *</c> populates it, an <c>AppUser</c> handed
    /// straight to a component or an API response leaks it by default; project a view model
    /// instead.</para>
    /// </remarks>
    public string LoginPass { get; set; } = string.Empty;

    /// <summary>
    /// When the account was created (<c>CreatedOn</c>, defaulted to <c>CURRENT_TIMESTAMP</c>).
    /// </summary>
    /// <remarks>
    /// System-generated and never edited through the profile screen. <c>InsertBlogUser</c> does not
    /// pass a value, so the row takes the column default — the <i>database server's</i>
    /// <c>CURRENT_TIMESTAMP</c>, which is that server's wall clock and not guaranteed to be UTC. The
    /// column is a bare <c>TIMESTAMP</c> with no time zone, so the value materialises with
    /// <see cref="DateTimeKind.Unspecified"/>: comparing it against <see cref="DateTime.UtcNow"/>
    /// works only to the extent the database runs on UTC, and calling <c>ToLocalTime</c> on it shifts
    /// an already-local reading. It also drives the "new users this month" tile via
    /// <c>AdminCounts.NewUsersThisMonth</c>.
    /// </remarks>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Timestamp of the most recent modification (<c>UpdatedOn TIMESTAMP</c>, nullable in the
    /// database).
    /// </summary>
    /// <remarks>
    /// Server-local, like <see cref="CreatedOn"/>. The column allows <c>NULL</c> for a never-updated
    /// account while this property is non-nullable, so an untouched row materialises as
    /// <see cref="DateTime.MinValue"/> — treat a year of 0001 as "never updated" rather than
    /// rendering it.
    /// </remarks>
    public DateTime UpdatedOn { get; set; }

    /// <summary>
    /// Authorisation role name (<c>UserRole VARCHAR(51) NOT NULL</c>) — the single input to every
    /// role check on the site.
    /// </summary>
    /// <remarks>
    /// Free text carrying one of the constants in <c>AppRoles</c> (Admin, Blogger, Subscriber).
    /// There is no lookup table and no check constraint, so a typo here is not a compile error and
    /// not a database error — it is a silent authorisation failure in which the user simply matches
    /// no policy. Always assign from <c>AppRoles</c>, never from a literal.
    /// </remarks>
    public string UserRole { get; set; } = string.Empty;

    /// <summary>
    /// Whether the account's email address has been confirmed
    /// (<c>IsConfirmed BOOLEAN DEFAULT FALSE</c>).
    /// </summary>
    /// <remarks>
    /// This is the <i>account</i> confirmation flag. It is unrelated to the anonymous double opt-in
    /// flow that governs comments, ratings and newsletter sign-ups — that state lives on
    /// <see cref="EmailVerificationToken"/> and <c>BlogComment.IsEmailVerified</c>.
    /// </remarks>
    public bool IsConfirmed { get; set; }

    /// <summary>
    /// Whether the account has been soft-deleted by an administrator
    /// (<c>IsDeleted BOOLEAN NOT NULL DEFAULT FALSE</c>, migration 030).
    /// </summary>
    /// <remarks>
    /// <para>Deleting a user is a flag, not a <c>DELETE</c>. <c>BlogUser</c> is the target of sixteen
    /// foreign keys and only four of them cascade, so a hard delete would be refused for any account
    /// that has ever written a post or left a comment — the very account an administrator wants to
    /// remove — while succeeding for a brand-new one and silently taking its ratings with it. The flag
    /// keeps referential integrity intact and keeps authored posts attributed to their author.</para>
    /// <para><b>A deleted row is also deactivated.</b> <c>SoftDeleteBlogUser</c> sets
    /// <see cref="IsConfirmed"/> to false in the same statement, so the single confirmation check on
    /// the sign-in path refuses deleted and deactivated accounts alike. Do not write a sign-in guard
    /// against this property expecting it to be the only one that matters.</para>
    /// </remarks>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Site-relative path to the profile photograph (<c>ProfileImagePath VARCHAR(255)</c>).
    /// </summary>
    /// <remarks>
    /// Holds the storage locator produced by <c>IFileStorage</c>, not an uploaded filename. Nullable
    /// in the database but non-nullable here, so "no photo" arrives as an empty string — fall back to
    /// an initials avatar rather than emitting <c>src=""</c>, which makes the browser re-request the
    /// current page.
    /// </remarks>
    public string ProfileImagePath { get; set; } = string.Empty;

    /// <summary>
    /// Biography shown on the profile and resume surfaces (<c>ProfileDescription TEXT</c>).
    /// </summary>
    /// <remarks>
    /// User-supplied free text of unbounded length, published verbatim on a public page — it must be
    /// HTML-encoded (or Markdown-rendered through <c>MarkdownRenderer</c>, which sanitises) on the way
    /// out. Never interpolate it into markup.
    /// </remarks>
    public string ProfileDescription { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the user's X (formerly Twitter) profile (<c>TwitterUrl VARCHAR(255)</c>).
    /// </summary>
    /// <remarks>
    /// This property was previously misspelled <c>TwiiterUrl</c>. Dapper maps by name, and a name
    /// that matches no column is not an error — it is silently skipped — so the property stayed empty
    /// however much data the column held, and the X/Twitter icon was permanently hidden on every
    /// profile (REQ-FN-029). The lesson generalises: on this type a rename is a data-loss bug that
    /// the compiler cannot catch.
    /// <para>Rendered as an outbound link, so validate the scheme before emitting it — a stored
    /// <c>javascript:</c> URL is script injection by another route.</para>
    /// </remarks>
    public string TwitterUrl { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the user's LinkedIn profile (<c>LinkedInUrl VARCHAR(255)</c>). Same
    /// scheme-validation caveat as <see cref="TwitterUrl"/>.
    /// </summary>
    public string LinkedInUrl { get; set; } = string.Empty;

    /// <summary>
    /// Absolute URL of the user's GitHub profile (<c>GitHubUrl VARCHAR(255)</c>). Same
    /// scheme-validation caveat as <see cref="TwitterUrl"/>.
    /// </summary>
    public string GitHubUrl { get; set; } = string.Empty;

    /// <summary>
    /// Podcasting blurb shown on the resume page (<c>PodDescription VARCHAR(1050)</c>).
    /// </summary>
    /// <remarks>
    /// User-supplied and published publicly; encode on render. The 1050-character column limit is not
    /// enforced anywhere in C#, so an over-long value fails at the database with a truncation error
    /// rather than a validation message.
    /// </remarks>
    public string PodDescription { get; set; } = string.Empty;

    /// <summary>
    /// Public-speaking blurb shown on the resume page (<c>SpeakDescription VARCHAR(1050)</c>). Same
    /// caveats as <see cref="PodDescription"/>.
    /// </summary>
    public string SpeakDescription { get; set; } = string.Empty;

    /// <summary>
    /// The JWT issued to this user at login. <b>Transient — there is no such column.</b>
    /// </summary>
    /// <remarks>
    /// <c>BlogUser</c> has no <c>AccessToken</c> column; the persisted session tokens live on the
    /// <c>UserLogin</c> table. This property is a purely in-memory courier that
    /// <c>AuthSvc.AppLogin</c> stamps onto the freshly loaded user so the caller can hand the token to
    /// the browser's storage. It therefore reads as an empty string on <i>every</i> user loaded by any
    /// other path, and assigning to it persists nothing.
    /// <para><b>Exposure:</b> a bearer credential. It legitimately reaches the client that just
    /// authenticated and nowhere else — never log it, never include it in a user list, never render
    /// it on a profile page.</para>
    /// </remarks>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The refresh token issued at login. <b>Transient — there is no such column</b>; see
    /// <see cref="AccessToken"/>.
    /// </summary>
    /// <remarks>
    /// Same in-memory-only lifetime and the same bearer-credential exposure rules as
    /// <see cref="AccessToken"/>.
    /// </remarks>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// URL-safe handle used in author-facing routes such as <c>/author/{username}</c>
    /// (<c>Username VARCHAR(50)</c>, added by migration 012; REQ-FN-029).
    /// </summary>
    /// <remarks>
    /// Unique where present, enforced by the partial index <c>IdxBlogUserUsername</c>, which allows
    /// any number of <c>NULL</c>s — so uniqueness is guaranteed only for users who actually have one.
    /// Lookups are case-insensitive (<c>LOWER(Username) = LOWER(@Username)</c>), so uniqueness in the
    /// index and uniqueness as a route key are not quite the same thing.
    /// <para><b>Route-bearing:</b> changing a username silently breaks every existing link to that
    /// author. Migration 013 back-filled the column from the email local part, so an inherited value
    /// may be a recognisable fragment of the user's address.</para>
    /// <para>Null on any instance loaded through <c>SelectBlogUserById</c> — see the trap on the
    /// type.</para>
    /// </remarks>
    public string? Username { get; set; }

    /// <summary>
    /// Marks the single site owner whose resume drives the home page
    /// (<c>IsSiteOwner BOOLEAN DEFAULT FALSE</c>, migration 012).
    /// </summary>
    /// <remarks>
    /// At most one row may carry <c>true</c>: the partial unique index <c>IdxSingleSiteOwner</c>
    /// enforces it in the database, which is why <c>BlogUserRepo</c> clears the existing owner before
    /// setting a new one rather than doing both in one statement. It is a content role, not a
    /// security role — authorisation comes from <see cref="UserRole"/> alone, and setting this flag
    /// grants nothing.
    /// <para>False on any instance loaded through <c>SelectBlogUserById</c> — see the trap on the
    /// type.</para>
    /// </remarks>
    public bool IsSiteOwner { get; set; }

    /// <summary>
    /// Professional title shown in the resume hero, e.g. "Principal Engineer"
    /// (<c>Title VARCHAR(150)</c>, migration 012).
    /// </summary>
    /// <remarks>
    /// Null when never filled in, and also null on any instance loaded through
    /// <c>SelectBlogUserById</c> — the two are indistinguishable, so do not treat null as "the user
    /// cleared it".
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>
    /// Short tagline shown beneath <see cref="Title"/> in the resume hero
    /// (<c>Tagline VARCHAR(500)</c>, migration 012). Same null caveat as <see cref="Title"/>.
    /// </summary>
    public string? Tagline { get; set; }

    /// <summary>
    /// Absolute URL of the user's Instagram profile (<c>InstagramUrl VARCHAR(255)</c>, migration 012).
    /// Same scheme-validation caveat as <see cref="TwitterUrl"/> and the same null caveat as
    /// <see cref="Title"/>.
    /// </summary>
    public string? InstagramUrl { get; set; }

    /// <summary>
    /// Contact telephone number (<c>PhoneNumber VARCHAR(50)</c>, migration 012).
    /// </summary>
    /// <remarks>
    /// Free text — no format is imposed, so it may hold any national or international notation and
    /// must not be parsed.
    /// <para><b>Exposure:</b> personal data. It is published on the resume contact block, so it
    /// reaches anonymous visitors whenever <see cref="ResumeEnabled"/> is set — which makes that flag,
    /// not this field, the consent gate.</para>
    /// </remarks>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Geographic location shown on the resume contact block (<c>Location VARCHAR(150)</c>,
    /// migration 012). Public when <see cref="ResumeEnabled"/> is set; same null caveat as
    /// <see cref="Title"/>.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Storage path of the downloadable CV document (<c>CVFilePath VARCHAR(550)</c>, migration 012).
    /// </summary>
    /// <remarks>
    /// Produced by <c>IFileStorage</c> like <see cref="ProfileImagePath"/>. The file is served from
    /// the public web root, so it is readable by anyone who knows the path <i>whether or not</i>
    /// <see cref="ResumeEnabled"/> is set — turning the resume off hides the link, not the document.
    /// </remarks>
    public string? CVFilePath { get; set; }

    /// <summary>
    /// Whether this user's public resume page is enabled
    /// (<c>ResumeEnabled BOOLEAN DEFAULT FALSE</c>, migration 012).
    /// </summary>
    /// <remarks>
    /// The consent gate for the whole résumé surface: with it false the resume route must not render,
    /// which keeps <see cref="PhoneNumber"/> and <see cref="Location"/> off the public site. Defaults
    /// to false, so a new user is private until they opt in.
    /// <para>False on any instance loaded through <c>SelectBlogUserById</c> — see the trap on the
    /// type. Never write it back from such an instance.</para>
    /// </remarks>
    public bool ResumeEnabled { get; set; }

    /// <summary>
    /// Forces the user to change their password before doing anything else (REQ-NFR-023;
    /// <c>MustChangePassword</c>, migration 017).
    /// </summary>
    /// <remarks>
    /// Set on the seeded bootstrap administrator and on any admin-created staff account, so a
    /// well-known seeded password cannot survive first use. Cleared by <c>AuthSvc.ChangePassword</c>
    /// and <c>AuthSvc.ResetPassword</c>.
    /// <para>This flag is the reason <c>SelectBlogUserById</c> had to be widened in migration 021: it
    /// was correct for exactly one render after login and then reverted to false on the next profile
    /// load, letting a flagged user leave the change-password screen with a refresh. If a future
    /// migration re-creates that function, this column must stay in its projection.</para>
    /// </remarks>
    public bool MustChangePassword { get; set; }
}

/// <summary>
/// Hard-coded placeholder users for design-time preview of avatar and byline layouts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives a component something to render before it is wired to real data, so
/// an author list or comment thread can be laid out without a database.</para>
///
/// <para><b>Code Flow:</b> Currently unreferenced anywhere in <c>source/</c> or <c>tests/</c> — the
/// mock-ups these backed have since been bound to live queries.</para>
///
/// <para><b>Dependencies:</b> The avatar images under <c>img/avatars/</c> in the RCL's static
/// assets. If those files are pruned, the placeholders point at nothing.</para>
///
/// <para><b>Usage:</b> Never let one of these reach a production render path — they are fictional
/// people, and each is a shared mutable static with a public setter, so anything that assigns to
/// one changes it for the whole process. A deletion candidate.</para>
/// </remarks>
public static class SampleUsers
{
    /// <summary>First placeholder author. Design-time only; see the remarks on the type.</summary>
    public static AppUser Avatar1 { get; set; } = new()
    {
        FirstName = "Daniel",
        LastName = "Mccoy",
        ProfileImagePath = "img/avatars/avatar.jpg",
    };

    /// <summary>Second placeholder author. Design-time only; see the remarks on the type.</summary>
    public static AppUser Avatar2 { get; set; } = new()
    {
        FirstName = "Dale",
        LastName = "Summers",
        ProfileImagePath = "img/avatars/avatar-2.jpg",
    };

    /// <summary>Third placeholder author. Design-time only; see the remarks on the type.</summary>
    public static AppUser Avatar3 { get; set; } = new()
    {
        FirstName = "Mary",
        LastName = "Fletcher",
        ProfileImagePath = "img/avatars/avatar-3.jpg",
    };

    /// <summary>Fourth placeholder author. Design-time only; see the remarks on the type.</summary>
    public static AppUser Avatar4 { get; set; } = new()
    {
        FirstName = "Anne",
        LastName = "Cameron",
        ProfileImagePath = "img/avatars/avatar-4.jpg",
    };
}
