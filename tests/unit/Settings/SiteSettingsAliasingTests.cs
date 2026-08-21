using System.Reflection;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Proves that editing site settings cannot leak into the process-wide cache (REQ-FN-061).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The admin Settings screen used to bind its form directly to the aggregate
/// <c>GetSettingsAsync</c> returns. That aggregate is the singleton service's cached instance, read
/// by every circuit and by anonymous public requests, so an unsaved edit — a previewed theme, a
/// half-typed site title, an SMTP host — became the live site configuration for every user the
/// instant it was typed, with nothing written to the database and no way to undo it short of a host
/// restart. These tests pin the boundary that closed it.</para>
///
/// <para><b>Code Flow:</b> Each test builds a real <see cref="SiteSettingsService"/> over
/// <see cref="FakeSiteSettingRepo"/>, saves a known aggregate so the cache is warm, then mutates
/// whatever the service handed out and re-reads the effective settings through an independent call
/// — which is the in-process equivalent of the second browser connection the requirement's
/// acceptance asks for.</para>
///
/// <para><b>Dependencies:</b> <see cref="FakeSiteSettingRepo"/>, xUnit.</para>
///
/// <para><b>Usage:</b> No database required. A failure here means an editing surface can
/// reconfigure the site for all users without saving.</para>
/// </remarks>
public class SiteSettingsAliasingTests
{
    private const string SeedTheme = "trblaze-modern";
    private const string PreviewedTheme = "minimal";

    /// <summary>
    /// Builds a service over a fresh in-memory repository, with a known aggregate already saved.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Saving through the service (rather than seeding the repository)
    /// leaves the cache warm and holding a real projection, which is the state every one of these
    /// tests is about.</para>
    /// <para><b>Side Effects:</b> Writes the seed rows into the fake repository.</para>
    /// </remarks>
    /// <returns>The service under test.</returns>
    private static async Task<SiteSettingsService> CreateWarmServiceAsync()
    {
        var service = new SiteSettingsService(
            new FakeSiteSettingRepo(),
            NullLogger<SiteSettingsService>.Instance);

        await service.SaveSettingsAsync(new SiteSettings
        {
            SiteTitle = "Seeded Title",
            SiteTheme = SeedTheme,
            PostsPerPage = 10,
            Smtp = new SmtpSettings { Host = "smtp.seed.test", Port = 587, Password = "seed-secret" },
            Storage = new StorageSettings { ProviderName = "Local", LocalRootPath = "/srv/seed" }
        });

        return service;
    }

    /// <summary>
    /// An unsaved theme change on the editable copy leaves the effective site theme untouched.
    /// </summary>
    /// <remarks>
    /// This is REQ-FN-061's headline scenario: an administrator opens the Theme tab, picks a theme
    /// to preview it, and navigates away without pressing Save. The next request — by anyone,
    /// including an anonymous visitor — must still be served the saved theme.
    /// </remarks>
    [Fact]
    public async Task UnsavedThemeChangeDoesNotAffectTheEffectiveSettings()
    {
        var service = await CreateWarmServiceAsync();

        var editable = await service.GetEditableSettingsAsync();
        editable.SiteTheme = PreviewedTheme;

        var effective = await service.GetSettingsAsync();
        Assert.Equal(SeedTheme, effective.SiteTheme);
    }

    /// <summary>
    /// An unsaved edit to any scalar field on the form leaves the effective settings untouched.
    /// </summary>
    /// <remarks>
    /// The requirement's acceptance asks for the whole pattern, not the theme field alone: the same
    /// page binds the title, pagination and the moderation switches to the same object, and each was
    /// leaking by exactly the same mechanism.
    /// </remarks>
    [Fact]
    public async Task UnsavedScalarEditsDoNotAffectTheEffectiveSettings()
    {
        var service = await CreateWarmServiceAsync();

        var editable = await service.GetEditableSettingsAsync();
        editable.SiteTitle = "Half-typed titl";
        editable.PostsPerPage = 999;
        editable.AreCommentsModerated = false;

        var effective = await service.GetSettingsAsync();
        Assert.Equal("Seeded Title", effective.SiteTitle);
        Assert.Equal(10, effective.PostsPerPage);
        Assert.True(effective.AreCommentsModerated);
    }

    /// <summary>
    /// An unsaved edit to the nested SMTP or storage sections leaves the effective settings untouched.
    /// </summary>
    /// <remarks>
    /// The sharp edge of the fix. A member-wise copy of the aggregate would pass every scalar test
    /// above and still share the two nested objects, so the Mail and Storage tabs — which carry the
    /// live SMTP password and cloud access key — would go on leaking. This test fails against a
    /// shallow copy and is the reason <c>SiteSettings.Clone</c> clones its children.
    /// </remarks>
    [Fact]
    public async Task UnsavedNestedEditsDoNotAffectTheEffectiveSettings()
    {
        var service = await CreateWarmServiceAsync();

        var editable = await service.GetEditableSettingsAsync();
        editable.Smtp.Host = "smtp.attacker.test";
        editable.Smtp.Password = "typed-then-abandoned";
        editable.Storage.LocalRootPath = "/tmp/wrong";

        var effective = await service.GetSettingsAsync();
        Assert.Equal("smtp.seed.test", effective.Smtp.Host);
        Assert.Equal("seed-secret", effective.Smtp.Password);
        Assert.Equal("/srv/seed", effective.Storage.LocalRootPath);
    }

    /// <summary>
    /// Abandoning an edited copy writes nothing to the database.
    /// </summary>
    /// <remarks>
    /// The original defect was invisible precisely because the database stayed correct while the
    /// served value diverged. This asserts the other half of that: the fix must not have swung the
    /// other way and started persisting unsaved edits.
    /// </remarks>
    [Fact]
    public async Task AbandoningAnEditedCopyWritesNothing()
    {
        var repo = new FakeSiteSettingRepo();
        var service = new SiteSettingsService(repo, NullLogger<SiteSettingsService>.Instance);
        await service.SaveSettingsAsync(new SiteSettings { SiteTheme = SeedTheme });

        var storedThemeBefore = repo.Rows.Single(row => row.SettingKey == SiteSettingKeys.SiteTheme).SettingValue;

        var editable = await service.GetEditableSettingsAsync();
        editable.SiteTheme = PreviewedTheme;

        var storedThemeAfter = repo.Rows.Single(row => row.SettingKey == SiteSettingKeys.SiteTheme).SettingValue;
        Assert.Equal(storedThemeBefore, storedThemeAfter);
        Assert.Equal(SeedTheme, storedThemeAfter);
    }

    /// <summary>
    /// The aggregate returned by a successful save is detached from the cache too.
    /// </summary>
    /// <remarks>
    /// The Settings screen re-binds its form to the save result so the user sees persisted truth.
    /// Were that result the cached instance, the leak would return the moment an administrator saved
    /// once and then kept editing — a narrower hole than the original, and a far harder one to spot.
    /// </remarks>
    [Fact]
    public async Task SaveResultIsDetachedFromTheCache()
    {
        var service = await CreateWarmServiceAsync();

        var saved = await service.SaveSettingsAsync(new SiteSettings
        {
            SiteTitle = "Saved Title",
            SiteTheme = SeedTheme
        });
        Assert.True(saved.IsSuccess);

        saved.Data.SiteTheme = PreviewedTheme;

        var effective = await service.GetSettingsAsync();
        Assert.Equal(SeedTheme, effective.SiteTheme);
    }

    /// <summary>
    /// The SMTP and storage projections are detached from the cache.
    /// </summary>
    /// <remarks>
    /// Both are handed to callers that legitimately normalise what they receive before use. Neither
    /// should be able to reconfigure the site by doing so.
    /// </remarks>
    [Fact]
    public async Task SubAggregateProjectionsAreDetachedFromTheCache()
    {
        var service = await CreateWarmServiceAsync();

        (await service.GetSmtpSettingsAsync()).Host = "smtp.mutated.test";
        (await service.GetStorageSettingsAsync()).LocalRootPath = "/tmp/mutated";

        var effective = await service.GetSettingsAsync();
        Assert.Equal("smtp.seed.test", effective.Smtp.Host);
        Assert.Equal("/srv/seed", effective.Storage.LocalRootPath);
    }

    /// <summary>
    /// Editing a copy and then saving it still takes effect, so the fix did not break saving.
    /// </summary>
    /// <remarks>
    /// The counterweight to every test above: detaching the form's model must not detach the Save
    /// button. Without this a copy that was never wired to the save path would pass the whole suite.
    /// </remarks>
    [Fact]
    public async Task SavingAnEditedCopyStillTakesEffect()
    {
        var service = await CreateWarmServiceAsync();

        var editable = await service.GetEditableSettingsAsync();
        editable.SiteTheme = PreviewedTheme;
        editable.SiteTitle = "Committed Title";
        var saved = await service.SaveSettingsAsync(editable);

        Assert.True(saved.IsSuccess);

        var effective = await service.GetSettingsAsync();
        Assert.Equal(PreviewedTheme, effective.SiteTheme);
        Assert.Equal("Committed Title", effective.SiteTitle);
    }

    /// <summary>
    /// The editable copy carries every value of the effective settings.
    /// </summary>
    /// <remarks>
    /// A copy is only safe if it is complete. This walks every public property by reflection rather
    /// than naming them, so a property added to <see cref="SiteSettings"/> and forgotten in
    /// <see cref="SiteSettings.Clone"/> fails the build instead of being silently blanked on the
    /// next save — which would turn this fix into a data-loss defect.
    /// </remarks>
    [Fact]
    public async Task EditableCopyCarriesEveryPropertyOfTheEffectiveSettings()
    {
        var service = await CreateWarmServiceAsync();

        var effective = await service.GetSettingsAsync();
        var editable = await service.GetEditableSettingsAsync();

        AssertEveryPropertyEqual(effective, editable);
        AssertEveryPropertyEqual(effective.Smtp, editable.Smtp);
        AssertEveryPropertyEqual(effective.Storage, editable.Storage);
    }

    /// <summary>
    /// Cloning an aggregate whose every field is non-default preserves all of them.
    /// </summary>
    /// <remarks>
    /// The reflection walk above compares two copies of a mostly seeded aggregate, so a property the
    /// clone dropped could still match if both sides held the built-in default. This one sets every
    /// scalar away from its default first, which removes that escape route.
    /// </remarks>
    [Fact]
    public void CloneOfAFullyPopulatedAggregatePreservesEveryProperty()
    {
        var original = new SiteSettings
        {
            SiteTitle = "Not The Default",
            SiteTagline = "Nor this",
            AdminEmail = "admin@clone.test",
            PostsPerPage = 42,
            PaginationWordCount = 4242,
            AreCommentsAllowed = false,
            AreCommentsModerated = false,
            IsRegistrationAllowed = false,
            SiteTheme = "developer",
            IsDarkModeDefault = false,
            MetaDescription = "meta description",
            MetaKeywords = "meta, keywords",
            TwitterUrl = "https://x.com/clone",
            LinkedInUrl = "https://linkedin.com/in/clone",
            GitHubUrl = "https://github.com/clone",
            UpdatedOn = new DateTime(2026, 8, 14, 9, 30, 0, DateTimeKind.Utc),
            Smtp = new SmtpSettings
            {
                Host = "smtp.clone.test",
                Port = 2525,
                IsSslEnabled = false,
                UserName = "clone-user",
                Password = "clone-secret",
                FromAddress = "from@clone.test",
                FromName = "Clone Sender"
            },
            Storage = new StorageSettings
            {
                ProviderName = "Cloud",
                LocalRootPath = "/srv/clone",
                NetworkRootPath = @"\\nas\clone",
                CloudServiceUrl = "https://objects.clone.test",
                CloudContainerName = "clone-container",
                CloudAccessKey = "clone-access-key",
                PublicBaseUrl = "https://cdn.clone.test"
            }
        };

        var copy = original.Clone();

        AssertEveryPropertyEqual(original, copy);
        AssertEveryPropertyEqual(original.Smtp, copy.Smtp);
        AssertEveryPropertyEqual(original.Storage, copy.Storage);
        Assert.NotSame(original.Smtp, copy.Smtp);
        Assert.NotSame(original.Storage, copy.Storage);
    }

    /// <summary>
    /// Asserts two instances of the same type agree on every readable public property.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Nested aggregates are compared by their own properties elsewhere
    /// in the calling test, so they are skipped here — comparing them by reference would defeat the
    /// point, and comparing by value would duplicate the caller's own assertion.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="expected">The instance holding the values that must survive.</param>
    /// <param name="actual">The instance under test.</param>
    private static void AssertEveryPropertyEqual(object expected, object actual)
    {
        var properties = expected.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .Where(property => property.PropertyType != typeof(SmtpSettings))
            .Where(property => property.PropertyType != typeof(StorageSettings));

        foreach (var property in properties)
        {
            Assert.Equal(property.GetValue(expected), property.GetValue(actual));
        }
    }
}
