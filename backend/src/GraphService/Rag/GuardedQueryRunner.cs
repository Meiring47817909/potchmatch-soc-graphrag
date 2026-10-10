using GraphService.Queries;
using Neo4j.Driver;

namespace GraphService.Rag;

public sealed record GuardedQueryResult(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphRelDto> Relationships,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated);

/// <summary>
/// Runs already-guarded Cypher in a read transaction with a 10 s timeout, keeping at most 200 records.
/// The read transaction is the second line of defence: Neo4j itself refuses writes in it.
/// </summary>
public class GuardedQueryRunner(IDriver driver)
{
    public const int MaxRecords = 200;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public virtual async Task<GuardedQueryResult> RunAsync(string cypher,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var args = parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        var (records, truncated) = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, args);
            var list = new List<IRecord>();
            var more = false;
            while (await cursor.FetchAsync())
            {
                ct.ThrowIfCancellationRequested();
                if (list.Count == MaxRecords) { more = true; break; }
                list.Add(cursor.Current);
            }
            return (list, more);
        }, tc => tc.WithTimeout(Timeout));

        var (nodes, rels, rows) = RecordShaper.Shape(records);
        return new GuardedQueryResult(nodes, rels, rows, truncated);
    }
}
