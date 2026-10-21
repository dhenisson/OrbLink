namespace OrbLink.Core;

public sealed record AppearanceSettings
{
    public double OrbSize { get; init; } = 68;
    public double IconSize { get; init; } = 30;
    public string BackgroundColor { get; init; } = "#152541";
    public string AccentColor { get; init; } = "#55A5FF";
    public bool BorderEnabled { get; init; } = true;
    public double ShadowIntensity { get; init; } = 0.5;
    public double PanelOpacity { get; init; } = 0.97;
    public bool AutoHide { get; init; } = true;
    public bool AnimationsEnabled { get; init; } = true;
    public bool SoundEnabled { get; init; }
    public string? CustomIconFile { get; init; }

    public AppearanceSettings Normalize() => this with
    {
        OrbSize = Clamp(OrbSize, 52, 100, 68),
        IconSize = Clamp(IconSize, 16, 56, 30),
        ShadowIntensity = Clamp(ShadowIntensity, 0, 1, 0.5),
        PanelOpacity = Clamp(PanelOpacity, 0.82, 1, 0.97),
        BackgroundColor = NormalizeColor(BackgroundColor, "#152541"),
        AccentColor = NormalizeColor(AccentColor, "#55A5FF"),
        CustomIconFile = IsSafeIconFile(CustomIconFile) ? CustomIconFile : null
    };

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static string NormalizeColor(string? value, string fallback) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).ToString().All(Uri.IsHexDigit)
            ? value.ToUpperInvariant() : fallback;

    private static bool IsSafeIconFile(string? file) =>
        !string.IsNullOrWhiteSpace(file) && file == Path.GetFileName(file) &&
        !file.Contains('/') && !file.Contains('\\') &&
        Path.GetExtension(file).ToLowerInvariant() is ".png" or ".ico";
}
