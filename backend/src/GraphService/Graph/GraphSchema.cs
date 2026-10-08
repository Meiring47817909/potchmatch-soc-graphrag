namespace GraphService.Graph;

/// <summary>The fixed vocabulary of the security knowledge graph: node labels with their unique key property, and relationship types.</summary>
public static class GraphSchema
{
    public static readonly IReadOnlyDictionary<string, string> KeyProperty = new Dictionary<string, string>
    {
        ["Service"] = "name",
        ["Endpoint"] = "key",
        ["Asset"] = "name",
        ["Role"] = "name",
        ["User"] = "userId",
        ["Event"] = "eventId",
        ["Alert"] = "alertId",
        ["Incident"] = "incidentId",
        ["Threat"] = "id",
        ["Control"] = "id",
        ["Vulnerability"] = "id",
        ["Document"] = "id",
        ["SyncState"] = "name",
    };

    public static readonly IReadOnlySet<string> RelationshipTypes = new HashSet<string>
    {
        "TRIGGERED", "GENERATED_BY", "TARGETED", "CREATED_ALERT", "INDICATES", "TARGETS",
        "EXPLOITS", "MITIGATES", "CONTAINS", "DEPENDS_ON", "EXPOSES", "HAS_ROLE",
        "DESCRIBES", "SUPPORTS_RESPONSE_TO", "AFFECTS", "USES", "REQUIRES_ROLE",
    };

    public static bool IsKnownLabel(string label) => KeyProperty.ContainsKey(label);

    public static bool IsKnownRelationship(string type) => RelationshipTypes.Contains(type);
}
