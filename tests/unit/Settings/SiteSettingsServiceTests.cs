using BlogEngine.Services;
using BlogModels;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Round-trip and caching tests for <see cref="SiteSettingsService"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers REQ-FN-040 (site settings persist and take effect without a
/// restart), REQ-UI-026 (every section is written, not just pagination) and REQ-UI-032 (the theme
/// is a persisted SITE setting rather than a browser preference).</para>
///
/// <para><b>Code Flow:</b> Each test builds the service over
/// <see cref="FakeSiteSettingRepo"/>, saves an aggregate, then reads it back through a cold cache
/// so the assertion exercises the real projection in both directions.</para>
///
/// <para><b>Dependencies:</b> <see cref="FakeSiteSettingRepo"/>, xUnit.</para>
///
/// <para><b>Usage:</b> No database required.</para>
/// </remarks>
public class SiteSettingsServiceTests
{
    /// <summary>
    /// Builds a service over a fresh in-memory repository.
    /// </summary>
    /// <param name="repo">Receives the repository the service was built over.</param>
    /// <returns>The service under test.</returns>
    private static SiteSettingsService CreateService(out FakeSiteSettingRepo repo)
    {
        repo = new FakeSiteSettingRepo();
        return new SiteSettingsService(repo, NullLogger<SiteSettingsService>.Instance);
    }

    /// <summary>
    /// Builds a fully populated aggregate whose every field differs from the built-in default,
    /// so a value surviving the round trip cannot be a default in disguise.
    /// </summary>
    /// <returns>The aggregate to save.</returns>
    private static SiteSettings CreateFullSettings() => new()
    {
        SiteTitle = "Round Trip Blog",
        SiteTagline = "Every section persists",
        AdminEmail = "owner@techieblog.test",
        PostsPerPage = 7,
        PaginationWordCount = 333,
        AreCommentsAllowed = false,
        AreCommentsModerated = false,
        IsRegistrationAllowed = false,
        SiteTheme = "developer",
        IsDarkModeDefault = true,
        MetaDescription = "A description that is not the default",
        MetaKeywords = "round, trip, keywords",
        TwitterUrl = "https://x.com/roundtrip",
        LinkedInUrl = "https://linkedin.com/in/roundtrip",
        GitHubUrl = "https://github.com/roundtrip",
        Smtp = new SmtpSettings
        {
            Host = "smtp.roundtrip.test",
            Port = 2525,
            IsSslEnabled = false,
            UserName = "mailer",
            Password = "MailerSecret1",
            FromAddress = "noreply@roundtrip.test",
            FromName = "Round Trip"
        },
        Storage = new StorageSettings
        {
            ProviderName = StorageProviderNames.Network,
            LocalRootPath = "local/root",
            NetworkRootPath = "//server/share",
            CloudServiceUrl = "https://s3.roundtrip.test",
            CloudContainerName = "roundtrip-media",
            CloudAccessKey = "CloudSecret1",
            PublicBaseUrl = "/media"
        }
    };

    /// <summary>
    /// Saving a fully populated aggregate and reading it back through a cold cache returns every
    /// value unchanged — general, blog, theme, SEO, social, SMTP and storage alike. This is the
    /// regression guard for the defect where only the pagination word count was persisted.
    /// </summary>
    [Fact]
    public async Task SaveThenLoadReturnsEverySection()
    {
        var service = CreateService(out var repo);
        var settings = CreateFullSettings();

        var saveResult = await service.SaveSettingsAsync(settings);
        service.InvalidateCache();
        var reloaded = await service.GetSettingsAsync();

        Assert.True(saveResult.IsSuccess);
        Assert.Equal("Round Trip Blog", reloaded.SiteTitle);
        Assert.Equal("Every section persists", reloaded.SiteTagline);
        Assert.Equal("owner@techieblog.test", reloaded.AdminEmail);
        Assert.Equal(7, reloaded.PostsPerPage);
        Assert.Equal(333, reloaded.PaginationWordCount);
        Assert.False(reloaded.AreCommentsAllowed);
        Assert.False(reloaded.AreCommentsModerated);
        Assert.False(reloaded.IsRegistrationAllowed);
        Assert.Equal("developer", reloaded.SiteTheme);
        Assert.True(reloaded.IsDarkModeDefault);
        Assert.Equal("A description that is not the default", reloaded.MetaDescription);
        Assert.Equal("round, trip, keywords", reloaded.MetaKeywords);
        Assert.Equal("https://x.com/roundtrip", reloaded.TwitterUrl);
        Assert.Equal("https://linkedin.com/in/roundtrip", reloaded.LinkedInUrl);
        Assert.Equal("https://github.com/roundtrip", reloaded.GitHubUrl);
        Assert.Equal("smtp.roundtrip.test", reloaded.Smtp.Host);
        Assert.Equal(2525, reloaded.Smtp.Port);
        Assert.False(reloaded.Smtp.IsSslEnabled);
        Assert.Equal(StorageProviderNames.Network, reloaded.Storage.ProviderName);
        Assert.Equal("/media", reloaded.Storage.PublicBaseUrl);
        Assert.NotEmpty(repo.Rows);
    }

    /// <summary>
    /// The admin-selected site theme survives the round trip, which is what makes it a site-wide
    /// setting rather than the per-browser preference it used to be (REQ-UI-032, BRD-68).
    /// </summary>
    [Fact]
    public async Task SiteThemeRoundTripsAsASiteSetting()
    {
        var service = CreateService(out var repo);

        await service.SaveSettingsAsync(new SiteSettings { SiteTheme = "minimal", IsDarkModeDefault = true });
        service.InvalidateCache();
        var reloaded = await service.GetSettingsAsync();

        Assert.Equal("minimal", reloaded.SiteTheme);
        Assert.True(reloaded.IsDarkModeDefault);
        Assert.Contains(repo.Rows, row => row.SettingKey == SiteSettingKeys.SiteTheme && row.SettingValue == "minimal");
    }

    /// <summary>
    /// Every persisted row is tagged with the group its key belongs to, so the admin screen can
    /// render sections from the data rather than from a hard-coded key list.
    /// </summary>
    [Fact]
    public async Task SaveTagsEveryRowWithItsGroup()
    {
        var service = CreateService(out var repo);

        await service.SaveSettingsAsync(CreateFullSettings());

        Assert.Equal(SiteSettingKeys.Groups.General,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.SiteTitle).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Blog,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.PostsPerPage).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Theme,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.SiteTheme).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Seo,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.MetaDescription).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Social,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.GitHubUrl).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Smtp,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.SmtpHost).SettingGroup);
        Assert.Equal(SiteSettingKeys.Groups.Storage,
            repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.StoragePublicBaseUrl).SettingGroup);
    }

    /// <summary>
    /// Credentials are encrypted at rest — the stored SMTP password is not the plain text — yet
    /// the service hands the plain value back to the admin screen.
    /// </summary>
    [Fact]
    public async Task SecretValuesAreEncryptedAtRestButReadBackPlain()
    {
        var service = CreateService(out var repo);

        await service.SaveSettingsAsync(CreateFullSettings());
        service.InvalidateCache();
        var reloaded = await service.GetSettingsAsync();

        var storedRow = repo.Rows.Single(r => r.SettingKey == SiteSettingKeys.SmtpPassword);
        Assert.True(storedRow.IsSecret);
        Assert.NotEqual("MailerSecret1", storedRow.SettingValue);
        Assert.Equal("MailerSecret1", reloaded.Smtp.Password);
        Assert.Equal("CloudSecret1", reloaded.Storage.CloudAccessKey);
    }

    /// <summary>
    /// Re-saving does not accumulate duplicate rows: the key is the natural key, so a second save
    /// of the same settings updates the existing rows in place.
    /// </summary>
    [Fact]
    public async Task RepeatedSavesUpdateRowsRatherThanDuplicateThem()
    {
        var service = CreateService(out var repo);

        await service.SaveSettingsAsync(CreateFullSettings());
        var firstCount = repo.Rows.Count;
        await service.SaveSettingsAsync(CreateFullSettings());

        Assert.Equal(firstCount, repo.Rows.Count);
        Assert.Equal(repo.Rows.Select(r => r.SettingKey).Distinct().Count(), repo.Rows.Count);
    }

    /// <summary>
    /// A save invalidates the cache, so the very next read sees the new values without a restart —
    /// the REQ-NFR-018 coherence requirement. A stale cache after a save would be a defect.
    /// </summary>
    [Fact]
    public async Task SaveInvalidatesTheCache()
    {
        var service = CreateService(out var repo);
        await service.SaveSettingsAsync(new SiteSettings { SiteTitle = "First" });
        var readsAfterFirstSave = repo.ReadCount;

        var cached = await service.GetSettingsAsync();
        var readsAfterCachedRead = repo.ReadCount;

        await service.SaveSettingsAsync(new SiteSettings { SiteTitle = "Second" });
        var afterSecondSave = await service.GetSettingsAsync();

        Assert.Equal("First", cached.SiteTitle);
        Assert.Equal(readsAfterFirstSave, readsAfterCachedRead);
        Assert.Equal("Second", afterSecondSave.SiteTitle);
    }

    /// <summary>
    /// Writing a single key through SetValueAsync also refreshes the cached aggregate, so a
    /// targeted write is as coherent as a full save.
    /// </summary>
    [Fact]
    public async Task SetValueRefreshesTheCachedAggregate()
    {
        var service = CreateService(out _);
        await service.SaveSettingsAsync(new SiteSettings { SiteTitle = "Before" });

        var result = await service.SetValueAsync(SiteSettingKeys.SiteTitle, "After", SiteSettingKeys.Groups.General);
        var reloaded = await service.GetSettingsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("After", reloaded.SiteTitle);
    }

    /// <summary>
    /// An unreachable database must not take the site down: the read falls back to the built-in
    /// defaults instead of throwing.
    /// </summary>
    [Fact]
    public async Task ReadFailureFallsBackToDefaults()
    {
        var service = CreateService(out var repo);
        repo.FailNextRead = true;

        var settings = await service.GetSettingsAsync();

        Assert.Equal("TechieBlog", settings.SiteTitle);
        Assert.Equal("trblaze-modern", settings.SiteTheme);
    }

    /// <summary>
    /// A failed write is reported as a failure Result rather than a thrown exception, so the admin
    /// screen can show the reason instead of a blank error boundary.
    /// </summary>
    [Fact]
    public async Task WriteFailureReturnsFailureResult()
    {
        var service = CreateService(out var repo);
        repo.FailNextWrite = true;

        var result = await service.SaveSettingsAsync(new SiteSettings { SiteTitle = "Doomed" });

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /// <summary>
    /// Validation rejects the values that would visibly break the public site before any write
    /// occurs, naming the offending field.
    /// </summary>
    [Fact]
    public void ValidationRejectsUnusableValues()
    {
        Assert.True(SiteSettingsService.ValidateSettings(new SiteSettings { SiteTitle = "  " }).IsFailure);
        Assert.True(SiteSettingsService.ValidateSettings(new SiteSettings { PostsPerPage = 0 }).IsFailure);
        Assert.True(SiteSettingsService.ValidateSettings(new SiteSettings { PaginationWordCount = -1 }).IsFailure);
        Assert.True(SiteSettingsService.ValidateSettings(
            new SiteSettings { Smtp = new SmtpSettings { Port = 70000 } }).IsFailure);
        Assert.True(SiteSettingsService.ValidateSettings(new SiteSettings()).IsSuccess);
    }

    /// <summary>
    /// An invalid aggregate never reaches the repository — validation is a gate, not a warning.
    /// </summary>
    [Fact]
    public async Task InvalidSettingsAreNeverWritten()
    {
        var service = CreateService(out var repo);

        var result = await service.SaveSettingsAsync(new SiteSettings { SiteTitle = string.Empty });

        Assert.True(result.IsFailure);
        Assert.Empty(repo.Rows);
    }

    /// <summary>
    /// Saving raises SettingsChanged with the freshly loaded aggregate, which is how a live circuit
    /// learns that the configuration changed without a restart.
    /// </summary>
    [Fact]
    public async Task SaveRaisesSettingsChangedWithTheEffectiveValues()
    {
        var service = CreateService(out _);
        SiteSettings? announced = null;
        service.SettingsChanged += (_, updated) => announced = updated;

        await service.SaveSettingsAsync(new SiteSettings { SiteTitle = "Announced", SiteTheme = "minimal" });

        Assert.NotNull(announced);
        Assert.Equal("Announced", announced!.SiteTitle);
        Assert.Equal("minimal", announced.SiteTheme);
    }

    /// <summary>
    /// GetValueAsync reads a single key straight through, decrypting a secret and substituting the
    /// caller's default for a key that was never written.
    /// </summary>
    [Fact]
    public async Task GetValueReadsOneKeyAndDefaultsWhenAbsent()
    {
        var service = CreateService(out _);
        await service.SaveSettingsAsync(CreateFullSettings());

        var theme = await service.GetValueAsync(SiteSettingKeys.SiteTheme, "fallback");
        var password = await service.GetValueAsync(SiteSettingKeys.SmtpPassword, "fallback");
        var missing = await service.GetValueAsync("Nothing.Here", "fallback");

        Assert.Equal("developer", theme);
        Assert.Equal("MailerSecret1", password);
        Assert.Equal("fallback", missing);
    }

    /// <summary>
    /// The typed SMTP and storage accessors project from the same cached aggregate, so a caller
    /// that only needs mail settings sees exactly what the admin saved.
    /// </summary>
    [Fact]
    public async Task TypedAccessorsProjectFromTheSavedAggregate()
    {
        var service = CreateService(out _);
        await service.SaveSettingsAsync(CreateFullSettings());
        service.InvalidateCache();

        var smtp = await service.GetSmtpSettingsAsync();
        var storage = await service.GetStorageSettingsAsync();

        Assert.Equal("smtp.roundtrip.test", smtp.Host);
        Assert.Equal(2525, smtp.Port);
        Assert.Equal("Round Trip", smtp.FromName);
        Assert.Equal(StorageProviderNames.Network, storage.ProviderName);
        Assert.Equal("//server/share", storage.NetworkRootPath);
    }
}
