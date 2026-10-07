using SocService.Health;

namespace SocService.Tests;

public class HealthTransitionDetectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string> S(params (string, string)[] items) =>
        items.ToDictionary(i => i.Item1, i => i.Item2);

    [Fact]
    public void Detector_FirstPoll_NoEvents() =>
        Assert.Empty(HealthTransitionDetector.Detect(null, S(("payment-service", "critical")), Now));

    [Fact]
    public void Detector_PassingToCritical_CriticalEvent()
    {
        var e = Assert.Single(HealthTransitionDetector.Detect(
            S(("payment-service", "passing")), S(("payment-service", "critical")), Now));
        Assert.Equal("SERVICE_HEALTH_CRITICAL", e.EventType);
        Assert.Equal("HIGH", e.Severity);
        Assert.Equal("soc-service", e.ServiceName);
        Assert.Equal("payment-service", e.AffectedEntity);
        Assert.Equal("payment-service health check is critical", e.Message);
        Assert.Equal(Now, e.Timestamp);
        Assert.Equal(Now, e.ReceivedAt);
        Assert.NotEqual(Guid.Empty, e.EventId);
    }

    [Fact]
    public void Detector_CriticalToPassing_RecoveredEvent()
    {
        var e = Assert.Single(HealthTransitionDetector.Detect(
            S(("payment-service", "critical")), S(("payment-service", "passing")), Now));
        Assert.Equal("SERVICE_HEALTH_RECOVERED", e.EventType);
        Assert.Equal("LOW", e.Severity);
        Assert.Equal("payment-service", e.AffectedEntity);
    }

    [Fact]
    public void Detector_NoChange_NoEvents()
    {
        var s = S(("a", "passing"), ("b", "critical"), ("c", "warning"));
        Assert.Empty(HealthTransitionDetector.Detect(s, S(("a", "passing"), ("b", "critical"), ("c", "warning")), Now));
    }

    [Fact]
    public void Detector_ServiceDisappears_CriticalEvent()
    {
        var e = Assert.Single(HealthTransitionDetector.Detect(
            S(("payment-service", "passing")), S(), Now));
        Assert.Equal("SERVICE_HEALTH_CRITICAL", e.EventType);
        Assert.Equal("payment-service", e.AffectedEntity);
    }

    [Fact]
    public void Detector_NewServiceAppearingCritical_NoEvent()
    {
        Assert.Empty(HealthTransitionDetector.Detect(S(), S(("a", "critical")), Now));
    }

    [Fact]
    public void Detector_VanishedServiceReturning_ReportsRecovery()
    {
        var prev = S(("a", "passing"));
        var baseline = HealthTransitionDetector.NextBaseline(prev, S());
        var e = Assert.Single(HealthTransitionDetector.Detect(baseline, S(("a", "passing")), Now));
        Assert.Equal("SERVICE_HEALTH_RECOVERED", e.EventType);
    }
}
