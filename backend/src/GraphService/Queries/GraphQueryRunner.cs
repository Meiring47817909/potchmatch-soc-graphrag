using Neo4j.Driver;

namespace GraphService.Queries;

/// <summary>Runs a named Cypher query read-only and shapes the records as an evidence-shaped result.</summary>
public class GraphQueryRunner(IDriver driver)
{
    public async Task<GraphQueryResult> RunAsync(string name, string cypher,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var args = parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        var records = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, args);
            return await cursor.ToListAsync(ct);
        });

        var (nodes, rels, rows) = RecordShaper.Shape(records);

        return new GraphQueryResult(name, cypher, parameters, nodes, rels, rows);
    }

    public async Task<bool> ExistsAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var args = parameters.ToDictionary(kv => kv.Key, kv => kv.Value);
        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher, args);
            var records = await cursor.ToListAsync(ct);
            return records.Count > 0 && records[0][0].As<bool>();
        });
    }
}

/// <summary>Shapes driver records into evidence form: graph values into nodes/relationships, scalars into table rows.</summary>
internal static class RecordShaper
{
    public static (List<GraphNodeDto> Nodes, List<GraphRelDto> Relationships, List<IReadOnlyDictionary<string, object?>> Rows)
        Shape(IEnumerable<IRecord> records)
    {
        var nodes = new Dictionary<string, GraphNodeDto>();
        var rels = new Dictionary<string, GraphRelDto>();
        var rows = new List<IReadOnlyDictionary<string, object?>>();

        foreach (var record in records)
        {
            var row = new Dictionary<string, object?>();
            foreach (var key in record.Keys)
            {
                var value = record[key];
                if (IsGraphColumn(value)) Collect(value, nodes, rels);
                else row[key] = JsonSafe.Convert(value);
            }
            if (row.Count > 0) rows.Add(row);
        }

        return (nodes.Values.ToList(), rels.Values.ToList(), rows);
    }

    private static bool IsGraphValue(object? v) => v is INode or IRelationship or IPath;

    // A column is graph data when it is a node/relationship/path, or a non-empty list of them (nulls ignored).
    private static bool IsGraphColumn(object? v) =>
        IsGraphValue(v) ||
        (v is System.Collections.IList l && l.Count > 0 && l.Cast<object?>().All(x => x is null || IsGraphValue(x)) && l.Cast<object?>().Any(x => x is not null));

    private static void Collect(object? v, Dictionary<string, GraphNodeDto> nodes, Dictionary<string, GraphRelDto> rels)
    {
        switch (v)
        {
            case INode n: AddNode(n, nodes); break;
            case IRelationship r: AddRel(r, rels); break;
            case IPath p:
                foreach (var n in p.Nodes) AddNode(n, nodes);
                foreach (var r in p.Relationships) AddRel(r, rels);
                break;
            case System.Collections.IList l:
                foreach (var x in l) Collect(x, nodes, rels);
                break;
        }
    }

    private static void AddNode(INode n, Dictionary<string, GraphNodeDto> nodes)
    {
        if (nodes.ContainsKey(n.ElementId)) return;
        nodes[n.ElementId] = new GraphNodeDto(n.ElementId, n.Labels.FirstOrDefault() ?? "",
            n.Properties.ToDictionary(kv => kv.Key, kv => JsonSafe.Convert(kv.Value)));
    }

    private static void AddRel(IRelationship r, Dictionary<string, GraphRelDto> rels)
    {
        if (!rels.ContainsKey(r.ElementId))
            rels[r.ElementId] = new GraphRelDto(r.ElementId, r.Type, r.StartNodeElementId, r.EndNodeElementId);
    }
}

/// <summary>Converts driver values to JSON-friendly ones: temporal to ISO-8601 strings, collections recursively.</summary>
internal static class JsonSafe
{
    public static object? Convert(object? v) => v switch
    {
        null => null,
        string or bool or long or int or double or float => v,
        ZonedDateTime z => z.ToDateTimeOffset().ToString("o"),
        LocalDateTime ldt => ldt.ToDateTime().ToString("o"),
        LocalDate d => d.ToDateOnly().ToString("o"),
        LocalTime t => t.ToTimeOnly().ToString("o"),
        OffsetTime ot => ot.ToString(),
        Duration du => du.ToString(),
        INode n => n.Properties.ToDictionary(kv => kv.Key, kv => Convert(kv.Value)),
        IRelationship r => r.Properties.ToDictionary(kv => kv.Key, kv => Convert(kv.Value)),
        IPath p => p.Nodes.Select(n => Convert(n)).ToList(),
        IDictionary<string, object> m => m.ToDictionary(kv => kv.Key, kv => Convert(kv.Value)),
        byte[] b => System.Convert.ToBase64String(b),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Convert).ToList(),
        _ => v.ToString(),
    };
}
