using System.Text.Json;
using RagService.Clients;

namespace RagService.Pipeline;

/// <summary>POST /api/rag/ask body. Optional context ids are bound as $alertId / $incidentId.</summary>
public sealed record AskRequest(string Question, string? AlertId, string? IncidentId);

/// <summary>How the evidence was retrieved (spec §5.4), plus the citation check.</summary>
public sealed record RetrievalInfo(
    string Method,
    string Model,
    int Attempts,
    string? Explanation,
    int RowCount,
    bool Truncated,
    IReadOnlyList<string> UnverifiedCitations);

public sealed record SourceRef(string Id, string? Name, string? Url);

/// <summary>Answer plus the full evidence it was grounded in (spec §5.4, camelCase JSON).</summary>
public sealed record RagAnswer(
    string Question,
    bool Answered,
    string Answer,
    string Mode,
    RetrievalInfo Retrieval,
    string? Cypher,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<EvidenceNode> Nodes,
    IReadOnlyList<EvidenceRel> Relationships,
    IReadOnlyList<Dictionary<string, JsonElement>> Rows,
    IReadOnlyList<SourceRef> Sources)
{
    public const string ModeText2Cypher = "text2cypher";
    public const string MethodText2Cypher = "LLM-generated Cypher, validated read-only, executed on Neo4j";
}

/// <summary>Model output of the Cypher step.</summary>
public sealed record CypherDraft(string? Cypher, Dictionary<string, JsonElement>? Parameters, string? Explanation);

/// <summary>Model output of the answer step.</summary>
public sealed record AnswerDraft(string? Answer);
