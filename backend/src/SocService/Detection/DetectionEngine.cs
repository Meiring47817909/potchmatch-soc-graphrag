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

        // Events linked only to RESOLVED alerts of this rule must not be re-evaluated,
        // otherwise a resolved alert would be re-opened from the same events. Events that
        // are also linked to a still-active alert of the rule stay visible: a rule can link
        // the same events to several alerts (Rule 1: account alert + IP alert), and
        // resolving one must not stop its sibling from absorbing new events.
        var events = await db.Events.AsNoTracking()
            .Where(e => types.Contains(e.EventType) && e.Timestamp >= from)
            .Where(e => !db.AlertEvents.Any(ae => ae.EventId == e.EventId &&
                db.Alerts.Any(a => a.AlertId == ae.AlertId
                    && a.Status == SocConstants.Status.Resolved
                    && a.RuleName == ruleName))
                || db.AlertEvents.Any(ae => ae.EventId == e.EventId &&
                db.Alerts.Any(a => a.AlertId == ae.AlertId
                    && a.Status != SocConstants.Status.Resolved
                    && a.RuleName == ruleName)))
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct);

        foreach (var match in rule.Evaluate(events, now).ToList())
        {
            // Per dedup key: events already in a RESOLVED alert with this key are consumed.
            var resolvedForKey = (await db.AlertEvents
                .Where(ae => db.Alerts.Any(a => a.AlertId == ae.AlertId
                    && a.DedupKey == match.DedupKey
                    && a.Status == SocConstants.Status.Resolved))
                .Select(ae => ae.EventId)
                .ToListAsync(ct)).ToHashSet();

            var effective = match;
            if (match.EventIds.Any(resolvedForKey.Contains))
            {
                // Re-evaluate without the consumed events; the match survives only if
                // the rule's condition still holds for what remains.
                var remaining = events.Where(e => !resolvedForKey.Contains(e.EventId)).ToList();
                var again = remaining.Count == 0
                    ? null
                    : rule.Evaluate(remaining, now).FirstOrDefault(m => m.DedupKey == match.DedupKey);
                if (again is null) continue;
                effective = again;
            }

            await writer.ApplyAsync(rule, effective, now, ct);
        }
    }
}
