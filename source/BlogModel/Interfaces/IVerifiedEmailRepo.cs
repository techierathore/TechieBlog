using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for the registry of confirmed email addresses.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a returning visitor skip the double opt-in step once their
/// address has been confirmed at least once. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> <c>EmailVerificationSvc</c> calls <see cref="IsVerifiedAsync"/>
/// before queuing a submission and <see cref="RecordVerifiedAsync"/> after a token is consumed.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>VerifiedEmailRepo</c> over the
/// <c>VerifiedEmail</c> table (migration 014).</para>
///
/// <para><b>Usage:</b> All matching is case-insensitive - addresses are compared on
/// <c>LOWER(Email)</c>, which is also how the unique index is built.</para>
/// </remarks>
public interface IVerifiedEmailRepo : IGenericRepository<VerifiedEmail>
{
    /// <summary>
    /// Reads the registry entry for an address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Case-insensitive lookup.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="email">The address to look up.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The registry row, or null when the address has never been confirmed.</returns>
    Task<VerifiedEmail?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests whether an address may submit without re-confirming.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> True only when a registry row exists AND it is not blocked -
    /// an administrator ban revokes the shortcut without deleting the history.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="email">The address to test.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>True when the address is confirmed and not blocked.</returns>
    Task<bool> IsVerifiedAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or refreshes the registry entry for a freshly confirmed address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>RecordVerifiedEmail</c> stored function,
    /// which upserts on <c>LOWER(Email)</c> and refreshes the last-used stamp.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one row in <c>VerifiedEmail</c>.</para>
    /// </remarks>
    /// <param name="email">The confirmed address.</param>
    /// <param name="displayName">The most recent display name seen for it; may be null.</param>
    /// <param name="cancellationToken">Cancels the upsert.</param>
    /// <returns>The primary key of the registry row.</returns>
    Task<long> RecordVerifiedAsync(string email, string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bans or un-bans an address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blocked address is treated as unverified everywhere,
    /// so its future submissions are stopped at the door.</para>
    /// <para><b>Side Effects:</b> Updates one row; no-op when the address is unknown.</para>
    /// </remarks>
    /// <param name="email">The address to change.</param>
    /// <param name="isBlocked">True to ban, false to lift the ban.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the update has been applied.</returns>
    Task SetBlockedAsync(string email, bool isBlocked, CancellationToken cancellationToken = default);
}
