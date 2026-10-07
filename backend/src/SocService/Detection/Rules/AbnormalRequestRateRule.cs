using Microsoft.Extensions.Options;
using SocService.Models;

namespace SocService.Detection.Rules;

/// <summary>Rule 3: abnormal request volume from one source IP at the API gateway.</summary>
public class AbnormalRequestRateRule(IOptions<DetectionOptions> options) : IDetectionRule
{
    private const string GatewayService = "api-gateway";
    private const string RequestReceived = "REQUEST_RECEIVED";

    private DetectionOptions.RequestRateOptions Cfg => options.Value.RequestRate;

    public string RuleName => SocConstants.Rules.RequestRate;
    public string Purpose => "Detect request floods from a single source that may indicate a denial-of-service attempt.";
    public IReadOnlyList<string> InputEventTypes { get; } = [RequestReceived];
    public string DetectionCondition =>
        $"More than {Cfg.Threshold} REQUEST_RECEIVED events at the API gateway within {Cfg.WindowSeconds} seconds from the same source IP.";
    public string Severity => SocConstants.Severity.High;
    public string AlertGenerated => "POSSIBLE_DOS alert (HIGH) naming the source IP, linked to the gateway request events.";
    public string RecommendedResponse => "Rate-limit or temporarily block the source IP at the gateway.";
    public TimeSpan Window => TimeSpan.FromSeconds(Cfg.WindowSeconds);

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        var cfg = Cfg;
        var from = now - Window;

        var groups = events
            .Where(e => e.EventType == RequestReceived && e.ServiceName == GatewayService
                        && e.Timestamp >= from && !string.IsNullOrWhiteSpace(e.SourceIp))
            .GroupBy(e => e.SourceIp!.Trim())
            .Where(g => g.Count() > cfg.Threshold)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            yield return new RuleMatch(
                $"{RuleName}:ip:{g.Key}",
                $"{g.Count()} requests from IP {g.Key} to the API gateway within {cfg.WindowSeconds} seconds.",
                GatewayService, null, GatewayService,
                g.Select(e => e.EventId).ToList());
        }
    }
}
