namespace RagService.Clients;

public interface IGraphQueryClient
{
    Task<GraphSchemaDto> GetSchemaAsync(CancellationToken ct);

    /// <summary>400 from GraphService becomes Ok=false; 503, 401 and connection failures throw <see cref="GraphUnavailableException"/>.</summary>
    Task<GraphQueryOutcome> QueryAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct);
}
