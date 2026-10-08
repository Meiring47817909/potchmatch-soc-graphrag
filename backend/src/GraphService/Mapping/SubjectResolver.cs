namespace GraphService.Mapping;

/// <summary>
/// Chooses the subject of an alert or incident as a prefixed key:
/// user (affected, then first triggering), then the most frequent source IP, then service, then "unknown".
/// </summary>
public static class SubjectResolver
{
    public static string Resolve(
        string? affectedUserId,
        IReadOnlyList<string> triggeringUserIds,
        IReadOnlyList<string> sourceIps,
        string? affectedService)
    {
        if (!string.IsNullOrWhiteSpace(affectedUserId))
        {
            return "user:" + affectedUserId.Trim();
        }

        var firstUser = triggeringUserIds
            .Where(IsPresent)
            .Select(v => v.Trim())
            .OrderBy(v => v, StringComparer.Ordinal)
            .FirstOrDefault();
        if (firstUser is not null)
        {
            return "user:" + firstUser;
        }

        var topIp = sourceIps
            .Where(IsPresent)
            .Select(v => v.Trim())
            .GroupBy(v => v, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
        if (topIp is not null)
        {
            return "ip:" + topIp;
        }

        if (!string.IsNullOrWhiteSpace(affectedService))
        {
            return "service:" + affectedService.Trim();
        }

        return "unknown";
    }

    private static bool IsPresent(string? value) => !string.IsNullOrWhiteSpace(value);
}
