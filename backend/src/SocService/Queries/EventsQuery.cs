using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Dtos;

namespace SocService.Queries;

public static class EventsQuery
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static int NormalisePageSize(int pageSize) =>
        pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

    public static async Task<List<SocEventResponse>> RunAsync(
        SocDbContext db, string? type, string? user, DateTimeOffset? from, DateTimeOffset? to,
        int page, int pageSize, CancellationToken ct = default,
        string? excludeType = null, Guid? alertId = null)
    {
        pageSize = NormalisePageSize(pageSize);
        page = Math.Max(page, 1);

        // Npgsql only accepts UTC offsets for timestamptz parameters.
        from = from?.ToUniversalTime();
        to = to?.ToUniversalTime();

        var q = db.Events.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(e => e.EventType == type);
        if (!string.IsNullOrWhiteSpace(excludeType)) q = q.Where(e => e.EventType != excludeType);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(e => e.UserId == user);
        if (alertId is { } aid)
            q = q.Where(e => db.AlertEvents.Any(ae => ae.AlertId == aid && ae.EventId == e.EventId));
        if (from is not null) q = q.Where(e => e.Timestamp >= from);
        if (to is not null) q = q.Where(e => e.Timestamp <= to);

        var rows = await q.OrderByDescending(e => e.Timestamp).ThenBy(e => e.EventId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return rows.Select(SocEventResponse.From).ToList();
    }
}
