using Microsoft.Win32;

namespace OrbLink.App;

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OrbLink";
    private const string LegacyValueName = "OrbLink";

    public static bool IsEnabled()
    {
        try
        {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string || key?.GetValue(LegacyValueName) is string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return false; }
    }

    public static string BuildCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') ||
            !string.Equals(Path.GetFileName(executable), "OrbLink.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Inicie o OrbLink.exe publicado para configurar a inicialização.");
        return $"\"{executable}\"";
    }

    public static void RefreshIfEnabled() { if (IsEnabled()) SetEnabled(true); }

    public static void SetEnabled(bool enabled)
    {
        var command = enabled ? BuildCommand(Environment.ProcessPath ?? "") : null;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, command!, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
        key.DeleteValue(LegacyValueName, false);
    }
}
