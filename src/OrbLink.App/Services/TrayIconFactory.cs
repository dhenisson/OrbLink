using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace OrbLink.App.Services;

internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var fill = new SolidBrush(Color.FromArgb(20, 43, 76));
        using var border = new Pen(Color.FromArgb(85, 165, 255), 1.5f);
        using var link = new Pen(Color.FromArgb(225, 242, 255), 2.5f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(fill, 2, 2, 28, 28);
        graphics.DrawEllipse(border, 2, 2, 28, 28);
        graphics.DrawArc(link, 7, 10, 12, 12, 70, 260);
        graphics.DrawArc(link, 13, 9, 12, 12, 250, 260);
        graphics.DrawLine(link, 12, 19, 20, 13);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
