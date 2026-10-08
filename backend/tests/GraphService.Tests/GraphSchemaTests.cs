using GraphService.Graph;

namespace GraphService.Tests;

public class GraphSchemaTests
{
    [Fact]
    public void GraphSchema_HasAllTwelveSpecLabelsWithKeys()
    {
        var expected = new Dictionary<string, string>
        {
            ["Service"] = "name",
            ["Endpoint"] = "key",
            ["Asset"] = "name",
            ["Role"] = "name",
            ["User"] = "userId",
            ["Event"] = "eventId",
            ["Alert"] = "alertId",
            ["Incident"] = "incidentId",
            ["Threat"] = "id",
            ["Control"] = "id",
            ["Vulnerability"] = "id",
            ["Document"] = "id",
            ["SyncState"] = "name",
        };

        Assert.Equal(13, GraphSchema.KeyProperty.Count);
        foreach (var (label, key) in expected)
        {
            Assert.True(GraphSchema.IsKnownLabel(label), label);
            Assert.Equal(key, GraphSchema.KeyProperty[label]);
        }
        Assert.False(GraphSchema.IsKnownLabel("Bogus"));
    }

    [Fact]
    public void GraphSchema_HasAll17RelationshipTypes()
    {
        var expected = new[]
        {
            "TRIGGERED", "GENERATED_BY", "TARGETED", "CREATED_ALERT", "INDICATES", "TARGETS",
            "EXPLOITS", "MITIGATES", "CONTAINS", "DEPENDS_ON", "EXPOSES", "HAS_ROLE",
            "DESCRIBES", "SUPPORTS_RESPONSE_TO", "AFFECTS", "USES", "REQUIRES_ROLE",
        };

        Assert.Equal(17, GraphSchema.RelationshipTypes.Count);
        foreach (var t in expected) Assert.True(GraphSchema.IsKnownRelationship(t), t);
        Assert.False(GraphSchema.IsKnownRelationship("NOPE"));
    }
}
