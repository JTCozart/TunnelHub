namespace Ztpr.Server.Data.Entities;

/// <summary>A cached Let's Encrypt certificate for one host, stored as a PFX blob.</summary>
public class IssuedCertificate
{
    public int Id { get; set; }

    /// <summary>The exact host the cert covers, e.g. <c>red-tiger.tun.example.com</c>. Unique.</summary>
    public required string Host { get; set; }

    /// <summary>PKCS#12 / PFX bytes (no password).</summary>
    public required byte[] PfxData { get; set; }

    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset NotAfter { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
}
