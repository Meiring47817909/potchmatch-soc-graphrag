namespace GraphService.Queries;

public record GraphNodeDto(string Id, string Label, IReadOnlyDictionary<string, object?> Properties);

public record GraphRelDto(string Id, string Type, string Start, string End);

/// <summary>Evidence-shaped query result: the Cypher, its parameters, the traversed graph and a flat table.</summary>
public record GraphQueryResult(
    string Query,
    string Cypher,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphRelDto> Relationships,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);
