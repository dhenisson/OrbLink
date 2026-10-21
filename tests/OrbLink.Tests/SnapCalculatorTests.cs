using OrbLink.Core;

namespace OrbLink.Tests;

public sealed class SnapCalculatorTests
{
    private static readonly ScreenArea Screen = new(0, 0, 1920, 1040);

    [Fact]
    public void SnapsToLeftEdgeWithinThreshold()
    {
        var result = SnapCalculator.Snap(12, 400, 64, 64, Screen, "DISPLAY1");
        Assert.Equal(0, result.Left);
        Assert.Equal(SnapEdge.Left, result.Edge);
        Assert.Equal("DISPLAY1", result.MonitorName);
    }

    [Fact]
    public void SnapsToBottomRightCorner()
    {
        var result = SnapCalculator.Snap(1850, 980, 64, 64, Screen, "DISPLAY1");
        Assert.Equal(1856, result.Left);
        Assert.Equal(976, result.Top);
        Assert.Equal(SnapEdge.BottomRight, result.Edge);
    }

    [Fact]
    public void ClampsPositionInsideNegativeCoordinateMonitor()
    {
        var secondary = new ScreenArea(-1600, 0, 1600, 900);
        var result = SnapCalculator.Snap(-1700, 920, 64, 64, secondary, "DISPLAY2");
        Assert.Equal(-1600, result.Left);
        Assert.Equal(836, result.Top);
    }
}
