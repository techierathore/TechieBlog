using System.Reflection;
using System.Text.RegularExpressions;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// Reads every SQL statement this solution's repositories declare and parses the parts of them a
/// projection-completeness gate has to reason about.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-016. Projection completeness is this codebase's single most
/// frequently repeated defect class — eight recorded instances at the time of writing. Every one of
/// them has the same shape: one statement names a column, its sibling statement does not, both
/// compile, both run, and the difference surfaces only as a wrong page or as data quietly
/// overwritten with <c>NULL</c>. Ordinary unit tests cannot see it, because the fakes never execute
/// SQL. This class supplies the raw material for gates that CAN see it — the statements themselves,
/// read straight off the production types.</para>
///
/// <para><b>Why reflection rather than reading the .cs files:</b> the statements are
/// <c>private const string</c> fields, so the compiler has already resolved every interpolated
/// fragment (<c>AnalyticsRepo</c> composes several statements from shared column and predicate
/// constants). Reflection therefore sees the FINAL text the database will receive, which is the
/// only text worth asserting on. A text scan of the source would see <c>{EngagementColumns}</c>.</para>
///
/// <para><b>Deliberately coarse:</b> the parsing here is not a SQL grammar. It recognises the
/// projection list, the SET list of an UPDATE, and the presence of named filters. That is enough to
/// catch a dropped column or a dropped <c>Published</c> filter while leaving ordinary edits — new
/// joins, changed ordering, added parameters — completely free.</para>
///
/// <para><b>Dependencies:</b> reflection over the built <c>BlogEngine</c> assembly. No database, no
/// host, no container.</para>
///
/// <para><b>Usage:</b> consumed by <see cref="ProjectionCompletenessTests"/>. Add nothing
/// repository-specific here — the gates stay self-wiring precisely because this class knows only
/// about SQL, not about any particular table.</para>
/// </remarks>
public static class SqlStatementInventory
{
    /// <summary>
    /// Namespace holding the repositories whose statements are gated.
    /// </summary>
    public const string RepositoryNamespace = "BlogEngine.DbAccess";

    /// <summary>
    /// Every concrete repository type in <see cref="RepositoryNamespace"/>, newest additions
    /// included automatically — a repository added tomorrow is gated the day it is written.
    /// </summary>
    /// <remarks>
    /// Nested and compiler-generated types are excluded: every <c>async</c> member produces a state
    /// machine struct in the same namespace, and a repository with 20 async members would otherwise
    /// contribute 20 meaningless theory rows with colliding display names.
    /// </remarks>
    /// <returns>The repository types, ordered by name for stable test output.</returns>
    public static IReadOnlyList<Type> RepositoryTypes()
    {
        return typeof(BlogEngine.DbAccess.BlogPostRepo).Assembly
            .GetTypes()
            .Where(candidate => candidate.Namespace == RepositoryNamespace)
            .Where(candidate => candidate.IsClass && !candidate.IsAbstract && !candidate.IsNested)
            .Where(candidate => !candidate.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .Where(candidate => candidate.Name.EndsWith("Repo", StringComparison.Ordinal))
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every constant SQL statement a repository declares, keyed by field name.
    /// </summary>
    /// <param name="repositoryType">The repository to read.</param>
    /// <returns>Field name to statement text, ordered by field name.</returns>
    public static IReadOnlyDictionary<string, string> Statements(Type repositoryType)
    {
        var statements = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var fields = repositoryType.GetFields(
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        foreach (var field in fields)
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
                continue;

            if (field.GetRawConstantValue() is not string value || string.IsNullOrWhiteSpace(value))
                continue;

            if (!LooksLikeSql(value))
                continue;

            statements[field.Name] = value;
        }

        return statements;
    }

    /// <summary>
    /// Reports whether a constant's text is a SQL statement rather than an ordinary string constant
    /// such as a cache key or a parameter name.
    /// </summary>
    /// <param name="value">The constant's text.</param>
    /// <returns><c>true</c> when the text opens with a recognised SQL verb.</returns>
    public static bool LooksLikeSql(string value)
    {
        var normalised = Normalise(value);

        return normalised.StartsWith("SELECT ", StringComparison.Ordinal)
            || normalised.StartsWith("INSERT ", StringComparison.Ordinal)
            || normalised.StartsWith("UPDATE ", StringComparison.Ordinal)
            || normalised.StartsWith("DELETE ", StringComparison.Ordinal)
            || normalised.StartsWith("WITH ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Upper-cases a statement and collapses every run of whitespace to a single space, so that
    /// line breaks and indentation can never hide a column or a filter from an assertion.
    /// </summary>
    /// <param name="sql">Raw statement text.</param>
    /// <returns>The normalised text.</returns>
    public static string Normalise(string sql)
    {
        return string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    /// <summary>
    /// Splits a comma-separated SQL list at bracket depth zero, so that a function call such as
    /// <c>CONCAT(u.FirstName, ' ', u.LastName)</c> counts as one item rather than three.
    /// </summary>
    /// <param name="list">The list text, already normalised.</param>
    /// <returns>The items, trimmed, empties removed.</returns>
    public static IReadOnlyList<string> SplitTopLevel(string list)
    {
        var items = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;

        foreach (var character in list)
        {
            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;

            if (character == ',' && depth == 0)
            {
                items.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        items.Add(current.ToString().Trim());

        return items.Where(item => item.Length > 0).ToList();
    }

    /// <summary>
    /// The set of column names a SELECT statement hands back to Dapper — the names Dapper matches
    /// against the entity's properties, so an omission here is a property left at its default.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>The projected output names, or <c>null</c> when the statement is not a plain
    /// SELECT or projects <c>*</c> (in which case the projection is the table's, not the
    /// statement's, and nothing can be asserted about it here).</returns>
    public static ISet<string>? ProjectedColumns(string sql)
    {
        var normalised = Normalise(sql);

        if (!normalised.StartsWith("SELECT ", StringComparison.Ordinal))
            return null;

        var fromIndex = IndexOfTopLevelFrom(normalised);
        if (fromIndex < 0)
            return null;

        var body = normalised[7..fromIndex].Trim();
        if (body == "*" || body.EndsWith(".*", StringComparison.Ordinal))
            return null;

        var columns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in SplitTopLevel(body))
        {
            var alias = Regex.Match(item, @"\sAS\s+([A-Z0-9_""]+)$");
            if (alias.Success)
            {
                // Record BOTH names. The alias is what Dapper binds to a property; the underlying
                // column is what an UPDATE writes. `userid AS LoginUserId` makes the row readable
                // and the column writable under two different spellings, and a gate that knew only
                // one of them would report a data-loss defect on every aliased repository.
                columns.Add(alias.Groups[1].Value.Trim('"'));
                continue;
            }

            var bare = item.Split('.').Last().Trim();
            if (IsColumnToken(bare))
                columns.Add(bare);
        }

        return columns;
    }

    /// <summary>
    /// SQL words that appear where a column name would but name no column. Without these the gate
    /// treats the literal in <c>TRUE as IsActive</c> as a column called TRUE, and every sibling read
    /// then looks as though it had lost one.
    /// </summary>
    private static readonly HashSet<string> NonColumnWords = new(StringComparer.Ordinal)
    {
        "TRUE", "FALSE", "NULL", "COALESCE", "CONCAT", "CAST", "COUNT", "SUM", "AVG", "MIN", "MAX",
        "NOW", "LOWER", "UPPER", "DISTINCT", "CASE", "WHEN", "THEN", "ELSE", "END", "AS", "AND",
        "OR", "NOT", "ROUND", "LENGTH", "TRIM", "DATE", "INTERVAL", "EXTRACT", "GREATEST", "LEAST",
        "SELECT", "FROM", "WHERE", "DOUBLE", "PRECISION", "INTEGER", "BIGINT", "NUMERIC", "TEXT",
        "BOOLEAN", "TIMESTAMP", "VARCHAR", "IS", "IN", "ON", "BY", "ASC", "DESC", "NULLIF", "FILTER",
    };

    /// <summary>
    /// Reports whether a token names a column rather than being a SQL literal, keyword or number.
    /// </summary>
    /// <param name="token">Candidate token, already upper-cased.</param>
    /// <returns><c>true</c> when the token can be a column name.</returns>
    private static bool IsColumnToken(string token)
    {
        return Regex.IsMatch(token, "^[A-Z_][A-Z0-9_]*$") && !NonColumnWords.Contains(token);
    }

    /// <summary>
    /// The column names inside a projected expression, so that a column wrapped in a function is
    /// still recognised as read. <c>COALESCE(ipaddress, '') AS ClientIP</c> reads <c>ipaddress</c>
    /// just as surely as a bare reference would, and a gate that could not see through the COALESCE
    /// would report a data-loss defect on every defensively-written repository.
    /// </summary>
    /// <param name="expression">The projected expression, without its alias.</param>
    /// <returns>The column names it references.</returns>
    private static IEnumerable<string> ColumnTokens(string expression)
    {
        // A correlated sub-select projects its own columns from its own tables. Reading names out of
        // it would credit the OUTER statement with columns it never returns, so an aliased
        // sub-select is opaque: only its alias counts.
        if (expression.Contains("SELECT", StringComparison.Ordinal))
            yield break;

        foreach (Match token in Regex.Matches(expression, @"[A-Z_][A-Z0-9_]*(?:\.[A-Z_][A-Z0-9_]*)*"))
        {
            var name = token.Value.Split('.').Last();

            if (IsColumnToken(name))
                yield return name;
        }
    }

    /// <summary>
    /// Every column name a SELECT statement makes available to its caller: the output names, plus
    /// the underlying columns of any aliased expression.
    /// </summary>
    /// <remarks>
    /// This is the set the write-back gate needs and <see cref="ProjectedColumns"/> is not. A read
    /// that projects <c>COALESCE(ipaddress, '') AS ClientIP</c> has genuinely LOADED <c>ipaddress</c>,
    /// so an UPDATE writing <c>ipaddress</c> is a safe round trip even though no output is called
    /// that. The narrow-read gate must not use this set, because an aggregate's input —
    /// <c>COUNT(v.ViewId)</c> — is not a column the caller receives.
    /// </remarks>
    /// <param name="sql">The statement text.</param>
    /// <returns>The readable column names, or <c>null</c> when the statement projects <c>*</c>.</returns>
    public static ISet<string>? ReadableColumns(string sql)
    {
        var outputs = ProjectedColumns(sql);
        if (outputs is null)
            return null;

        var normalised = Normalise(sql);
        var fromIndex = IndexOfTopLevelFrom(normalised);
        if (fromIndex < 0)
            return outputs;

        foreach (var item in SplitTopLevel(normalised[7..fromIndex].Trim()))
        {
            var alias = Regex.Match(item, @"\sAS\s+([A-Z0-9_""]+)$");
            if (!alias.Success)
                continue;

            foreach (var source in ColumnTokens(item[..alias.Index]))
            {
                outputs.Add(source);
            }
        }

        return outputs;
    }

    /// <summary>
    /// The first table a statement reads from, so a gate can tell whether an UPDATE and a SELECT in
    /// the same repository are even talking about the same rows. Several repositories own more than
    /// one table — <c>NewsletterRepo</c> also writes <c>Subscriber</c> — and comparing across them
    /// produces nothing but noise.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>The table name, or <c>null</c> when there is no plain FROM clause.</returns>
    public static string? SourceTable(string sql)
    {
        var normalised = Normalise(sql);
        var fromIndex = IndexOfTopLevelFrom(normalised);

        if (fromIndex < 0)
            return null;

        var match = Regex.Match(normalised[(fromIndex + 6)..], @"^([A-Z0-9_]+)");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reports whether a COUNT statement is a uniqueness probe — the "is this slug already taken,
    /// ignoring the row I am editing" shape — rather than the COUNT that pages a listing. The two
    /// have similar names and completely different jobs, so the filter-parity gate must not pair a
    /// listing with a uniqueness probe.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns><c>true</c> when the statement excludes a row by key.</returns>
    public static bool IsUniquenessProbe(string sql)
    {
        var normalised = Normalise(sql);

        return normalised.Contains("!=", StringComparison.Ordinal)
            || normalised.Contains("<>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Locates the FROM that closes a statement's projection list, ignoring any FROM nested inside
    /// a sub-select or a function call.
    /// </summary>
    /// <param name="normalised">The normalised statement.</param>
    /// <returns>Index of the closing FROM, or -1 when the statement has none.</returns>
    private static int IndexOfTopLevelFrom(string normalised)
    {
        var depth = 0;

        for (var index = 0; index < normalised.Length - 6; index++)
        {
            var character = normalised[index];

            if (character == '(')
                depth++;
            else if (character == ')')
                depth--;

            if (depth == 0 && string.CompareOrdinal(normalised, index, " FROM ", 0, 6) == 0)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// The table an UPDATE writes to and the set of columns it SETs — the write half of every
    /// read-modify-write round trip in this codebase.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>Table name and column set, or <c>null</c> when the statement is not an UPDATE.</returns>
    public static (string Table, ISet<string> Columns)? UpdatedColumns(string sql)
    {
        var normalised = Normalise(sql);

        var match = Regex.Match(normalised, @"^UPDATE\s+(\w+)\s+SET\s+(.*?)(?:\s+WHERE\s+|\s+RETURNING\s+|$)");
        if (!match.Success)
            return null;

        var columns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in SplitTopLevel(match.Groups[2].Value))
        {
            var name = assignment.Split('=')[0].Trim();
            if (Regex.IsMatch(name, "^[A-Z0-9_]+$"))
                columns.Add(name);
        }

        return (match.Groups[1].Value, columns);
    }

    /// <summary>
    /// The PostgreSQL stored function a statement reads through, for the repositories that call a
    /// function instead of embedding a projection. Those are the statements whose projection lives
    /// in a migration script, which is exactly where the REQ-FN-053 data-loss defect hid.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns>The function name, or <c>null</c> when the statement embeds its own projection.</returns>
    public static string? StoredFunctionRead(string sql)
    {
        var match = Regex.Match(Normalise(sql), @"^SELECT\s+\*\s+FROM\s+([A-Z0-9_]+)\s*\(");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reports whether a statement restricts its rows to published posts, accepting the aliased and
    /// unaliased spellings of the filter.
    /// </summary>
    /// <param name="sql">The statement text.</param>
    /// <returns><c>true</c> when the published filter is present.</returns>
    public static bool FiltersOnPublished(string sql)
    {
        var normalised = Normalise(sql);

        return normalised.Contains("P.PUBLISHED = TRUE", StringComparison.Ordinal)
            || normalised.Contains(" PUBLISHED = TRUE", StringComparison.Ordinal);
    }
}
