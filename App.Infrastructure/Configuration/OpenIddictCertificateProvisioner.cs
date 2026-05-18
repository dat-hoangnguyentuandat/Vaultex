using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace App.Infrastructure.Configuration;

/// <summary>
/// Provisions persistent signing/encryption certificates for OpenIddict in production.
///
/// In production we cannot use ephemeral keys — every restart would invalidate all
/// previously issued refresh tokens and auth codes. We also cannot ship a fixed cert
/// in source control. So: on first startup, generate self-signed RSA certs and
/// persist them as PFX files. On subsequent startups, load from disk.
///
/// Operators can override by providing their own cert via the OpenIddict:Certificates
/// configuration section — useful when running multiple instances behind a load balancer
/// (all instances must share the same keys), or when integrating with a secrets manager.
/// </summary>
public static class OpenIddictCertificateProvisioner
{
    public static X509Certificate2 GetOrCreateSigningCertificate(string path, string password)
        => GetOrCreate(path, password, "Vaultex Signing", forSigning: true);

    public static X509Certificate2 GetOrCreateEncryptionCertificate(string path, string password)
        => GetOrCreate(path, password, "Vaultex Encryption", forSigning: false);

    private static X509Certificate2 GetOrCreate(string path, string password, string subjectName, bool forSigning)
    {
        const X509KeyStorageFlags loadFlags =
            X509KeyStorageFlags.MachineKeySet |
            X509KeyStorageFlags.PersistKeySet |
            X509KeyStorageFlags.Exportable;

        if (File.Exists(path))
            return new X509Certificate2(path, password, loadFlags);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var keyUsage = forSigning
            ? X509KeyUsageFlags.DigitalSignature
            : X509KeyUsageFlags.KeyEncipherment;
        request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(path, pfxBytes);

        // Restrict file permissions on Unix-like systems.
        if (!OperatingSystem.IsWindows())
        {
            try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* best-effort; not all filesystems support chmod */ }
        }

        return new X509Certificate2(path, password, loadFlags);
    }
}
