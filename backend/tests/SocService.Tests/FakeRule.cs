using SocService.Detection;
using SocService.Models;

namespace SocService.Tests;

public class FakeRule : IDetectionRule
{
    public Func<IReadOnlyList<SocEvent>, IEnumerable<RuleMatch>> Matcher { get; set; } = _ => [];
    public List<IReadOnlyList<SocEvent>> Received { get; } = new();

    public string RuleName { get; init; } = SocConstants.Rules.FailedLogins;
    public string Purpose => "test";
    public IReadOnlyList<string> InputEventTypes { get; init; } = ["FAILED_LOGIN"];
    public string DetectionCondition => "test";
    public string Severity { get; init; } = SocConstants.Severity.High;
    public string AlertGenerated => "test";
    public string RecommendedResponse => "Do the thing.";
    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(5);

    public IEnumerable<RuleMatch> Evaluate(IReadOnlyList<SocEvent> events, DateTimeOffset now)
    {
        Received.Add(events);
        return Matcher(events);
    }
}
