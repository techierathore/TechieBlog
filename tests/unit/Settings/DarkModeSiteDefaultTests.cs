using System.Runtime.CompilerServices;
using BlogEngine.Common;
using BlogModels;
using BlogModels.Models;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Pins DARK as the shipped site-wide light/dark default across every layer that can supply it
/// (owner decision, 2026-08-10).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The site opened light for one reason: the VALUE was <c>False</c> in the
/// three independent places that can answer "what does a visitor with no preference get" — the
/// seeded settings row, the <see cref="SiteSettings"/> property initialiser, and the theme
/// service's failure path. Any one of them left behind makes a fresh database and an established
/// one open in different modes, and nothing in the compiler notices. This suite is what keeps the
/// three honest. (The service's failure path is asserted in the BlogUI suite, which cannot be
/// referenced from here without dragging the UI graph into every run.)</para>
///
/// <para><b>What is pinned:</b> that a default-constructed aggregate is dark; that the mapper
/// still round-trips the flag in both directions, so an administrator can switch the site back to
/// light; that an absent row leaves the dark default standing rather than reading as "off"; and
/// that the migration folder ends up seeding <c>True</c>, which is the only one of the three that
/// a code review cannot see from C# alone.</para>
///
/// <para><b>Dependencies:</b> none beyond BlogEngine and BlogModels, plus read access to
/// <c>source/BlogDb/PostgresScripts</c> located by walking up from the test assembly.</para>
/// </remarks>
public class DarkModeSiteDefaultTests
{
    /// <summary>
    /// A default-constructed settings aggregate — the state used whenever the settings table has
    /// no theme row — is dark.
    /// </summary>
    [Fact]
    public void ShippedSettingsAggregateIsDark()
    {
        // Arrange, Act
        var settings = new SiteSettings();

        // Assert
        Assert.True(settings.IsDarkModeDefault);
    }

    /// <summary>
    /// An empty settings table leaves the shipped dark default standing rather than reading the
    /// missing row as "light".
    /// </summary>
    [Fact]
    public void MissingThemeRowKeepsTheDarkDefault()
    {
        // Arrange
        var values = new Dictionary<string, string>();

        // Act
        var settings = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        // Assert
        Assert.True(settings.IsDarkModeDefault);
    }

    /// <summary>
    /// An administrator who stores <c>False</c> gets light, so making dark the default did not
    /// remove the light option or break the settings switch.
    /// </summary>
    [Fact]
    public void StoredLightValueStillWins()
    {
        // Arrange
        var values = new Dictionary<string, string>
        {
            [SiteSettingKeys.IsDarkModeDefault] = "False"
        };

        // Act
        var settings = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        // Assert
        Assert.False(settings.IsDarkModeDefault);
    }

    /// <summary>
    /// The flag survives a write followed by a read in both directions, which is what makes the
    /// admin switch usable rather than one-way.
    /// </summary>
    /// <param name="isDark">The value an administrator saves.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FlagRoundTripsThroughPersistence(bool isDark)
    {
        // Arrange
        var rows = SiteSettingsMapper.ToRows(new SiteSettings { IsDarkModeDefault = isDark });
        var values = rows.ToDictionary(row => row.SettingKey, row => row.SettingValue ?? string.Empty);

        // Act
        var reloaded = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        // Assert
        Assert.Equal(isDark, reloaded.IsDarkModeDefault);
    }

    /// <summary>
    /// Replaying the migration folder in order leaves the seeded theme row on a dark value, so a
    /// database built from scratch agrees with the code-side defaults above.
    /// </summary>
    [Fact]
    public void MigrationFolderSeedsDarkAsTheLastWord()
    {
        // Arrange
        var scriptFolder = LocateScriptFolder();
        Assert.True(scriptFolder is not null,
            "source/BlogDb/PostgresScripts was not found by walking up from the test assembly.");

        var scripts = Directory.GetFiles(scriptFolder!, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        // Act — the last script that mentions the key at all decides what a fresh database gets.
        var lastMention = scripts.LastOrDefault(path =>
            File.ReadAllText(path).Contains(SiteSettingKeys.IsDarkModeDefault, StringComparison.Ordinal));

        // Assert
        Assert.True(lastMention is not null,
            $"No migration mentions {SiteSettingKeys.IsDarkModeDefault}; the seeded default is unknowable.");

        var body = File.ReadAllText(lastMention!);
        Assert.Contains("'True'", body, StringComparison.Ordinal);
        Assert.DoesNotContain("'Theme.IsDarkModeDefault',    'False'", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test assembly, then from this source file, looking for the migration
    /// folder.
    /// </summary>
    /// <returns>The migration folder, or <c>null</c> when it cannot be found.</returns>
    private static string? LocateScriptFolder()
    {
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
        {
            return null;
        }

        var current = new DirectoryInfo(startFolder);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "source", "BlogDb", "PostgresScripts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// This source file's path, captured by the compiler.
    /// </summary>
    /// <param name="filePath">Supplied by the compiler; never pass a value.</param>
    /// <returns>The absolute path of this file on the machine that compiled it.</returns>
    private static string ThisFilePath([CallerFilePath] string filePath = "")
    {
        return filePath;
    }
}
