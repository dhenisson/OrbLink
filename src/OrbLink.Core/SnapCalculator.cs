namespace OrbLink.Core;

public static class SnapCalculator
{
    public const double DefaultThreshold = 28;

    public static BubblePlacement Snap(
        double left,
        double top,
        double width,
        double height,
        ScreenArea workArea,
        string? monitorName,
        double threshold = DefaultThreshold)
    {
        var clampedLeft = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        var clampedTop = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));

        var nearLeft = Math.Abs(clampedLeft - workArea.Left) <= threshold;
        var nearRight = Math.Abs((clampedLeft + width) - workArea.Right) <= threshold;
        var nearTop = Math.Abs(clampedTop - workArea.Top) <= threshold;
        var nearBottom = Math.Abs((clampedTop + height) - workArea.Bottom) <= threshold;

        var edge = (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => SnapEdge.TopLeft,
            (_, true, true, _) => SnapEdge.TopRight,
            (true, _, _, true) => SnapEdge.BottomLeft,
            (_, true, _, true) => SnapEdge.BottomRight,
            (true, _, _, _) => SnapEdge.Left,
            (_, true, _, _) => SnapEdge.Right,
            (_, _, true, _) => SnapEdge.Top,
            (_, _, _, true) => SnapEdge.Bottom,
            _ => SnapEdge.None
        };

        if (nearLeft) clampedLeft = workArea.Left;
        if (nearRight) clampedLeft = workArea.Right - width;
        if (nearTop) clampedTop = workArea.Top;
        if (nearBottom) clampedTop = workArea.Bottom - height;

        return new BubblePlacement(clampedLeft, clampedTop, monitorName, edge);
    }
}
