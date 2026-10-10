using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RagService;
using RagService.Controllers;
using RagService.Options;
using RagService.Pipeline;

namespace RagService.Tests;

public class AskEndpointTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static RagController Controller(GeminiOptions? o = null) =>
        new(Microsoft.Extensions.Options.Options.Create(o ?? new GeminiOptions { ApiKey = "k" }));

    private static Task<IActionResult> Ask(FakeGemini g, FakeGraph gr, AskRequest req, GeminiOptions? o = null) =>
        Controller(o).Ask(req, Evidence.Pipeline(g, gr, o), CancellationToken.None);

    private static JsonElement Body(IActionResult result, int status)
    {
        var obj = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, obj.StatusCode ?? 200);
        return JsonSerializer.SerializeToElement(obj.Value, Web);
    }

    [Fact]
    public async Task Ask_HappyPath_200_JsonShapeMatchesSpec()
    {
        var g = new FakeGemini(FakeGemini.Cypher("MATCH (a:Alert) RETURN a"), FakeGemini.Answer("See [T1110] and [T9999]."));
        var body = Body(await Ask(g, new FakeGraph(Evidence.Ok()), new AskRequest("Why?", "a1", null)), 200);

        Assert.Equal("Why?", body.GetProperty("question").GetString());
        Assert.True(body.GetProperty("answered").GetBoolean());
        Assert.Equal("text2cypher", body.GetProperty("mode").GetString());
        var ret = body.GetProperty("retrieval");
        Assert.Equal("LLM-generated Cypher, validated read-only, executed on Neo4j", ret.GetProperty("method").GetString());
        Assert.Equal(1, ret.GetProperty("attempts").GetInt32());
        Assert.Equal(2, ret.GetProperty("rowCount").GetInt32());
        Assert.False(ret.GetProperty("truncated").GetBoolean());
        Assert.Equal("T9999", ret.GetProperty("unverifiedCitations")[0].GetString());
        Assert.True(ret.TryGetProperty("explanation", out _));
        Assert.True(ret.TryGetProperty("model", out _));
        Assert.Equal("a1", body.GetProperty("parameters").GetProperty("alertId").GetString());
        Assert.Equal("Threat", body.GetProperty("nodes")[1].GetProperty("label").GetString());
        Assert.Equal("T1110", body.GetProperty("nodes")[1].GetProperty("properties").GetProperty("id").GetString());
        Assert.Equal("INDICATES", body.GetProperty("relationships")[0].GetProperty("type").GetString());
        Assert.Equal("4:x:1", body.GetProperty("relationships")[0].GetProperty("start").GetString());
        Assert.Equal(2, body.GetProperty("rows").GetArrayLength());
        Assert.True(body.TryGetProperty("cypher", out _));
        var src = body.GetProperty("sources")[0];
        Assert.Equal("DOC-PASSWORD-RESET", src.GetProperty("id").GetString());
        Assert.Equal("Password Reset Procedure", src.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, src.GetProperty("url").ValueKind);
    }

    [Fact]
    public async Task Ask_BadModelJson_LlmUnavailable503()
    {
        var body = Body(await Ask(new FakeGemini("not json at all"), new FakeGraph(), new AskRequest("Why?", null, null)), 503);
        Assert.Equal("llm_unavailable", body.GetProperty("error").GetString());
        Assert.Equal("The language model is busy or unavailable; try again in a minute.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Ask_LlmException_503()
    {
        var g = new FakeGemini(new LlmUnavailableException(RagMessages.LlmRejected(404)));
        var body = Body(await Ask(g, new FakeGraph(), new AskRequest("Why?", null, null)), 503);
        Assert.Equal("llm_unavailable", body.GetProperty("error").GetString());
        Assert.Equal("The language model rejected the request (HTTP 404); check GEMINI_API_KEY and GEMINI_MODEL.", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Ask_GraphUnavailable_503()
    {
        var g = new FakeGemini(FakeGemini.Cypher("MATCH (a:Alert) RETURN a"));
        var body = Body(await Ask(g, new FakeGraph(new GraphUnavailableException("GraphService returned HTTP 503")), new AskRequest("Why?", null, null)), 503);
        Assert.Equal("graph_unavailable", body.GetProperty("error").GetString());
        Assert.Equal(RagMessages.GraphUnavailable, body.GetProperty("message").GetString());
        Assert.Equal(2, body.EnumerateObject().Count());
    }

    [Fact]
    public async Task Ask_SchemaUnavailable_503()
    {
        var graph = new FakeGraph { SchemaError = new GraphUnavailableException("down") };
        var body = Body(await Ask(new FakeGemini(), graph, new AskRequest("Why?", null, null)), 503);
        Assert.Equal("graph_unavailable", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ask_NoKey_503RagDisabled()
    {
        var body = Body(await Ask(new FakeGemini(), new FakeGraph(), new AskRequest("Why?", null, null), new GeminiOptions()), 503);
        Assert.Equal("rag_disabled", body.GetProperty("error").GetString());
        Assert.StartsWith("GEMINI_API_KEY is not set", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task Ask_QuestionEmptyOrTooLong_400(int length)
    {
        var g = new FakeGemini();
        var body = Body(await Ask(g, new FakeGraph(), new AskRequest(new string('q', length), null, null)), 400);
        Assert.Equal("invalid_question", body.GetProperty("error").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
        Assert.Empty(g.Calls);
    }

    [Fact]
    public async Task Ask_QuestionWhitespaceOrNull_400()
    {
        Body(await Ask(new FakeGemini(), new FakeGraph(), new AskRequest("   ", null, null)), 400);
        Body(await Ask(new FakeGemini(), new FakeGraph(), new AskRequest(null!, null, null)), 400);
        Body(await Ask(new FakeGemini(), new FakeGraph(), new AskRequest("ok", new string('a', 201), null)), 400);
    }

    [Fact]
    public async Task Ask_500Chars_Accepted()
    {
        var g = new FakeGemini(FakeGemini.Cypher("MATCH (a:Alert) RETURN a"), FakeGemini.Answer("x [a1]"));
        Body(await Ask(g, new FakeGraph(Evidence.Ok()), new AskRequest(new string('q', 500), null, null)), 200);
    }

    [Fact]
    public void Ask_IsPostApiRagAsk_AdminOnly()
    {
        var m = typeof(RagController).GetMethod(nameof(RagController.Ask))!;
        Assert.Equal("ask", m.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("api/rag", typeof(RagController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("Admin", typeof(RagController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()!.Roles);
    }
}
