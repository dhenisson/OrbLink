using System.Text.RegularExpressions;

namespace OrbLink.Storage;

public sealed class LocalLog(AppPaths paths)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastCleanup;
    private const long MaxLogBytes = 8 * 1024 * 1024;

    public async Task WriteAsync(string level, string message, Exception? exception = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = Path.Combine(paths.Logs, $"orblink-{DateTime.UtcNow:yyyy-MM-dd}.log");
            var sanitized = Redact(message);
            level = Redact(level);
            level = level[..Math.Min(level.Length, 16)];
            var line = $"{DateTimeOffset.Now:O} [{level}] {sanitized}";
            if (exception is not null)
                // Exception messages may embed HTTP credentials, clipboard text, SQL or private paths.
                line += $" | {exception.GetType().Name} HRESULT=0x{exception.HResult:X8}";
            if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
            {
                for (var index = 3; index >= 1; index--)
                {
                    var from = index == 1 ? path : path + $".{index - 1}";
                    if (File.Exists(from)) File.Move(from, path + $".{index}", overwrite: true);
                }
            }
            if (_lastCleanup.Date != DateTime.UtcNow.Date)
            {
                _lastCleanup = DateTime.UtcNow;
                foreach (var old in Directory.EnumerateFiles(paths.Logs, "orblink-*.log*"))
                    if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-14)) File.Delete(old);
            }
            await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* A full disk or restricted log folder must not take down transfers or startup. */ }
        finally
        {
            _gate.Release();
        }
    }

    private static string Redact(string value)
    {
        value = value[..Math.Min(value.Length, 4096)];
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        value = Regex.Replace(value, @"(?i)([\w-]*(?:token|password|secret|api[_-]?key|private[_-]?key)[\w-]*\s*[=:]\s*)[^\s,;]+", "$1[redacted]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"[A-Za-z0-9+/]{40,}={0,2}", "[redacted]", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return value[..Math.Min(value.Length, 1200)];
    }
}
