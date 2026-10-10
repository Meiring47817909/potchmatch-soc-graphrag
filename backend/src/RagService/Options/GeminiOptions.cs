namespace RagService.Options;

public sealed class GeminiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-2.5-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    public int TimeoutSeconds { get; set; } = 25;
    /// <summary>Gemini 2.5 thinking budget in tokens; 0 disables thinking (lower latency). Null omits the setting for models that do not support it.</summary>
    public int? ThinkingBudget { get; set; } = 0;
    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
