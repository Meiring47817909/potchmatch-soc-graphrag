namespace GraphService.Mapping;

/// <summary>Maps SOC rule names to MITRE ATT&amp;CK technique ids.</summary>
public static class RuleThreatMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["Multiple Failed Logins"] = "T1110",
        ["Unauthorised Endpoint Access"] = "T1078",
        ["POSSIBLE_DOS"] = "T1499",
        ["SERVICE_FAILURE"] = "T1499",
        ["GRAPH_ACCESS"] = "OWASP-A01-2025",
    };

    public static bool TryGetThreatId(string ruleName, out string threatId)
    {
        if (ruleName is not null && Map.TryGetValue(ruleName, out var id))
        {
            threatId = id;
            return true;
        }

        threatId = string.Empty;
        return false;
    }
}
