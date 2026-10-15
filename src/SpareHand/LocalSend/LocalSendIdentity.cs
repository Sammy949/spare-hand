using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SpareHand.LocalSend;

/// <summary>
/// Our LocalSend identity: one self-signed certificate, created on first run and kept in %LOCALAPPDATA%.
/// Its SHA-256 is our fingerprint, and we present it as the TLS client certificate. Newer LocalSend servers
/// request one, and Windows' TLS stack fails the handshake without it (docs/research.md).
/// </summary>
internal sealed class LocalSendIdentity
{
    private LocalSendIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        Fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
    }

    public X509Certificate2 Certificate { get; }

    public string Fingerprint { get; }

    public static LocalSendIdentity LoadOrCreate()
    {
        var path = AppPaths.File("localsend-identity.pfx");
        if (File.Exists(path))
        {
            try
            {
                return new LocalSendIdentity(X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.UserKeySet));
            }
            catch (Exception e)
            {
                Log.Write($"localsend identity unreadable, recreating: {e.Message}");
            }
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Spare Hand", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = created.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfx);

        // Windows' TLS needs a persisted key for client auth, hence the PFX round-trip.
        return new LocalSendIdentity(X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet));
    }
}
