using Microsoft.Extensions.Options;
using SocService.Detection.Rules;
using SocService.Models;

namespace SocService.Tests;

public class ServiceFailureRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static ServiceFailureRule NewRule() => new(Options.Create(new SocService.Detection.DetectionOptions()));

    private static SocEvent Ev(string type, int ageSeconds, string? affected = "payment-service", string service = "soc-service") => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = Now.AddSeconds(-ageSeconds),
        ServiceName = service,
        EventType = type,
        Severity = SocConstants.Severity.High,
        AffectedEntity = affected,
    };

    [Fact]
    public void ServiceFailure_HealthCritical_MatchesImmediately()
    {
        var match = Assert.Single(NewRule().Evaluate([Ev("SERVICE_HEALTH_CRITICAL", 10)], Now));
        Assert.Equal("SERVICE_FAILURE:payment-service", match.DedupKey);
        Assert.Equal("payment-service", match.AffectedService);
        Assert.Equal("payment-service", match.AffectedEntity);
    }

    [Fact]
    public void ServiceFailure_TwoCallFailures_NoMatch() =>
        Assert.Empty(NewRule().Evaluate([Ev("INTER_SERVICE_CALL_FAILED", 10), Ev("INTER_SERVICE_CALL_FAILED", 20)], Now));

    [Fact]
    public void ServiceFailure_ThreeCallFailures_Match()
    {
        var events = Enumerable.Range(1, 3).Select(i => Ev("INTER_SERVICE_CALL_FAILED", i * 10)).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal(3, match.EventIds.Count);
    }

    [Fact]
    public void ServiceFailure_UnhandledException_UsesOwnServiceName()
    {
        var events = Enumerable.Range(1, 3)
            .Select(i => Ev("UNHANDLED_EXCEPTION", i * 10, affected: null, service: "booking-service")).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("SERVICE_FAILURE:booking-service", match.DedupKey);
    }

    [Fact]
    public void ServiceFailure_CriticalThenRecovered_NoMatch() =>
        Assert.Empty(NewRule().Evaluate([Ev("SERVICE_HEALTH_CRITICAL", 60), Ev("SERVICE_HEALTH_RECOVERED", 30)], Now));

    [Fact]
    public void ServiceFailure_FailsAgainAfterRecovery_NewMatch()
    {
        var first = Ev("SERVICE_HEALTH_CRITICAL", 90);
        var recovered = Ev("SERVICE_HEALTH_RECOVERED", 60);
        var second = Ev("SERVICE_HEALTH_CRITICAL", 20);
        var match = Assert.Single(NewRule().Evaluate([first, recovered, second], Now));
        Assert.Equal([second.EventId], match.EventIds);
    }

    [Fact]
    public void ServiceFailure_FailuresBeforeRecovery_DoNotCount()
    {
        var events = new List<SocEvent>
        {
            Ev("INTER_SERVICE_CALL_FAILED", 90), Ev("INTER_SERVICE_CALL_FAILED", 80),
            Ev("SERVICE_HEALTH_RECOVERED", 70), Ev("INTER_SERVICE_CALL_FAILED", 10),
        };
        Assert.Empty(NewRule().Evaluate(events, Now));
    }

    [Fact]
    public void ServiceFailure_DifferentServices_AreIndependent()
    {
        var events = new List<SocEvent>
        {
            Ev("SERVICE_HEALTH_CRITICAL", 60, "a-service"), Ev("SERVICE_HEALTH_RECOVERED", 30, "b-service"),
        };
        Assert.Equal("SERVICE_FAILURE:a-service", Assert.Single(NewRule().Evaluate(events, Now)).DedupKey);
    }

    [Fact]
    public void FindRecoveries_ReturnsLatestPerService()
    {
        var events = new List<SocEvent>
        {
            Ev("SERVICE_HEALTH_RECOVERED", 60), Ev("SERVICE_HEALTH_RECOVERED", 20), Ev("SERVICE_HEALTH_CRITICAL", 5),
        };
        var r = Assert.Single(NewRule().FindRecoveries(events));
        Assert.Equal(("payment-service", Now.AddSeconds(-20)), r);
    }
}
