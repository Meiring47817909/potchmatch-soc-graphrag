using GraphService.Mapping;
using Neo4j.Driver;

namespace GraphService.Sync;

/// <summary>Idempotent MERGE-based writer for live data (players, alerts, events, sync cursors).</summary>
public class GraphWriter(IDriver driver, ILogger<GraphWriter> log)
{
    public async Task UpsertPlayersAsync(IReadOnlyList<SyncPlayer> players, CancellationToken ct)
    {
        if (players.Count == 0) return;
        var rows = players.Select(p => new Dictionary<string, object?>
        {
            ["id"] = p.Id.ToString(),
            ["email"] = UserMatcher.EmailKey(p.Email),
            ["name"] = $"{p.Name} {p.Surname}",
            ["role"] = p.Role,
            ["unknownId"] = UserMatcher.UnknownUserId(p.Email),
        }).ToList();

        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
                UNWIND $rows AS p
                MERGE (u:User {userId: p.id})
                SET u.email = p.email, u.name = p.name, u.unknownAccount = false
                """, new { rows });
            var missingCursor = await tx.RunAsync("""
                UNWIND $rows AS p
                WITH p WHERE NOT EXISTS { MATCH (:Role {name: p.role}) }
                RETURN DISTINCT p.role AS role
                """, new { rows });
            foreach (var m in await missingCursor.ToListAsync())
                log.LogWarning("Role {Role} has no Role node; keeping existing HAS_ROLE for affected players", m["role"].As<string>());
            // Replace HAS_ROLE with the current role (only when that Role node exists).
            await tx.RunAsync("""
                UNWIND $rows AS p
                MATCH (u:User {userId: p.id})-[old:HAS_ROLE]->(r:Role)
                WHERE r.name <> p.role AND EXISTS { MATCH (:Role {name: p.role}) }
                DELETE old
                """, new { rows });
            await tx.RunAsync("""
                UNWIND $rows AS p
                MATCH (u:User {userId: p.id})
                MATCH (r:Role {name: p.role})
                MERGE (u)-[:HAS_ROLE]->(r)
                """, new { rows });
            // A placeholder account created from a failed-login email becomes the real user.
            await tx.RunAsync("""
                UNWIND $rows AS p
                MATCH (u:User {userId: p.id})
                MATCH (x:User {email: p.email, unknownAccount: true})
                WHERE x <> u
                MATCH (x)-[t:TRIGGERED]->(ev:Event)
                MERGE (u)-[:TRIGGERED]->(ev)
                DELETE t
                """, new { rows });
            // Incidents opened against the placeholder now belong to the real user, so later alerts join them.
            await tx.RunAsync("""
                UNWIND $rows AS p
                MATCH (x:User {email: p.email, unknownAccount: true})
                WHERE x.userId <> p.id
                MATCH (i:Incident {subject: 'user:' + x.userId})
                SET i.subject = 'user:' + p.id
                """, new { rows });
            await tx.RunAsync("""
                UNWIND $rows AS p
                MATCH (x:User {email: p.email, unknownAccount: true})
                WHERE x.userId <> p.id
                DETACH DELETE x
                """, new { rows });
        });
    }

    public async Task<bool> HasPlaceholderUsersAsync(CancellationToken ct)
    {
        await using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync("RETURN EXISTS { MATCH (u:User {unknownAccount: true}) } AS any");
            return (await cursor.SingleAsync())["any"].As<bool>();
        });
    }

    public async Task UpsertAlertAsync(SyncAlert alert, CancellationToken ct)
    {
        var hasThreat = RuleThreatMap.TryGetThreatId(alert.RuleName, out var threatId);
        if (!hasThreat)
            log.LogWarning("No threat mapping for rule {Rule}; alert {AlertId} loaded without INDICATES", alert.RuleName, alert.AlertId);

        var alertRow = new Dictionary<string, object?>
        {
            ["alertId"] = alert.AlertId.ToString(),
            ["timestamp"] = alert.Timestamp,
            ["updatedAt"] = alert.UpdatedAt,
            ["ruleName"] = alert.RuleName,
            ["severity"] = alert.Severity,
            ["status"] = alert.Status,
            ["description"] = alert.Description,
            ["affectedService"] = alert.AffectedService,
            ["affectedUser"] = alert.AffectedUser,
            ["affectedEntity"] = alert.AffectedEntity,
            ["recommendedAction"] = alert.RecommendedAction,
        };

        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("""
                MERGE (a:Alert {alertId: $a.alertId})
                SET a.timestamp = $a.timestamp, a.updatedAt = $a.updatedAt, a.ruleName = $a.ruleName,
                    a.severity = $a.severity, a.status = $a.status, a.description = $a.description,
                    a.affectedService = $a.affectedService, a.affectedUser = $a.affectedUser,
                    a.affectedEntity = $a.affectedEntity, a.recommendedAction = $a.recommendedAction
                """, new { a = alertRow });

            await WriteEventsAsync(tx, alert.RelatedEvents);

            if (alert.RelatedEvents.Count > 0)
            {
                await tx.RunAsync("""
                    MATCH (a:Alert {alertId: $alertId})
                    UNWIND $eventIds AS id
                    MATCH (e:Event {eventId: id})
                    MERGE (e)-[:CREATED_ALERT]->(a)
                    """, new { alertId = alert.AlertId.ToString(), eventIds = alert.RelatedEvents.Select(e => e.EventId.ToString()).ToList() });
            }

            if (hasThreat)
            {
                await tx.RunAsync("""
                    MATCH (a:Alert {alertId: $alertId})
                    MATCH (t:Threat {id: $threatId})
                    MERGE (a)-[:INDICATES]->(t)
                    """, new { alertId = alert.AlertId.ToString(), threatId });
            }

            if (!string.IsNullOrWhiteSpace(alert.AffectedService))
            {
                await tx.RunAsync("""
                    MATCH (a:Alert {alertId: $alertId})
                    MERGE (s:Service {name: $service}) ON CREATE SET s.discovered = true
                    MERGE (a)-[:AFFECTS]->(s)
                    """, new { alertId = alert.AlertId.ToString(), service = alert.AffectedService.Trim() });
            }
        });
    }

    public async Task UpsertEventsAsync(IReadOnlyList<SyncEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => WriteEventsAsync(tx, events));
    }

    private static async Task WriteEventsAsync(IAsyncQueryRunner tx, IReadOnlyList<SyncEvent> events)
    {
        if (events.Count == 0) return;
        var rows = events.Select(e => new Dictionary<string, object?>
        {
            ["eventId"] = e.EventId.ToString(),
            ["timestamp"] = e.Timestamp,
            ["serviceName"] = e.ServiceName?.Trim(),
            ["eventType"] = e.EventType,
            ["severity"] = e.Severity,
            ["sourceIp"] = e.SourceIp,
            ["statusCode"] = e.StatusCode,
            ["message"] = e.Message,
            ["correlationId"] = e.CorrelationId,
            ["affectedEntity"] = e.AffectedEntity,
            ["endpointKey"] = EndpointNormaliser.ToKey(e.HttpMethod, e.Endpoint),
            ["guidUserId"] = Guid.TryParse(e.UserId?.Trim(), out var g) ? g.ToString() : null,
            ["emailKey"] = UserMatcher.LooksLikeEmail(e.UserId) ? UserMatcher.EmailKey(e.UserId!) : null,
            ["unknownId"] = UserMatcher.LooksLikeEmail(e.UserId) ? UserMatcher.UnknownUserId(e.UserId!) : null,
        }).ToList();

        await tx.RunAsync("""
            UNWIND $rows AS e
            MERGE (ev:Event {eventId: e.eventId})
            SET ev.timestamp = e.timestamp, ev.eventType = e.eventType, ev.severity = e.severity,
                ev.message = e.message, ev.statusCode = e.statusCode, ev.sourceIp = e.sourceIp,
                ev.correlationId = e.correlationId, ev.affectedEntity = e.affectedEntity
            """, new { rows });
        await tx.RunAsync("""
            UNWIND [e IN $rows WHERE e.serviceName IS NOT NULL AND e.serviceName <> ''] AS e
            MATCH (ev:Event {eventId: e.eventId})
            MERGE (s:Service {name: e.serviceName}) ON CREATE SET s.discovered = true
            MERGE (ev)-[:GENERATED_BY]->(s)
            """, new { rows });
        await tx.RunAsync("""
            UNWIND [e IN $rows WHERE e.endpointKey IS NOT NULL] AS e
            MATCH (ev:Event {eventId: e.eventId})
            MERGE (ep:Endpoint {key: e.endpointKey})
              ON CREATE SET ep.discovered = true,
                            ep.method = split(e.endpointKey, ' ')[0],
                            ep.path = substring(e.endpointKey, size(split(e.endpointKey, ' ')[0]) + 1),
                            ep.protected = false
            MERGE (ev)-[:TARGETED]->(ep)
            """, new { rows });
        await tx.RunAsync("""
            UNWIND [e IN $rows WHERE e.guidUserId IS NOT NULL] AS e
            MATCH (ev:Event {eventId: e.eventId})
            MERGE (u:User {userId: e.guidUserId})
            MERGE (u)-[:TRIGGERED]->(ev)
            """, new { rows });
        await tx.RunAsync("""
            UNWIND [e IN $rows WHERE e.emailKey IS NOT NULL] AS e
            WITH DISTINCT e.emailKey AS key, e.unknownId AS unknownId
            WHERE NOT EXISTS { MATCH (:User {email: key}) }
            MERGE (u:User {userId: unknownId})
              ON CREATE SET u.email = key, u.unknownAccount = true
            """, new { rows });
        await tx.RunAsync("""
            UNWIND [e IN $rows WHERE e.emailKey IS NOT NULL] AS e
            MATCH (ev:Event {eventId: e.eventId})
            MATCH (u:User {email: e.emailKey})
            WITH ev, u ORDER BY coalesce(u.unknownAccount, false) ASC
            WITH ev, head(collect(u)) AS u
            MERGE (u)-[:TRIGGERED]->(ev)
            """, new { rows });
    }

    public async Task<SyncCursor?> GetCursorAsync(string name, CancellationToken ct)
    {
        await using var session = driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                "MATCH (s:SyncState {name: $name}) RETURN s.cursorAt AS at, s.cursorId AS id", new { name });
            var rows = await cursor.ToListAsync();
            if (rows.Count == 0 || rows[0]["at"] is null) return null;
            var at = rows[0]["at"].As<ZonedDateTime>().ToDateTimeOffset();
            return new SyncCursor(at, Guid.Parse(rows[0]["id"].As<string>()));
        });
    }

    public async Task SetCursorAsync(string name, SyncCursor cursor, CancellationToken ct)
    {
        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                "MERGE (s:SyncState {name: $name}) SET s.cursorAt = $at, s.cursorId = $id",
                new { name, at = cursor.At, id = cursor.Id.ToString() });
        });
    }
}
