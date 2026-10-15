using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RemoteDesktop.Transport;

internal static class LanCertificate
{
    internal const string HostName = "projectremote.local";
    internal const string ServerAuth = "1.3.6.1.5.5.7.3.1";

    internal static X509Certificate2 Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={HostName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(ServerAuth) }, true));
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName(HostName);
        request.CertificateExtensions.Add(names.Build());
        var now = DateTimeOffset.UtcNow;
        var ephemeral = request.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(4));
        if (!OperatingSystem.IsWindows()) return ephemeral;
        using (ephemeral)
        {
            // Windows Schannel cannot acquire credentials from an ephemeral CNG
            // key. Import into a temporary user key container instead: omitting
            // PersistKeySet makes .NET delete that backing key with the certificate.
            // This never installs the certificate in an OS certificate/trust store.
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var pfx = ephemeral.Export(X509ContentType.Pkcs12, password);
            try { return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
    }

    internal static bool Validate(X509Certificate? remote, SslPolicyErrors errors, byte[] expectedPin)
    {
        if (remote == null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0) return false;
        using var certificate = X509CertificateLoader.LoadCertificate(remote.GetRawCertData());
        var fingerprint = SHA256.HashData(certificate.RawData);
        if (!CryptographicOperations.FixedTimeEquals(fingerprint, expectedPin)) return false;
        var now = DateTime.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime()) return false;
        // The user-approved SHA-256 pin is the ephemeral trust anchor. Build a real
        // chain against that anchor, retaining name, lifetime, signature and EKU checks.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(certificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuth));
        return chain.Build(certificate);
    }
}
