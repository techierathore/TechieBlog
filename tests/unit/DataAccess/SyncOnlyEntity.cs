namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Minimal entity used by the async data-access contract tests (REQ-NFR-026).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>IGenericRepository&lt;TEntity&gt;</c> constrains its type argument to a
/// reference type and nothing more, so the contract tests need an entity with no schema, no table
/// and no behaviour — anything richer would let a test pass or fail for reasons unrelated to the
/// contract under test.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Test-only; never registered or persisted.</para>
/// </remarks>
public class SyncOnlyEntity
{
    /// <summary>Identifier the fakes match on.</summary>
    public long EntityId { get; set; }

    /// <summary>Value the assertions compare, so a wrong row is visibly wrong.</summary>
    public string Name { get; set; } = string.Empty;
}
