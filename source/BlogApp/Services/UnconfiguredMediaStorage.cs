using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogApp.Services;

/// <summary>
/// Refuses uploads on a desktop head that has no media location configured, instead of writing them
/// to the operator's own machine (REQ-FN-062, UAT-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BlogApp always talks to the SITE's database (BRD-96: no local database, no
/// sync). An image written to this laptop therefore records a row in the shared database whose
/// <c>/uploads/{category}/{file}</c> URL points at a file that exists on exactly one machine — and
/// the website answers 404 for it forever. Before this type, that is precisely what happened
/// whenever <see cref="MediaTransports.None"/> was selected: <see cref="DesktopFileStorageFactory"/>
/// handed back the engine's local provider, the upload "succeeded", and the broken reference was
/// saved with no warning anywhere.</para>
///
/// <para><b>Why refusing is the correct behaviour, not a regression.</b> The earlier design let the
/// unconfigured head fall through to the engine "so a head that has not opted in behaves precisely
/// as it did before". That behaviour was the defect: there is no configuration of this head for
/// which a local write produces a usable result, because the database it writes to is never local.
/// A refusal costs the operator one dialog; a silent success costs them a logo that can never
/// appear and gives them no way to find out why. This is the same judgement REQ-FN-062 already
/// made when it chose to refuse a local fixed drive "before creating anything" — that guard simply
/// never covered the <c>None</c> case, because <c>None</c> never reached a probe.</para>
///
/// <para><b>Reads stay harmless.</b> Only <see cref="SaveAsync"/> throws. Existence, delete and read
/// report "nothing here" so the media library, cleanup paths and migration can enumerate without
/// having to special-case an unconfigured head — the same contract those members already have for a
/// file that is genuinely absent.</para>
///
/// <para><b>Code Flow:</b> <c>BlogImageService.StoreAsync</c> →
/// <see cref="DesktopFileStorageFactory"/> → this type → <see cref="InvalidOperationException"/>
/// carrying <see cref="NotConfiguredMessage"/>, which <c>BlogImageService</c> lets through untouched
/// and <c>ImagePicker</c> renders in its upload dialog.</para>
///
/// <para><b>Dependencies:</b> None — it holds no connection and touches no disk.</para>
///
/// <para><b>Usage:</b> Returned by <see cref="DesktopFileStorageFactory"/> when
/// <c>ConnectionSettings.HasMediaLocation()</c> is false. Never registered directly.</para>
/// </remarks>
public class UnconfiguredMediaStorage : IFileStorage
{
    /// <summary>
    /// Message shown to the operator when an upload is attempted with no media location set.
    /// </summary>
    /// <remarks>
    /// Names the fix and the screen that applies it. Carries no path, host name or exception text,
    /// so it is safe to render directly (REQ-NFR-033).
    /// </remarks>
    public const string NotConfiguredMessage =
        "This app has no media storage configured, so the file would be saved on this computer only "
        + "and the website could never display it. Open Change connection and set up Media storage "
        + "(SFTP or a shared folder), then try again.";

    /// <inheritdoc />
    public string ProviderName => "Unconfigured";

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Always refuses. Writing here would produce a database row the
    /// website cannot serve, which is worse than not writing at all.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None — nothing is created, so there is nothing to clean up.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Always, carrying <see cref="NotConfiguredMessage"/>.</exception>
    public Task<FileStorageResult> SaveAsync(
        Stream content,
        string relativePath,
        string contentType,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(NotConfiguredMessage);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Nothing was ever stored here, so nothing is removed — the same
    /// answer this member gives for an absent file.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public Task<bool> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> No media location means no file is reachable from this head.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Null is the interface's own "not found", so callers need no
    /// special case for an unconfigured head.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        return Task.FromResult<Stream?>(null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the site-relative URL the website would use, unchanged.
    /// Existing rows still render correctly against a server that does hold the file; this type only
    /// prevents NEW unusable rows being created.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    public string GetPublicUrl(string relativePath)
    {
        return string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : "/" + relativePath.TrimStart('/');
    }
}
