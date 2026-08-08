using Blazored.LocalStorage;
using BlogModels.Interfaces;

namespace BlogUI;

/// <summary>
/// Provides theme management services for the TechieBlog application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages theme selection, light/dark mode toggle, and CSS variable injection.
/// This service is the central point for all theme-related operations in the UI layer.</para>
///
/// <para><b>Two layers, deliberately (REQ-UI-032 / REQ-FN-039, BRD-66 / BRD-68):</b></para>
/// <list type="bullet">
///   <item><b>Site layer</b> — the administrator's choice, persisted server-side through
///     <see cref="ISiteSettingsService"/>. It is what an anonymous first-time visitor receives,
///     and it is site-wide rather than per browser.</item>
///   <item><b>Visitor layer</b> — an individual's own light/dark toggle and theme override, kept
///     in LocalStorage. It wins when present, so BRD-66 keeps working; when absent the site layer
///     shows through.</item>
/// </list>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>On application start, reads the visitor's LocalStorage preference</item>
///   <item>Falls back to the admin-selected site default when the visitor has none</item>
///   <item>Applies theme via the data-site-theme attribute and the dark class on the HTML element</item>
///   <item>Persists visitor preferences to LocalStorage on any theme change</item>
///   <item>Notifies subscribers via OnThemeChanged event when theme changes</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>Blazored.LocalStorage - Persists per-visitor preferences across browser sessions</item>
///   <item><see cref="ISiteSettingsService"/> - Supplies the admin-selected site-wide default</item>
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
    private readonly ISiteSettingsService siteSettingsSvc;

    private const string ThemeStorageKey = "techieblog-theme";
    private const string DarkModeStorageKey = "techieblog-dark-mode";
    private const string DefaultTheme = "trblaze-modern";

    /// <summary>
    /// Pre-migration identifier for the default theme, still present in the
    /// LocalStorage of returning visitors. Mapped onto <see cref="DefaultTheme"/>.
    /// </summary>
    private const string LegacyDefaultTheme = "fluent-modern";

    private string currentTheme = DefaultTheme;
    private bool isDarkMode = false;

    /// <summary>
    /// Event raised when the theme or dark mode setting changes.
    /// </summary>
    /// <remarks>
    /// Subscribe to this event to update UI components when theme changes.
    /// The event passes the new theme name and dark mode state.
    /// </remarks>
    public event Action<string, bool>? OnThemeChanged;

    /// <summary>
    /// Initializes a new instance of ThemeService with LocalStorage dependency.
    /// </summary>
    /// <remarks>
    /// The LocalStorage service is injected via DI from Blazored.LocalStorage package.
    /// This is already installed as noted in Story 1.4 completion.
    /// </remarks>
    /// <param name="localStorageSvc">LocalStorage service for persisting theme preferences.</param>
    /// <param name="siteSettingsSvc">Site settings service supplying the admin-selected default.</param>
    public ThemeService(ILocalStorageService localStorageSvc, ISiteSettingsService siteSettingsSvc)
    {
        this.localStorageSvc = localStorageSvc;
        this.siteSettingsSvc = siteSettingsSvc;
    }

    /// <summary>
    /// Reads the administrator-selected site-wide theme identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the default every visitor sees before expressing a
    /// preference of their own (BRD-68). A stored identifier this build no longer ships — including
    /// the pre-migration <c>fluent-modern</c> — is mapped onto the shipped default so a stale
    /// database can never leave the site unstyled.</para>
    /// <para><b>Flow:</b> Read the settings aggregate, normalise the identifier.</para>
    /// <para><b>Side Effects:</b> None. A read failure falls back to the shipped default.</para>
    /// </remarks>
    /// <returns>A theme identifier this build ships. Never null or empty.</returns>
    public async Task<string> GetSiteDefaultThemeAsync()
    {
        try
        {
            var settings = await siteSettingsSvc.GetSettingsAsync();
            return NormaliseTheme(settings.SiteTheme);
        }
        catch
        {
            return DefaultTheme;
        }
    }

    /// <summary>
    /// Reads whether the site defaults to dark mode for visitors with no stored preference.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The administrator sets the starting point; a visitor's own
    /// toggle still overrides it (BRD-66), so this value only decides what an untouched browser
    /// gets.</para>
    /// <para><b>Side Effects:</b> None. A read failure falls back to light mode.</para>
    /// </remarks>
    /// <returns>True when new visitors should start in dark mode.</returns>
    public async Task<bool> GetSiteDefaultDarkModeAsync()
    {
        try
        {
            var settings = await siteSettingsSvc.GetSettingsAsync();
            return settings.IsDarkModeDefault;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Maps a stored theme identifier onto one this build actually ships.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Guards every entry point — settings, LocalStorage and the
    /// settings screen — so an unknown identifier degrades to the shipped default instead of
    /// producing an HTML element with a <c>data-site-theme</c> no stylesheet answers.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="themeName">The identifier to normalise; may be null, empty or legacy.</param>
    /// <returns>A theme identifier present in <see cref="GetAvailableThemes"/>.</returns>
    public string NormaliseTheme(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName) || themeName == LegacyDefaultTheme)
        {
            return DefaultTheme;
        }

        return GetAvailableThemes().Any(theme => theme.Id == themeName) ? themeName : DefaultTheme;
    }

    /// <summary>
    /// Retrieves the current site theme name from LocalStorage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Attempts to read the visitor's own theme from LocalStorage</item>
    ///   <item>Falls back to the administrator's site-wide theme when the visitor has none —
    ///     this is what makes the admin's choice the site default (REQ-UI-032, BRD-68)</item>
    ///   <item>Updates internal state to match the resolved value</item>
    /// </list>
    /// </remarks>
    /// <returns>
    /// Theme name string: "trblaze-modern", "developer", or "minimal".
    /// Returns the admin-selected site theme when the visitor has stored no preference.
    /// </returns>
    public async Task<string> GetCurrentThemeAsync()
    {
        string? storedTheme = null;
        try
        {
            storedTheme = await localStorageSvc.GetItemAsync<string>(ThemeStorageKey);
        }
        catch
        {
            storedTheme = null;
        }

        // A visitor override wins; otherwise the site-wide default shows through.
        currentTheme = string.IsNullOrEmpty(storedTheme) || storedTheme == LegacyDefaultTheme
            ? await GetSiteDefaultThemeAsync()
            : NormaliseTheme(storedTheme);

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
        currentTheme = NormaliseTheme(themeName);
        await localStorageSvc.SetItemAsync(ThemeStorageKey, currentTheme);
        OnThemeChanged?.Invoke(currentTheme, isDarkMode);
    }

    /// <summary>
    /// Applies a theme to the current circuit without recording a visitor preference.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The Settings screen previews a theme as the administrator
    /// clicks through the swatches. Writing that preview to LocalStorage would silently convert
    /// the administrator into a visitor with an override, and the site-wide default would then be
    /// invisible to them for ever after. So the preview only raises the change event.</para>
    /// <para><b>Side Effects:</b> Raises <see cref="OnThemeChanged"/>; touches no storage.</para>
    /// </remarks>
    /// <param name="themeName">The theme to preview.</param>
    public void PreviewTheme(string themeName)
    {
        currentTheme = NormaliseTheme(themeName);
        OnThemeChanged?.Invoke(currentTheme, isDarkMode);
    }

    /// <summary>
    /// Retrieves the current dark mode state from LocalStorage.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Attempts to read the visitor's dark-mode preference from LocalStorage</item>
    ///   <item>Falls back to the administrator's site-wide default when the visitor has none</item>
    /// </list>
    /// <para>The value is read as a nullable bool on purpose: a stored <c>false</c> is a real
    /// visitor choice ("I want light mode") and must not be confused with an absent key, which is
    /// the only case where the site default applies.</para>
    /// </remarks>
    /// <returns>True if dark mode is enabled, false for light mode.</returns>
    public async Task<bool> GetDarkModeAsync()
    {
        bool? storedDarkMode;
        try
        {
            storedDarkMode = await localStorageSvc.GetItemAsync<bool?>(DarkModeStorageKey);
        }
        catch
        {
            storedDarkMode = null;
        }

        isDarkMode = storedDarkMode ?? await GetSiteDefaultDarkModeAsync();
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
    ///   <item>trblaze-modern: neutral shadcn palette, blue primary (default)</item>
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
                Id = "trblaze-modern",
                Name = "TrBlaze Modern",
                Description = "Clean, professional shadcn palette with a blue primary",
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
    /// <example>"trblaze-modern", "developer", "minimal"</example>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the theme shown in UI.
    /// </summary>
    /// <example>"TrBlaze Modern", "Developer Dark", "Minimal Clean"</example>
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
