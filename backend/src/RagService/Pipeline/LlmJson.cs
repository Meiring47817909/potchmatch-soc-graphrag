using System.Text.Json;

namespace RagService.Pipeline;

/// <summary>
/// Tolerant parser for LLM output: strips code fences and surrounding prose by taking the outermost {…};
/// when that is not valid JSON (e.g. a stray brace after the object), falls back to the first balanced,
/// string-aware {…} object that parses.
/// </summary>
public static class LlmJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static T? Parse<T>(string? text) where T : class
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        var outer = TryDeserialize<T>(text.AsSpan(start, end - start + 1));
        if (outer is not null) return outer;

        for (var i = start; i >= 0 && i < text.Length; i = text.IndexOf('{', i + 1))
        {
            var close = BalancedEnd(text, i);
            if (close < 0) continue;
            var candidate = TryDeserialize<T>(text.AsSpan(i, close - i + 1));
            if (candidate is not null) return candidate;
        }
        return null;
    }

    private static T? TryDeserialize<T>(ReadOnlySpan<char> json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>Index of the '}' closing the '{' at <paramref name="open"/>, ignoring braces inside JSON strings; -1 if unbalanced.</summary>
    private static int BalancedEnd(string text, int open)
    {
        var depth = 0;
        var inString = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    if (--depth == 0) return i;
                    break;
            }
        }
        return -1;
    }
}
