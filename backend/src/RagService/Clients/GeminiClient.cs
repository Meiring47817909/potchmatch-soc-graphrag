using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagService.Options;

namespace RagService.Clients;

/// <summary>Typed client for Gemini generateContent (JSON mode, temperature 0). Logs status codes only — never the key or prompts.</summary>
public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger) : IGeminiClient
{
    private readonly GeminiOptions _o = options.Value;

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (!_o.Enabled) throw new RagDisabledException();

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = BuildGenerationConfig(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{_o.Model}:generateContent")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-goog-api-key", _o.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                logger.LogWarning("Gemini returned HTTP {StatusCode}", code);
                // 400/401/403/404: bad key, unknown model or malformed request — retrying will not help.
                throw new LlmUnavailableException(code is 400 or 401 or 403 or 404
                    ? RagMessages.LlmRejected(code)
                    : RagMessages.LlmBusy);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var text = CandidateText(doc.RootElement);
            if (text is not null) return text;

            logger.LogWarning("Gemini returned no usable candidate text");
            throw new LlmUnavailableException(RagMessages.LlmBusy);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Gemini request timed out");
            throw new LlmUnavailableException(RagMessages.LlmBusy, ex);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Gemini request failed: {ErrorType}", ex.GetType().Name);
            throw new LlmUnavailableException(RagMessages.LlmBusy, ex);
        }
        catch (JsonException ex)
        {
            logger.LogWarning("Gemini returned an unreadable response");
            throw new LlmUnavailableException(RagMessages.LlmBusy, ex);
        }
    }

    private Dictionary<string, object> BuildGenerationConfig()
    {
        var cfg = new Dictionary<string, object> { ["temperature"] = 0, ["responseMimeType"] = "application/json" };
        if (_o.ThinkingBudget is { } budget) cfg["thinkingConfig"] = new { thinkingBudget = budget };
        return cfg;
    }

    /// <summary>Concatenates every text part of the first candidate (skipping thought parts); null when there is none.</summary>
    private static string? CandidateText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("candidates", out var cands)
            || cands.ValueKind != JsonValueKind.Array || cands.GetArrayLength() == 0
            || cands[0].ValueKind != JsonValueKind.Object
            || !cands[0].TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
            return null;

        var sb = new StringBuilder();
        var any = false;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind != JsonValueKind.Object) continue;
            if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
            if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
            {
                sb.Append(t.GetString());
                any = true;
            }
        }
        return any && sb.Length > 0 ? sb.ToString() : null;
    }
}
