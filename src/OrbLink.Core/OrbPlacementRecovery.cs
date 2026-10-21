namespace OrbLink.Core;

public sealed record MonitorArea(string Name, ScreenArea Area, bool IsPrimary = false);

public static class OrbPlacementRecovery
{
    public static bool NeedsRecovery(BubblePlacement? saved, IReadOnlyList<MonitorArea> monitors, double size) =>
        saved is null || !double.IsFinite(saved.Left) || !double.IsFinite(saved.Top) ||
        (!string.IsNullOrEmpty(saved.MonitorName) && !monitors.Any(m => m.Name == saved.MonitorName)) ||
        !monitors.Any(m => Math.Min(saved.Left + size, m.Area.Right) - Math.Max(saved.Left, m.Area.Left) >= 16 &&
                           Math.Min(saved.Top + size, m.Area.Bottom) - Math.Max(saved.Top, m.Area.Top) >= 16);

    public static BubblePlacement Resolve(BubblePlacement? saved, IReadOnlyList<MonitorArea> monitors, double size)
    {
        if (monitors.Count == 0) throw new ArgumentException("Nenhum monitor disponível.", nameof(monitors));
        if (NeedsRecovery(saved, monitors, size))
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            return new BubblePlacement(Math.Max(primary.Area.Left, primary.Area.Right - size),
                primary.Area.Top + Math.Max(0, (primary.Area.Height - size) / 2), primary.Name, SnapEdge.Right);
        }
        var monitor = monitors.FirstOrDefault(m => m.Name == saved!.MonitorName) ??
            monitors.First(m => saved!.Left < m.Area.Right && saved.Left + size > m.Area.Left &&
                                saved.Top < m.Area.Bottom && saved.Top + size > m.Area.Top);
        return SnapCalculator.Snap(saved!.Left, saved.Top, size, size, monitor.Area, monitor.Name);
    }
}
