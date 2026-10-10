using System.Text.Json;

namespace RagService.Pipeline;

/// <summary>Tolerant parser for LLM output: strips code fences and surrounding prose, takes the outermost {…}.</summary>
public static class LlmJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T? Parse<T>(string? text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try { return JsonSerializer.Deserialize<T>(text.AsSpan(start, end - start + 1), Options); }
        catch (JsonException) { return null; }
    }
}
