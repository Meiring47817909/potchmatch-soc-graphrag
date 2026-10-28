using GraphService.Mapping;

namespace GraphService.Tests;

public class MappingTests
{
    [Fact]
    public void Normalise_GuidNumberAndQuery()
    {
        Assert.Equal(
            "GET /api/players/{id}",
            EndpointNormaliser.ToKey("get", "/api/players/5e9a8f2e-0000-0000-0000-000000000001?x=1"));
        Assert.Equal(
            "PUT /api/payments/{id}/refund",
            EndpointNormaliser.ToKey("PUT", "/api/payments/42/refund/"));
    }

    [Fact]
    public void Normalise_KeepsNonIdSegmentsAndRootPath()
    {
        Assert.Equal("GET /api/v2/players", EndpointNormaliser.ToKey("GET", "/api/v2/players"));
        Assert.Equal("GET /api/items/-5", EndpointNormaliser.ToKey("GET", "/api/items/-5"));
        Assert.Equal("GET /", EndpointNormaliser.ToKey("GET", "/?x=1"));
    }

    [Fact]
    public void Normalise_NullInputs_ReturnNull()
    {
        Assert.Null(EndpointNormaliser.ToKey(null, "/api/x"));
        Assert.Null(EndpointNormaliser.ToKey("GET", null));
        Assert.Null(EndpointNormaliser.ToKey(" ", "/api/x"));
    }

    [Fact]
    public void RuleThreatMap_AllFourRules()
    {
        Assert.True(RuleThreatMap.TryGetThreatId("Multiple Failed Logins", out var t1));
        Assert.Equal("T1110", t1);
        Assert.True(RuleThreatMap.TryGetThreatId("Unauthorised Endpoint Access", out var t2));
        Assert.Equal("T1078", t2);
        Assert.True(RuleThreatMap.TryGetThreatId("POSSIBLE_DOS", out var t3));
        Assert.Equal("T1499", t3);
        Assert.True(RuleThreatMap.TryGetThreatId("SERVICE_FAILURE", out var t4));
        Assert.Equal("T1499", t4);
    }

    [Fact]
    public void RuleThreatMap_Unknown_False()
    {
        Assert.False(RuleThreatMap.TryGetThreatId("Something Else", out _));
        Assert.False(RuleThreatMap.TryGetThreatId(null!, out _));
    }

    [Fact]
    public void UserMatcher_NormalisesEmail()
    {
        Assert.Equal("wickus.coach@potchmatch.demo", UserMatcher.EmailKey(" Wickus.Coach@Potchmatch.Demo "));
        Assert.Equal("email:wickus.coach@potchmatch.demo", UserMatcher.UnknownUserId(" Wickus.Coach@Potchmatch.Demo "));
    }

    [Fact]
    public void UserMatcher_LooksLikeEmail()
    {
        Assert.True(UserMatcher.LooksLikeEmail("someone@example.com"));
        Assert.False(UserMatcher.LooksLikeEmail("5e9a8f2e-0000-0000-0000-000000000001"));
        Assert.False(UserMatcher.LooksLikeEmail("plainusername"));
        Assert.False(UserMatcher.LooksLikeEmail(null));
        Assert.False(UserMatcher.LooksLikeEmail("   "));
    }

    [Fact]
    public void Subject_PrefersAffectedUser()
    {
        var result = SubjectResolver.Resolve(
            "  alice ", ["bob"], ["10.0.0.1"], "soc-service");
        Assert.Equal("user:alice", result);
    }

    [Fact]
    public void Subject_FallsBackToTriggeringUser()
    {
        var result = SubjectResolver.Resolve(null, ["zed", "bob"], ["10.0.0.1"], "soc-service");
        Assert.Equal("user:bob", result);
    }

    [Fact]
    public void Subject_MostFrequentIp()
    {
        Assert.Equal(
            "ip:10.0.0.2",
            SubjectResolver.Resolve(null, [], ["10.0.0.2", "10.0.0.1", "10.0.0.2"], null));
        // Tie broken by ordinal minimum.
        Assert.Equal(
            "ip:10.0.0.1",
            SubjectResolver.Resolve(null, [], ["10.0.0.9", "10.0.0.1"], null));
        // Blank entries are ignored.
        Assert.Equal(
            "ip:10.0.0.1",
            SubjectResolver.Resolve(null, [" ", ""], [" ", "10.0.0.1"], null));
    }

    [Fact]
    public void Subject_Service()
    {
        Assert.Equal("service:soc-service", SubjectResolver.Resolve(null, [], [], "soc-service"));
    }

    [Fact]
    public void Subject_Unknown()
    {
        Assert.Equal("unknown", SubjectResolver.Resolve(null, [], [], null));
        Assert.Equal("unknown", SubjectResolver.Resolve(" ", [" "], [" "], " "));
    }

    [Fact]
    public void RuleThreatMap_GraphAccess_MapsToOwaspA01()
    {
        Assert.True(RuleThreatMap.TryGetThreatId("GRAPH_ACCESS", out var id));
        Assert.Equal("OWASP-A01-2025", id);
    }
}
