namespace TechieBlog.Tests.Ops;

/// <summary>
/// Finds the repository root for tests that assert facts about files outside <c>source/</c>
/// (workflow, NuGet config, README, brand assets).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> several REQ-NFR gates read the repository layout itself; they share one
/// root lookup instead of each carrying a copy.</para>
/// <para><b>Code Flow:</b> walk up from the test assembly until a folder holds both
/// <c>source/</c> and <c>TechieBlog.slnx</c>.</para>
/// </remarks>
internal static class RepositoryLocator
{
    /// <summary>
    /// Locates the repository root.
    /// </summary>
    /// <returns>The absolute path of the root, or <c>null</c> when the assembly runs outside a checkout.</returns>
    internal static string? FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "source"))
                && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
