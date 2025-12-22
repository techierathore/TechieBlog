using Blazored.LocalStorage;

namespace BlogUI;

/// <summary>
/// Provides theme management services for the TechieBlog application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages theme selection, light/dark mode toggle, and CSS variable injection.
/// This service is the central point for all theme-related operations in the UI layer.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>On application start, retrieves saved theme preferences from LocalStorage</item>
///   <item>Applies theme via data-site-theme and data-theme attributes on HTML element</item>
///   <item>Persists user preferences to LocalStorage on any theme change</item>
///   <item>Notifies subscribers via OnThemeChanged event when theme changes</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>Blazored.LocalStorage - Persists theme preferences across browser sessions</item>
/// </list>
///
/// <para><b>Usage:</b> Inject via DI and use in ThemeProvider component or any component
/// that needs to respond to theme changes.</para>
/// </remarks>
/// <example>
/// <code>
/// @inject ThemeService ThemeService
///
/// // Get current theme
/// var theme = await ThemeService.GetCurrentThemeAsync();
///
/// // Set a specific theme
/// await ThemeService.SetThemeAsync("developer");
///
/// // Toggle dark mode
/// await ThemeService.ToggleDarkModeAsync();
/// </code>
/// </example>
public class ThemeService
{
    private readonly ILocalStorageService localStorageSvc;

    private const string ThemeStorageKey = "techieblog-theme";
    private const string DarkModeStorageKey = "techieblog-dark-mode";
    private const string DefaultTheme = "fluent-modern";

    private string currentTheme = DefaultTheme;
    private bool isDarkMode = false;

    /// <summary>
    /// Event raised when the theme or dark mode setting changes.
    /// </summary>
    /// <remarks>
    /// Subscribe to this event to update UI components when theme changes.
    /// The event passes the new theme name and dark mode state.
    /// </remarks>
    public event Action<string, bool> OnThemeChanged;

    /// <summary>
    /// Initializes a new instance of ThemeService with LocalStorage dependency.
    /// </summary>
    /// <remarks>
    /// The LocalStorage service is injected via DI from Blazored.LocalStorage package.
    /// This is already installed as noted in Story 1.4 completion.
    /// </remarks>
    /// <param name="localStorageSvc">LocalStorage service for persisting theme preferences.</param>
    public ThemeService(ILocalStorageService localStorageSvc)
    {
        this.localStorageSvc = localStorageSvc;
    }

    /// <summary>
    /// Retrieves the current site theme name from LocalStorage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Attempts to read theme from LocalStorage</item>
    ///   <item>Falls back to "fluent-modern" if no theme is saved</item>
    ///   <item>Updates internal state to match stored value</item>
    /// </list>
    /// </remarks>
    /// <returns>
    /// Theme name string: "fluent-modern", "developer", or "minimal".
    /// Returns "fluent-modern" as default if no preference is stored.
    /// </returns>
    public async Task<string> GetCurrentThemeAsync()
    {
        try
        {
            var storedTheme = await localStorageSvc.GetItemAsync<string>(ThemeStorageKey);
            currentTheme = string.IsNullOrEmpty(storedTheme) ? DefaultTheme : storedTheme;
        }
        catch
        {
            currentTheme = DefaultTheme;
        }
        return currentTheme;
    }

    /// <summary>
    /// Sets the site theme and persists to LocalStorage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Validates theme name against available themes</item>
    ///   <item>Updates internal state</item>
    ///   <item>Persists to LocalStorage for session persistence</item>
    ///   <item>Raises OnThemeChanged event to notify subscribers</item>
    /// </list>
    /// </remarks>
    /// <param name="themeName">
    /// The theme to apply. Valid values: "fluent-modern", "developer", "minimal".
    /// Invalid values are ignored and default theme is used.
    /// </param>
    public async Task SetThemeAsync(string themeName)
    {
        var validThemes = GetAvailableThemes().Select(t => t.Id).ToList();
        if (!validThemes.Contains(themeName))
        {
            themeName = DefaultTheme;
        }

        currentTheme = themeName;
        await localStorageSvc.SetItemAsync(ThemeStorageKey, themeName);
        OnThemeChanged?.Invoke(currentTheme, isDarkMode);
    }

    /// <summary>
    /// Retrieves the current dark mode state from LocalStorage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Attempts to read dark mode preference from LocalStorage</item>
    ///   <item>Defaults to false (light mode) if no preference is stored</item>
    /// </list>
    /// </remarks>
    /// <returns>True if dark mode is enabled, false for light mode.</returns>
    public async Task<bool> GetDarkModeAsync()
    {
        try
        {
            isDarkMode = await localStorageSvc.GetItemAsync<bool>(DarkModeStorageKey);
        }
        catch
        {
            isDarkMode = false;
        }
        return isDarkMode;
    }

    /// <summary>
    /// Toggles between light and dark mode.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Inverts current dark mode state</item>
    ///   <item>Persists new state to LocalStorage</item>
    ///   <item>Raises OnThemeChanged event to notify subscribers</item>
    /// </list>
    ///
    /// <para><b>Side Effects:</b> Updates LocalStorage and triggers UI refresh
    /// via OnThemeChanged event.</para>
    /// </remarks>
    public async Task ToggleDarkModeAsync()
    {
        isDarkMode = !isDarkMode;
        await localStorageSvc.SetItemAsync(DarkModeStorageKey, isDarkMode);
        OnThemeChanged?.Invoke(currentTheme, isDarkMode);
    }

    /// <summary>
    /// Sets dark mode to a specific value.
    /// </summary>
    /// <remarks>
    /// Use this method when you need to explicitly set dark mode state
    /// rather than toggling. Useful for initializing state or syncing
    /// with system preferences.
    /// </remarks>
    /// <param name="darkMode">True to enable dark mode, false for light mode.</param>
    public async Task SetDarkModeAsync(bool darkMode)
    {
        isDarkMode = darkMode;
        await localStorageSvc.SetItemAsync(DarkModeStorageKey, isDarkMode);
        OnThemeChanged?.Invoke(currentTheme, isDarkMode);
    }

    /// <summary>
    /// Returns the list of all available themes with metadata.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Provides theme information for UI theme pickers
    /// and settings screens.</para>
    ///
    /// <para><b>Available Themes:</b></para>
    /// <list type="bullet">
    ///   <item>fluent-modern: Microsoft Fluent UI design (default)</item>
    ///   <item>developer: Code editor inspired with monospace fonts</item>
    ///   <item>minimal: Typography-focused with serif fonts</item>
    /// </list>
    /// </remarks>
    /// <returns>List of ThemeInfo objects containing theme metadata.</returns>
    public List<ThemeInfo> GetAvailableThemes()
    {
        return new List<ThemeInfo>
        {
            new ThemeInfo
            {
                Id = "fluent-modern",
                Name = "Fluent Modern",
                Description = "Clean, professional Microsoft Fluent UI design",
                IsDefault = true
            },
            new ThemeInfo
            {
                Id = "developer",
                Name = "Developer Dark",
                Description = "Code editor inspired with monospace typography",
                IsDefault = false
            },
            new ThemeInfo
            {
                Id = "minimal",
                Name = "Minimal Clean",
                Description = "Typography-focused design inspired by Medium",
                IsDefault = false
            }
        };
    }

    /// <summary>
    /// Gets the current theme name synchronously (from cached value).
    /// </summary>
    /// <remarks>
    /// Use GetCurrentThemeAsync() for initial load to ensure LocalStorage
    /// is properly read. This property returns the cached value after
    /// initialization for quick access without async overhead.
    /// </remarks>
    public string CurrentTheme => currentTheme;

    /// <summary>
    /// Gets the current dark mode state synchronously (from cached value).
    /// </summary>
    /// <remarks>
    /// Use GetDarkModeAsync() for initial load to ensure LocalStorage
    /// is properly read. This property returns the cached value after
    /// initialization for quick access without async overhead.
    /// </remarks>
    public bool IsDarkMode => isDarkMode;
}

/// <summary>
/// Contains metadata about a theme option.
/// </summary>
/// <remarks>
/// Used by ThemeService.GetAvailableThemes() to provide theme information
/// for UI theme pickers and settings screens.
/// </remarks>
public class ThemeInfo
{
    /// <summary>
    /// Unique identifier for the theme, used in data-site-theme attribute.
    /// </summary>
    /// <example>"fluent-modern", "developer", "minimal"</example>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the theme shown in UI.
    /// </summary>
    /// <example>"Fluent Modern", "Developer Dark", "Minimal Clean"</example>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Brief description of the theme's visual characteristics.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if this is the default theme applied to new users.
    /// </summary>
    public bool IsDefault { get; set; }
}
