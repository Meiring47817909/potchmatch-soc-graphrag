using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SocService.Controllers;
using SocService.Detection;
using SocService.Detection.Rules;
using SocService.Dtos;
using SocService.Models;

namespace SocService.Tests;

public class GraphAccessRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Prefixes = ["/api/graph/", "/api/rag/"];

    private static GraphAccessRule NewRule() => new(Options.Create(new DetectionOptions()));

    private static SocEvent Denied(string? user, string? ip, string endpoint, int ageSeconds = 60,
        string type = "UNAUTHORISED_ACCESS") => new()
    {
        EventId = Guid.NewGuid(),
        Timestamp = Now - TimeSpan.FromSeconds(ageSeconds),
        ServiceName = "api-gateway",
        EventType = type,
        Severity = SocConstants.Severity.Medium,
        UserId = user,
        SourceIp = ip,
        Endpoint = endpoint,
    };

    [Theory]
    [InlineData("/API/Graph/incidents", true)]
    [InlineData("/api/rag", true)]
    [InlineData("/api/rag/ask", true)]
    [InlineData("/api/graphql", false)]
    [InlineData("/api/soc/alerts", false)]
    [InlineData(null, false)]
    public void SensitivePaths_CaseAndBarePrefix(string? endpoint, bool expected) =>
        Assert.Equal(expected, SensitivePaths.IsSensitive(endpoint, Prefixes));

    [Fact]
    public void GraphAccess_ThreeDenialsSameUser_Fires()
    {
        var events = new List<SocEvent>
        {
            Denied("u1", "1.1.1.1", "/api/graph/incidents", 120),
            Denied("u1", "1.1.1.1", "/api/graph/incidents", 60),
            Denied("u1", "1.1.1.1", "/api/rag/ask", 10),
        };
        var m = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("GRAPH_ACCESS:user:u1", m.DedupKey);
        Assert.Equal("u1", m.AffectedUser);
        Assert.Equal("graph-service", m.AffectedService);
        Assert.Equal("/api/rag/ask", m.AffectedEntity);
        Assert.Equal(3, m.EventIds.Count);
        Assert.Equal("3 denied requests to sensitive graph/RAG endpoints by u1 within 5 minutes.", m.Description);
    }

    [Fact]
    public void GraphAccess_TwoDenials_NoMatch() =>
        Assert.Empty(NewRule().Evaluate(
            [Denied("u1", null, "/api/graph/a"), Denied("u1", null, "/api/graph/b")], Now));

    [Fact]
    public void GraphAccess_OutsideWindow_NoMatch() =>
        Assert.Empty(NewRule().Evaluate(
            [Denied("u1", null, "/api/graph/a"), Denied("u1", null, "/api/graph/b"),
             Denied("u1", null, "/api/graph/c", 360)], Now));

    [Fact]
    public void GraphAccess_NoUser_GroupsByIp()
    {
        var events = Enumerable.Range(0, 3).Select(_ => Denied(null, "7.7.7.7", "/api/graph/x")).ToList();
        var m = Assert.Single(NewRule().Evaluate(events, Now));
        Assert.Equal("GRAPH_ACCESS:ip:7.7.7.7", m.DedupKey);
        Assert.Null(m.AffectedUser);
        Assert.Equal("3 denied requests to sensitive graph/RAG endpoints by IP 7.7.7.7 within 5 minutes.", m.Description);
    }

    [Fact]
    public void GraphAccess_AuthTokenRejected_Counts()
    {
        var events = new List<SocEvent>
        {
            Denied("u1", null, "/api/graph/a"),
            Denied("u1", null, "/api/graph/b"),
            Denied("u1", null, "/api/rag/ask", type: "AUTH_TOKEN_REJECTED"),
        };
        Assert.Single(NewRule().Evaluate(events, Now));
    }

    [Fact]
    public void GraphAccess_NonSensitivePaths_Ignored() =>
        Assert.Empty(NewRule().Evaluate(
            Enumerable.Range(0, 5).Select(_ => Denied("u1", null, "/api/soc/alerts")).ToList(), Now));

    [Fact]
    public void GraphAccess_RagMostTargeted_AffectsRagService()
    {
        var events = new List<SocEvent>
        {
            Denied("u1", null, "/api/rag/ask"), Denied("u1", null, "/api/rag/ask"), Denied("u1", null, "/api/graph/x"),
        };
        Assert.Equal("rag-service", Assert.Single(NewRule().Evaluate(events, Now)).AffectedService);
    }

    [Fact]
    public void GraphAccess_Tie_AffectsGraphService()
    {
        var events = new List<SocEvent>
        {
            Denied("u1", null, "/api/rag/ask"), Denied("u1", null, "/api/graph/x"),
            Denied("u1", null, "/api/rag/status"), Denied("u1", null, "/api/graph/y"),
        };
        Assert.Equal("graph-service", Assert.Single(NewRule().Evaluate(events, Now)).AffectedService);
    }

    [Fact]
    public void UnauthorisedAccess_IgnoresSensitivePaths()
    {
        var rule = new UnauthorisedEndpointAccessRule(Options.Create(new DetectionOptions()));
        var graph = Enumerable.Range(0, 3).Select(_ => Denied("u1", null, "/api/graph/incidents")).ToList();
        Assert.Empty(rule.Evaluate(graph, Now));
        var soc = Enumerable.Range(0, 3).Select(_ => Denied("u1", null, "/api/soc/alerts")).ToList();
        Assert.Single(rule.Evaluate(soc, Now));
    }

    [Fact]
    public void Rules_ListsGraphAccess()
    {
        var opts = Options.Create(new DetectionOptions());
        var controller = new RulesController([new UnauthorisedEndpointAccessRule(opts), new GraphAccessRule(opts)]);
        var docs = Assert.IsAssignableFrom<List<RuleDocResponse>>(Assert.IsType<OkObjectResult>(controller.Get()).Value);
        var d = Assert.Single(docs, r => r.RuleName == "GRAPH_ACCESS");
        Assert.False(string.IsNullOrWhiteSpace(d.Purpose));
        Assert.NotEmpty(d.InputEvents);
        Assert.False(string.IsNullOrWhiteSpace(d.DetectionCondition));
        Assert.Equal("HIGH", d.Severity);
        Assert.False(string.IsNullOrWhiteSpace(d.AlertGenerated));
        Assert.False(string.IsNullOrWhiteSpace(d.RecommendedResponse));
    }
}
