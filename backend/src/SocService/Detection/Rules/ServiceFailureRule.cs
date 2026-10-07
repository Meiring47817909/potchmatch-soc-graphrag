using Microsoft.Extensions.Options;
using SocService.Models;

namespace SocService.Detection.Rules;

/// <summary>Rule 4: a service reported critical, or repeated inter-service failures / unhandled exceptions.</summary>
public class ServiceFailureRule(IOptions<DetectionOptions> options) : IDetectionRule
{
    public const string HealthCritical = "SERVICE_HEALTH_CRITICAL";
    public const string HealthRecovered = "SERVICE_HEALTH_RECOVERED";
    private const string CallFailed = "INTER_SERVICE_CALL_FAILED";
    private const string UnhandledException = "UNHANDLED_EXCEPTION";

    private DetectionOptions.ServiceFailureOptions Cfg => options.Value.ServiceFailure;

    public string RuleName => SocConstants.Rules.ServiceFailure;
    public string Purpose => "Detect a backend service that is down or repeatedly failing, and clear the alert when it recovers.";
    public IReadOnlyList<string> InputEventTypes { get; } = [HealthCritical, HealthRecovered, CallFailed, UnhandledException];
    public string DetectionCondition =>
        $"Any SERVICE_HEALTH_CRITICAL event, or {Cfg.Threshold} or more INTER_SERVICE_CALL_FAILED or UNHANDLED_EXCEPTION events for the same service within {Cfg.WindowMinutes} minutes, with no later SERVICE_HEALTH_RECOVERED event.";
    public string Severity => SocConstants.Severity.High;
    public string AlertGenerated => "SERVICE_FAILURE alert (HIGH) naming the affected service, auto-resolved when the service reports recovery.";
    public string RecommendedResponse =>
        "Check the affected service's container and logs; dependent services are degraded until it recovers.";
    public TimeSpan Window => TimeSpan.FromMinutes(Cfg.WindowMinutes);

    private static string? ServiceOf(SocEvent e) =>
        (e.EventType == UnhandledException ? e.ServiceName : e.AffectedEntity)?.Trim() is { Length: > 0 } s ? s : null;

    /// <summary>Latest SERVICE_HEALTH_RECOVERED timestamp per service.</summary>
    public IEnumerable<(string Service, DateTimeOffset At)> FindRecoveries(IReadOnlyList<SocEvent> events) =>
        events
            .Where(e => e.EventType == HealthRecovered)
            .Select(e => (Service: ServiceOf(e), e.Timestamp))
            .Where(x => x.Service is not null)
            .GroupBy(x => x.Service!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Max(x => x.Timestamp)));

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        var cfg = Cfg;
        var from = now - Window;
        var recoveries = FindRecoveries(events).ToDictionary(r => r.Service, r => r.At);

        var groups = events
            .Where(e => e.EventType != HealthRecovered && e.Timestamp >= from)
            .Select(e => (Event: e, Service: ServiceOf(e)))
            .Where(x => x.Service is not null)
            .Where(x => !recoveries.TryGetValue(x.Service!, out var r) || x.Event.Timestamp > r)
            .GroupBy(x => x.Service!)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var evs = g.Select(x => x.Event).ToList();
            var critical = evs.Count(e => e.EventType == HealthCritical);
            var failures = evs.Count - critical;
            if (critical == 0 && failures < cfg.Threshold)
                continue;

            var description = critical > 0
                ? $"{g.Key} reported a critical health status."
                : $"{failures} inter-service call failures or unhandled exceptions for {g.Key} within {cfg.WindowMinutes} minutes.";

            yield return new RuleMatch(
                $"{RuleName}:{g.Key}", description, g.Key, null, g.Key,
                evs.Select(e => e.EventId).ToList());
        }
    }
}
