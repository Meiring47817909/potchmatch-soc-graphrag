using System.Text.Encodings.Web;
using System.Text.Json;

namespace RagService.Pipeline;

/// <summary>JSON for prompt text only (never HTML): compact, without escaping &amp;, quotes-in-text or non-ASCII.</summary>
internal static class PromptJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Serialises for a prompt; the result never contains a raw control or line-separator character.</summary>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, Options).Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
}
