namespace RagService;

/// <summary>Gemini is rate limiting, down, timed out or returned nothing usable.</summary>
public sealed class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>GraphService is unreachable, timed out or reported itself unavailable.</summary>
public sealed class GraphUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>No Gemini API key is configured.</summary>
public sealed class RagDisabledException() : Exception("GEMINI_API_KEY is not set");

/// <summary>User-facing messages for the 503 bodies (spec §5.2). Never contain the key, prompts or the question.</summary>
public static class RagMessages
{
    public const string LlmBusy = "The language model is busy or unavailable; try again in a minute.";
    public const string RagDisabled = "GEMINI_API_KEY is not set; add it to backend/.env and restart rag-service.";
    public const string GraphUnavailable = "The knowledge graph service is unavailable; try again shortly.";

    public static string LlmRejected(int statusCode) =>
        $"The language model rejected the request (HTTP {statusCode}); check GEMINI_API_KEY and GEMINI_MODEL.";
}
