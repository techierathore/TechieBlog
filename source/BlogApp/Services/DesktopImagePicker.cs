using BlogUI;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace BlogApp.Services;

/// <summary>
/// BlogApp's <see cref="INativeImagePicker"/>: chooses an image through MAUI's file picker, never
/// through WebView2's <c>&lt;input type="file"&gt;</c> dialog.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Production report 2026-10-21 — BlogApp crashed natively inside
/// <c>EmbeddedBrowserWebView.dll</c> while the owner was choosing images (see
/// <see cref="INativeImagePicker"/>). MAUI's picker is the WinUI <c>FileOpenPicker</c> owned by the
/// app window, the same picker the connection screen already uses through
/// <see cref="FilePickerService"/>. It does not go through the browser control at all.</para>
///
/// <para><b>Code Flow:</b> <c>ImagePicker</c> → <see cref="PickImageAsync"/> → the main thread →
/// <c>FilePicker.Default.PickAsync</c> filtered to the category's file types →
/// <see cref="PickedImageFile"/>, an <see cref="IBrowserFile"/> over the chosen path.</para>
///
/// <para><b>Usage:</b> Registered as a singleton <see cref="INativeImagePicker"/> in
/// <c>MauiProgram.cs</c>. The website registers none.</para>
/// </remarks>
public class DesktopImagePicker : INativeImagePicker
{
    /// <summary>File extensions offered in the dialog for each MIME type the image rules allow.</summary>
    private static readonly Dictionary<string, string[]> ExtensionsByMimeType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
        ["image/png"] = new[] { ".png" },
        ["image/webp"] = new[] { ".webp" },
        ["image/gif"] = new[] { ".gif" },
        ["image/svg+xml"] = new[] { ".svg" },
        ["image/x-icon"] = new[] { ".ico" },
        ["image/vnd.microsoft.icon"] = new[] { ".ico" },
        ["application/pdf"] = new[] { ".pdf" }
    };

    private readonly ILogger<DesktopImagePicker> logger;

    /// <summary>
    /// Creates the picker.
    /// </summary>
    /// <param name="logger">Structured logger for picker failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public DesktopImagePicker(ILogger<DesktopImagePicker> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Flow:</b> map the MIME list to extensions → marshal to the UI thread (the Windows
    /// picker throws when opened from the WebView's callback thread) → open the picker → wrap the
    /// chosen path.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog; logs a failure.</para>
    /// </remarks>
    public async Task<IBrowserFile?> PickImageAsync(string acceptTypes)
    {
        try
        {
            var options = new PickOptions
            {
                PickerTitle = "Choose an image",
                FileTypes = BuildFileTypes(acceptTypes)
            };

            var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options));
            if (result == null)
            {
                return null;
            }

            var info = new FileInfo(result.FullPath);
            logger.LogInformation("Image chosen for upload: {FileName} ({Size} bytes)", info.Name, info.Length);
            return new PickedImageFile(info, result.ContentType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The image picker could not be opened");
            return null;
        }
    }

    /// <summary>
    /// Builds the dialog's file-type filter from a comma-separated MIME allow-list.
    /// </summary>
    /// <param name="acceptTypes">The category's accept attribute.</param>
    /// <returns>The filter, or <c>null</c> (no filter) when no listed type is recognised.</returns>
    private static FilePickerFileType? BuildFileTypes(string acceptTypes)
    {
        var extensions = (acceptTypes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(mime => ExtensionsByMimeType.TryGetValue(mime, out var known) ? known : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (extensions.Length == 0)
        {
            return null;
        }

        return new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = extensions,
            [DevicePlatform.MacCatalyst] = extensions.Select(e => e.TrimStart('.')).ToArray()
        });
    }

    /// <summary>
    /// An <see cref="IBrowserFile"/> over a file the native picker returned.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Honours <c>maxAllowedSize</c> the way the framework's own browser
    /// file does, by throwing <see cref="IOException"/>. The image service relies on that ceiling
    /// (REQ-FN-026), and a desktop file must not get round it.</para>
    /// </remarks>
    private sealed class PickedImageFile : IBrowserFile
    {
        private readonly FileInfo file;

        /// <summary>Wraps the chosen file.</summary>
        /// <param name="file">The file on disk.</param>
        /// <param name="contentType">The MIME type the picker reported.</param>
        public PickedImageFile(FileInfo file, string? contentType)
        {
            this.file = file;
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        }

        /// <inheritdoc />
        public string Name => file.Name;

        /// <inheritdoc />
        public DateTimeOffset LastModified => file.LastWriteTimeUtc;

        /// <inheritdoc />
        public long Size => file.Length;

        /// <inheritdoc />
        public string ContentType { get; }

        /// <inheritdoc />
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (file.Length > maxAllowedSize)
            {
                throw new IOException(
                    $"Supplied file with size {file.Length} bytes exceeds the maximum of {maxAllowedSize} bytes.");
            }

            return new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        }
    }
}
