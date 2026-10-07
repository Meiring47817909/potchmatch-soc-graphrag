using Microsoft.Extensions.Options;
using SocService.Models;

namespace SocService.Detection.Rules;

/// <summary>Rule 1: repeated failed logins against one account or from one source IP.</summary>
public class MultipleFailedLoginsRule(IOptions<DetectionOptions> options) : IDetectionRule
{
    private DetectionOptions.FailedLoginsOptions Cfg => options.Value.FailedLogins;

    public string RuleName => SocConstants.Rules.FailedLogins;
    public string Purpose => "Detect brute-force or credential-stuffing attempts against the login endpoint.";
    public IReadOnlyList<string> InputEventTypes { get; } = ["FAILED_LOGIN"];
    public string DetectionCondition =>
        $"{Cfg.Threshold} or more FAILED_LOGIN events within {Cfg.WindowMinutes} minutes for the same target account or for the same source IP.";
    public string Severity => SocConstants.Severity.High;
    public string AlertGenerated => "Multiple Failed Logins alert (HIGH) naming the targeted account or source IP, linked to the failed-login events.";
    public string RecommendedResponse =>
        "Temporarily block the source IP and verify whether the account is compromised; consider forcing a password reset.";
    public TimeSpan Window => TimeSpan.FromMinutes(Cfg.WindowMinutes);

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        var cfg = Cfg;
        var from = now - Window;
        var recent = events
            .Where(e => e.EventType == "FAILED_LOGIN" && e.Timestamp >= from)
            .ToList();

        var byAccount = recent
            .Where(e => !string.IsNullOrWhiteSpace(e.UserId))
            .GroupBy(e => e.UserId!.Trim().ToLowerInvariant())
            .Where(g => g.Count() >= cfg.Threshold)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in byAccount)
        {
            yield return new RuleMatch(
                $"{RuleName}:account:{g.Key}",
                $"{g.Count()} failed login attempts for {g.Key} within {cfg.WindowMinutes} minutes.",
                "identity-service", g.Key, "/api/auth/login",
                g.Select(e => e.EventId).ToList());
        }

        var byIp = recent
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceIp))
            .GroupBy(e => e.SourceIp!.Trim())
            .Where(g => g.Count() >= cfg.Threshold)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in byIp)
        {
            yield return new RuleMatch(
                $"{RuleName}:ip:{g.Key}",
                $"{g.Count()} failed login attempts for IP {g.Key} within {cfg.WindowMinutes} minutes.",
                "identity-service", null, "/api/auth/login",
                g.Select(e => e.EventId).ToList());
        }
    }
}
