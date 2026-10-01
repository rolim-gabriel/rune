using Microsoft.UI.Windowing;
using Rune.Services;
using Windows.Graphics;

namespace Rune;

/// <summary>
/// Remembers where the main window was (position, size, maximized) across
/// launches. Tracked continuously rather than read at close, because the
/// size AppWindow reports for a maximized window is the monitor's, not the
/// one the user would get back on un-maximizing.
/// </summary>
public sealed partial class MainWindow
{
    private RectInt32? _lastNormalBounds;
    private bool _isMaximized;

    /// <summary>
    /// Puts the window where it was last time. Called from the constructor,
    /// before the first Activate(), so the window never appears at the
    /// default size and then jumps.
    /// </summary>
    private void ApplySavedPlacement()
    {
        AppWindow.Changed += AppWindow_Changed;

        if (_state.Window is not { } saved)
        {
            return;
        }

        // The monitor it was on may be gone (laptop undocked): clamp into the
        // nearest one's work area so it is always reachable.
        var wanted = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
        var area = DisplayArea.GetFromRect(wanted, DisplayAreaFallback.Nearest).WorkArea;
        if (saved.Fit(area.X, area.Y, area.Width, area.Height) is not { } fit)
        {
            return;
        }

        var bounds = new RectInt32(fit.X, fit.Y, fit.Width, fit.Height);
        AppWindow.MoveAndResize(bounds);
        _lastNormalBounds = bounds;

        if (fit.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
            _isMaximized = true;
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange && !args.DidPositionChange && !args.DidPresenterChange)
        {
            return;
        }

        var state = (sender.Presenter as OverlappedPresenter)?.State;
        switch (state)
        {
            case OverlappedPresenterState.Restored:
                _lastNormalBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
                _isMaximized = false;
                break;
            case OverlappedPresenterState.Maximized:
                _isMaximized = true;
                break;
            // Minimized: keep whatever was true before, that is what comes back.
        }
    }

    /// <summary>Writes the tracked bounds into state. Called from the Closed handler before saving.</summary>
    private void CapturePlacement()
    {
        if (_lastNormalBounds is not { } b)
        {
            return;
        }
        _state.Window = new WindowPlacement
        {
            X = b.X,
            Y = b.Y,
            Width = b.Width,
            Height = b.Height,
            IsMaximized = _isMaximized,
        };
    }
}
