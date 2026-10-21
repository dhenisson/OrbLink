using System.Net;

namespace OrbLink.Network;

internal sealed class ProgressStreamContent(
    Stream source,
    long length,
    IProgress<double>? progress,
    CancellationToken transferToken) : HttpContent
{
    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        CopyAsync(stream, transferToken);

    protected override async Task SerializeToStreamAsync(
        Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(transferToken, cancellationToken);
        await CopyAsync(stream, linked.Token);
    }

    private async Task CopyAsync(Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long sent = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            sent += read;
            progress?.Report(length == 0 ? 1 : (double)sent / length);
        }
        if (sent != length) throw new InvalidDataException("O tamanho do arquivo mudou durante o envio.");
        progress?.Report(1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}
