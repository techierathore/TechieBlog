using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging;
using TechieBlog.Tests.Dashboard;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Media;

/// <summary>
/// Unit tests for the upload failure path of <see cref="BlogImageService"/> (REQ-NFR-040,
/// REQ-NFR-033).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The defect these tests lock down was invisible by construction. When the
/// uploads directory was not writable by the container's user the upload failed, the container
/// stayed Up, <c>/healthz</c> stayed 200 Healthy, the startup line still announced that uploads were
/// being served, and the container log carried zero <c>[ERR]</c>, <c>[WRN]</c> or <c>[FTL]</c>
/// entries. Nothing anywhere said the feature was dead. So the assertions here are on the two
/// observables that were missing: an error log line naming the target path, and an administrator
/// message that distinguishes "the server cannot write here" from a retry-able failure.</para>
///
/// <para><b>Why the assertions are on the log and not only on the exception:</b> the transaction was
/// already correct — no partial file, no orphaned <c>blogimage</c> row — so a test that only checked
/// behaviour would have passed against the broken build. Observability was the whole defect.</para>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for the storage and repository seams, and
/// <c>RecordingLogger&lt;T&gt;</c> to capture what was logged.</para>
/// </remarks>
public class BlogImageUploadFailureTests
{
    /// <summary>
    /// A permissions refusal from the storage backend now produces exactly one error-level log
    /// entry, and that entry names the storage-relative target path and carries the underlying
    /// exception — the two facts an operator grepping the container log found nothing of before.
    /// </summary>
    [Fact]
    public async Task PermissionsRefusalLogsErrorNamingPathAndException()
    {
        var thrown = new UnauthorizedAccessException("Access to the path '/app/uploads/blog' is denied.");
        var recorder = new RecordingLogger<BlogImageService>();
        var service = BuildService(thrown, recorder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(BuildFile(), "blog", userId: 1));

        var errors = recorder.Entries.Where(entry => entry.Level == LogLevel.Error).ToList();
        Assert.Single(errors);
        Assert.Contains("uploads/blog/", errors[0].Message);
        Assert.Same(thrown, errors[0].Error);
    }

    /// <summary>
    /// The administrator-facing message for a permissions refusal says the server cannot write
    /// there and says a retry will not help, so the operator is sent to the hosting problem rather
    /// than into a retry loop against a directory that will never become writable.
    /// </summary>
    [Fact]
    public async Task PermissionsRefusalTellsAdministratorTheServerCannotWrite()
    {
        var service = BuildService(
            new UnauthorizedAccessException("Access to the path '/app/uploads/blog' is denied."),
            new RecordingLogger<BlogImageService>());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(BuildFile(), "blog", userId: 1));

        Assert.Contains("cannot write", failure.Message);
        Assert.Contains("Retrying will not help", failure.Message);
    }

    /// <summary>
    /// The administrator-facing message never carries the exception text or the absolute server
    /// path, which is the REQ-NFR-033 half of the same change — the log gets both, the screen gets
    /// neither.
    /// </summary>
    [Fact]
    public async Task AdministratorMessageDisclosesNoServerPathOrExceptionText()
    {
        var service = BuildService(
            new UnauthorizedAccessException("Access to the path '/app/uploads/blog' is denied."),
            new RecordingLogger<BlogImageService>());

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(BuildFile(), "blog", userId: 1));

        Assert.DoesNotContain("/app/uploads", failure.Message);
        Assert.DoesNotContain("Access to the path", failure.Message);
        Assert.DoesNotContain("denied", failure.Message);
    }

    /// <summary>
    /// A non-permissions I/O failure — a full disk, a dropped network share — is reported with a
    /// different message that does invite a retry, because unlike a permissions refusal it may
    /// genuinely clear.
    /// </summary>
    [Fact]
    public async Task TransientIoFailureIsDistinguishedFromAPermissionsRefusal()
    {
        var recorder = new RecordingLogger<BlogImageService>();
        var service = BuildService(new IOException("There is not enough space on the disk."), recorder);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(BuildFile(), "blog", userId: 1));

        Assert.Contains("Please try again", failure.Message);
        Assert.DoesNotContain("Retrying will not help", failure.Message);
        Assert.Contains(recorder.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The failure stays transactionally clean: a write the backend refused leaves no metadata row
    /// behind, so the only thing this requirement changed is whether anyone can see the failure.
    /// </summary>
    [Fact]
    public async Task RefusedUploadWritesNoMetadataRow()
    {
        var repo = Substitute.For<IBlogImageRepo>();
        var service = BuildService(
            new UnauthorizedAccessException("denied"), new RecordingLogger<BlogImageService>(), repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UploadImageAsync(BuildFile(), "blog", userId: 1));

        repo.DidNotReceiveWithAnyArgs().InsertToGetId(default!);
    }

    /// <summary>
    /// Builds an image service whose storage backend refuses every write with the supplied
    /// exception.
    /// </summary>
    /// <param name="storageFailure">The exception the storage backend raises.</param>
    /// <param name="logger">Recorder standing in for the injected logger.</param>
    /// <param name="imageRepo">Optional repository substitute; one is created when omitted.</param>
    /// <returns>The service under test.</returns>
    private static BlogImageService BuildService(
        Exception storageFailure, ILogger<BlogImageService> logger, IBlogImageRepo? imageRepo = null)
    {
        var storage = Substitute.For<IFileStorage>();
        storage.ProviderName.Returns("Local");
        storage
            .SaveAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<FileStorageResult>(storageFailure));

        var factory = Substitute.For<IFileStorageFactory>();
        factory.GetStorageAsync().Returns(storage);

        return new BlogImageService(imageRepo ?? Substitute.For<IBlogImageRepo>(), factory, logger);
    }

    /// <summary>
    /// Builds a small, valid PNG-named upload that clears the category's format and size rules, so
    /// the test reaches the storage write rather than stopping at validation.
    /// </summary>
    /// <returns>The stub upload.</returns>
    private static StubBrowserFile BuildFile()
    {
        return new StubBrowserFile("diagram.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    }
}
