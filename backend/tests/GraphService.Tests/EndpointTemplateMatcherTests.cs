using GraphService.Mapping;

namespace GraphService.Tests;

public class EndpointTemplateMatcherTests
{
    private static readonly string[] Templates =
    [
        "GET /api/graph/services/{name}/alerts",
        "GET /api/graph/incidents",
        "GET /api/graph/incidents/{id}",
        "GET /api/payments/{id}",
        "GET /api/payments/wallet",
        "PUT /api/players/{id}",
        "GET /api/a/{x}/{y}",
        "GET /api/a/b/{y}",
        "GET /api/a/{x}/c",
    ];

    [Theory]
    [InlineData("GET /api/graph/services/identity-service/alerts", "GET /api/graph/services/{name}/alerts")]
    [InlineData("GET /api/graph/incidents/INC-abc123", "GET /api/graph/incidents/{id}")]
    [InlineData("GET /api/graph/incidents", "GET /api/graph/incidents")]
    [InlineData("GET /api/payments/{id}", "GET /api/payments/{id}")]
    public void Match_SegmentWildcards(string key, string expected) =>
        Assert.Equal(expected, EndpointTemplateMatcher.Match(key, Templates));

    [Fact]
    public void Match_PrefersExactOverWildcard() =>
        Assert.Equal("GET /api/payments/wallet", EndpointTemplateMatcher.Match("GET /api/payments/wallet", Templates));

    [Fact]
    public void Match_TieBreak_FewestWildcardsThenOrdinal()
    {
        // b/c matches all three; {x}/{y} has two wildcards, the others one each -> ordinal between "/api/a/b/{y}" and "/api/a/{x}/c".
        Assert.Equal("GET /api/a/b/{y}", EndpointTemplateMatcher.Match("GET /api/a/b/c", Templates));
        Assert.Equal("GET /api/a/{x}/{y}", EndpointTemplateMatcher.Match("GET /api/a/q/r", Templates));
    }

    [Theory]
    [InlineData("POST /api/graph/incidents/INC-1")] // method differs
    [InlineData("PUT /api/players/1/extra")]         // segment count differs
    [InlineData("GET /api/graph/other/x")]           // literal differs
    public void Match_NoTemplate_ReturnsNull(string key) =>
        Assert.Null(EndpointTemplateMatcher.Match(key, Templates));
}
