using Blazored.LocalStorage;
using BlogModels.Interfaces;
using BlogModels.Models;
using BlogUI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// Pins the two-layer light/dark resolution in <see cref="ThemeService"/> now that the shipped
/// site default is DARK (owner decision, 2026-08-10).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> "Open in dark mode" and "let a visitor keep light mode" are one decision
/// made in two layers, and the interesting cases are the boundaries between them. The layer that
/// broke the requirement before this change was not the mechanism but the VALUE: every default in
/// the stack said light. Two of those defaults live in code, and only a test keeps them in step
/// with the seeded row and with each other.</para>
///
/// <para><b>What is pinned:</b> that a visitor with nothing stored inherits the administrator's
/// site default; that a stored <c>false</c> is honoured as a deliberate choice of light and is not
/// mistaken for an absent key; that a stored <c>true</c> is honoured; and that a settings failure
/// degrades to DARK rather than silently repainting the site light.</para>
///
/// <para><b>Dependencies:</b> NSubstitute for <see cref="ILocalStorageService"/> and
/// <see cref="ISiteSettingsService"/>. BlogUI, therefore this suite compiles only under
/// <c>-p:IncludeBlogUiTests=true</c>, which is the default.</para>
/// </remarks>
public class ThemeDefaultDarkModeTests
{
    private const string DarkModeStorageKey = "techieblog-dark-mode";

    private readonly ILocalStorageService localStorage = Substitute.For<ILocalStorageService>();
    private readonly ISiteSettingsService siteSettings = Substitute.For<ISiteSettingsService>();
    private readonly ThemeService service;

    /// <summary>
    /// Wires the service under test to substituted storage and settings.
    /// </summary>
    public ThemeDefaultDarkModeTests()
    {
        service = new ThemeService(localStorage, siteSettings);
    }

    /// <summary>
    /// Points the settings substitute at a site default.
    /// </summary>
    /// <param name="isDarkDefault">The administrator's stored choice.</param>
    private void SiteDefaultIs(bool isDarkDefault) =>
        siteSettings.GetSettingsAsync()
            .Returns(Task.FromResult(new SiteSettings { IsDarkModeDefault = isDarkDefault }));

    /// <summary>
    /// Points the storage substitute at a visitor preference, or at none.
    /// </summary>
    /// <param name="storedValue">The stored preference; <c>null</c> means the key is absent.</param>
    private void VisitorStored(bool? storedValue) =>
        localStorage.GetItemAsync<bool?>(DarkModeStorageKey, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<bool?>(storedValue));

    /// <summary>
    /// The shipped aggregate defaults to dark, so a database whose theme row is missing still
    /// renders the site the way the owner asked for.
    /// </summary>
    [Fact]
    public void ShippedSiteSettingsDefaultToDarkMode()
    {
        // Arrange, Act
        var settings = new SiteSettings();

        // Assert
        Assert.True(settings.IsDarkModeDefault);
    }

    /// <summary>
    /// A first-time visitor — nothing in LocalStorage — inherits the administrator's dark default.
    /// </summary>
    [Fact]
    public async Task FirstTimeVisitorInheritsTheDarkSiteDefault()
    {
        // Arrange
        SiteDefaultIs(true);
        VisitorStored(null);

        // Act
        var isDark = await service.GetDarkModeAsync();

        // Assert
        Assert.True(isDark);
    }

    /// <summary>
    /// A visitor who explicitly chose light keeps light, even though the site default is dark —
    /// a stored <c>false</c> is a real choice, not an absent key.
    /// </summary>
    [Fact]
    public async Task ExplicitVisitorLightChoiceSurvivesTheDarkSiteDefault()
    {
        // Arrange
        SiteDefaultIs(true);
        VisitorStored(false);

        // Act
        var isDark = await service.GetDarkModeAsync();

        // Assert
        Assert.False(isDark);
    }

    /// <summary>
    /// A visitor who chose dark keeps dark even when an administrator switches the site default
    /// back to light, so the toggle stays a per-browser override in both directions.
    /// </summary>
    [Fact]
    public async Task ExplicitVisitorDarkChoiceSurvivesALightSiteDefault()
    {
        // Arrange
        SiteDefaultIs(false);
        VisitorStored(true);

        // Act
        var isDark = await service.GetDarkModeAsync();

        // Assert
        Assert.True(isDark);
    }

    /// <summary>
    /// An administrator who switches the site default to light is obeyed for visitors who have
    /// expressed no preference — removing the light default did not remove the light option.
    /// </summary>
    [Fact]
    public async Task AdminLightDefaultStillReachesFreshVisitors()
    {
        // Arrange
        SiteDefaultIs(false);
        VisitorStored(null);

        // Act
        var isDark = await service.GetDarkModeAsync();

        // Assert
        Assert.False(isDark);
    }

    /// <summary>
    /// A settings read that throws falls back to dark, so an outage leaves the site looking the
    /// way it is meant to look instead of repainting every anonymous visitor light.
    /// </summary>
    [Fact]
    public async Task SettingsFailureFallsBackToDark()
    {
        // Arrange
        siteSettings.GetSettingsAsync().ThrowsAsync(new InvalidOperationException("settings down"));

        // Act
        var isDark = await service.GetSiteDefaultDarkModeAsync();

        // Assert
        Assert.True(isDark);
    }
}
