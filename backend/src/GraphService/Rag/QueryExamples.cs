using GraphService.Queries;

namespace GraphService.Rag;

public sealed record QueryExample(string Name, string Question, string Cypher);

/// <summary>Worked question-to-Cypher examples for prompting, one per named query in <see cref="GraphQueries"/>.</summary>
public static class QueryExamples
{
    public static IReadOnlyList<QueryExample> All { get; } =
    [
        new(GraphQueries.AlertsAffectingServiceName,
            "Which alerts affect the service named $name?", GraphQueries.AlertsAffectingService),
        new(GraphQueries.UsersHighSeverityName,
            "Which users triggered HIGH severity alerts, and how many each?", GraphQueries.UsersHighSeverity),
        new(GraphQueries.ThreatsForServiceName,
            "Which threats target or are indicated by alerts on the service named $name?", GraphQueries.ThreatsForService),
        new(GraphQueries.ControlsForAlertName,
            "Which controls, documents and vulnerabilities relate to the alert with id $alertId?", GraphQueries.ControlsForAlert),
        new(GraphQueries.DependencyImpactName,
            "Which services depend, directly or transitively, on the service named $name?", GraphQueries.DependencyImpact),
        new(GraphQueries.IncidentsName,
            "List incidents with status $status (pass null for all incidents).", GraphQueries.Incidents),
        new(GraphQueries.IncidentDetailName,
            "What alerts, users, services and threats are part of the incident with id $incidentId?", GraphQueries.IncidentDetail),
    ];
}
