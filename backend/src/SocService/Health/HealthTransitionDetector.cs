using SocService.Detection.Rules;
using SocService.Models;

namespace SocService.Health;

public static class HealthTransitionDetector
{
    private const string Critical = "critical";
    private const string Passing = "passing";

    public static IReadOnlyList<SocEvent> Detect(
        IReadOnlyDictionary<string, string>? previous,
        IReadOnlyDictionary<string, string> current,
        DateTimeOffset now)
    {
        if (previous is null)
            return [];

        var events = new List<SocEvent>();
        foreach (var (service, before) in previous.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // A service that vanished from Consul counts as critical.
            var after = current.TryGetValue(service, out var s) ? s : Critical;
            var wasCritical = before == Critical;
            var isCritical = after == Critical;
            if (!wasCritical && isCritical)
                events.Add(Make(service, ServiceFailureRule.HealthCritical, SocConstants.Severity.High,
                    $"{service} health check is critical", now));
            else if (wasCritical && !isCritical)
                events.Add(Make(service, ServiceFailureRule.HealthRecovered, SocConstants.Severity.Low,
                    $"{service} health check recovered ({after})", now));
        }

        return events;
    }

    /// <summary>
    /// Baseline to keep for the next poll: current statuses, plus services that vanished
    /// remembered as critical so their return is reported as a recovery.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NextBaseline(
        IReadOnlyDictionary<string, string>? previous, IReadOnlyDictionary<string, string> current)
    {
        var next = new Dictionary<string, string>(current, StringComparer.Ordinal);
        if (previous is not null)
            foreach (var service in previous.Keys)
                next.TryAdd(service, Critical);
        return next;
    }

    private static SocEvent Make(string service, string type, string severity, string message, DateTimeOffset now) => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = now,
        ReceivedAt = now,
        ServiceName = "soc-service",
        EventType = type,
        Severity = severity,
        Message = message,
        AffectedEntity = service,
    };
}
