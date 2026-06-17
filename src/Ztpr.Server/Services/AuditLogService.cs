using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ztpr.Server.Data;
using Ztpr.Server.Data.Entities;

namespace Ztpr.Server.Services;

/// <summary>
/// Records security-relevant events to the audit log (DB) and mirrors them to <see cref="ILogger"/>
/// so they also land in the host's journal. Safe to resolve from singletons: it uses the DbContext
/// factory and never holds a scoped context.
/// </summary>
public sealed class AuditLogService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuditLogService> logger)
{
    /// <summary>Size cap for the audit log. When exceeded, the oldest rows are purged (limited disk).</summary>
    public const long MaxLogBytes = 1L * 1024 * 1024 * 1024; // 1 GiB

    /// <summary>
    /// Conservative per-row footprint estimate (id + timestamp + enum + a few short strings,
    /// plus index overhead) used to translate the byte cap into a row cap. Deliberately
    /// generous so the on-disk log stays comfortably under <see cref="MaxLogBytes"/>.
    /// </summary>
    public const long ApproxBytesPerRow = 512;

    /// <summary>Maximum audit rows retained, derived from the size cap.</summary>
    public static long MaxRows => MaxLogBytes / ApproxBytesPerRow;

    /// <summary>
    /// Append an audit entry. <paramref name="ip"/> is used when given; otherwise the current
    /// request's remote IP is captured if a request context is available (best-effort outside
    /// HTTP — e.g. Blazor circuit callbacks may have none).
    /// </summary>
    public async Task LogAsync(
        AuditEventType type,
        string? userId = null,
        string? userEmail = null,
        string? ip = null,
        string? detail = null,
        CancellationToken ct = default)
    {
        ip ??= httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            db.AuditEvents.Add(new AuditEvent
            {
                EventType = type,
                UserId = userId,
                UserEmail = userEmail,
                IpAddress = ip,
                Detail = detail,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditing must never break the action it accompanies.
            logger.LogError(ex, "Failed to persist audit event {EventType}", type);
        }

        logger.LogInformation("AUDIT {EventType} user={UserEmail} ip={Ip} {Detail}",
            type, userEmail ?? "-", ip ?? "-", detail ?? "");
    }

    /// <summary>
    /// Enforce the size cap by deleting the oldest rows once the log grows past
    /// <see cref="MaxRows"/> (≈ <see cref="MaxLogBytes"/>). Returns rows removed.
    /// </summary>
    public async Task<int> EnforceSizeCapAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.AuditEvents.LongCountAsync(ct);
        if (count <= MaxRows)
            return 0;

        var toDelete = (int)Math.Min(int.MaxValue, count - MaxRows);
        var oldestIds = db.AuditEvents.OrderBy(e => e.CreatedAtUnixMs).Take(toDelete).Select(e => e.Id);
        var removed = await db.AuditEvents.Where(e => oldestIds.Contains(e.Id)).ExecuteDeleteAsync(ct);
        if (removed > 0)
            logger.LogInformation("Audit log size cap reached; purged {Removed} oldest entries", removed);
        return removed;
    }
}
