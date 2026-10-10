using System.Text.Json;
using GraphService.Controllers;
using GraphService.Queries;
using GraphService.Rag;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Neo4j.Driver;

namespace GraphService.Tests;

[Collection("neo4j")]
public class InternalQueryTests(Neo4jFixture fx)
{
    private const string Key = "test-graph-query-key";

    private static IConfiguration Config(string key) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GraphQuery:Key"] = key }).Build();

    private static InternalQueryController Controller(IDriver driver, string? header, string configuredKey = Key)
    {
        var c = new InternalQueryController(new GuardedQueryRunner(driver), new GraphSchemaDescriber(driver), Config(configuredKey))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        if (header is not null) c.Request.Headers["X-Graph-Query-Key"] = header;
        return c;
    }

    private static InternalQueryRequest Req(string cypher, string? parametersJson = null) =>
        new(cypher, parametersJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(parametersJson));

    private static async Task<IDriver> DeadDriverAsync() =>
        await Task.FromResult(GraphDatabase.Driver("bolt://127.0.0.1:1", AuthTokens.None,
            o => o.WithMaxTransactionRetryTime(TimeSpan.FromMilliseconds(200))));

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task InternalQuery_WrongOrMissingKey_401(string? header)
    {
        var c = Controller(await DeadDriverAsync(), header);
        Assert.IsType<UnauthorizedResult>(await c.Query(Req("MATCH (n) RETURN n"), default));
        Assert.IsType<UnauthorizedResult>(await c.Schema(default));
    }

    [Fact]
    public async Task InternalQuery_EmptyConfiguredKey_401()
    {
        var c = Controller(await DeadDriverAsync(), "", "");
        Assert.IsType<UnauthorizedResult>(await c.Query(Req("MATCH (n) RETURN n"), default));
        var c2 = Controller(await DeadDriverAsync(), null, "");
        Assert.IsType<UnauthorizedResult>(await c2.Query(Req("MATCH (n) RETURN n"), default));
    }

    [Fact]
    public async Task InternalQuery_GuardRejects_400Rejected()
    {
        var c = Controller(await DeadDriverAsync(), Key);
        var res = Assert.IsType<BadRequestObjectResult>(await c.Query(Req("CREATE (n) RETURN n"), default));
        Assert.Equal("rejected", Err(res.Value, "error"));
        Assert.False(string.IsNullOrEmpty(Err(res.Value, "reason")));
    }

    [Fact]
    public async Task InternalQuery_Unreachable_503()
    {
        var c = Controller(await DeadDriverAsync(), Key);
        var res = Assert.IsType<ObjectResult>(await c.Query(Req("MATCH (n) RETURN n"), default));
        Assert.Equal(503, res.StatusCode);
        Assert.Equal("graph_unavailable", Err(res.Value, "error"));
    }

    [Fact]
    public async Task InternalQuery_ReadOnly_ReturnsEvidence()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        var c = Controller(fx.Driver, Key);
        var res = Assert.IsType<OkObjectResult>(await c.Query(
            Req("MATCH p=(c:Control)-[:MITIGATES]->(t:Threat {id:$id}) RETURN p", """{"id":"T1110"}"""), default));
        var body = Assert.IsType<InternalQueryResponse>(res.Value);
        Assert.Contains(body.Nodes, n => n.Label == "Control" && (string)n.Properties["id"]! == "M1032");
        Assert.Contains(body.Relationships, r => r.Type == "MITIGATES");
        Assert.False(body.Truncated);
        Assert.Contains("LIMIT 200", body.Cypher);
        Assert.Equal("T1110", body.Parameters["id"]);
    }

    [Fact]
    public async Task GuardedRunner_WriteRefusedByReadTransaction()
    {
        await fx.ResetAsync();
        var runner = new GuardedQueryRunner(fx.Driver);
        await Assert.ThrowsAsync<ClientException>(() =>
            runner.RunAsync("CREATE (n:Hack) RETURN n", new Dictionary<string, object?>(), default));
        Assert.Equal(0, await new SyncHarness(fx.Driver).CountAsync("MATCH (n:Hack) RETURN count(n)"));
    }

    [Fact]
    public async Task InternalQuery_SyntaxError_400CypherError()
    {
        var c = Controller(fx.Driver, Key);
        var res = Assert.IsType<BadRequestObjectResult>(await c.Query(Req("MATCH (n RETURN n"), default));
        Assert.Equal("cypher_error", Err(res.Value, "error"));
        Assert.False(string.IsNullOrEmpty(Err(res.Value, "reason")));
    }

    [Fact]
    public async Task GuardedRunner_TruncatesAt200()
    {
        var r = await new GuardedQueryRunner(fx.Driver)
            .RunAsync("UNWIND range(1,500) AS i RETURN i", new Dictionary<string, object?>(), default);
        Assert.Equal(200, r.Rows.Count);
        Assert.True(r.Truncated);
        var small = await new GuardedQueryRunner(fx.Driver)
            .RunAsync("UNWIND range(1,200) AS i RETURN i", new Dictionary<string, object?>(), default);
        Assert.False(small.Truncated);
    }

    [Fact]
    public void ParameterConverter_ConvertsJsonElements()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"s":"a","i":5,"d":1.5,"t":true,"n":null,"l":[1,"x"],"o":{"k":2}}""")!;
        var p = ParameterConverter.Convert(json);
        Assert.Equal("a", p["s"]);
        Assert.Equal(5L, p["i"]);
        Assert.Equal(1.5, p["d"]);
        Assert.Equal(true, p["t"]);
        Assert.Null(p["n"]);
        Assert.Equal(new object?[] { 1L, "x" }, ((List<object?>)p["l"]!).ToArray());
        Assert.Equal(2L, ((Dictionary<string, object?>)p["o"]!)["k"]);
        Assert.Empty(ParameterConverter.Convert(null));
    }

    private static string? Err(object? body, string prop) =>
        body!.GetType().GetProperty(prop)!.GetValue(body) as string;
}
