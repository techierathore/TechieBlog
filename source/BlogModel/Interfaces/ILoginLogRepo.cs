using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for the sign-in audit log.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists sign-in attempts for audit and abuse investigation. Beyond the
/// generic CRUD surface it declares one lookup, keyed on the attempted address, because that is the
/// key a brute-force investigation actually reads by.</para>
/// <para><b>Code Flow:</b> <c>AuthSvc</c> writes one row per sign-in attempt through the inherited
/// <c>InsertAsync</c>, successful or not; an investigation reads them back with
/// <see cref="GetRecentByAttemptedEmailAsync"/>.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.LoginLogRepo</c>.</para>
///
/// <para><b>Usage:</b> Append-only in practice — nothing in the application updates or deletes an
/// audit row, and callers should not start. Writing the row must never be allowed to fail a sign-in
/// that otherwise succeeded, so the caller guards the insert rather than letting an audit failure
/// surface to the user. This contract is an evidence trail, not a control: it does not throttle
/// anything, which is <c>ILoginThrottle</c>'s job.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> the CRUD surface is inherited from
/// <see cref="IGenericRepository{TEntity}"/>, whose <c>…Async</c> members <c>LoginLogRepo</c>
/// overrides with genuine async Dapper.</para>
///
/// <para><b>REQ-FN-051 — repaired and now exercised:</b> the implementation's INSERT used to
/// hard-code <c>success = true</c> and <c>attemptedemail = ''</c>, so the audit trail could not
/// record a failed sign-in and nothing in the application resolved this interface anyway. Both
/// halves are fixed: <see cref="LoginLog"/> carries the outcome columns, the statements bind them,
/// and <c>BlogEngine.Services.AuthSvc</c> resolves this repository and writes one row per sign-in
/// attempt, successful or not.</para>
/// </remarks>
public interface ILoginLogRepo : IGenericRepository<LoginLog>
{
    /// <summary>
    /// Gets the most recent sign-in attempts made against one address, newest first.
    /// </summary>
    /// <remarks>
    /// The address is the only key that spans a brute-force run, because an attempt against an
    /// address that matches no account carries no user id at all.
    /// </remarks>
    /// <param name="attemptedEmail">The address that was typed into the sign-in form.</param>
    /// <param name="maxRows">Upper bound on the rows returned.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching attempts, newest first, or an empty sequence when there are none.</returns>
    Task<IEnumerable<LoginLog>> GetRecentByAttemptedEmailAsync(
        string attemptedEmail, int maxRows = 50, CancellationToken cancellationToken = default);
}
