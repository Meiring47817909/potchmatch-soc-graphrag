using System.Text.Json;
using GraphService.Graph;
using Neo4j.Driver;

namespace GraphService.Seeding;

public class SeedLoader(IDriver driver, ILogger<SeedLoader> log)
{
    /// <summary>
    /// Validates then MERGEs a seed file in one transaction. Returns the refs now known to exist
    /// (this file's nodes plus <paramref name="alreadyLoaded"/>). Throws <see cref="SeedValidationException"/> before writing anything.
    /// </summary>
    public async Task<IReadOnlySet<NodeRef>> LoadAsync(string path, IReadOnlySet<NodeRef> alreadyLoaded, CancellationToken ct)
    {
        SeedFile file;
        try
        {
            file = SeedFile.Parse(await File.ReadAllTextAsync(path, ct));
        }
        catch (JsonException ex)
        {
            throw new SeedValidationException(path, [$"Malformed JSON: {ex.Message}"]);
        }

        var errors = SeedValidator.Validate(file, alreadyLoaded);
        if (errors.Count > 0) throw new SeedValidationException(path, errors);

        await using var session = driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            // Labels and relationship types cannot be parameters; they are interpolated only after validation against GraphSchema.
            foreach (var group in file.Nodes.GroupBy(n => n.Label))
            {
                var keyProp = GraphSchema.KeyProperty[group.Key];
                var rows = group.Select(n => new Dictionary<string, object?>
                {
                    ["key"] = n.Key,
                    ["props"] = ToProps(n.Properties),
                }).ToList();
                var cursor = await tx.RunAsync(
                    $"UNWIND $rows AS row MERGE (n:{group.Key} {{{keyProp}: row.key}}) SET n += row.props", new { rows });
                await cursor.ConsumeAsync();
            }

            foreach (var group in file.Relationships.GroupBy(r => (r.Type, FromLabel: r.From.Label, ToLabel: r.To.Label)))
            {
                var rows = group.Select(r => new Dictionary<string, object?> { ["from"] = r.From.Key, ["to"] = r.To.Key }).ToList();
                var fromKey = GraphSchema.KeyProperty[group.Key.FromLabel];
                var toKey = GraphSchema.KeyProperty[group.Key.ToLabel];
                var cursor = await tx.RunAsync(
                    $"UNWIND $rows AS row MATCH (a:{group.Key.FromLabel} {{{fromKey}: row.from}}) MATCH (b:{group.Key.ToLabel} {{{toKey}: row.to}}) " +
                    $"MERGE (a)-[:{group.Key.Type}]->(b)", new { rows });
                await cursor.ConsumeAsync();
            }
        });

        log.LogInformation("Loaded seed {Path}: {Nodes} nodes, {Relationships} relationships", path, file.Nodes.Count, file.Relationships.Count);

        var result = new HashSet<NodeRef>(alreadyLoaded);
        foreach (var n in file.Nodes) result.Add(new NodeRef(n.Label, n.Key));
        return result;
    }

    private static Dictionary<string, object?> ToProps(IReadOnlyDictionary<string, JsonElement>? props)
    {
        var result = new Dictionary<string, object?>();
        if (props is null) return result;
        foreach (var (k, v) in props)
            if (v.ValueKind != JsonValueKind.Null) result[k] = ToValue(v);
        return result;
    }

    private static object? ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.Array => e.EnumerateArray().Where(x => x.ValueKind != JsonValueKind.Null).Select(ToValue).ToList(),
        _ => e.GetRawText(),
    };
}
