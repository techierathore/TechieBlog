using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="ISiteSettingsService"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the comment tests drive the <c>Blog.AreCommentsModerated</c>
/// setting [BRD-38] without a settings table, and lets a test prove the fail-safe by making
/// the read throw.</para>
/// <para><b>Code Flow:</b> <see cref="GetSettingsAsync"/> returns <see cref="Settings"/>, or
/// throws when <see cref="ThrowOnRead"/> is set.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Assign <c>Settings.AreCommentsModerated</c> before exercising the
/// service under test.</para>
/// </remarks>
public class FakeSiteSettingsService : ISiteSettingsService
{
    /// <inheritdoc />
    public event EventHandler<SiteSettings>? SettingsChanged;

    /// <summary>
    /// Gets or sets the settings every read returns. Moderated by default, as the product is.
    /// </summary>
    public SiteSettings Settings { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether a read should throw, to prove the fail-safe.
    /// </summary>
    public bool ThrowOnRead { get; set; }

    /// <inheritdoc />
    public Task<SiteSettings> GetSettingsAsync()
    {
        return ThrowOnRead
            ? throw new InvalidOperationException("The settings store is unavailable.")
            : Task.FromResult(Settings);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Copies like the real service does (REQ-FN-061), so a test binding to the result cannot
    /// mutate this double's stored aggregate and quietly pass where production would leak.
    /// </remarks>
    public Task<SiteSettings> GetEditableSettingsAsync()
    {
        return ThrowOnRead
            ? throw new InvalidOperationException("The settings store is unavailable.")
            : Task.FromResult(Settings.Clone());
    }

    /// <inheritdoc />
    public Task<Result<SiteSettings>> SaveSettingsAsync(SiteSettings settings)
    {
        Settings = settings;
        SettingsChanged?.Invoke(this, settings);
        return Task.FromResult(Result<SiteSettings>.Success(settings.Clone()));
    }

    /// <inheritdoc />
    public Task<string> GetValueAsync(string settingKey, string defaultValue) => Task.FromResult(defaultValue);

    /// <inheritdoc />
    public Task<Result> SetValueAsync(string settingKey, string settingValue, string settingGroup)
    {
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<SmtpSettings> GetSmtpSettingsAsync() => Task.FromResult(new SmtpSettings());

    /// <inheritdoc />
    public Task<StorageSettings> GetStorageSettingsAsync() => Task.FromResult(new StorageSettings());

    /// <inheritdoc />
    public void InvalidateCache()
    {
    }
}
