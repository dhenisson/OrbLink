namespace AMS.Transfer.Core;

public sealed record DeviceIdentity(Guid Id, string Name);

public sealed record DiscoveredDevice(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string CertificateFingerprint,
    DateTimeOffset LastSeenUtc);

public sealed record PairedDevice(
    Guid Id,
    string Name,
    string Address,
    int Port,
    string CertificateFingerprint,
    string SendToken,
    string ReceiveToken,
    DateTimeOffset LastSeenUtc);

public enum TransferDirection { Sent, Received }
public enum TransferStatus { Pending, InProgress, Completed, Failed, Cancelled }
public enum TransferKind { File, Image, Text }

public sealed record TransferRecord(
    Guid Id,
    string Name,
    TransferKind Kind,
    TransferDirection Direction,
    TransferStatus Status,
    long Size,
    Guid RemoteDeviceId,
    string RemoteDeviceName,
    string? LocalPath,
    DateTimeOffset CreatedUtc,
    string? Error = null);

public sealed record BubblePlacement(
    double Left,
    double Top,
    string? MonitorName,
    SnapEdge Edge = SnapEdge.None);

public enum SnapEdge { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

public readonly record struct ScreenArea(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public sealed record PairRequestMessage(
    Guid RequesterId,
    string RequesterName,
    int RequesterPort,
    string RequesterCertificateFingerprint,
    string RequesterReceiveToken);

public sealed record PairRequestReceipt(Guid RequestId, string Status);

public sealed record PairStatusMessage(
    string Status,
    Guid? ResponderId = null,
    string? ResponderName = null,
    string? ResponderReceiveToken = null,
    string? Error = null);

public sealed record IncomingPairRequest(
    Guid RequestId,
    PairRequestMessage Message,
    string RemoteAddress);

public sealed record TextTransferMessage(string Text, string? Label = null);
