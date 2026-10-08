using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Dtos;

namespace SocService.Queries;

/// <summary>
/// Tie-safe keyset pages for the graph sync: (timestamp, id) strictly greater than the cursor.
/// </summary>
public class SyncQuery
{
    private readonly SocDbContext _db;
    public SyncQuery(SocDbContext db) => _db = db;

    public async Task<IReadOnlyList<SyncAlertResponse>> GetAlertsAsync(
        DateTimeOffset? since, Guid? afterId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 500);
        var q = _db.Alerts.AsNoTracking().AsQueryable();
        if (since is not null && afterId is null)
        {
            // No tiebreaker supplied: strictly after the instant.
            var s = since.Value.ToUniversalTime();
            q = q.Where(a => a.UpdatedAt > s);
        }
        else if (since is not null || afterId is not null)
        {
            var s = (since ?? DateTimeOffset.MinValue).ToUniversalTime();
            var id = afterId!.Value;
            q = q.Where(a => a.UpdatedAt > s || (a.UpdatedAt == s && a.AlertId.CompareTo(id) > 0));
        }

        var alerts = await q.OrderBy(a => a.UpdatedAt).ThenBy(a => a.AlertId)
            .Take(limit).Include(a => a.AlertEvents).ToListAsync(ct);

        var eventIds = alerts.SelectMany(a => a.AlertEvents).Select(ae => ae.EventId).Distinct().ToList();
        var events = eventIds.Count == 0
            ? new Dictionary<Guid, Models.SocEvent>()
            : await _db.Events.AsNoTracking().Where(e => eventIds.Contains(e.EventId))
                .ToDictionaryAsync(e => e.EventId, ct);

        return alerts.Select(a => new SyncAlertResponse(
            a.AlertId, a.Timestamp, a.UpdatedAt, a.RuleName, a.Severity, a.Status, a.Description,
            a.AffectedService, a.AffectedUser, a.AffectedEntity, a.RecommendedAction,
            a.AlertEvents.Where(ae => events.ContainsKey(ae.EventId))
                .Select(ae => events[ae.EventId])
                .OrderBy(e => e.Timestamp).ThenBy(e => e.EventId)
                .Select(SocEventResponse.From).ToList())).ToList();
    }

    public async Task<IReadOnlyList<SyncEventResponse>> GetEventsAsync(
        DateTimeOffset? since, Guid? afterId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 1000);
        var q = _db.Events.AsNoTracking()
            .Where(e => e.EventType != "REQUEST_RECEIVED"
                        && !_db.AlertEvents.Any(ae => ae.EventId == e.EventId));
        if (since is not null && afterId is null)
        {
            var s = since.Value.ToUniversalTime();
            q = q.Where(e => e.ReceivedAt > s);
        }
        else if (since is not null || afterId is not null)
        {
            var s = (since ?? DateTimeOffset.MinValue).ToUniversalTime();
            var id = afterId!.Value;
            q = q.Where(e => e.ReceivedAt > s || (e.ReceivedAt == s && e.EventId.CompareTo(id) > 0));
        }

        var rows = await q.OrderBy(e => e.ReceivedAt).ThenBy(e => e.EventId).Take(limit).ToListAsync(ct);
        return rows.Select(e => new SyncEventResponse(SocEventResponse.From(e), e.ReceivedAt)).ToList();
    }
}
