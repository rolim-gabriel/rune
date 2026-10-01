using Rune.Engine;

namespace Rune.Tests;

public class LaunchRequestTests
{
    [Fact]
    public void Parse_NoArguments_IsEmpty()
    {
        var request = LaunchRequest.Parse([]);

        Assert.False(request.HasPaths);
        Assert.Null(request.Page);
        Assert.Null(request.Zoom);
        Assert.False(request.NewWindow);
    }

    [Fact]
    public void Parse_ExplorerStyle_SinglePath()
    {
        var request = LaunchRequest.Parse([@"C:\Docs\report.pdf"]);

        Assert.Equal([@"C:\Docs\report.pdf"], request.Paths);
        Assert.Null(request.Page);
        Assert.Null(request.Zoom);
    }

    [Fact]
    public void Parse_HarnessStyle_PageAndZoom()
    {
        var request = LaunchRequest.Parse([@"C:\Docs\a.pdf", "--page", "7", "--zoom", "1.5"]);

        Assert.Equal([@"C:\Docs\a.pdf"], request.Paths);
        Assert.Equal(7, request.Page);
        Assert.Equal(1.5, request.Zoom);
    }

    [Fact]
    public void Parse_SwitchesBeforePath_StillFindPath()
    {
        var request = LaunchRequest.Parse(["--page", "3", @"C:\Docs\a.pdf"]);

        Assert.Equal([@"C:\Docs\a.pdf"], request.Paths);
        Assert.Equal(3, request.Page);
    }

    [Fact]
    public void Parse_NewWindow_SetsFlagAndKeepsPath()
    {
        var request = LaunchRequest.Parse([@"C:\Docs\a.pdf", "--new-window"]);

        Assert.True(request.NewWindow);
        Assert.Equal([@"C:\Docs\a.pdf"], request.Paths);
    }

    [Fact]
    public void Parse_MultiplePaths_AllKeptInOrder()
    {
        var request = LaunchRequest.Parse([@"C:\a.pdf", @"C:\b.pdf"]);

        Assert.Equal([@"C:\a.pdf", @"C:\b.pdf"], request.Paths);
    }

    [Fact]
    public void Parse_UnknownSwitchDropped_BadValueFallsThroughAsPath()
    {
        // "--page x": the switch consumes nothing, so "x" is read as a path
        // (the window reports it as not found). "--bogus" is dropped outright
        // rather than mistaken for a file.
        var request = LaunchRequest.Parse(["--page", "x", "--bogus", @"C:\a.pdf"]);

        Assert.Null(request.Page);
        Assert.Equal(["x", @"C:\a.pdf"], request.Paths);
    }
}
