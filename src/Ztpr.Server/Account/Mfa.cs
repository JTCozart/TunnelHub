using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace Ztpr.Server.Account;

/// <summary>
/// Helpers for TOTP authenticator enrollment: the <c>otpauth://</c> provisioning URI,
/// an inline-SVG QR code for it, and a human-readable rendering of the shared key.
/// </summary>
public static class Mfa
{
    /// <summary>Failed backup-code attempts that lock an account.</summary>
    public const int MaxBackupCodeFailures = 5;

    /// <summary>Issuer/label shown in the user's authenticator app.</summary>
    private const string Issuer = "Ztpr";

    /// <summary>Build the otpauth provisioning URI an authenticator app expects.</summary>
    public static string BuildUri(string email, string unformattedKey) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}" +
        $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    /// <summary>Render the provisioning URI as a self-contained SVG QR code.</summary>
    public static string QrSvg(string uri)
    {
        var qr = QrCode.EncodeText(uri, QrCode.Ecc.Medium);
        return qr.ToSvgString(border: 1);
    }

    /// <summary>Group the shared key into 4-char blocks so it's easy to type by hand.</summary>
    public static string FormatKey(string unformattedKey)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < unformattedKey.Length; i += 4)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(unformattedKey.AsSpan(i, Math.Min(4, unformattedKey.Length - i)));
        }
        return sb.ToString().ToLowerInvariant();
    }
}
