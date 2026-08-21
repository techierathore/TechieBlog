using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TechieBlog.Tests.Media;

/// <summary>
/// Unit tests for the metadata <see cref="BlogImageService"/> records against an upload.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers REQ-FN-026 / BRD-46 — "BlogImage carries category, alt text, MIME
/// type and dimensions". The service used to persist only name, path, size, time, owner, category
/// and MIME type, so alt text, width and height were NULL on every row the application had ever
/// written. These tests assert the row handed to the repository now carries all four attributes.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IBlogImageRepo"/>,
/// <see cref="IFileStorageFactory"/> and <see cref="IFileStorage"/>;
/// <see cref="StubBrowserFile"/> for the upload. No database, disk or network is touched.</para>
/// </remarks>
public class BlogImageMetadataTests
{
    private readonly IBlogImageRepo imageRepo = Substitute.For<IBlogImageRepo>();
    private readonly IFileStorage storage = Substitute.For<IFileStorage>();
    private readonly IFileStorageFactory storageFactory = Substitute.For<IFileStorageFactory>();
    private readonly BlogImageService service;

    /// <summary>
    /// Bytes the storage provider was handed, captured while the buffer is still open because the
    /// service disposes it before a test could measure it.
    /// </summary>
    private long savedContentLength;

    /// <summary>
    /// Wires the service under test to substituted storage and metadata dependencies.
    /// </summary>
    public BlogImageMetadataTests()
    {
        storage.ProviderName.Returns("Stub");
        storage.SaveAsync(
                Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                savedContentLength = callInfo.ArgAt<Stream>(0).Length;
                return Task.FromResult(new FileStorageResult
                {
                    RelativePath = callInfo.ArgAt<string>(1),
                    PublicUrl = "/" + callInfo.ArgAt<string>(1),
                    SizeInBytes = savedContentLength,
                    ProviderName = "Stub"
                });
            });

        storageFactory.GetStorageAsync().Returns(Task.FromResult(storage));
        imageRepo.InsertToGetId(Arg.Any<BlogImage>()).Returns(42L);

        service = new BlogImageService(imageRepo, storageFactory, NullLogger<BlogImageService>.Instance);
    }

    /// <summary>
    /// A PNG upload records its MIME type, the alt text the uploader typed, and the pixel width and
    /// height read out of the file's own IHDR chunk — the three attributes that were previously
    /// left NULL.
    /// </summary>
    [Fact]
    public async Task UploadRecordsAltTextAndDimensions()
    {
        var file = new StubBrowserFile("diagram.png", BuildPng(1200, 630));

        var uploaded = await service.UploadImageAsync(file, "blog", 7, "Architecture diagram");

        Assert.Equal("Architecture diagram", uploaded.AltText);
        Assert.Equal(1200, uploaded.Width);
        Assert.Equal(630, uploaded.Height);
        Assert.Equal("image/png", uploaded.MimeType);
    }

    /// <summary>
    /// The record actually handed to the repository — not merely the object returned to the caller —
    /// carries the dimensions, which is what proves the INSERT has values to bind.
    /// </summary>
    [Fact]
    public async Task PersistedRowCarriesDimensions()
    {
        var file = new StubBrowserFile("hero.png", BuildPng(800, 400));

        await service.UploadImageAsync(file, "blog", 7, "Hero banner");

        imageRepo.Received(1).InsertToGetId(Arg.Is<BlogImage>(image =>
            image.Width == 800 && image.Height == 400 && image.AltText == "Hero banner"));
    }

    /// <summary>
    /// An uploader who leaves alt text blank still gets a readable accessible name derived from the
    /// original file name, because a NULL alt text is an accessibility defect (REQ-NFR-007) and the
    /// generated storage name is not a description.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAltTextFallsBackToFileName(string? suppliedAltText)
    {
        var file = new StubBrowserFile("my-holiday_photo.png", BuildPng(64, 64));

        var uploaded = await service.UploadImageAsync(file, "blog", 7, suppliedAltText);

        Assert.Equal("my holiday photo", uploaded.AltText);
    }

    /// <summary>
    /// Alt text longer than the <c>VARCHAR(255)</c> column is clipped rather than allowed to fail
    /// the insert, so a paste of a long caption cannot lose the whole upload.
    /// </summary>
    [Fact]
    public async Task OverlongAltTextIsClippedToColumnWidth()
    {
        var file = new StubBrowserFile("wide.png", BuildPng(64, 64));

        var uploaded = await service.UploadImageAsync(file, "blog", 7, new string('a', 400));

        Assert.Equal(255, uploaded.AltText!.Length);
    }

    /// <summary>
    /// A format whose dimensions cannot be read from a header — a CV in PDF form — stores NULL
    /// rather than zero, so "not probed" stays distinguishable from "no pixels".
    /// </summary>
    [Fact]
    public async Task UnreadableFormatStoresNullDimensions()
    {
        var file = new StubBrowserFile("resume.pdf", "%PDF-1.7 not a real document"u8.ToArray());

        var uploaded = await service.UploadImageAsync(file, "cv", 7);

        Assert.Null(uploaded.Width);
        Assert.Null(uploaded.Height);
        Assert.Equal("application/pdf", uploaded.MimeType);
    }

    /// <summary>
    /// The whole upload still reaches the storage provider after the header has been inspected —
    /// buffering for the dimension read must not consume the bytes the file is made of.
    /// </summary>
    [Fact]
    public async Task StorageStillReceivesTheCompleteFile()
    {
        var content = BuildPng(32, 32);
        var file = new StubBrowserFile("small.png", content);

        await service.UploadImageAsync(file, "blog", 7);

        Assert.Equal(content.Length, savedContentLength);
    }

    /// <summary>
    /// Builds the smallest byte sequence a PNG dimension read needs: the signature followed by an
    /// IHDR chunk carrying the requested size.
    /// </summary>
    /// <param name="width">Width to declare.</param>
    /// <param name="height">Height to declare.</param>
    /// <returns>A byte array whose header is a valid PNG IHDR.</returns>
    private static byte[] BuildPng(int width, int height)
    {
        var content = new byte[64];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(content, 0);

        WriteBigEndianInt32(content, 16, width);
        WriteBigEndianInt32(content, 20, height);
        return content;
    }

    /// <summary>Writes a big-endian 32-bit integer into a header buffer.</summary>
    /// <param name="buffer">The buffer being built.</param>
    /// <param name="offset">Index of the most significant byte.</param>
    /// <param name="value">The value to encode.</param>
    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
