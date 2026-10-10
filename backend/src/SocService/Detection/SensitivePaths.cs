namespace SocService.Detection;

/// <summary>Shared matcher for the sensitive graph/RAG endpoint prefixes (Rule 5 includes them, Rule 2 excludes them).</summary>
public static class SensitivePaths
{
    public static bool IsSensitive(string? endpoint, IReadOnlyList<string> prefixes)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrEmpty(prefix)) continue;
            if (endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            if (prefix.EndsWith('/') && string.Equals(endpoint, prefix[..^1], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
