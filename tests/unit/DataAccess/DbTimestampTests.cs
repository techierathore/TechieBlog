using BlogEngine.DaCore;
using Xunit;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Unit tests for <see cref="DbTimestamp"/>, the guard against the <c>42883</c> overload trap (REQ-NFR-026).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Npgsql chooses between <c>timestamp</c> and <c>timestamptz</c> from a
/// <see cref="DateTime"/>'s <see cref="DateTimeKind"/>, and PostgreSQL resolves stored-function
/// overloads strictly, so a value with <c>Kind = Utc</c> silently fails to match a function that
/// declares <c>TIMESTAMP</c>. The failure is a runtime one — it compiles, and a green build says
/// nothing about it — which is why the normalisation has tests of its own.</para>
///
/// <para><b>Dependencies:</b> xUnit only.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite.</para>
/// </remarks>
public class DbTimestampTests
{
    /// <summary>
    /// A UTC timestamp keeps its instant but loses the Kind that would make Npgsql send it as
    /// timestamptz — this is the whole fix, and the instant must not move while it happens.
    /// </summary>
    [Fact]
    public void UtcValueKeepsInstantAndLosesKind()
    {
        var utcValue = new DateTime(2026, 8, 7, 13, 45, 30, DateTimeKind.Utc);

        var normalised = DbTimestamp.AsTimestamp(utcValue);

        Assert.Equal(DateTimeKind.Unspecified, normalised.Kind);
        Assert.Equal(utcValue.Ticks, normalised.Ticks);
    }

    /// <summary>
    /// A value that is already Unspecified passes through untouched, so applying the helper twice —
    /// or to a value read back from the database — is harmless.
    /// </summary>
    [Fact]
    public void UnspecifiedValuePassesThroughUnchanged()
    {
        var unspecified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);

        var normalised = DbTimestamp.AsTimestamp(unspecified);

        Assert.Equal(DateTimeKind.Unspecified, normalised.Kind);
        Assert.Equal(unspecified.Ticks, normalised.Ticks);
    }

    /// <summary>
    /// A local timestamp is converted to UTC before its Kind is dropped. Simply stripping the Kind
    /// would record the machine's wall-clock reading as though it were UTC, so every row written on
    /// a non-UTC host would be silently offset by that host's time-zone rules.
    /// </summary>
    [Fact]
    public void LocalValueIsConvertedToUtcFirst()
    {
        var localValue = new DateTime(2026, 8, 7, 13, 45, 30, DateTimeKind.Local);

        var normalised = DbTimestamp.AsTimestamp(localValue);

        Assert.Equal(DateTimeKind.Unspecified, normalised.Kind);
        Assert.Equal(localValue.ToUniversalTime().Ticks, normalised.Ticks);
    }

    /// <summary>
    /// A null timestamp stays null so an optional column is written as SQL NULL rather than as the
    /// zero date.
    /// </summary>
    [Fact]
    public void NullValueStaysNull()
    {
        Assert.Null(DbTimestamp.AsTimestamp((DateTime?)null));
    }

    /// <summary>
    /// The nullable overload applies the same normalisation as the non-nullable one when a value is
    /// present.
    /// </summary>
    [Fact]
    public void NullableOverloadNormalisesPresentValue()
    {
        DateTime? utcValue = new DateTime(2026, 8, 7, 13, 45, 30, DateTimeKind.Utc);

        var normalised = DbTimestamp.AsTimestamp(utcValue);

        Assert.NotNull(normalised);
        Assert.Equal(DateTimeKind.Unspecified, normalised!.Value.Kind);
    }
}
