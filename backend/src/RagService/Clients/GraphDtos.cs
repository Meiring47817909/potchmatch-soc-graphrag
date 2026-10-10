using System.Text.Json;

namespace RagService.Clients;

public sealed record EvidenceNode(string Id, string Label, Dictionary<string, JsonElement> Properties);

public sealed record EvidenceRel(string Id, string Type, string Start, string End);

public sealed record GraphEvidence(
    string Cypher,
    Dictionary<string, JsonElement> Parameters,
    IReadOnlyList<EvidenceNode> Nodes,
    IReadOnlyList<EvidenceRel> Relationships,
    IReadOnlyList<Dictionary<string, JsonElement>> Rows,
    bool Truncated);

/// <summary>Result of one guarded query: evidence, or a 400 (rejected / cypher_error) with its reason.</summary>
public sealed record GraphQueryOutcome(bool Ok, GraphEvidence? Evidence, string? Error, string? Reason);

public sealed record SchemaLabel(string Label, string KeyProperty, IReadOnlyList<string> Properties, long Count);

public sealed record SchemaRelType(string Type, IReadOnlyList<string> From, IReadOnlyList<string> To, long Count);

public sealed record SchemaSamples(
    IReadOnlyList<string> RuleNames, IReadOnlyList<string> ThreatIds, IReadOnlyList<string> Severities,
    IReadOnlyList<string> AlertStatuses, IReadOnlyList<string> IncidentStatuses, IReadOnlyList<string> ServiceNames);

public sealed record SchemaExample(string Name, string Question, string Cypher);

public sealed record GraphSchemaDto(
    IReadOnlyList<SchemaLabel> Labels, IReadOnlyList<SchemaRelType> RelationshipTypes,
    SchemaSamples Samples, IReadOnlyList<SchemaExample> Examples);
