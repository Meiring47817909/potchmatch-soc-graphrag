using GraphService.Queries;
using GraphService.Rag;

namespace GraphService.Tests;

[Collection("neo4j")]
public class SchemaDescriberTests(Neo4jFixture fx)
{
    [Fact]
    public async Task Schema_DescribesLabelsTypesSamplesExamples()
    {
        await fx.ResetAsync();
        await SyncHarness.LoadSeedsAsync(fx.Driver);
        var s = await new GraphSchemaDescriber(fx.Driver).DescribeAsync(default);

        var threat = Assert.Single(s.Labels, l => l.Label == "Threat");
        Assert.Equal("id", threat.KeyProperty);
        Assert.Contains("name", threat.Properties);
        Assert.True(threat.Count > 0);
        Assert.DoesNotContain(s.Labels, l => l.Label == "SyncState");

        var mitigates = Assert.Single(s.RelationshipTypes, r => r.Type == "MITIGATES");
        Assert.Contains("Control", mitigates.From);
        Assert.Contains("Threat", mitigates.To);
        Assert.True(mitigates.Count > 0);

        Assert.Contains("T1110", s.Samples.ThreatIds);
        Assert.Contains("identity-service", s.Samples.ServiceNames);
        Assert.Contains(s.Samples.ThreatNames, n => n.Contains("Brute Force"));
        Assert.All(new[] { s.Samples.RuleNames, s.Samples.ThreatIds, s.Samples.Severities,
            s.Samples.AlertStatuses, s.Samples.IncidentStatuses, s.Samples.ServiceNames, s.Samples.ThreatNames }, l => Assert.True(l.Count <= 30));

        Assert.Equal(7, s.Examples.Count);
        Assert.All(s.Examples, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Question));
            Assert.True(CypherGuard.Check(e.Cypher).Allowed, e.Name);
        });
        Assert.Contains(s.Examples, e => e.Name == GraphQueries.AlertsAffectingServiceName && e.Question.Contains("$name"));
    }
}
