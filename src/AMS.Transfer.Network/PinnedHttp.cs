using System.Security.Cryptography;
using System.Text;

namespace AMS.Transfer.Network;

internal static class PinnedHttp
{
    public static HttpClient Create(string expectedFingerprint, Action<bool>? certificateChecked = null)
    {
        var expected = expectedFingerprint.Replace(":", "", StringComparison.Ordinal).ToUpperInvariant();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false, // LAN / explicit tunnel peers must not leak authentication to a proxy.
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                var actual = certificate is null ? "" : CertificateService.GetFingerprint(certificate);
                var a = Encoding.ASCII.GetBytes(actual);
                var b = Encoding.ASCII.GetBytes(expected);
                var matches = expected.Length == 64 && a.Length == b.Length &&
                    CryptographicOperations.FixedTimeEquals(a, b);
                certificateChecked?.Invoke(matches);
                return matches;
            }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
