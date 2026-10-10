using Microsoft.Extensions.Options;
using SocService.Models;

namespace SocService.Detection.Rules;

/// <summary>Rule 2: repeated rejected requests to protected endpoints by one user (or IP when anonymous).</summary>
public class UnauthorisedEndpointAccessRule(IOptions<DetectionOptions> options) : IDetectionRule
{
    private DetectionOptions.UnauthorisedAccessOptions Cfg => options.Value.UnauthorisedAccess;

    public string RuleName => SocConstants.Rules.UnauthorisedAccess;
    public string Purpose => "Detect users or clients repeatedly attempting to reach endpoints they are not authorised for.";
    public IReadOnlyList<string> InputEventTypes { get; } = ["AUTH_TOKEN_REJECTED", "UNAUTHORISED_ACCESS"];
    public string DetectionCondition =>
        $"{Cfg.Threshold} or more AUTH_TOKEN_REJECTED or UNAUTHORISED_ACCESS events within {Cfg.WindowMinutes} minutes for the same user; events without a user are grouped by source IP.";
    public string Severity => SocConstants.Severity.Medium;
    public string AlertGenerated => "Unauthorised Endpoint Access alert (MEDIUM) naming the user or source IP, linked to the rejected-request events.";
    public string RecommendedResponse =>
        "Review the user's role assignments and recent activity; revoke the session if access attempts continue.";
    public TimeSpan Window => TimeSpan.FromMinutes(Cfg.WindowMinutes);

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        var cfg = Cfg;
        var from = now - Window;

        var groups = events
            .Where(e => InputEventTypes.Contains(e.EventType) && e.Timestamp >= from
                        && !SensitivePaths.IsSensitive(e.Endpoint, options.Value.GraphAccess.SensitivePathPrefixes))
            .Select(e => new
            {
                Event = e,
                Key = !string.IsNullOrWhiteSpace(e.UserId) ? $"user:{e.UserId!.Trim()}"
                    : !string.IsNullOrWhiteSpace(e.SourceIp) ? $"ip:{e.SourceIp!.Trim()}"
                    : null,
            })
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!)
            .Where(g => g.Count() >= cfg.Threshold)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var evs = g.Select(x => x.Event).ToList();
            var isUser = g.Key.StartsWith("user:", StringComparison.Ordinal);
            var id = g.Key[(g.Key.IndexOf(':') + 1)..];
            var service = evs.GroupBy(e => e.ServiceName)
                .OrderByDescending(s => s.Count()).ThenBy(s => s.Key, StringComparer.Ordinal)
                .First().Key;
            var latest = evs.OrderByDescending(e => e.Timestamp).First();

            yield return new RuleMatch(
                $"{RuleName}:{g.Key}",
                $"{evs.Count} rejected requests to protected endpoints by {(isUser ? id : $"IP {id}")} within {cfg.WindowMinutes} minutes.",
                service, isUser ? id : null, latest.Endpoint,
                evs.Select(e => e.EventId).ToList());
        }
    }
}
