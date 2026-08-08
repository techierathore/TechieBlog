using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace BlogApp.Services;

/// <summary>
/// Desktop stand-in for the ASP.NET Core hosting environment the shared engine expects.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogEngine.Storage.FileStorageFactory</c> resolves
/// <see cref="IWebHostEnvironment"/> to locate the local upload folder. That service is part of the
/// DI graph BlogApp must register unchanged (REQ-FN-046), but a MAUI head has no web host, so this
/// class supplies the same contract backed by the desktop app's own data directory.</para>
/// <para><b>Code Flow:</b> registered as a singleton in <c>MauiProgram</c> →
/// <c>FileStorageFactory</c> reads <see cref="WebRootPath"/> when site settings select the local
/// storage provider.</para>
/// <para><b>Dependencies:</b> <see cref="PhysicalFileProvider"/>.</para>
/// <para><b>Usage:</b> Uploads made from BlogApp with the *local* storage provider land in the
/// desktop machine's app-data folder rather than on the web server's disk, so a site that serves
/// images from its own <c>wwwroot</c> should be switched to the configured cloud provider before
/// uploading from the desktop head. Database-recorded image metadata is shared either way.</para>
/// </remarks>
public class DesktopHostEnvironment : IWebHostEnvironment
{
    /// <summary>
    /// Creates the environment and materialises its content and web root folders.
    /// </summary>
    /// <param name="contentRootPath">Root folder for BlogApp's writable data.</param>
    /// <exception cref="ArgumentException"><paramref name="contentRootPath"/> is blank.</exception>
    public DesktopHostEnvironment(string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new ArgumentException("A content root path is required.", nameof(contentRootPath));
        }

        ApplicationName = typeof(DesktopHostEnvironment).Assembly.GetName().Name;
        EnvironmentName = "Desktop";

        ContentRootPath = contentRootPath;
        WebRootPath = Path.Combine(contentRootPath, "wwwroot");

        Directory.CreateDirectory(ContentRootPath);
        Directory.CreateDirectory(WebRootPath);

        ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
    }

    /// <inheritdoc />
    public string ApplicationName { get; set; }

    /// <inheritdoc />
    public IFileProvider ContentRootFileProvider { get; set; }

    /// <inheritdoc />
    public string ContentRootPath { get; set; }

    /// <inheritdoc />
    public string EnvironmentName { get; set; }

    /// <inheritdoc />
    public IFileProvider WebRootFileProvider { get; set; }

    /// <inheritdoc />
    public string WebRootPath { get; set; }
}
