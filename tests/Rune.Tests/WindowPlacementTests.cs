using Rune.Services;

namespace Rune.Tests;

public class WindowPlacementTests
{
    private static WindowPlacement P(int x, int y, int w, int h, bool max = false) =>
        new() { X = x, Y = y, Width = w, Height = h, IsMaximized = max };

    [Fact]
    public void Fit_InsideArea_Unchanged()
    {
        var fit = P(100, 100, 1200, 800).Fit(0, 0, 2560, 1400)!;
        Assert.Equal((100, 100, 1200, 800), (fit.X, fit.Y, fit.Width, fit.Height));
    }

    [Fact]
    public void Fit_OffScreenRight_PulledBackIntoArea()
    {
        // Last seen on a second monitor that is no longer there.
        var fit = P(3000, 200, 1200, 800).Fit(0, 0, 1920, 1040)!;
        Assert.Equal(1920 - 1200, fit.X);
        Assert.Equal(200, fit.Y);
    }

    [Fact]
    public void Fit_LargerThanArea_ShrunkToArea()
    {
        var fit = P(0, 0, 3000, 2000).Fit(0, 0, 1920, 1040)!;
        Assert.Equal((0, 0, 1920, 1040), (fit.X, fit.Y, fit.Width, fit.Height));
    }

    [Fact]
    public void Fit_NegativeOrigin_ClampedToAreaOrigin()
    {
        // Monitor to the left of the primary has negative X; area origin is respected.
        var fit = P(-5000, -50, 800, 600).Fit(-1920, 0, 1920, 1040)!;
        Assert.Equal((-1920, 0), (fit.X, fit.Y));
    }

    [Fact]
    public void Fit_TinyOrCorruptSize_IsRejected()
    {
        Assert.Null(P(0, 0, 10, 10).Fit(0, 0, 1920, 1040));
        Assert.Null(P(0, 0, 0, 0).Fit(0, 0, 1920, 1040));
    }

    [Fact]
    public void Fit_KeepsMaximizedFlag()
    {
        Assert.True(P(0, 0, 800, 600, max: true).Fit(0, 0, 1920, 1040)!.IsMaximized);
    }
}
