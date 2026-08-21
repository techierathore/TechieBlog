using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for application users and their profile and resume fields.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against the <c>BlogUser</c> table, including the
/// resume/portfolio columns that drive the public home page.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Identify — <see cref="GetUserByEmailAsync"/>, <see cref="GetUserByMobileAsync"/> and
///         <see cref="GetByUsernameAsync"/> resolve an account from each of its three natural keys;
///         <c>AuthSvc</c> uses the first for sign-in and password reset.</item>
///   <item>Claim a handle — <see cref="IsUsernameAvailableAsync"/> then
///         <see cref="UpdateUsernameAsync"/>.</item>
///   <item>Publish — <see cref="GetSiteOwnerAsync"/> resolves the single account whose resume the home
///         page renders; <see cref="SetSiteOwnerAsync"/> moves that designation;
///         <see cref="UpdateResumeFieldsAsync"/> writes only the resume columns.</item>
///   <item>List — <see cref="GetAllAuthorsAsync"/> backs the authors page.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogUserRepo</c> over Dapper.</para>
///
/// <para><b>Usage:</b> Injected into <c>AuthSvc</c> and the profile pages. <b>Credential hashes are
/// read through <c>IUserCredentialRepo</c> instead</b>, so the projections this contract returns never
/// carry a password — a caller must not expect one, and must not add one to a member here.
/// <see cref="UpdateResumeFieldsAsync"/> exists for the same reason in reverse: it touches the resume
/// columns only, so saving a profile cannot silently overwrite credential or role fields the caller
/// never loaded. The <c>bool</c>-returning write members report whether a row was affected, so
/// <c>false</c> means "no such user" rather than "failed" — a genuine failure is thrown.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> All ten <c>…Async</c> members carry default implementations
/// that call their synchronous twin and wrap the result with <c>Task.FromResult</c>. <b>An inherited
/// default is not asynchronous and does not observe the token at all</b> — it runs inline, parks the
/// calling thread for the whole round trip, and throws synchronously rather than returning a faulted
/// task. <c>BlogUserRepo</c> overrides all ten with genuine async Dapper and does honour the token; any
/// other implementer still inheriting the defaults is unconverted, however green the build is. The
/// member-level <c>Flow:</c> notes describe that override, not the default.</para>
/// </remarks>
public interface IBlogUserRepo : IGenericRepository<AppUser>
{
    /// <summary>
    /// Looks a user up by email and password in a single query.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Legacy sign-in path retained for compatibility; current
    /// authentication verifies a PBKDF2 hash through <c>IUserCredentialRepo</c> instead.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The account's email address.</param>
    /// <param name="password">The password to match.</param>
    /// <returns>The matching user, or <c>null</c> when the pair does not match an account.</returns>
    AppUser? GetLoginUser(string loginEmail, string password);

    /// <summary>
    /// Looks a user up by email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address is the account's natural key, so this is the lookup
    /// behind password reset and duplicate-account checks.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The address to search for.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that address.</returns>
    AppUser? GetUserByEmail(string loginEmail);

    /// <summary>
    /// Looks a user up by mobile number.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="mobileNo">The mobile number to search for.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that number.</returns>
    AppUser? GetUserByMobile(string mobileNo);

    /// <summary>
    /// Retrieves a user by their username (case-insensitive).
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <returns>AppUser if found, null otherwise.</returns>
    AppUser? GetByUsername(string username);

    /// <summary>
    /// Retrieves the site owner (user with IsSiteOwner=true).
    /// </summary>
    /// <returns>AppUser if found, null otherwise.</returns>
    AppUser? GetSiteOwner();

    /// <summary>
    /// Retrieves all users who have written at least one blog post.
    /// </summary>
    /// <returns>Collection of authors.</returns>
    IEnumerable<AppUser> GetAllAuthors();

    /// <summary>
    /// Updates a user's username.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool UpdateUsername(long userId, string username);

    /// <summary>
    /// Sets a user as the site owner, removing the flag from any previous owner.
    /// </summary>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool SetSiteOwner(long userId);

    /// <summary>
    /// Checks if a username is available (not already taken).
    /// </summary>
    /// <param name="username">The username to check.</param>
    /// <returns>True if available, false if taken.</returns>
    bool IsUsernameAvailable(string username);

    /// <summary>
    /// Updates only the resume-related fields for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool UpdateResumeFields(long userId, AppUser resumeData);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // Each member carries a default implementation that runs its synchronous twin, so adding the
    // async surface cannot break an implementer that has not been converted yet. The default is
    // correct but is not the fix: it still blocks the calling thread. BlogUserRepo overrides all of
    // them with genuine async Dapper.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Looks a user up by email and password in a single query, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Legacy sign-in path retained for compatibility; current
    /// authentication verifies a PBKDF2 hash through <c>IUserCredentialRepo</c> instead.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The account's email address.</param>
    /// <param name="password">The password to match.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when the pair does not match an account.</returns>
    Task<AppUser?> GetLoginUserAsync(string loginEmail, string password, CancellationToken cancellationToken = default)
        => Task.FromResult(GetLoginUser(loginEmail, password));

    /// <summary>
    /// Looks a user up by email address, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address is the account's natural key, so this is the lookup
    /// behind password reset and duplicate-account checks.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginEmail">The address to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that address.</returns>
    Task<AppUser?> GetUserByEmailAsync(string loginEmail, CancellationToken cancellationToken = default)
        => Task.FromResult(GetUserByEmail(loginEmail));

    /// <summary>
    /// Looks a user up by mobile number, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="mobileNo">The mobile number to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when no account uses that number.</returns>
    Task<AppUser?> GetUserByMobileAsync(string mobileNo, CancellationToken cancellationToken = default)
        => Task.FromResult(GetUserByMobile(mobileNo));

    /// <summary>
    /// Retrieves a user by their username (case-insensitive), without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="username">The username to search for.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching user, or <c>null</c> when the username is unknown.</returns>
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUsername(username));

    /// <summary>
    /// Retrieves the site owner, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Exactly one account carries <c>IsSiteOwner</c>; the landing page
    /// and the résumé page both render from it, so "no owner configured" must read as <c>null</c>
    /// rather than throw.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The site owner, or <c>null</c> when none is flagged.</returns>
    Task<AppUser?> GetSiteOwnerAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetSiteOwner());

    /// <summary>
    /// Retrieves all users who have written at least one published post, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The authors, or an empty sequence when nobody has published.</returns>
    Task<IEnumerable<AppUser>> GetAllAuthorsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(GetAllAuthors());

    /// <summary>
    /// Updates a user's username, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was updated.</returns>
    Task<bool> UpdateUsernameAsync(long userId, string username, CancellationToken cancellationToken = default)
        => Task.FromResult(UpdateUsername(userId, username));

    /// <summary>
    /// Sets a user as the site owner, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Clearing the previous owner and flagging the new one happen in a
    /// single transaction, so the site can never be left with two owners or none.</para>
    /// <para><b>Side Effects:</b> Updates up to two <c>BlogUser</c> rows.</para>
    /// </remarks>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when the new owner was flagged.</returns>
    Task<bool> SetSiteOwnerAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(SetSiteOwner(userId));

    /// <summary>
    /// Checks whether a username is still free, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="username">The username to check.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when no account already uses the name.</returns>
    Task<bool> IsUsernameAvailableAsync(string username, CancellationToken cancellationToken = default)
        => Task.FromResult(IsUsernameAvailable(username));

    /// <summary>
    /// Updates only the resume-related fields for a user, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was updated.</returns>
    Task<bool> UpdateResumeFieldsAsync(long userId, AppUser resumeData, CancellationToken cancellationToken = default)
        => Task.FromResult(UpdateResumeFields(userId, resumeData));
}
