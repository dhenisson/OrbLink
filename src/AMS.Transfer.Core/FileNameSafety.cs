using System.Text;
using System.Globalization;

namespace AMS.Transfer.Core;

public static class FileNameSafety
{
    public static string Sanitize(string? suppliedName)
    {
        var name = Path.GetFileName((suppliedName ?? string.Empty).Replace('/', '\\'));
        if (string.IsNullOrWhiteSpace(name)) name = "arquivo";

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
            builder.Append(invalid.Contains(character) || character < 32 ? '_' : character);

        var safe = builder.ToString().TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safe)) safe = "arquivo";
        var deviceName = safe.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (deviceName is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            (deviceName.Length == 4 && (deviceName.StartsWith("COM") || deviceName.StartsWith("LPT")) &&
             "123456789¹²³".Contains(deviceName[3]))) safe = "_" + safe;
        return Fit(safe, "");
    }

    public static string GetUniquePath(string directory, string fileName)
    {
        var safe = Sanitize(fileName);
        var candidate = Path.Combine(directory, safe);
        if (!Exists(candidate)) return candidate;

        for (var index = 1; index < 10_000; index++)
        {
            candidate = Path.Combine(directory, Fit(safe, $" ({index})"));
            if (!Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, Fit(safe, $"-{Guid.NewGuid():N}"));
    }

    // The move itself reserves the name. An existence check alone is not safe under concurrency.
    public static string MoveToUniquePath(string temporaryPath, string directory, string fileName)
    {
        var safe = Sanitize(fileName);
        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? "" : index < 10_000 ? $" ({index})" : $"-{Guid.NewGuid():N}";
            var candidate = Path.Combine(directory, Fit(safe, suffix));
            try { File.Move(temporaryPath, candidate, overwrite: false); return candidate; }
            catch (IOException) when (Exists(candidate)) { }
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Fit(string name, string suffix)
    {
        if (name.Length + suffix.Length <= 180 && suffix.Length == 0) return name;
        var originalExtension = Path.GetExtension(name);
        if (originalExtension == name) originalExtension = ""; // A leading dot is a valid name, not an empty stem.
        var stem = originalExtension.Length == 0 ? name : name[..^originalExtension.Length];
        var extension = Truncate(originalExtension, 180 - suffix.Length - 1);
        var shortStem = Truncate(stem, 180 - suffix.Length - extension.Length);
        return (shortStem.Length == 0 ? "_" : shortStem) + suffix + extension;
    }

    private static string Truncate(string value, int length)
    {
        if (value.Length <= length) return value;
        var indices = StringInfo.ParseCombiningCharacters(value);
        var end = indices.LastOrDefault(index => index <= length);
        return value[..end];
    }

    public static TransferKind DetectKind(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif"
            ? TransferKind.Image
            : TransferKind.File;
    }
}
