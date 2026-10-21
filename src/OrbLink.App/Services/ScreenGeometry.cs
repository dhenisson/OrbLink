using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using OrbLink.Core;
using Forms = System.Windows.Forms;

namespace OrbLink.App.Services;

public static class ScreenGeometry
{
    public static IReadOnlyList<MonitorArea> Monitors(Window window) => Forms.Screen.AllScreens
        .Select(screen => new MonitorArea(screen.DeviceName, WorkArea(window, screen), screen.Primary)).ToArray();

    public static Forms.Screen ForWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle == IntPtr.Zero ? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(handle);
    }

    public static ScreenArea WorkArea(Window window, Forms.Screen? selected = null)
    {
        var screen = selected ?? ForWindow(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        return new ScreenArea(screen.WorkingArea.Left / dpi.DpiScaleX, screen.WorkingArea.Top / dpi.DpiScaleY,
            screen.WorkingArea.Width / dpi.DpiScaleX, screen.WorkingArea.Height / dpi.DpiScaleY);
    }

    public static void PlacePanel(Window panel, Window orb)
    {
        // Create the HWND on the orb's monitor before converting screen pixels to WPF DIPs.
        new WindowInteropHelper(panel).EnsureHandle();
        var anchor = new WindowInteropHelper(orb).Handle;
        if (GetWindowRect(anchor, out var rect))
            SetWindowPos(new WindowInteropHelper(panel).Handle, IntPtr.Zero, rect.Left, rect.Top, 0, 0, 0x0015);
        panel.UpdateLayout();
        var area = WorkArea(panel, ForWindow(orb));
        var physical = orb.PointToScreen(new Point(0, 0));
        var dpi = VisualTreeHelper.GetDpi(panel);
        var orbDpi = VisualTreeHelper.GetDpi(orb);
        var bounds = PanelPlacement.NearOrb(physical.X / dpi.DpiScaleX, physical.Y / dpi.DpiScaleY,
            orb.Width * orbDpi.DpiScaleX / dpi.DpiScaleX, orb.Height * orbDpi.DpiScaleY / dpi.DpiScaleY,
            panel.Width, panel.Height, area);
        panel.Width = bounds.Width;
        panel.Height = bounds.Height;
        panel.Left = bounds.Left;
        panel.Top = bounds.Top;
    }

    public static void MoveInPixels(Window window, int x, int y) =>
        SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, x, y, 0, 0, 0x0015);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
