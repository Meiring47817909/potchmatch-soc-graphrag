using GraphService.Seeding;
using Microsoft.Extensions.Logging.Abstractions;
using Neo4j.Driver;

namespace GraphService.Tests;

[Collection("neo4j")]
public class SeedLoaderTests(Neo4jFixture fx)
{
    private static string SeedDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "graph-data"))) dir = dir.Parent;
        return dir is null ? throw new DirectoryNotFoundException("graph-data not found") : Path.Combine(dir.FullName, "graph-data");
    }

    private SeedLoader Loader() => new(fx.Driver, NullLogger<SeedLoader>.Instance);

    private async Task<IReadOnlyDictionary<string, long>> CountsAsync(string cypher)
    {
        await using var session = fx.Driver.AsyncSession();
        var cursor = await session.RunAsync(cypher);
        var rows = await cursor.ToListAsync();
        return rows.ToDictionary(r => r["k"].As<string>(), r => r["c"].As<long>());
    }

    private Task<IReadOnlyDictionary<string, long>> NodeCounts() =>
        CountsAsync("MATCH (n) RETURN labels(n)[0] AS k, count(*) AS c");

    private Task<IReadOnlyDictionary<string, long>> RelCounts() =>
        CountsAsync("MATCH ()-[r]->() RETURN type(r) AS k, count(*) AS c");

    private async Task<IReadOnlySet<NodeRef>> LoadRealAsync()
    {
        var dir = SeedDirectory();
        var structure = await Loader().LoadAsync(Path.Combine(dir, "app-structure.json"), new HashSet<NodeRef>(), default);
        return await Loader().LoadAsync(Path.Combine(dir, "knowledge", "security-knowledge.json"), structure, default);
    }

    [Fact]
    public async Task Load_RealSeedFiles_ExpectedCounts()
    {
        await fx.ResetAsync();
        await LoadRealAsync();

        var nodes = await NodeCounts();
        foreach (var label in new[] { "Service", "Endpoint", "Asset", "Role", "Threat", "Control", "Vulnerability", "Document" })
            Assert.True(nodes.GetValueOrDefault(label) >= 1, $"no {label} nodes");
        Assert.Equal(7, nodes["Service"]);
        Assert.Equal(3, nodes["Role"]);
        Assert.Equal(6, nodes["Asset"]);
        Assert.Equal(16, nodes["Threat"]); // 4 app-structure stubs merge into the 16 curated threats
        Assert.Equal(11, nodes["Control"]);
        Assert.Equal(6, nodes["Vulnerability"]);
        Assert.Equal(15, nodes["Document"]);

        var rels = await RelCounts();
        foreach (var type in new[] { "TARGETS", "EXPLOITS", "MITIGATES", "DEPENDS_ON", "EXPOSES", "DESCRIBES", "SUPPORTS_RESPONSE_TO", "USES", "REQUIRES_ROLE" })
            Assert.True(rels.GetValueOrDefault(type) >= 1, $"no {type} relationships");
        Assert.Equal(nodes["Endpoint"], rels["EXPOSES"]);
        Assert.Equal(11, rels["TARGETS"]);
        Assert.Equal(46, rels["MITIGATES"]);
        Assert.Equal(20, rels["EXPLOITS"]);
        Assert.Equal(12, rels["DESCRIBES"]);
        Assert.Equal(15, rels["SUPPORTS_RESPONSE_TO"]);

        // every seeded endpoint has method/path/protected
        await using var session = fx.Driver.AsyncSession();
        var cursor = await session.RunAsync(
            "MATCH (e:Endpoint) WHERE e.method IS NULL OR e.path IS NULL OR e.protected IS NULL OR e.key <> e.method + ' ' + e.path RETURN count(e) AS bad");
        Assert.Equal(0, (await cursor.SingleAsync())["bad"].As<long>());

        // every knowledge node comes from the curated Graph Builder export (no fixture data left)
        cursor = await session.RunAsync(
            "MATCH (n) WHERE (n:Threat OR n:Control OR n:Vulnerability OR n:Document) AND NOT coalesce(n.origin, '') IN ['extracted', 'curated'] RETURN count(n) AS bad");
        Assert.Equal(0, (await cursor.SingleAsync())["bad"].As<long>());
    }

    [Fact]
    public async Task Seeds_OwaspA01HasControlsAndTargets()
    {
        await fx.ResetAsync();
        await LoadRealAsync();
        await using var session = fx.Driver.AsyncSession();

        var cursor = await session.RunAsync(
            "MATCH (:Control)-[m:MITIGATES]->(:Threat {id: 'OWASP-A01-2025'}) RETURN count(m) AS c");
        Assert.True((await cursor.SingleAsync())["c"].As<long>() >= 2);

        cursor = await session.RunAsync(
            "MATCH (:Threat {id: 'OWASP-A01-2025'})-[:TARGETS]->(s:Service) RETURN collect(s.name) AS names");
        var targets = (await cursor.SingleAsync())["names"].As<List<string>>();
        Assert.Contains("rag-service", targets);
        Assert.Contains("graph-service", targets);

        cursor = await session.RunAsync(
            "MATCH (:Service {name: 'rag-service'})-[:EXPOSES]->(e:Endpoint) " +
            "OPTIONAL MATCH (e)-[:REQUIRES_ROLE]->(r:Role {name: 'Admin'}) RETURN count(e) AS endpoints, count(r) AS admin");
        var row = await cursor.SingleAsync();
        Assert.Equal(2, row["endpoints"].As<long>());
        Assert.Equal(2, row["admin"].As<long>());
    }

    [Fact]
    public async Task Load_Twice_IsIdempotent()
    {
        await fx.ResetAsync();
        await LoadRealAsync();
        var nodes1 = await NodeCounts();
        var rels1 = await RelCounts();

        await LoadRealAsync();

        Assert.Equal(nodes1, await NodeCounts());
        Assert.Equal(rels1, await RelCounts());
    }

    [Fact]
    public async Task Load_InvalidFile_ThrowsAndWritesNothing()
    {
        await fx.ResetAsync();
        var path = Path.Combine(Path.GetTempPath(), $"bad-seed-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, """
            { "nodes": [ { "label": "Service", "key": "ok", "properties": {} }, { "label": "Banana", "key": "x", "properties": {} } ],
              "relationships": [ { "type": "DEPENDS_ON", "from": { "label": "Service", "key": "ok" }, "to": { "label": "Service", "key": "ghost" } } ] }
            """);
        try
        {
            var ex = await Assert.ThrowsAsync<SeedValidationException>(() => Loader().LoadAsync(path, new HashSet<NodeRef>(), default));
            Assert.Equal(2, ex.Errors.Count);
            Assert.Empty(await NodeCounts());
        }
        finally { File.Delete(path); }
    }
}
