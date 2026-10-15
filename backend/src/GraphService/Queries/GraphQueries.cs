namespace GraphService.Queries;

/// <summary>
/// Named, parameterised Cypher. Every query returns the paths/nodes it traverses (collected into
/// nodes/relationships by <see cref="GraphQueryRunner"/>) plus scalar columns that become table rows.
/// </summary>
public static class GraphQueries
{
    public const string AlertsAffectingServiceName = "alerts-affecting-service";
    public const string UsersHighSeverityName = "users-high-severity";
    public const string ThreatsForServiceName = "threats-for-service";
    public const string ControlsForAlertName = "controls-for-alert";
    public const string DependencyImpactName = "dependency-impact";
    public const string IncidentsName = "incidents";
    public const string IncidentDetailName = "incident-detail";

    public const string AlertsAffectingService = """
        MATCH p = (a:Alert)-[:AFFECTS]->(s:Service {name: $name})
        RETURN p, a.alertId AS alertId, a.ruleName AS ruleName, a.severity AS severity,
               a.status AS status, a.timestamp AS timestamp, a.description AS description
        ORDER BY a.timestamp DESC, a.alertId
        """;

    public const string UsersHighSeverity = """
        MATCH p = (u:User)-[:TRIGGERED]->(:Event)-[:CREATED_ALERT]->(a:Alert)
        WHERE toUpper(a.severity) = 'HIGH'
        WITH u, collect(p) AS paths, count(DISTINCT a) AS alertCount
        RETURN paths, u.userId AS userId, u.name AS name, u.email AS email, alertCount
        ORDER BY alertCount DESC, u.userId
        """;

    public const string ThreatsForService = """
        MATCH p = (t:Threat)-[:TARGETS]->(s:Service {name: $name})
        RETURN collect(p) AS paths, t.id AS threatId, t.name AS name, 'TARGETS' AS via, 0 AS alertCount
        UNION
        MATCH p = (s:Service {name: $name})<-[:AFFECTS]-(a:Alert)-[:INDICATES]->(t:Threat)
        RETURN collect(p) AS paths, t.id AS threatId, t.name AS name, 'ALERT' AS via, count(DISTINCT a) AS alertCount
        """;

    public const string ControlsForAlert = """
        MATCH p = (a:Alert {alertId: $alertId})-[:INDICATES]->(t:Threat)<-[:MITIGATES]-(c:Control)
        OPTIONAL MATCH dp = (d:Document)-[:DESCRIBES]->(c)
        WITH p, t, c, collect(dp) AS dps, collect(DISTINCT d.id) AS controlDocuments
        OPTIONAL MATCH rp = (rd:Document)-[:SUPPORTS_RESPONSE_TO]->(t)
        WITH p, t, c, dps, controlDocuments, collect(rp) AS rps, collect(DISTINCT rd.id) AS responseDocuments
        OPTIONAL MATCH vp = (t)-[:EXPLOITS]->(v:Vulnerability)
        WITH p, t, c, dps, controlDocuments, rps, responseDocuments, collect(vp) AS vps, collect(DISTINCT v.id) AS vulnerabilities
        RETURN [p] + dps + rps + vps AS paths, t.id AS threatId, t.name AS threatName,
               c.id AS controlId, c.name AS controlName,
               controlDocuments, responseDocuments, vulnerabilities
        ORDER BY c.id
        """;

    public const string DependencyImpact = """
        MATCH p = (dep:Service)-[:DEPENDS_ON*1..]->(s:Service {name: $name})
        WITH dep, collect(p) AS paths, min(length(p)) AS depth
        RETURN paths, dep.name AS service, depth
        ORDER BY depth, service
        """;

    public const string Incidents = """
        MATCH (i:Incident)
        WHERE $status IS NULL OR i.status = $status
        OPTIONAL MATCH (i)-[:CONTAINS]->(a:Alert)
        WITH i, a ORDER BY a.ruleName
        WITH i, collect(DISTINCT a.ruleName) AS rules
        RETURN i, i.incidentId AS incidentId, i.title AS title, i.subject AS subject, i.severity AS severity,
               i.status AS status, i.escalated AS escalated, i.priority AS priority,
               i.openedAt AS openedAt, i.lastAlertAt AS lastAlertAt, rules
        ORDER BY i.lastAlertAt DESC, i.incidentId
        """;

    public const string IncidentDetail = """
        MATCH cp = (i:Incident {incidentId: $incidentId})-[:CONTAINS]->(a:Alert)
        OPTIONAL MATCH up = (u:User)-[:TRIGGERED]->(:Event)-[:CREATED_ALERT]->(a)
        WITH i, cp, a, collect(up) AS ups, collect(DISTINCT u.userId) AS users
        OPTIONAL MATCH sp = (a)-[:AFFECTS]->(s:Service)
        WITH i, cp, a, ups, users, collect(sp) AS sps, collect(DISTINCT s.name) AS services
        OPTIONAL MATCH tp = (a)-[:INDICATES]->(t:Threat)
        WITH i, cp, a, ups, users, sps, services, collect(tp) AS tps, collect(DISTINCT t.id) AS threats
        RETURN [cp] + ups + sps + tps AS paths, i.incidentId AS incidentId,
               a.alertId AS alertId, a.ruleName AS ruleName, a.severity AS severity, a.status AS status,
               a.timestamp AS timestamp, users, services, threats
        ORDER BY a.timestamp, a.alertId
        """;

    // Existence checks (one tiny query per entity kind) so the API can answer 404 instead of an empty result.
    public const string ServiceExists = "MATCH (n:Service {name: $name}) RETURN count(n) > 0 AS exists";
    public const string AlertExists = "MATCH (n:Alert {alertId: $alertId}) RETURN count(n) > 0 AS exists";
    public const string IncidentExists = "MATCH (n:Incident {incidentId: $incidentId}) RETURN count(n) > 0 AS exists";
}
