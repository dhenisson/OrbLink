using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using OrbLink.Storage;

namespace OrbLink.Network;

public sealed class CertificateService(
    AppPaths paths,
    SqliteStore store,
    ISecretProtector? suppliedProtector = null,
    X509KeyStorageFlags? suppliedKeyStorageFlags = null)
{
    private readonly ISecretProtector _protector = suppliedProtector ?? new SecretProtector();
    private readonly X509KeyStorageFlags _keyStorageFlags = suppliedKeyStorageFlags ??
        (X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
    public async Task<X509Certificate2> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var protectedPassword = await store.GetSettingAsync("CertificatePassword", cancellationToken);
        if (File.Exists(paths.Certificate) || !string.IsNullOrWhiteSpace(protectedPassword))
        {
            if (!File.Exists(paths.Certificate) || string.IsNullOrWhiteSpace(protectedPassword))
                throw new InvalidDataException("Estado do certificado incompleto. Preserve os dados locais e consulte docs/SECURITY-AUDIT.md; a identidade não será substituída automaticamente.");
            try
            {
                var password = _protector.Unprotect(protectedPassword);
                return X509CertificateLoader.LoadPkcs12FromFile(paths.Certificate, password, _keyStorageFlags);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw new InvalidDataException("Não foi possível abrir o certificado local. A identidade e os pareamentos foram preservados; restaure um backup do mesmo usuário Windows.", ex);
            }
        }

        var newPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=OrbLink {Environment.MachineName}", key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var exported = generated.Export(X509ContentType.Pfx, newPassword);
        var temporary = paths.Certificate + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await File.WriteAllBytesAsync(temporary, exported, cancellationToken);
            await store.SetSettingAsync("CertificatePassword", _protector.Protect(newPassword), cancellationToken);
            File.Move(temporary, paths.Certificate, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return X509CertificateLoader.LoadPkcs12(exported, newPassword, _keyStorageFlags);
    }

    public static string GetFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
