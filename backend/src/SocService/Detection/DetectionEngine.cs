using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Models;

namespace SocService.Detection;

public class DetectionEngine(
    SocDbContext db,
    IEnumerable<IDetectionRule> rules,
    AlertWriter writer,
    ILogger<DetectionEngine> logger)
{
    public async Task RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (var rule in rules)
        {
            try
            {
                await RunRuleAsync(rule, now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Detection rule {RuleName} failed; skipping this cycle", rule.RuleName);
                db.ChangeTracker.Clear();
            }
        }

        // Post-rules step (e.g. service-failure recovery auto-resolve) is added here.
    }

    private async Task RunRuleAsync(IDetectionRule rule, DateTimeOffset now, CancellationToken ct)
    {
        var from = now - rule.Window;
        var types = rule.InputEventTypes.ToList();
        var ruleName = rule.RuleName;

        // Events already linked to a RESOLVED alert of this rule must not be re-evaluated,
        // otherwise a resolved alert would be re-opened from the same events.
        var events = await db.Events.AsNoTracking()
            .Where(e => types.Contains(e.EventType) && e.Timestamp >= from)
            .Where(e => !db.AlertEvents.Any(ae => ae.EventId == e.EventId &&
                db.Alerts.Any(a => a.AlertId == ae.AlertId
                    && a.Status == SocConstants.Status.Resolved
                    && a.RuleName == ruleName)))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);

        foreach (var match in rule.Evaluate(events, now).ToList())
            await writer.ApplyAsync(rule, match, now, ct);
    }
}
