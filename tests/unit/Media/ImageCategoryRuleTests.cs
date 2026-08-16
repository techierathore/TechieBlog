using System.Text.RegularExpressions;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Media;

/// <summary>
/// Unit tests pinning the per-category upload limits and proving the screen cannot advertise a
/// number the server will not honour (REQ-FN-025, BRD-45, BRD-46).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-025 was demoted on 2026-08-11 because the upload dialog stated two
/// contradictory ceilings for one upload — "Max 2MB" from the page's own table and "Max size: 10 MB"
/// from the dropzone component's untouched default. Both numbers rendered, neither came from the
/// service, and the larger one accepted files the service was certain to reject. The limits now live
/// once in <see cref="ImageCategoryRules"/>; these tests pin each of the seven values and assert the
/// three properties that make a repeat impossible to ship quietly.</para>
///
/// <para><b>What is checked:</b></para>
/// <list type="number">
///   <item>Each of the seven categories carries the size ceiling and format allow-list BRD-45
///     specifies, and renders it as one sentence — so a limit change is a deliberate act with a test
///     to update.</item>
///   <item>The value the service <i>advertises</i> through <c>GetCategoryRule</c> is the value it
///     <i>enforces</i> in <c>ValidateImageAsync</c>, category by category, at the exact byte.</item>
///   <item>The server rejects an oversize upload through <c>UploadImageAsync</c> even when nothing
///     validated on the client, and writes neither bytes nor a metadata row.</item>
///   <item>No upload surface re-types a limit: the media page and the picker carry no size literal
///     outside their comments.</item>
/// </list>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for the repository and storage seams, and the
/// repository layout for the markup scan. No database, no host, no container.</para>
/// </remarks>
public class ImageCategoryRuleTests
{
    /// <summary>
    /// One kilobyte, so the expectations below read as the limits are written down.
    /// </summary>
    private const long Kilobyte = 1024;

    /// <summary>
    /// One megabyte.
    /// </summary>
    private const long Megabyte = Kilobyte * 1024;

    /// <summary>
    /// A size literal — <c>2MB</c>, <c>500 KB</c> — that must not appear in an upload surface.
    /// </summary>
    private static readonly Regex SizeLiteral = new(@"\b\d+\s?(MB|KB)\b", RegexOptions.Compiled);

    /// <summary>
    /// A Razor comment block, stripped before the markup scan so this requirement's own explanatory
    /// notes are not mistaken for the defect they describe.
    /// </summary>
    private static readonly Regex RazorComment = new(
        @"@\*.*?\*@", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// The seven fixed categories with the ceiling, the allow-list and the accept filter each must
    /// carry (BRD-45, BRD-46).
    /// </summary>
    /// <returns>One case per category.</returns>
    public static TheoryData<string, long, string, string, string> ExpectedRules()
    {
        return new TheoryData<string, long, string, string, string>
        {
            { "profiles", 2 * Megabyte, "2 MB", "jpg, jpeg, png, webp", "image/jpeg,image/png,image/webp" },
            { "logos", 500 * Kilobyte, "500 KB", "jpg, jpeg, png, svg, webp", "image/jpeg,image/png,image/svg+xml,image/webp" },
            { "awards", 500 * Kilobyte, "500 KB", "jpg, jpeg, png, svg, webp", "image/jpeg,image/png,image/svg+xml,image/webp" },
            { "icons", 200 * Kilobyte, "200 KB", "png, svg, webp", "image/png,image/svg+xml,image/webp" },
            { "blog", 5 * Megabyte, "5 MB", "jpg, jpeg, png, gif, webp", "image/jpeg,image/png,image/gif,image/webp" },
            { "cv", 10 * Megabyte, "10 MB", "pdf", "application/pdf" },
            { "general", 5 * Megabyte, "5 MB", "jpg, jpeg, png, gif, webp", "image/jpeg,image/png,image/gif,image/webp" }
        };
    }

    /// <summary>
    /// Every category states the ceiling, the format list and the accept filter BRD-45 specifies,
    /// and renders them as the single sentence every upload surface prints — so the dialog's caption
    /// and the dropzone's caption are the same string, not merely the same intention.
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="expectedBytes">Its ceiling in bytes.</param>
    /// <param name="expectedDisplay">How that ceiling must be written on screen.</param>
    /// <param name="expectedFormats">Its allow-list as displayed.</param>
    /// <param name="expectedAccept">The file input's accept filter.</param>
    [Theory]
    [MemberData(nameof(ExpectedRules))]
    public void EveryCategoryCarriesItsSpecifiedLimits(
        string category, long expectedBytes, string expectedDisplay, string expectedFormats,
        string expectedAccept)
    {
        var rule = ImageCategoryRules.For(category);

        Assert.Equal(expectedBytes, rule.MaxSizeBytes);
        Assert.Equal(expectedDisplay, rule.MaxSizeDisplay);
        Assert.Equal(expectedFormats, rule.FormatsDisplay);
        Assert.Equal(expectedAccept, rule.AcceptAttribute);
        Assert.Equal($"Max {expectedDisplay}, formats: {expectedFormats}", rule.ConstraintsText);
    }

    /// <summary>
    /// The site offers exactly the seven categories BRD-46 fixes, so a surface enumerating them
    /// cannot quietly gain or lose one.
    /// </summary>
    [Fact]
    public void SevenCategoriesAreOffered()
    {
        Assert.Equal(
            new[] { "profiles", "logos", "awards", "icons", "blog", "cv", "general" },
            ImageCategoryRules.Categories);
    }

    /// <summary>
    /// The limit the service hands a screen to advertise is the same limit it enforces: a file one
    /// byte under the advertised ceiling is accepted and a file one byte over is rejected, for every
    /// category. This is the property the demotion was about — the dialog promised 10 MB while the
    /// service enforced 2.
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="expectedBytes">Its ceiling in bytes.</param>
    /// <param name="expectedDisplay">How that ceiling must be written on screen.</param>
    /// <param name="expectedFormats">Unused here; supplied by the shared table.</param>
    /// <param name="expectedAccept">Unused here; supplied by the shared table.</param>
    [Theory]
    [MemberData(nameof(ExpectedRules))]
    public async Task AdvertisedLimitIsTheEnforcedLimit(
        string category, long expectedBytes, string expectedDisplay, string expectedFormats,
        string expectedAccept)
    {
        _ = expectedFormats;
        _ = expectedAccept;
        var service = BuildService();

        var advertised = service.GetCategoryRule(category);
        Assert.Equal(expectedBytes, advertised.MaxSizeBytes);
        Assert.Equal(expectedDisplay, advertised.MaxSizeDisplay);

        var atLimit = await service.ValidateImageAsync(
            BuildDeclaredFile(category, expectedBytes), category);
        Assert.True(atLimit.IsValid, $"a file of exactly {expectedDisplay} must be accepted");

        var overLimit = await service.ValidateImageAsync(
            BuildDeclaredFile(category, expectedBytes + 1), category);
        Assert.False(overLimit.IsValid);
        Assert.Contains(expectedDisplay, overLimit.Error);
        Assert.Contains(category, overLimit.Error);
    }

    /// <summary>
    /// The server is still the authority. An oversize upload pushed straight at
    /// <c>UploadImageAsync</c> — the shape of a bypassed or hostile client — is refused with the
    /// advertised limit named, and leaves neither stored bytes nor a metadata row behind.
    /// </summary>
    /// <param name="category">The category under test.</param>
    /// <param name="expectedBytes">Its ceiling in bytes.</param>
    /// <param name="expectedDisplay">How that ceiling must be written on screen.</param>
    /// <param name="expectedFormats">Unused here; supplied by the shared table.</param>
    /// <param name="expectedAccept">Unused here; supplied by the shared table.</param>
    [Theory]
    [MemberData(nameof(ExpectedRules))]
    public async Task OversizeUploadIsRefusedServerSideWhenTheClientIsBypassed(
        string category, long expectedBytes, string expectedDisplay, string expectedFormats,
        string expectedAccept)
    {
        _ = expectedFormats;
        _ = expectedAccept;
        var repo = Substitute.For<IBlogImageRepo>();
        var storage = Substitute.For<IFileStorage>();
        var service = BuildService(repo, storage);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(
                BuildDeclaredFile(category, expectedBytes + 1), category, userId: 1));

        Assert.Contains(expectedDisplay, failure.Message);
        repo.DidNotReceiveWithAnyArgs().InsertToGetId(default!);
        await storage.DidNotReceiveWithAnyArgs()
            .SaveAsync(default!, default!, default!, default);
    }

    /// <summary>
    /// A client that understates its file's size does not get past the server either: the upload
    /// buffer is bounded by the same category ceiling, so the oversize bytes are refused while
    /// being read and nothing is persisted.
    /// </summary>
    [Fact]
    public async Task UnderstatedFileSizeIsStillStoppedByTheBoundedUploadBuffer()
    {
        var repo = Substitute.For<IBlogImageRepo>();
        var storage = Substitute.For<IFileStorage>();
        var service = BuildService(repo, storage);
        var rule = ImageCategoryRules.For("profiles");
        var lying = new StubBrowserFile(
            "avatar.png", new byte[rule.MaxSizeBytes + 1], declaredSize: 1024);

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.UploadImageAsync(lying, "profiles", userId: 1));

        repo.DidNotReceiveWithAnyArgs().InsertToGetId(default!);
    }

    /// <summary>
    /// A category outside the seven is an input error, not something to be quietly validated under
    /// the general limits, and the rejection lists the categories that do exist.
    /// </summary>
    [Fact]
    public async Task UnknownCategoryIsRejectedRatherThanSubstituted()
    {
        var service = BuildService();

        var outcome = await service.ValidateImageAsync(
            BuildDeclaredFile("general", 1024), "screenshots");

        Assert.False(outcome.IsValid);
        Assert.Contains("screenshots", outcome.Error);
        Assert.Contains("profiles", outcome.Error);
    }

    /// <summary>
    /// A format outside a category's allow-list is refused with the allowed list named — the
    /// <c>accept</c> attribute is a convenience for the file dialog, never the gate.
    /// </summary>
    [Fact]
    public async Task DisallowedFormatIsRefusedWithTheAllowedListNamed()
    {
        var service = BuildService();

        var outcome = await service.ValidateImageAsync(
            new StubBrowserFile("resume.pdf", [1, 2, 3]), "profiles");

        Assert.False(outcome.IsValid);
        Assert.Contains("pdf", outcome.Error);
        Assert.Contains("jpg, jpeg, png, webp", outcome.Error);
    }

    /// <summary>
    /// No upload surface re-types a size limit. This is the regression guard for the defect itself:
    /// the two screens must obtain every advertised number from the service, so a literal such as
    /// "2MB" or "10 MB" outside a comment means a fifth copy of the limits has appeared.
    /// </summary>
    [Fact]
    public void UploadSurfacesCarryNoHardcodedSizeLimit()
    {
        var sourceRoot = FindSourceRoot();
        Assert.SkipWhen(sourceRoot == null, "source/ not found next to the test assembly");

        string[] surfaces =
        [
            Path.Combine(sourceRoot!, "BlogUI", "Pages", "AdminPages", "ManageImages.razor"),
            Path.Combine(sourceRoot!, "BlogUI", "Pages", "AdminPages", "ManageImages.razor.cs"),
            Path.Combine(sourceRoot!, "BlogUI", "Components", "ImagePicker.razor"),
            Path.Combine(sourceRoot!, "BlogUI", "Components", "ImagePicker.razor.cs")
        ];

        var violations = new List<string>();
        foreach (var surface in surfaces)
        {
            Assert.True(File.Exists(surface), $"{surface} is missing");
            var lines = RazorComment
                .Replace(File.ReadAllText(surface), string.Empty)
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].TrimEnd('\r');
                if (line.TrimStart().StartsWith("//"))
                {
                    continue;
                }

                if (SizeLiteral.IsMatch(line))
                {
                    violations.Add($"{Path.GetFileName(surface)}:{index + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "An upload surface must derive every advertised limit from IBlogImageService." +
            "GetCategoryRule, never re-type one (REQ-FN-025). Offending lines:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Builds the service over inert substitutes; the tests here stop at validation, so neither the
    /// repository nor the storage backend is expected to be touched.
    /// </summary>
    /// <param name="imageRepo">Optional repository substitute.</param>
    /// <param name="storage">Optional storage substitute.</param>
    /// <returns>The service under test.</returns>
    private static BlogImageService BuildService(
        IBlogImageRepo? imageRepo = null, IFileStorage? storage = null)
    {
        var factory = Substitute.For<IFileStorageFactory>();
        factory.GetStorageAsync().Returns(storage ?? Substitute.For<IFileStorage>());

        return new BlogImageService(
            imageRepo ?? Substitute.For<IBlogImageRepo>(),
            factory,
            NullLogger<BlogImageService>.Instance);
    }

    /// <summary>
    /// Builds an upload of a stated size in a format the category accepts, without allocating the
    /// bytes — validation judges the declared size, so a ten-megabyte array would only slow the
    /// suite down.
    /// </summary>
    /// <param name="category">The category whose allow-list the name must satisfy.</param>
    /// <param name="declaredSize">The size the file reports.</param>
    /// <returns>The stub upload.</returns>
    private static StubBrowserFile BuildDeclaredFile(string category, long declaredSize)
    {
        var extension = ImageCategoryRules.For(category).AllowedFormats[0];
        return new StubBrowserFile($"upload.{extension}", [1, 2, 3, 4], declaredSize);
    }

    /// <summary>
    /// Walks up from the test assembly until a folder containing <c>source/</c> is found.
    /// </summary>
    /// <returns>The absolute path of <c>source/</c>, or <c>null</c> when it is not present.</returns>
    private static string? FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
