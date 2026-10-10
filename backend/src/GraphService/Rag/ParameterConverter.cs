using System.Text.Json;

namespace GraphService.Rag;

/// <summary>Converts JSON request parameters to driver-friendly CLR values (string, long, double, bool, null, lists, dictionaries).</summary>
public static class ParameterConverter
{
    public static IReadOnlyDictionary<string, object?> Convert(IReadOnlyDictionary<string, JsonElement>? parameters) =>
        parameters is null
            ? new Dictionary<string, object?>()
            : parameters.ToDictionary(kv => kv.Key, kv => ConvertElement(kv.Value));

    private static object? ConvertElement(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(ConvertElement).ToList(),
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ConvertElement(p.Value)),
        _ => null,
    };
}
