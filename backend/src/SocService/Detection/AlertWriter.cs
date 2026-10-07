using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Models;

namespace SocService.Detection;

/// <summary>Writes rule matches as alerts with deduplication (spec 6.3).</summary>
public class AlertWriter(SocDbContext db)
{
    private static readonly string[] ActiveStatuses = [SocConstants.Status.Open, SocConstants.Status.Acknowledged];

    public async Task<Alert> ApplyAsync(IDetectionRule rule, RuleMatch match, DateTimeOffset now, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.AlertEvents)
            .Where(a => a.DedupKey == match.DedupKey && ActiveStatuses.Contains(a.Status))
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (alert is null)
        {
            alert = new Alert
            {
                AlertId = Guid.NewGuid(),
                Timestamp = now,
                UpdatedAt = now,
                RuleName = rule.RuleName,
                Severity = rule.Severity,
                Status = SocConstants.Status.Open,
                Description = match.Description,
                AffectedService = match.AffectedService,
                AffectedUser = match.AffectedUser,
                AffectedEntity = match.AffectedEntity,
                RecommendedAction = rule.RecommendedResponse,
                DedupKey = match.DedupKey,
            };
            db.Alerts.Add(alert);
        }
        else
        {
            alert.UpdatedAt = now;
        }

        var existing = alert.AlertEvents.Select(ae => ae.EventId).ToHashSet();
        foreach (var eventId in match.EventIds.Distinct())
        {
            if (existing.Add(eventId))
                alert.AlertEvents.Add(new AlertEvent { AlertId = alert.AlertId, EventId = eventId });
        }

        await db.SaveChangesAsync(ct);
        return alert;
    }

    public async Task<int> ResolveAsync(string dedupKey, DateTimeOffset createdBefore, CancellationToken ct)
    {
        var alerts = await db.Alerts
            .Where(a => a.DedupKey == dedupKey && ActiveStatuses.Contains(a.Status) && a.Timestamp < createdBefore)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var alert in alerts)
        {
            alert.Status = SocConstants.Status.Resolved;
            alert.UpdatedAt = now;
        }

        if (alerts.Count > 0)
            await db.SaveChangesAsync(ct);
        return alerts.Count;
    }
}
