using Microsoft.AspNetCore.Components.Forms;

namespace BlogUI;

/// <summary>
/// Chooses an image file through the host operating system's own picker instead of the page's
/// <c>&lt;input type="file"&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Production report 2026-10-21. BlogApp crashed while the owner was choosing
/// images to upload: three crashes in one morning, all in <c>EmbeddedBrowserWebView.dll</c> (exception
/// 0x80000003), each with the Windows file-open dialog loaded, and two of them before any upload
/// code had run. That dialog is the one WebView2 opens for an <c>&lt;input type="file"&gt;</c>. It
/// runs inside BlogApp's process, so when it fails the whole app goes down, and no .NET handler or
/// log line sees it. The desktop head therefore picks files with its own native picker, and the page's
/// file input is never clicked there.</para>
///
/// <para><b>Code Flow:</b> <c>ImagePicker</c> resolves this OPTIONALLY from the service provider. The
/// website registers none, so the upload dialog keeps TrBlazeUI's <c>FileUpload</c> dropzone
/// unchanged. BlogApp registers an implementation, and the dialog shows a "Choose image…" button
/// instead. The <see cref="IBrowserFile"/> it returns goes through the exact validation and upload
/// path a browser file does (<c>IBlogImageService</c>).</para>
///
/// <para><b>Usage:</b> Register at most one implementation, in a desktop head's composition root
/// (<c>BlogApp/MauiProgram.cs</c>). <c>BlogUI</c> must not reference MAUI (REQ-UI-052), which is why
/// this is an interface here and the implementation lives in the head.</para>
/// </remarks>
public interface INativeImagePicker
{
    /// <summary>
    /// Asks the operator to choose one image file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A cancelled dialog and a failed one both return <c>null</c>. The
    /// caller's response is the same either way (nothing is selected). Implementations log a
    /// failure and never throw.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <param name="acceptTypes">
    /// The category's comma-separated MIME allow-list (e.g. <c>image/jpeg,image/png</c>), used to
    /// filter the dialog. The chosen file is still validated by the image service afterwards.
    /// </param>
    /// <returns>The chosen file, or <c>null</c> when nothing was chosen.</returns>
    Task<IBrowserFile?> PickImageAsync(string acceptTypes);
}
