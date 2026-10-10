using GraphService.Graph;
using Neo4j.Driver;

namespace GraphService.Rag;

public sealed record LabelInfo(string Label, string KeyProperty, IReadOnlyList<string> Properties, long Count);

public sealed record RelTypeInfo(string Type, IReadOnlyList<string> From, IReadOnlyList<string> To, long Count);

public sealed record SchemaSamples(
    IReadOnlyList<string> RuleNames, IReadOnlyList<string> ThreatIds, IReadOnlyList<string> Severities,
    IReadOnlyList<string> AlertStatuses, IReadOnlyList<string> IncidentStatuses, IReadOnlyList<string> ServiceNames);

public sealed record GraphSchemaDescription(
    IReadOnlyList<LabelInfo> Labels, IReadOnlyList<RelTypeInfo> RelationshipTypes,
    SchemaSamples Samples, IReadOnlyList<QueryExample> Examples);

/// <summary>Describes the live graph (labels, properties, relationship shapes, sample values) plus worked examples, for prompting.</summary>
public class GraphSchemaDescriber(IDriver driver)
{
    public const int MaxSamples = 30;

    public virtual async Task<GraphSchemaDescription> DescribeAsync(CancellationToken ct)
    {
        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        return await session.ExecuteReadAsync(async tx =>
        {
            // Labels and relationship types come from the fixed GraphSchema vocabulary, so interpolating them is safe.
            var labels = new List<LabelInfo>();
            foreach (var (label, key) in GraphSchema.KeyProperty.Where(k => k.Key != "SyncState").OrderBy(k => k.Key))
            {
                var props = await Strings(tx, $"MATCH (n:{label}) UNWIND keys(n) AS k RETURN DISTINCT k AS v ORDER BY v", ct);
                if (!props.Contains(key)) props.Insert(0, key);
                var count = await Scalar(tx, $"MATCH (n:{label}) RETURN count(n) AS c", ct);
                labels.Add(new LabelInfo(label, key, props, count));
            }

            var types = new List<RelTypeInfo>();
            foreach (var type in GraphSchema.RelationshipTypes.OrderBy(t => t, StringComparer.Ordinal))
            {
                var cursor = await tx.RunAsync(
                    $"MATCH (a)-[r:{type}]->(b) RETURN collect(DISTINCT labels(a)[0]) AS f, collect(DISTINCT labels(b)[0]) AS t, count(r) AS c");
                var rec = await cursor.SingleAsync();
                types.Add(new RelTypeInfo(type,
                    rec["f"].As<List<string>>().Order().ToList(), rec["t"].As<List<string>>().Order().ToList(), rec["c"].As<long>()));
            }

            var samples = new SchemaSamples(
                await Sample(tx, "Alert", "ruleName", ct), await Sample(tx, "Threat", "id", ct),
                await Sample(tx, "Alert", "severity", ct), await Sample(tx, "Alert", "status", ct),
                await Sample(tx, "Incident", "status", ct), await Sample(tx, "Service", "name", ct));

            return new GraphSchemaDescription(labels, types, samples, QueryExamples.All);
        }, tc => tc.WithTimeout(GuardedQueryRunner.Timeout));
    }

    private static Task<List<string>> Sample(IAsyncQueryRunner tx, string label, string prop, CancellationToken ct) =>
        Strings(tx, $"MATCH (n:{label}) WHERE n.{prop} IS NOT NULL RETURN DISTINCT toString(n.{prop}) AS v ORDER BY v LIMIT {MaxSamples}", ct);

    private static async Task<List<string>> Strings(IAsyncQueryRunner tx, string cypher, CancellationToken ct)
    {
        var cursor = await tx.RunAsync(cypher);
        return (await cursor.ToListAsync(ct)).Select(r => r["v"].As<string>()).ToList();
    }

    private static async Task<long> Scalar(IAsyncQueryRunner tx, string cypher, CancellationToken ct)
    {
        var cursor = await tx.RunAsync(cypher);
        return (await cursor.ToListAsync(ct))[0]["c"].As<long>();
    }
}
