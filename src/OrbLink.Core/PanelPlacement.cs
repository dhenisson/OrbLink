namespace OrbLink.Core;

public readonly record struct PanelBounds(double Left, double Top, double Width, double Height);

public static class PanelPlacement
{
    public static PanelBounds NearOrb(double orbLeft, double orbTop, double orbWidth, double orbHeight,
        double preferredWidth, double preferredHeight, ScreenArea workArea)
    {
        const double gap = 12;
        var width = Math.Min(preferredWidth, workArea.Width);
        var height = Math.Min(preferredHeight, workArea.Height);
        var left = orbLeft + orbWidth + gap;
        if (left + width > workArea.Right) left = orbLeft - width - gap;
        var top = orbTop + orbHeight / 2 - height / 2;
        return new PanelBounds(
            Math.Clamp(left, workArea.Left, workArea.Right - width),
            Math.Clamp(top, workArea.Top, workArea.Bottom - height), width, height);
    }
}
