using Microsoft.Extensions.Options;
using SocService.Detection.Rules;
using SocService.Models;

namespace SocService.Tests;

public class UnauthorisedEndpointAccessRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static UnauthorisedEndpointAccessRule NewRule() => new(Options.Create(new SocService.Detection.DetectionOptions()));

    private static SocEvent Rejected(string? user, string? ip, int ageSeconds = 60, string service = "api-gateway", string endpoint = "/api/x") => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = Now - TimeSpan.FromSeconds(ageSeconds),
        ServiceName = service,
        EventType = "AUTH_TOKEN_REJECTED",
        Severity = SocConstants.Severity.Medium,
        UserId = user,
        SourceIp = ip,
        Endpoint = endpoint,
    };

    [Fact]
    public void UnauthorisedAccess_ThreeForSameUser_Match()
    {
        var events = new List<SocEvent>
        {
            Rejected("u1", "1.1.1.1", 120, "a-service", "/old"),
            Rejected("u1", "1.1.1.1", 60, "b-service", "/mid"),
            Rejected("u1", "1.1.1.1", 10, "b-service", "/latest"),
        };
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("Unauthorised Endpoint Access:user:u1", match.DedupKey);
        Assert.Equal(3, match.EventIds.Count);
        Assert.Equal("b-service", match.AffectedService);
        Assert.Equal("/latest", match.AffectedEntity);
        Assert.Equal("3 rejected requests to protected endpoints by u1 within 5 minutes.", match.Description);
    }

    [Fact]
    public void UnauthorisedAccess_TwoForSameUser_NoMatch()
    {
        var events = new[] { Rejected("u1", null), Rejected("u1", null) };
        Assert.Empty(NewRule().Evaluate(events, Now));
    }

    [Fact]
    public void UnauthorisedAccess_NoUser_GroupsByIp()
    {
        var events = Enumerable.Range(0, 3).Select(_ => Rejected(null, "7.7.7.7")).ToList();
        var match = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("Unauthorised Endpoint Access:ip:7.7.7.7", match.DedupKey);
        Assert.Null(match.AffectedUser);
        Assert.Equal("3 rejected requests to protected endpoints by IP 7.7.7.7 within 5 minutes.", match.Description);
    }

    [Fact]
    public void UnauthorisedAccess_IgnoresEventsWithNoUserOrIp()
    {
        var events = Enumerable.Range(0, 5).Select(_ => Rejected(null, null)).ToList();
        Assert.Empty(NewRule().Evaluate(events, Now));
    }
}
