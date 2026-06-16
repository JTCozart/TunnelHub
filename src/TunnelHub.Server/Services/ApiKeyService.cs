using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TunnelHub.Server.Data;
using TunnelHub.Server.Data.Entities;

namespace TunnelHub.Server.Services;

/// <summary>Generates, hashes, and verifies API keys.</summary>
public sealed class ApiKeyService(AppDbContext db)
{
    private const string Prefix = "th_";

    public sealed record CreatedKey(ApiKey Record, string RawKey);

    /// <summary>
    /// Create a new key for a user. Returns the persisted record plus the raw
    /// key — the only time the raw value is ever available.
    /// </summary>
    public async Task<CreatedKey> CreateAsync(string userId, string label, CancellationToken ct = default)
    {
        var raw = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var record = new ApiKey
        {
            OwnerId = userId,
            Label = string.IsNullOrWhiteSpace(label) ? "key" : label.Trim(),
            DisplayPrefix = raw[..11] + "…",
            KeyHash = Hash(raw),
        };
        db.ApiKeys.Add(record);
        await db.SaveChangesAsync(ct);
        return new CreatedKey(record, raw);
    }

    /// <summary>Look up the active, non-blocked key matching a raw value, or null.</summary>
    public async Task<ApiKey?> VerifyAsync(string rawKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
            return null;

        var hash = Hash(rawKey.Trim());
        var key = await db.ApiKeys
            .Include(k => k.Owner)
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.IsActive, ct);

        if (key is null || key.Owner is null || key.Owner.IsBlocked)
            return null;

        return key;
    }

    public async Task RevokeAsync(Guid keyId, string ownerId, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == keyId && k.OwnerId == ownerId, ct);
        if (key is null || !key.IsActive)
            return;
        key.IsActive = false;
        key.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public static string Hash(string raw)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
