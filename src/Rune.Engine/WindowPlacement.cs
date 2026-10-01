namespace Rune.Services;

/// <summary>
/// Where the main window was when Rune last closed, so the next launch can
/// put it back. Coordinates are physical pixels, as AppWindow reports them.
/// Width/Height are the restored (non-maximized) size; IsMaximized says
/// whether to maximize on top of that.
/// </summary>
public sealed class WindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsMaximized { get; set; }

    /// <summary>Smallest window worth restoring; anything less is treated as corrupt.</summary>
    public const int MinWidth = 400;
    public const int MinHeight = 300;

    /// <summary>
    /// Clamps a saved placement into a monitor's work area, so a window last
    /// seen on a monitor that is no longer plugged in, or dragged half off
    /// the screen, still comes back somewhere the user can grab it. Returns
    /// null when the saved size is unusable.
    /// </summary>
    public WindowPlacement? Fit(int areaX, int areaY, int areaWidth, int areaHeight)
    {
        if (Width < MinWidth || Height < MinHeight || areaWidth <= 0 || areaHeight <= 0)
        {
            return null;
        }

        int width = Math.Min(Width, areaWidth);
        int height = Math.Min(Height, areaHeight);
        int x = Math.Clamp(X, areaX, areaX + areaWidth - width);
        int y = Math.Clamp(Y, areaY, areaY + areaHeight - height);

        return new WindowPlacement { X = x, Y = y, Width = width, Height = height, IsMaximized = IsMaximized };
    }
}
