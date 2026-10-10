using Microsoft.Extensions.Options;
using SocService.Models;

namespace SocService.Detection.Rules;

/// <summary>Rule 5: repeated denied requests to the knowledge-graph / RAG endpoints by one user (or IP when anonymous).</summary>
public class GraphAccessRule(IOptions<DetectionOptions> options) : IDetectionRule
{
    private DetectionOptions.GraphAccessOptions Cfg => options.Value.GraphAccess;

    public string RuleName => SocConstants.Rules.GraphAccess;
    public string Purpose => "Detect someone probing the knowledge graph or RAG interface without authorisation (reconnaissance of users, dependencies, incidents).";
    public IReadOnlyList<string> InputEventTypes { get; } = ["AUTH_TOKEN_REJECTED", "UNAUTHORISED_ACCESS"];
    public string DetectionCondition =>
        $"{Cfg.Threshold} or more AUTH_TOKEN_REJECTED or UNAUTHORISED_ACCESS events whose endpoint starts with a sensitive prefix ({string.Join(", ", Cfg.SensitivePathPrefixes)}) within {Cfg.WindowMinutes} minutes for the same user; events without a user are grouped by source IP.";
    public string Severity => SocConstants.Severity.High;
    public string AlertGenerated =>
        "GRAPH_ACCESS alert (HIGH) naming the user or source IP and the count; affected service is graph-service or rag-service (most targeted, ties to graph-service); affected entity is the last endpoint tried; linked to the denied-request events.";
    public string RecommendedResponse =>
        "Review the account's role and recent activity; revoke its sessions and block the source IP if the attempts were not expected.";
    public TimeSpan Window => TimeSpan.FromMinutes(Cfg.WindowMinutes);

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        var cfg = Cfg;
        var from = now - Window;

        var groups = events
            .Where(e => InputEventTypes.Contains(e.EventType) && e.Timestamp >= from
                        && SensitivePaths.IsSensitive(e.Endpoint, cfg.SensitivePathPrefixes))
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
            var ragCount = evs.Count(e => IsRag(e.Endpoint));
            var service = ragCount > evs.Count - ragCount ? "rag-service" : "graph-service";
            var latest = evs.OrderByDescending(e => e.Timestamp).First();

            yield return new RuleMatch(
                $"{RuleName}:{g.Key}",
                $"{evs.Count} denied requests to sensitive graph/RAG endpoints by {(isUser ? id : $"IP {id}")} within {cfg.WindowMinutes} minutes.",
                service, isUser ? id : null, latest.Endpoint,
                evs.Select(e => e.EventId).ToList());
        }
    }

    private static bool IsRag(string? endpoint) =>
        SensitivePaths.IsSensitive(endpoint, ["/api/rag/"]);
}
