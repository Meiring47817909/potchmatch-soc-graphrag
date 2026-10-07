using Microsoft.Extensions.Options;
using SocService.Detection.Rules;
using SocService.Models;

namespace SocService.Tests;

public class AbnormalRequestRateRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static AbnormalRequestRateRule NewRule() => new(Options.Create(new SocService.Detection.DetectionOptions()));

    private static List<SocEvent> Requests(int count, string ip = "9.9.9.9", string service = "api-gateway") =>
        Enumerable.Range(0, count).Select(i => new SocEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = Now.AddSeconds(-(i % 50)),
            ServiceName = service,
            EventType = "REQUEST_RECEIVED",
            Severity = SocConstants.Severity.Low,
            SourceIp = ip,
        }).ToList();

    [Fact]
    public void RequestRate_100_NoMatch() => Assert.Empty(NewRule().Evaluate(Requests(100), Now));

    [Fact]
    public void RequestRate_101_SameIp_Match()
    {
        var match = Assert.Single(NewRule().Evaluate(Requests(101), Now));
        Assert.Equal("POSSIBLE_DOS:ip:9.9.9.9", match.DedupKey);
        Assert.Equal("api-gateway", match.AffectedService);
        Assert.Equal("api-gateway", match.AffectedEntity);
        Assert.Equal(101, match.EventIds.Count);
    }

    [Fact]
    public void RequestRate_IgnoresNonGatewayEvents() =>
        Assert.Empty(NewRule().Evaluate(Requests(101, service: "identity-service"), Now));

    [Fact]
    public void RequestRate_SplitAcrossIps_NoMatch()
    {
        var events = Requests(60, "1.1.1.1").Concat(Requests(60, "2.2.2.2")).ToList();
        Assert.Empty(NewRule().Evaluate(events, Now));
    }
}
