using System.Net;
using System.Text.Json;
using RagService;
using RagService.Clients;
using RagService.Options;

namespace RagService.Tests;

public class GraphQueryClientTests
{
    private static GraphQueryClient Make(FakeHandler h) =>
        new(new HttpClient(h) { BaseAddress = new Uri("http://graph-service:8080/") },
            Microsoft.Extensions.Options.Options.Create(new GraphQueryOptions { Url = "http://graph-service:8080", Key = "k1" }));

    private const string QueryOk = """
        {"cypher":"MATCH (n) RETURN n LIMIT 200","parameters":{"alertId":"a1"},
         "nodes":[{"id":"a1","label":"Alert","properties":{"severity":"HIGH","count":3}}],
         "relationships":[{"id":"r1","type":"INDICATES","start":"a1","end":"T1110"}],
         "rows":[{"n":"x"}],"truncated":true}
        """;

    [Fact]
    public async Task GraphClient_SendsKeyHeader()
    {
        var h = FakeHandler.Json(HttpStatusCode.OK, QueryOk);
        var o = await Make(h).QueryAsync("MATCH (n) RETURN n", new Dictionary<string, object?> { ["alertId"] = "a1" }, CancellationToken.None);

        Assert.Equal("http://graph-service:8080/api/internal/query", h.Request!.RequestUri!.ToString());
        Assert.Equal("k1", h.Request.Headers.GetValues("X-Graph-Query-Key").Single());
        using var body = JsonDocument.Parse(h.Body!);
        Assert.Equal("MATCH (n) RETURN n", body.RootElement.GetProperty("cypher").GetString());
        Assert.Equal("a1", body.RootElement.GetProperty("parameters").GetProperty("alertId").GetString());

        Assert.True(o.Ok);
        var e = o.Evidence!;
        Assert.True(e.Truncated);
        Assert.Equal("Alert", e.Nodes[0].Label);
        Assert.Equal("HIGH", e.Nodes[0].Properties["severity"].GetString());
        Assert.Equal("INDICATES", e.Relationships[0].Type);
        Assert.Equal("T1110", e.Relationships[0].End);
        Assert.Single(e.Rows);
    }

    [Theory]
    [InlineData("rejected", "Write clause CREATE is not allowed")]
    [InlineData("cypher_error", "Syntax error")]
    public async Task GraphClient_400_ReturnsRejectedOutcome(string error, string reason)
    {
        var h = FakeHandler.Json(HttpStatusCode.BadRequest, $$"""{"error":"{{error}}","reason":"{{reason}}"}""");
        var o = await Make(h).QueryAsync("x", new Dictionary<string, object?>(), CancellationToken.None);
        Assert.False(o.Ok);
        Assert.Null(o.Evidence);
        Assert.Equal(error, o.Error);
        Assert.Equal(reason, o.Reason);
    }

    [Fact]
    public async Task GraphClient_503_ThrowsGraphUnavailable()
    {
        var h = FakeHandler.Json(HttpStatusCode.ServiceUnavailable, """{"error":"graph_unavailable"}""");
        await Assert.ThrowsAsync<GraphUnavailableException>(() => Make(h).QueryAsync("x", new Dictionary<string, object?>(), CancellationToken.None));
        await Assert.ThrowsAsync<GraphUnavailableException>(() => Make(h).GetSchemaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GraphClient_ConnectionFailureOrTimeout_ThrowsGraphUnavailable()
    {
        await Assert.ThrowsAsync<GraphUnavailableException>(() =>
            Make(new FakeHandler(_ => throw new HttpRequestException("down"))).QueryAsync("x", new Dictionary<string, object?>(), CancellationToken.None));
        await Assert.ThrowsAsync<GraphUnavailableException>(() =>
            Make(new FakeHandler(_ => throw new TaskCanceledException("t"))).GetSchemaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GraphClient_401_ThrowsGraphUnavailable()
    {
        var h = FakeHandler.Json(HttpStatusCode.Unauthorized, "{}");
        await Assert.ThrowsAsync<GraphUnavailableException>(() => Make(h).GetSchemaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GraphClient_Schema_ParsedWithKeyHeader()
    {
        var h = FakeHandler.Json(HttpStatusCode.OK, """
            {"labels":[{"label":"Alert","keyProperty":"id","properties":["id","severity"],"count":4}],
             "relationshipTypes":[{"type":"INDICATES","from":["Alert"],"to":["Threat"],"count":2}],
             "samples":{"ruleNames":["R1"],"threatIds":["T1110"],"severities":["HIGH"],"alertStatuses":["OPEN"],"incidentStatuses":["OPEN"],"serviceNames":["graph-service"]},
             "examples":[{"name":"n","question":"q","cypher":"c"}]}
            """);
        var s = await Make(h).GetSchemaAsync(CancellationToken.None);
        Assert.Equal("http://graph-service:8080/api/internal/schema", h.Request!.RequestUri!.ToString());
        Assert.Equal("k1", h.Request.Headers.GetValues("X-Graph-Query-Key").Single());
        Assert.Equal("Alert", s.Labels[0].Label);
        Assert.Equal(4, s.Labels[0].Count);
        Assert.Equal("Threat", s.RelationshipTypes[0].To[0]);
        Assert.Equal("T1110", s.Samples.ThreatIds[0]);
        Assert.Equal("c", s.Examples[0].Cypher);
    }
}
