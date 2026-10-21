using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using AMS.Transfer.Storage;

namespace AMS.Transfer.App.Services;

public sealed class IconStore(AppPaths paths)
{
    public async Task<string> ImportAsync(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not ".png" and not ".ico")
            throw new InvalidDataException("Escolha uma imagem PNG ou um ícone ICO.");
        // Validate the exact immutable snapshot that will be persisted, not a second open of the file.
        var bytes = ReadIconBytes(sourcePath);
        _ = LoadImage(bytes, 512);
        var name = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension;
        var destination = Path.Combine(paths.Appearance, name);
        if (!File.Exists(destination)) await File.WriteAllBytesAsync(destination, bytes);
        return name;
    }

    public BitmapSource? TryLoad(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return null;
        try { return LoadImage(Path.Combine(paths.Appearance, name), 256); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or OverflowException or System.IO.FileFormatException)
        { return null; }
    }

    public static BitmapSource LoadImage(string path, int maxWidth = 256)
    {
        return LoadImage(ReadIconBytes(path), maxWidth);
    }

    private static byte[] ReadIconBytes(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("Escolha um ícone de até 4 MB.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static BitmapSource LoadImage(byte[] bytes, int maxWidth)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        // Inspect dimensions before requesting decoded pixel data.
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames.OrderByDescending(item => item.PixelWidth).First();
        if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 ||
            (long)frame.PixelWidth * frame.PixelHeight > 32_000_000)
            throw new InvalidDataException("A imagem é grande demais para ser usada como ícone.");
        stream.Position = 0;
        var result = new BitmapImage();
        result.BeginInit(); result.CacheOption = BitmapCacheOption.OnLoad;
        result.DecodePixelWidth = Math.Min(frame.PixelWidth, maxWidth);
        result.StreamSource = stream;
        result.EndInit();
        result.Freeze();
        return result;
    }
}
