using BlogEngine.Storage;
using Microsoft.Extensions.Configuration;

namespace TechieBlog.Tests.Media;

/// <summary>
/// REQ-FN-025 — the single directory uploaded media is written to and served from.
/// </summary>
/// <remarks>
/// The defect this guards against is invisible until a redeploy: in a container <c>wwwroot</c> lives
/// inside the image, so images an editor uploaded are destroyed the next time the image is pulled.
/// The deployment contract moves them to a mounted host path
/// (<c>/srv/data/techieblog/uploads</c> → <c>/app/uploads</c>). The subtle part is that there are
/// TWO paths — <c>BlogImageService</c> composes <c>uploads/{category}/{file}</c>, so the storage root
/// is the PARENT of the served folder — and the pair must be derived from one another or the writer
/// and the reader silently disagree. These tests pin that relationship.
/// </remarks>
public class UploadsLocationTests
{
    private static readonly string ContentRoot =
        Path.Combine(Path.GetTempPath(), "techieblog-content-root");

    private static readonly string WebRoot = Path.Combine(ContentRoot, "wwwroot");

    /// <summary>
    /// Builds an in-memory configuration carrying the uploads path, or nothing at all.
    /// </summary>
    /// <param name="uploadsPath">Value for <c>Uploads:Path</c>, or null to omit it.</param>
    /// <returns>A configuration root carrying only the supplied value.</returns>
    private static IConfiguration BuildConfiguration(string? uploadsPath)
    {
        var values = new Dictionary<string, string?>();

        if (uploadsPath != null)
        {
            values[UploadsLocation.PathConfigurationKey] = uploadsPath;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Nothing configured: uploads stay under the web root, which is the clone-and-run behaviour a
    /// developer with no deployment settings depends on.
    /// </summary>
    [Fact]
    public void UnconfiguredFallsBackToTheWebRoot()
    {
        var location = UploadsLocation.Resolve(BuildConfiguration(null), WebRoot, ContentRoot);

        Assert.False(location.IsConfigured);
        Assert.Equal(Path.GetFullPath(WebRoot), location.StorageRootPath);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(WebRoot), UploadsLocation.FolderName),
            location.UploadsRootPath);
    }

    /// <summary>
    /// A host with no web root — a console head or a test host — still resolves, using the
    /// conventional <c>wwwroot</c> beneath the content root.
    /// </summary>
    /// <param name="webRootPath">The absent web root value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingWebRootFallsBackToContentRootWwwroot(string? webRootPath)
    {
        var location = UploadsLocation.Resolve(BuildConfiguration(null), webRootPath, ContentRoot);

        Assert.Equal(Path.GetFullPath(Path.Combine(ContentRoot, "wwwroot")), location.StorageRootPath);
    }

    /// <summary>
    /// The deployment contract exactly: <c>/app/uploads</c> is served verbatim and the storage root
    /// is its parent, so a write of <c>uploads/blog/x.jpg</c> lands in the mounted volume.
    /// </summary>
    [Fact]
    public void ConfiguredUploadsPathIsServedVerbatimWithItsParentAsStorageRoot()
    {
        var configured = Path.Combine(Path.DirectorySeparatorChar.ToString(), "app", "uploads");
        var location = UploadsLocation.Resolve(BuildConfiguration(configured), WebRoot, ContentRoot);

        Assert.True(location.IsConfigured);
        Assert.Equal(Path.GetFullPath(configured), location.UploadsRootPath);
        Assert.Equal(
            Path.GetDirectoryName(Path.GetFullPath(configured)), location.StorageRootPath);
    }

    /// <summary>
    /// The invariant that makes the writer and the reader agree: the served directory is ALWAYS the
    /// storage root plus the upload folder name, whatever was configured.
    /// </summary>
    /// <param name="configuredPath">The configured uploads path under test.</param>
    [Theory]
    [InlineData("/app/uploads")]
    [InlineData("/srv/data/techieblog/uploads")]
    [InlineData("/srv/media")]
    [InlineData("")]
    public void UploadsRootIsAlwaysTheStorageRootPlusTheFolderName(string configuredPath)
    {
        var location = UploadsLocation.Resolve(
            BuildConfiguration(configuredPath.Length == 0 ? null : configuredPath),
            WebRoot,
            ContentRoot);

        Assert.Equal(
            Path.Combine(location.StorageRootPath, UploadsLocation.FolderName),
            location.UploadsRootPath);
    }

    /// <summary>
    /// A trailing separator is not a different directory, and must not turn the leaf into an empty
    /// segment that stops matching the upload folder name.
    /// </summary>
    [Fact]
    public void TrailingSeparatorIsIgnored()
    {
        var withSeparator = Path.Combine(Path.DirectorySeparatorChar.ToString(), "app", "uploads")
            + Path.DirectorySeparatorChar;
        var location = UploadsLocation.Resolve(
            BuildConfiguration(withSeparator), WebRoot, ContentRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "app", "uploads")),
            location.UploadsRootPath);
    }

    /// <summary>
    /// A relative configured path resolves against the content root, not the working directory —
    /// the same rule the log path follows, and for the same reason.
    /// </summary>
    [Fact]
    public void RelativeConfiguredPathResolvesAgainstTheContentRoot()
    {
        var location = UploadsLocation.Resolve(
            BuildConfiguration("media/uploads"), WebRoot, ContentRoot);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(ContentRoot, "media", "uploads")),
            location.UploadsRootPath);
    }

    /// <summary>
    /// The URL prefix and the folder name are one definition, because the stored public URL
    /// (<c>/uploads/blog/x.jpg</c>) and the disk layout have to agree character for character.
    /// </summary>
    [Fact]
    public void RequestPathMatchesTheFolderName()
    {
        Assert.Equal("uploads", UploadsLocation.FolderName);
        Assert.Equal("/uploads", UploadsLocation.RequestPath);
    }
}
