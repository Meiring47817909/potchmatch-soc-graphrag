namespace RagService.Clients;

public interface IGeminiClient
{
    /// <summary>Returns the model's JSON-mode text. Throws <see cref="LlmUnavailableException"/> on 429/5xx/timeout/empty result.</summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
