using System.Security.Cryptography;
using System.Text;

namespace GraphService.Incidents;

public record AlertForCorrelation(string AlertId, DateTimeOffset Timestamp, string RuleName, string Severity, string Status, string Subject);

public record OpenIncident(string IncidentId, string Subject, DateTimeOffset LastAlertAt, string Status);

public record IncidentAssignment(string IncidentId, string AlertId, string Subject);

/// <summary>Properties of an incident derived from its contained alerts.</summary>
public record IncidentDerived(
    string Subject, string Severity, string Status, string Title, bool Escalated, string Priority,
    DateTimeOffset OpenedAt, DateTimeOffset LastAlertAt);

public static class IncidentIds
{
    public static string For(string subject, string firstAlertId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(subject + firstAlertId));
        return "INC-" + Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}

public static class IncidentCorrelator
{
    public static IReadOnlyList<IncidentAssignment> Assign(
        IReadOnlyList<AlertForCorrelation> uncontained, IReadOnlyList<OpenIncident> existing, TimeSpan window)
    {
        var open = existing
            .Where(i => !IsResolved(i.Status))
            .Select(i => new Working(i.IncidentId, i.Subject, i.LastAlertAt))
            .OrderBy(i => i.LastAlertAt).ThenBy(i => i.IncidentId, StringComparer.Ordinal)
            .ToList();

        var result = new List<IncidentAssignment>();
        foreach (var alert in uncontained
                     .OrderBy(a => a.Timestamp).ThenBy(a => a.AlertId, StringComparer.Ordinal))
        {
            var target = open
                .Where(i => i.Subject == alert.Subject && i.LastAlertAt >= alert.Timestamp - window)
                .OrderByDescending(i => i.LastAlertAt).ThenBy(i => i.IncidentId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (target is null)
            {
                target = new Working(IncidentIds.For(alert.Subject, alert.AlertId), alert.Subject, alert.Timestamp);
                open.Add(target);
            }
            else if (alert.Timestamp > target.LastAlertAt)
            {
                target.LastAlertAt = alert.Timestamp;
            }
            result.Add(new IncidentAssignment(target.IncidentId, alert.AlertId, alert.Subject));
        }
        return result;
    }

    public static IncidentDerived Derive(string subject, IReadOnlyList<AlertForCorrelation> alerts)
    {
        var rules = alerts.Select(a => a.RuleName).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var severity = alerts.OrderByDescending(a => SeverityRank(a.Severity)).First().Severity;
        var open = alerts.Any(a => a.Status.Equals("OPEN", StringComparison.OrdinalIgnoreCase)
                                   || a.Status.Equals("ACKNOWLEDGED", StringComparison.OrdinalIgnoreCase));
        var escalated = rules.Count >= 2;
        return new IncidentDerived(
            subject, severity, open ? "OPEN" : "RESOLVED",
            $"Activity from {subject}: {string.Join(", ", rules)}",
            escalated, escalated ? "HIGH" : severity,
            alerts.Min(a => a.Timestamp), alerts.Max(a => a.Timestamp));
    }

    private static int SeverityRank(string severity) => severity.ToUpperInvariant() switch
    {
        "CRITICAL" => 4,
        "HIGH" => 3,
        "MEDIUM" => 2,
        "LOW" => 1,
        _ => 0,
    };

    private static bool IsResolved(string status) => status.Equals("RESOLVED", StringComparison.OrdinalIgnoreCase);

    private sealed class Working(string incidentId, string subject, DateTimeOffset lastAlertAt)
    {
        public string IncidentId { get; } = incidentId;
        public string Subject { get; } = subject;
        public DateTimeOffset LastAlertAt { get; set; } = lastAlertAt;
    }
}
