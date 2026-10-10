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
}
