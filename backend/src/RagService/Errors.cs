namespace RagService;

/// <summary>Gemini is rate limiting, down, timed out or returned nothing usable.</summary>
public sealed class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>GraphService is unreachable, timed out or reported itself unavailable.</summary>
public sealed class GraphUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>No Gemini API key is configured.</summary>
public sealed class RagDisabledException() : Exception("GEMINI_API_KEY is not set");
