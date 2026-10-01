using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Rune.Engine;

namespace Rune;

/// <summary>
/// How a launch request (this process's own command line, or one handed over
/// by a second Rune.exe that Explorer started) becomes tabs in this window.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Opens this process's own launch arguments. Window is already active.</summary>
    internal async Task OpenFromLaunchAsync(LaunchRequest request)
    {
        for (int i = 0; i < request.Paths.Count; i++)
        {
            // Page/zoom apply to the last file, which ends up the selected tab.
            bool last = i == request.Paths.Count - 1;
            await LoadDocumentAsync(request.Paths[i], last ? request.Page : null, last ? request.Zoom : null);
        }
    }

    /// <summary>
    /// A second Rune.exe handed over its activation: open its files as tabs
    /// and bring this window to the front, where the user expects the
    /// document they just double-clicked to appear.
    /// </summary>
    internal async Task HandleActivationAsync(LaunchRequest request)
    {
        BringToFront();
        await OpenFromLaunchAsync(request);
    }

    private void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        Activate();

        // Activate() alone does not take the foreground from Explorer. The
        // process that redirected to us lent its foreground right
        // (AllowSetForegroundWindow) before handing over, so this succeeds.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
