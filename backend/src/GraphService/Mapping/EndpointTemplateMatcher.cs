namespace GraphService.Mapping;

/// <summary>
/// Matches a normalised endpoint key ("{METHOD} {path}") against seeded Endpoint template keys of the same method.
/// A template segment of the form {x} matches any single segment; other segments must be equal (ordinal).
/// An exact key match wins; otherwise the template with the fewest wildcards, then the ordinally smallest key.
/// </summary>
public static class EndpointTemplateMatcher
{
    public static string? Match(string key, IEnumerable<string> templates)
    {
        var (method, segments) = Split(key);
        string? best = null;
        var bestWildcards = int.MaxValue;
        foreach (var template in templates)
        {
            if (string.Equals(template, key, StringComparison.Ordinal)) return template;
            var (tMethod, tSegments) = Split(template);
            if (!string.Equals(tMethod, method, StringComparison.Ordinal) || tSegments.Length != segments.Length) continue;

            var wildcards = 0;
            var matches = true;
            for (var i = 0; i < segments.Length && matches; i++)
            {
                if (IsWildcard(tSegments[i])) wildcards++;
                else matches = string.Equals(tSegments[i], segments[i], StringComparison.Ordinal);
            }
            if (!matches) continue;
            if (wildcards < bestWildcards || (wildcards == bestWildcards && string.CompareOrdinal(template, best) < 0))
            {
                best = template;
                bestWildcards = wildcards;
            }
        }
        return best;
    }

    private static (string Method, string[] Segments) Split(string key)
    {
        var space = key.IndexOf(' ');
        if (space < 0) return (key, []);
        return (key[..space], key[(space + 1)..].Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsWildcard(string segment) =>
        segment.Length > 2 && segment[0] == '{' && segment[^1] == '}';
}
