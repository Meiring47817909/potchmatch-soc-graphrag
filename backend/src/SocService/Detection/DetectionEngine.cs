using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Detection.Rules;
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
        // Recovery runs first so a failure arriving in the same cycle opens a fresh alert
        // instead of being absorbed into an alert that is about to be resolved.
        try
        {
            await ResolveRecoveredServicesAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Service-failure recovery auto-resolve failed; skipping this cycle");
            db.ChangeTracker.Clear();
        }

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

    }

    private async Task ResolveRecoveredServicesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rule = rules.OfType<ServiceFailureRule>().FirstOrDefault();
        if (rule is null) return;

        // Loaded directly: recovered events are never linked to alerts, and must not be hidden.
        var from = now - rule.Window;
        var recovered = await db.Events.AsNoTracking()
            .Where(e => e.EventType == ServiceFailureRule.HealthRecovered && e.Timestamp >= from)
            .ToListAsync(ct);

        foreach (var (service, at) in rule.FindRecoveries(recovered))
            await writer.ResolveAsync($"{rule.RuleName}:{service}", at, ct);
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
