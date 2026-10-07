using SocService.Detection;

namespace SocService.Dtos;

public record RuleDocResponse(
    string RuleName,
    string Purpose,
    IReadOnlyList<string> InputEvents,
    string DetectionCondition,
    string Severity,
    string AlertGenerated,
    string RecommendedResponse)
{
    public static RuleDocResponse From(IDetectionRule r) => new(r.RuleName, r.Purpose, r.InputEventTypes,
        r.DetectionCondition, r.Severity, r.AlertGenerated, r.RecommendedResponse);
}
