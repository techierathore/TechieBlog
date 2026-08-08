namespace BlogEngine.DaCore;

/// <summary>
/// Normalises <see cref="DateTime"/> values so Npgsql sends them as PostgreSQL <c>TIMESTAMP</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Every timestamp column in this schema is <c>TIMESTAMP</c> (without time
/// zone), and every stored function that takes one declares it the same way. Npgsql, however, picks
/// the wire type from the value's <see cref="DateTimeKind"/>: a <c>DateTime</c> whose Kind is
/// <see cref="DateTimeKind.Utc"/> — which is exactly what <c>DateTime.UtcNow</c> produces — is sent
/// as <c>timestamptz</c>. PostgreSQL resolves function overloads strictly, so the call then matches
/// no function at all and fails with SQLSTATE <c>42883</c>, "function … does not exist".</para>
///
/// <para><b>Code Flow:</b> repository binds a parameter → wraps the value in
/// <see cref="AsTimestamp"/> → Npgsql infers <c>timestamp</c> → the declared function signature
/// matches.</para>
///
/// <para><b>Dependencies:</b> none — this is a pure value transformation.</para>
///
/// <para><b>Usage:</b> apply to every <c>DateTime</c> passed to a stored function or to a
/// <c>TIMESTAMP</c> column. Setting <c>DbType</c> instead does <i>not</i> work: since Npgsql 6,
/// <c>DbType.DateTime</c> itself maps to <c>timestamptz</c>, and asking for <c>timestamp</c> while
/// the value still carries <c>Kind = Utc</c> is rejected outright. Changing the Kind is what changes
/// the wire type. Plain parameterised SQL happens to survive without this because PostgreSQL casts
/// the argument to the target column type, which is why the failure only ever shows up on the
/// stored-function paths (see <c>PasswordResetTokenRepo.InsertToGetId</c>, REQ-NFR-026).</para>
/// </remarks>
public static class DbTimestamp
{
    /// <summary>
    /// Returns the same instant with its <see cref="DateTimeKind"/> dropped to
    /// <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the Kind label changes; the numeric instant is untouched, so
    /// a UTC value stays UTC and keeps matching every other timestamp in the database. Values that
    /// are already <see cref="DateTimeKind.Unspecified"/> pass through unchanged. <b>A
    /// <see cref="DateTimeKind.Local"/> value is converted to UTC first</b>, because dropping the Kind
    /// from a local time would silently record the wall-clock reading of the server's time zone as
    /// though it were UTC.</para>
    ///
    /// <para><b>Flow:</b> inspect Kind → convert local to UTC → re-stamp as Unspecified.</para>
    ///
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The timestamp to bind as a PostgreSQL <c>TIMESTAMP</c>.</param>
    /// <returns>The same instant, carrying <see cref="DateTimeKind.Unspecified"/>.</returns>
    public static DateTime AsTimestamp(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return DateTime.SpecifyKind(utcValue, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Nullable overload of <see cref="AsTimestamp(DateTime)"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A <c>null</c> timestamp stays <c>null</c> so an optional column is
    /// still written as SQL <c>NULL</c> rather than as the zero date.</para>
    /// <para><b>Flow:</b> null check → delegate.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The timestamp to bind, or <c>null</c>.</param>
    /// <returns>The normalised instant, or <c>null</c>.</returns>
    public static DateTime? AsTimestamp(DateTime? value)
    {
        return value.HasValue ? AsTimestamp(value.Value) : null;
    }
}
