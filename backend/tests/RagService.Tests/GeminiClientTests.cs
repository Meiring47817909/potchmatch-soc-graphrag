using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagService;
using RagService.Clients;
using RagService.Options;

namespace RagService.Tests;

public class GeminiClientTests
{
    private const string Ok = """{"candidates":[{"content":{"parts":[{"text":"{\"cypher\":\"MATCH (n) RETURN n\"}"}]}}]}""";

    private static GeminiClient Make(FakeHandler h, GeminiOptions? o = null, ListLogger<GeminiClient>? log = null)
    {
        o ??= new GeminiOptions { ApiKey = "secret-key" };
        return new GeminiClient(new HttpClient(h) { BaseAddress = new Uri(o.BaseUrl) }, Microsoft.Extensions.Options.Options.Create(o),
            log ?? new ListLogger<GeminiClient>());
    }

    [Fact]
    public async Task Gemini_SendsKeyHeaderModelAndJsonMode()
    {
        var h = FakeHandler.Json(HttpStatusCode.OK, Ok);
        await Make(h).GenerateAsync("sys", "usr", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent", h.Request.RequestUri!.ToString());
        Assert.Equal("secret-key", h.Request.Headers.GetValues("x-goog-api-key").Single());

        using var doc = JsonDocument.Parse(h.Body!);
        var root = doc.RootElement;
        Assert.Equal("sys", root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        var content = root.GetProperty("contents")[0];
        Assert.Equal("user", content.GetProperty("role").GetString());
        Assert.Equal("usr", content.GetProperty("parts")[0].GetProperty("text").GetString());
        var cfg = root.GetProperty("generationConfig");
        Assert.Equal("application/json", cfg.GetProperty("responseMimeType").GetString());
        Assert.Equal(0, cfg.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public async Task Gemini_ReturnsCandidateText()
    {
        var text = await Make(FakeHandler.Json(HttpStatusCode.OK, Ok)).GenerateAsync("s", "u", CancellationToken.None);
        Assert.Equal("{\"cypher\":\"MATCH (n) RETURN n\"}", text);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Gemini_RateLimitOrServerError_ThrowsLlmUnavailable(HttpStatusCode code)
    {
        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Make(FakeHandler.Json(code, "{}")).GenerateAsync("s", "u", CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("""{}""")]
    [InlineData("""{"candidates":[{"finishReason":"SAFETY"}]}""")]
    [InlineData("not json")]
    public async Task Gemini_NoCandidates_ThrowsLlmUnavailable(string body)
    {
        await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Make(FakeHandler.Json(HttpStatusCode.OK, body)).GenerateAsync("s", "u", CancellationToken.None));
    }

    [Fact]
    public async Task Gemini_Timeout_ThrowsLlmUnavailable()
    {
        var h = new FakeHandler(_ => throw new TaskCanceledException("timeout"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => Make(h).GenerateAsync("s", "u", CancellationToken.None));
    }

    [Fact]
    public async Task Gemini_ConnectionFailure_ThrowsLlmUnavailable()
    {
        var h = new FakeHandler(_ => throw new HttpRequestException("down"));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => Make(h).GenerateAsync("s", "u", CancellationToken.None));
    }

    [Fact]
    public async Task Gemini_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var h = new FakeHandler(_ => throw new TaskCanceledException("c", null, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Make(h).GenerateAsync("s", "u", cts.Token));
    }

    [Fact]
    public async Task Gemini_NoKey_ThrowsRagDisabled()
    {
        var c = Make(FakeHandler.Json(HttpStatusCode.OK, Ok), new GeminiOptions());
        await Assert.ThrowsAsync<RagDisabledException>(() => c.GenerateAsync("s", "u", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    public async Task Gemini_ClientError_RejectedMessage_LogsStatusOnly(HttpStatusCode code, int n)
    {
        var log = new ListLogger<GeminiClient>();
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Make(FakeHandler.Json(code, """{"error":{"message":"API key not valid"}}"""), log: log)
                .GenerateAsync("system-prompt-text", "user-prompt-text", CancellationToken.None));

        Assert.Equal($"The language model rejected the request (HTTP {n}); check GEMINI_API_KEY and GEMINI_MODEL.", ex.Message);
        Assert.Contains(log.Messages, m => m.Contains(n.ToString()));
        Assert.DoesNotContain(log.Messages, m => m.Contains("secret-key") || m.Contains("prompt-text"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Gemini_BusyOrServerError_BusyMessage(HttpStatusCode code)
    {
        var log = new ListLogger<GeminiClient>();
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Make(FakeHandler.Json(code, "{}"), log: log).GenerateAsync("s", "u", CancellationToken.None));
        Assert.Equal("The language model is busy or unavailable; try again in a minute.", ex.Message);
        Assert.Contains(log.Messages, m => m.Contains(((int)code).ToString()));
        Assert.DoesNotContain(log.Messages, m => m.Contains("secret-key"));
    }

    [Fact]
    public async Task Gemini_Timeout_BusyMessage()
    {
        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() =>
            Make(new FakeHandler(_ => throw new TaskCanceledException("t"))).GenerateAsync("s", "u", CancellationToken.None));
        Assert.Equal("The language model is busy or unavailable; try again in a minute.", ex.Message);
    }

    [Fact]
    public async Task Gemini_MultipleTextParts_Concatenated_ThoughtsSkipped()
    {
        const string body = """
            {"candidates":[{"content":{"parts":[
              {"text":"thinking...","thought":true},
              {"text":"{\"cypher\":\"MATCH (n) "},
              {"text":"RETURN n\"}"}]}}]}
            """;
        var text = await Make(FakeHandler.Json(HttpStatusCode.OK, body)).GenerateAsync("s", "u", CancellationToken.None);
        Assert.Equal("{\"cypher\":\"MATCH (n) RETURN n\"}", text);
    }
}
