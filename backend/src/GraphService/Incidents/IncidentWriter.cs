using GraphService.Mapping;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace GraphService.Incidents;

/// <summary>Groups uncontained alerts into Incident nodes and keeps incident properties in step with their alerts.</summary>
public class IncidentWriter(IDriver driver, IOptions<IncidentOptions> options, ILogger<IncidentWriter> log) : IIncidentCorrelation
{
    public async Task CorrelateAsync(CancellationToken ct)
    {
        var window = TimeSpan.FromMinutes(options.Value.WindowMinutes);

        // Bring statuses up to date first so a freshly resolved incident is not reused below.
        await RecomputeAllAsync();

        var uncontained = await ReadUncontainedAsync();
        if (uncontained.Count > 0)
        {
            var existing = await ReadIncidentsAsync();
            var assignments = IncidentCorrelator.Assign(uncontained, existing, window);
            await WriteAssignmentsAsync(assignments);
            log.LogInformation("Correlated {Alerts} alerts into incidents", assignments.Count);
        }

        await RecomputeAllAsync();
    }

    private async Task<List<AlertForCorrelation>> ReadUncontainedAsync()
    {
        await using var session = driver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("""
                MATCH (a:Alert)
                WHERE NOT EXISTS { MATCH (:Incident)-[:CONTAINS]->(a) }
                CALL {
                    WITH a
                    OPTIONAL MATCH (ev:Event)-[:CREATED_ALERT]->(a)
                    RETURN collect(ev.sourceIp) AS ips, collect(ev) AS events
                }
                CALL {
                    WITH events
                    UNWIND events AS ev
                    OPTIONAL MATCH (u:User)-[:TRIGGERED]->(ev)
                    RETURN collect(DISTINCT u.userId) AS userIds
                }
                RETURN a.alertId AS alertId, a.timestamp AS ts, a.ruleName AS rule, a.severity AS severity,
                       a.status AS status, a.affectedService AS service, a.affectedUser AS affectedUser,
                       ips, userIds
                """);
            return await cursor.ToListAsync();
        });
        if (rows.Count == 0) return [];

        var resolved = await ResolveAffectedUsersAsync(
            rows.Select(r => r["affectedUser"].As<string?>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct().ToList());

        return rows.Select(r =>
        {
            var raw = r["affectedUser"].As<string?>();
            string? affectedUserId = raw is not null && resolved.TryGetValue(raw, out var id) ? id : null;
            var subject = SubjectResolver.Resolve(
                affectedUserId,
                r["userIds"].As<List<string>>(),
                r["ips"].As<List<string?>>().Where(v => v is not null).Select(v => v!).ToList(),
                r["service"].As<string?>());
            return new AlertForCorrelation(
                r["alertId"].As<string>(), r["ts"].As<ZonedDateTime>().ToDateTimeOffset(),
                r["rule"].As<string>(), r["severity"].As<string>(), r["status"].As<string>(), subject);
        }).ToList();
    }

    /// <summary>Maps raw Alert.affectedUser values to User.userId (spec 4.3); values matching no node are omitted.</summary>
    private async Task<Dictionary<string, string>> ResolveAffectedUsersAsync(IReadOnlyList<string> raws)
    {
        var result = new Dictionary<string, string>();
        if (raws.Count == 0) return result;

        var probes = raws.Select(raw =>
        {
            var trimmed = raw.Trim();
            return new Dictionary<string, object?>
            {
                ["raw"] = raw,
                ["userId"] = Guid.TryParse(trimmed, out var g) ? g.ToString() : trimmed,
                ["emailKey"] = UserMatcher.LooksLikeEmail(trimmed) ? UserMatcher.EmailKey(trimmed) : null,
                ["unknownId"] = UserMatcher.LooksLikeEmail(trimmed) ? UserMatcher.UnknownUserId(trimmed) : null,
            };
        }).ToList();

        await using var session = driver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("""
                UNWIND $probes AS p
                OPTIONAL MATCH (u:User)
                WHERE (p.emailKey IS NULL AND u.userId = p.userId)
                   OR (p.emailKey IS NOT NULL AND u.email = p.emailKey)
                WITH p, u ORDER BY coalesce(u.unknownAccount, false) ASC, u.userId ASC
                WITH p, head(collect(u)) AS u
                RETURN p.raw AS raw, u.userId AS userId
                """, new { probes });
            return await cursor.ToListAsync();
        });
        foreach (var r in rows)
        {
            var id = r["userId"].As<string?>();
            if (id is not null) result[r["raw"].As<string>()] = id;
        }
        return result;
    }

    private async Task<List<OpenIncident>> ReadIncidentsAsync()
    {
        await using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                "MATCH (i:Incident) WHERE i.lastAlertAt IS NOT NULL RETURN i.incidentId AS id, i.subject AS subject, i.lastAlertAt AS last, i.status AS status");
            var rows = await cursor.ToListAsync();
            return rows.Select(r => new OpenIncident(
                r["id"].As<string>(), r["subject"].As<string>(),
                r["last"].As<ZonedDateTime>().ToDateTimeOffset(), r["status"].As<string?>() ?? "OPEN")).ToList();
        });
    }

    private async Task WriteAssignmentsAsync(IReadOnlyList<IncidentAssignment> assignments)
    {
        if (assignments.Count == 0) return;
        var rows = assignments.Select(a => new Dictionary<string, object?>
        {
            ["incidentId"] = a.IncidentId, ["alertId"] = a.AlertId, ["subject"] = a.Subject,
        }).ToList();
        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
                UNWIND $rows AS r
                MATCH (a:Alert {alertId: r.alertId})
                MERGE (i:Incident {incidentId: r.incidentId})
                  ON CREATE SET i.subject = r.subject
                MERGE (i)-[:CONTAINS]->(a)
                """, new { rows });
        });
    }

    /// <summary>Recomputes derived properties for every incident from its contained alerts.</summary>
    private async Task RecomputeAllAsync()
    {
        await using var session = driver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("""
                MATCH (i:Incident)-[:CONTAINS]->(a:Alert)
                WITH i, collect({alertId: a.alertId, ts: a.timestamp, rule: a.ruleName, severity: a.severity, status: a.status}) AS alerts
                OPTIONAL MATCH (u:User {userId: CASE WHEN i.subject STARTS WITH 'user:' THEN substring(i.subject, 5) END})
                RETURN i.incidentId AS id, i.subject AS subject, u.email AS email, u.name AS name, alerts
                """);
            return await cursor.ToListAsync();
        });

        var updates = new List<Dictionary<string, object?>>();
        foreach (var r in rows)
        {
            var subject = r["subject"].As<string>();
            var alerts = r["alerts"].As<List<Dictionary<string, object>>>().Select(m => new AlertForCorrelation(
                (string)m["alertId"], ((ZonedDateTime)m["ts"]).ToDateTimeOffset(),
                (string)m["rule"], (string)m["severity"], (string)m["status"], subject)).ToList();
            var display = IncidentCorrelator.SubjectDisplay(subject, r["email"].As<string?>(), r["name"].As<string?>());
            var d = IncidentCorrelator.Derive(subject, alerts, display);
            updates.Add(new Dictionary<string, object?>
            {
                ["id"] = r["id"].As<string>(), ["subject"] = d.Subject, ["severity"] = d.Severity, ["status"] = d.Status,
                ["title"] = d.Title, ["escalated"] = d.Escalated, ["priority"] = d.Priority,
                ["openedAt"] = d.OpenedAt, ["lastAlertAt"] = d.LastAlertAt,
            });
        }
        if (updates.Count == 0) return;

        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
                UNWIND $updates AS u
                MATCH (i:Incident {incidentId: u.id})
                SET i.subject = u.subject, i.severity = u.severity, i.status = u.status, i.title = u.title,
                    i.escalated = u.escalated, i.priority = u.priority, i.openedAt = u.openedAt, i.lastAlertAt = u.lastAlertAt
                """, new { updates });
        });
    }
}
