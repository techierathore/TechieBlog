using Microsoft.Extensions.Logging;

namespace BlogApp.Services;

/// <summary>
/// Lets the connection screen ask the operating system for a file or folder path (REQ-FN-062).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Two settings on the connection screen are paths the operator has to
/// produce from their own machine — the SSH private key, and the folder of images to send to the
/// server. Typing those by hand is exactly the interaction that caused REQ-FN-062's second round:
/// a hand-typed path is a path nobody checked, and the one that was typed
/// (<c>C:\srv\data\techieblog\uploads</c>) looked correct and was not. A picker cannot return a
/// path that does not exist.</para>
///
/// <para><b>Code Flow:</b> Razor component → <see cref="PickFileAsync"/> or
/// <see cref="PickFolderAsync"/> → MAUI's picker on the UI thread → the chosen path, or
/// <c>null</c> when the operator cancelled.</para>
///
/// <para><b>Dependencies:</b> <c>Microsoft.Maui.Storage</c>.</para>
///
/// <para><b>Usage:</b> Registered as a singleton. Every call is marshalled to the main thread:
/// the Windows pickers are UI components and throw when opened from the WebView's callback thread,
/// which is where a Blazor event handler runs.</para>
/// </remarks>
public class FilePickerService
{
    private readonly ILogger<FilePickerService> logger;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="logger">Structured logger for picker failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public FilePickerService(ILogger<FilePickerService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Asks the operator to choose a file.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No file-type filter is applied. An OpenSSH private key has no
    /// conventional extension — <c>id_ed25519</c>, <c>id_rsa</c> and <c>.pem</c> are all normal —
    /// and a filter that hides the very file being looked for is worse than none.</para>
    /// <para>A cancelled dialog and a failed one both return <c>null</c>, because the screen's
    /// response is the same either way: leave the field alone. The failure is logged so the two are
    /// still distinguishable afterwards.</para>
    /// <para><b>Flow:</b> marshal to the UI thread → open the picker → return the full path.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <param name="title">Dialog title describing what is being chosen.</param>
    /// <returns>The full path, or <c>null</c> when nothing was chosen.</returns>
    public async Task<string?> PickFileAsync(string title)
    {
        try
        {
            var result = await MainThread.InvokeOnMainThreadAsync(
                () => FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title }));
            return result?.FullPath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The file picker could not be opened");
            return null;
        }
    }

    /// <summary>
    /// Asks the operator to choose a folder.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used to locate a folder of images to send to the server, so the
    /// operator points at what they can see rather than reconstructing a path from memory.</para>
    /// <para><b>Flow:</b> marshal to the UI thread → open the picker → return the folder path.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <param name="title">Dialog title describing what is being chosen.</param>
    /// <returns>The folder path, or <c>null</c> when nothing was chosen.</returns>
    public async Task<string?> PickFolderAsync(string title)
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(PickFolderCoreAsync);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The folder picker could not be opened for {Title}", title);
            return null;
        }
    }

    /// <summary>
    /// Opens the platform's folder picker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> MAUI Essentials 9 exposes a file picker but no folder picker, so
    /// Windows goes straight to the WinUI one. A WinUI picker has to be told which window owns it —
    /// without that initialisation it throws rather than opening, which is the classic symptom of a
    /// picker "doing nothing" in an unpackaged desktop app.</para>
    /// <para>Elsewhere the operator picks any FILE inside the folder and its directory is used. That
    /// is a smaller ask than it looks — the folders in question are full of the images being sent —
    /// and it keeps the feature working on Mac Catalyst without a second native implementation.</para>
    /// <para><b>Flow:</b> Windows → WinUI picker bound to the app window; otherwise → file picker,
    /// then take the containing directory.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <returns>The chosen folder path, or <c>null</c>.</returns>
    private async Task<string?> PickFolderCoreAsync()
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
            as Microsoft.UI.Xaml.Window;
        if (window != null)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        }

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
#else
        var file = await FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "Choose any file inside the folder" });
        return file == null ? null : Path.GetDirectoryName(file.FullPath);
#endif
    }
}
