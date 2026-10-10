using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagService.Options;

namespace RagService.Clients;

/// <summary>Typed client for Gemini generateContent (JSON mode, temperature 0). Never logs the key or prompts.</summary>
public sealed class GeminiClient(HttpClient http, IOptions<GeminiOptions> options) : IGeminiClient
{
    private readonly GeminiOptions _o = options.Value;

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (!_o.Enabled) throw new RagDisabledException();

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
            generationConfig = new { temperature = 0, responseMimeType = "application/json" },
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
                throw new LlmUnavailableException($"Gemini returned HTTP {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("candidates", out var cands)
                && cands.ValueKind == JsonValueKind.Array && cands.GetArrayLength() > 0
                && cands[0].TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts)
                && parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0
                && parts[0].TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
                return text.GetString()!;

            throw new LlmUnavailableException("Gemini returned no candidate");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmUnavailableException("Gemini request timed out", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException("Gemini request failed", ex);
        }
        catch (JsonException ex)
        {
            throw new LlmUnavailableException("Gemini returned an unreadable response", ex);
        }
    }
}
