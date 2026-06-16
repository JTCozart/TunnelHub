using Microsoft.AspNetCore.DataProtection;

namespace Ztpr.Server.Services;

/// <summary>
/// Encrypts/decrypts small secrets (e.g. the Route 53 secret access key) before they
/// are persisted to the database, using ASP.NET Core Data Protection. The keyring is
/// persisted to disk (see <c>Program.cs</c>) so protected values survive restarts.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Ztpr.Secrets.v1");

    /// <summary>Encrypt a plaintext secret. Returns null for null/blank input.</summary>
    public string? Protect(string? plaintext) =>
        string.IsNullOrEmpty(plaintext) ? null : _protector.Protect(plaintext);

    /// <summary>
    /// Decrypt a previously protected value. Returns null for null/blank input, and
    /// null (rather than throwing) if the ciphertext can't be unprotected — e.g. the
    /// keyring was lost — so callers can treat it as "not configured".
    /// </summary>
    public string? Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            return null;
        try { return _protector.Unprotect(ciphertext); }
        catch { return null; }
    }
}
