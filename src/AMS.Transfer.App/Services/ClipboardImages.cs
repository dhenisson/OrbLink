using System.Windows;
using System.Windows.Media.Imaging;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;

namespace AMS.Transfer.App.Services;

public static class ClipboardImages
{
    public static BitmapSource? ReadCurrentImage()
    {
        var data = Clipboard.GetDataObject();
        return ReadImage(data) ?? (Clipboard.ContainsImage() ? Clipboard.GetImage() : null);
    }

    public static BitmapSource? ReadImage(IDataObject? data)
    {
        if (data is null) return null;
        // Browsers and Office often provide an encoded PNG in addition to a Windows bitmap.
        foreach (var format in new[] { "PNG", "image/png" })
        {
            try
            {
                if (!data.GetDataPresent(format)) continue;
                var value = data.GetData(format);
                if (value is Stream source)
                {
                    if (source.CanSeek) source.Position = 0;
                    var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
                if (value is byte[] bytes)
                {
                    using var stream = new MemoryStream(bytes);
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or System.IO.FileFormatException)
            { /* Some apps advertise PNG but only supply a usable Windows bitmap. */ }
        }
        return data.GetDataPresent(DataFormats.Bitmap) ? data.GetData(DataFormats.Bitmap) as BitmapSource : null;
    }

    public static void CopyImage(BitmapSource image)
        => Clipboard.SetDataObject(CreateImageDataObject(image), true);

    public static DataObject CreateImageDataObject(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        var data = new DataObject();
        data.SetImage(image);
        data.SetData("PNG", stream);
        return data;
    }
}

public sealed class ClipboardImageSender
{
    private readonly AppPaths _paths;
    private readonly Func<CancellationToken, Task<IReadOnlyList<PairedDevice>>> _getPeers;
    private readonly Func<PairedDevice, string, IProgress<double>?, CancellationToken, Task<TransferRecord>> _send;

    public ClipboardImageSender(AppPaths paths, NetworkCoordinator network) : this(paths,
        network.GetAvailablePeersForSendAsync, network.SendFileAsync) { }

    public ClipboardImageSender(AppPaths paths,
        Func<CancellationToken, Task<IReadOnlyList<PairedDevice>>> getPeers,
        Func<PairedDevice, string, IProgress<double>?, CancellationToken, Task<TransferRecord>> send)
    {
        _paths = paths;
        _getPeers = getPeers;
        _send = send;
    }

    public async Task SendAsync(BitmapSource image, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if ((long)image.PixelWidth * image.PixelHeight > 64_000_000)
            throw new InvalidDataException("Esta imagem é muito grande. Escolha uma captura menor.");
        var peers = await _getPeers(cancellationToken);
        if (peers.Count == 0) throw new InvalidOperationException("Nenhum computador pareado está online. Abra Computadores para conectar.");
        var path = Path.Combine(_paths.Outgoing, $"Imagem-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                encoder.Save(output);
            await _send(peers[0], path, progress, cancellationToken);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
