using SocService.Models;

namespace SocService.Detection;

public interface IDetectionRule
{
    string RuleName { get; }
    string Purpose { get; }
    IReadOnlyList<string> InputEventTypes { get; }
    string DetectionCondition { get; }
    string Severity { get; }
    string AlertGenerated { get; }
    string RecommendedResponse { get; }
    TimeSpan Window { get; }
    IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now);
}
