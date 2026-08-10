using System.Text.RegularExpressions;

namespace TechieBlog.Tests.DataAccess;

/// <summary>
/// The effective definition of every PostgreSQL stored function this solution ships, read from the
/// DbUp migration scripts in the order DbUp applies them.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-NFR-016 / REQ-FN-053. Several repositories do not embed a projection at
/// all — <c>BlogUserRepo.SelectByIdSql</c> is literally
/// <c>SELECT * FROM SelectBlogUserById(@pUserId)</c>. For those the projection lives in a migration
/// script, and that is where the worst instance of the projection-completeness defect class hid:
/// <c>SelectBlogUserById</c> returned 17 of <c>BlogUser</c>'s 26 columns, so opening Manage Profile
/// and pressing Save with no edits erased the site owner's entire resume. A gate that reads only the
/// C# constants is blind to exactly that case, so this class makes the SQL side readable too.</para>
///
/// <para><b>Code Flow:</b> walk up from the test assembly to the repository root, read every
/// <c>source/BlogDb/PostgresScripts/*.sql</c> in ordinal filename order, and record each
/// <c>CREATE OR REPLACE FUNCTION … RETURNS TABLE (…)</c>. Later scripts overwrite earlier ones, so
/// what remains is the definition a freshly migrated database actually has — script 022's widened
/// <c>SelectBlogUserById</c>, not script 002's original.</para>
///
/// <para><b>Dependencies:</b> the migration scripts on disk. No database and no Docker: this reads
/// text, it does not run it. When the scripts cannot be located the catalogue is empty and the gates
/// that use it fail loudly rather than passing vacuously.</para>
///
/// <para><b>Usage:</b> consumed by <see cref="ProjectionCompletenessTests"/>.</para>
/// </remarks>
public static class StoredFunctionCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<string>>> Catalogue =
        new(BuildCatalogue);

    /// <summary>
    /// Absolute path of the migration-script folder, or <c>null</c> when it could not be located.
    /// </summary>
    public static string? ScriptFolder => LocateScriptFolder();

    /// <summary>
    /// Every function the scripts define, keyed by upper-cased name, valued by the column names its
    /// <c>RETURNS TABLE</c> clause hands back.
    /// </summary>
    /// <returns>The catalogue.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Functions() => Catalogue.Value;

    /// <summary>
    /// Looks a function up by name, case-insensitively.
    /// </summary>
    /// <param name="functionName">Function name as written in the calling SQL.</param>
    /// <returns>The returned column names, or <c>null</c> when the function is not a
    /// <c>RETURNS TABLE</c> function or is not defined in the scripts.</returns>
    public static IReadOnlyList<string>? ReturnedColumns(string functionName)
    {
        return Functions().TryGetValue(functionName.ToUpperInvariant(), out var columns)
            ? columns
            : null;
    }

    /// <summary>
    /// Parses every migration script and keeps the last definition of each function.
    /// </summary>
    /// <returns>The catalogue.</returns>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildCatalogue()
    {
        var catalogue = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var folder = LocateScriptFolder();
        if (folder is null)
            return catalogue;

        var scripts = Directory.GetFiles(folder, "*.sql").OrderBy(path => path, StringComparer.Ordinal);

        foreach (var script in scripts)
        {
            foreach (var (name, columns) in ParseFunctions(File.ReadAllText(script)))
            {
                catalogue[name] = columns;
            }
        }

        return catalogue;
    }

    /// <summary>
    /// Finds every <c>CREATE OR REPLACE FUNCTION … RETURNS TABLE (…)</c> in one script and reads the
    /// column names out of its returns clause.
    /// </summary>
    /// <param name="scriptText">The script's full text.</param>
    /// <returns>Name/column-list pairs in the order they appear.</returns>
    private static IEnumerable<(string Name, IReadOnlyList<string> Columns)> ParseFunctions(string scriptText)
    {
        var withoutComments = Regex.Replace(scriptText, @"^\s*--.*$", string.Empty, RegexOptions.Multiline);

        var declarations = Regex.Matches(
            withoutComments,
            @"CREATE\s+(?:OR\s+REPLACE\s+)?FUNCTION\s+([A-Za-z0-9_]+)\s*\(",
            RegexOptions.IgnoreCase);

        foreach (Match declaration in declarations)
        {
            var afterParameters = SkipBracketed(withoutComments, declaration.Index + declaration.Length - 1);
            if (afterParameters < 0)
                continue;

            var returnsTable = Regex.Match(
                withoutComments[afterParameters..Math.Min(withoutComments.Length, afterParameters + 120)],
                @"^\s*RETURNS\s+TABLE\s*\(",
                RegexOptions.IgnoreCase);

            if (!returnsTable.Success)
                continue;

            var openBracket = afterParameters + returnsTable.Length - 1;
            var closeBracket = SkipBracketed(withoutComments, openBracket);
            if (closeBracket < 0)
                continue;

            var body = withoutComments[(openBracket + 1)..(closeBracket - 1)];

            var columns = SqlStatementInventory
                .SplitTopLevel(SqlStatementInventory.Normalise(body))
                .Select(item => item.Split(' ')[0].Trim())
                .Where(item => Regex.IsMatch(item, "^[A-Z0-9_]+$"))
                .ToList();

            if (columns.Count > 0)
                yield return (declaration.Groups[1].Value.ToUpperInvariant(), columns);
        }
    }

    /// <summary>
    /// Walks past a bracketed section, honouring nesting so that a column type such as
    /// <c>VARCHAR(100)</c> does not close the list it sits in.
    /// </summary>
    /// <param name="text">The text being scanned.</param>
    /// <param name="openIndex">Index of the opening bracket.</param>
    /// <returns>Index just past the matching closing bracket, or -1 when it is unbalanced.</returns>
    private static int SkipBracketed(string text, int openIndex)
    {
        var depth = 0;

        for (var index = openIndex; index < text.Length; index++)
        {
            if (text[index] == '(')
                depth++;
            else if (text[index] == ')')
            {
                depth--;
                if (depth == 0)
                    return index + 1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the folder holding the migration scripts, trying three routes in order: the
    /// <c>TechieBlogMigrationScripts</c> override, a walk up from the test assembly, and a walk up
    /// from this source file's compile-time path.
    /// </summary>
    /// <remarks>
    /// The third route matters: <c>dotnet test --artifacts-path</c> stages the assembly OUTSIDE the
    /// repository tree, and the assembly walk then finds nothing. Without the source-path fallback
    /// the gate that depends on this catalogue would report success while asserting against an empty
    /// dictionary, which is the one failure mode a gate must never have.
    /// </remarks>
    /// <returns>Absolute path to the script folder, or <c>null</c> when it is not present.</returns>
    private static string? LocateScriptFolder()
    {
        var configured = Environment.GetEnvironmentVariable(Integration.PostgresFixture.ScriptFolderOverrideName);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        return WalkUpFrom(AppContext.BaseDirectory) ?? WalkUpFrom(Path.GetDirectoryName(ThisFilePath()));
    }

    /// <summary>
    /// Walks up from a starting folder looking for <c>source/BlogDb/PostgresScripts</c>.
    /// </summary>
    /// <param name="startFolder">Folder to start from; may be <c>null</c>.</param>
    /// <returns>The script folder, or <c>null</c>.</returns>
    private static string? WalkUpFrom(string? startFolder)
    {
        if (string.IsNullOrWhiteSpace(startFolder) || !Directory.Exists(startFolder))
            return null;

        var current = new DirectoryInfo(startFolder);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "source", "BlogDb", "PostgresScripts");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// This source file's path, captured by the compiler.
    /// </summary>
    /// <param name="filePath">Supplied by the compiler; never pass a value.</param>
    /// <returns>The absolute path of this file on the machine that compiled it.</returns>
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string filePath = "")
    {
        return filePath;
    }
}
