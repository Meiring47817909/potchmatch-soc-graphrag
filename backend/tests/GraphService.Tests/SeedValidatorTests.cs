using System.Text.Json;
using GraphService.Seeding;

namespace GraphService.Tests;

public class SeedValidatorTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoProps = new Dictionary<string, JsonElement>();

    private static SeedNode Node(string label, string key) => new(label, key, NoProps);
    private static SeedRelationship Rel(string type, NodeRef from, NodeRef to) => new(type, from, to);
    private static readonly IReadOnlySet<NodeRef> None = new HashSet<NodeRef>();

    [Fact]
    public void ValidFile_NoErrors()
    {
        var file = new SeedFile(
            [Node("Service", "a"), Node("Service", "b")],
            [Rel("DEPENDS_ON", new("Service", "a"), new("Service", "b"))]);
        Assert.Empty(SeedValidator.Validate(file, None));
    }

    [Fact]
    public void UnknownLabel_Error()
    {
        var file = new SeedFile([Node("Banana", "x")], []);
        var errors = SeedValidator.Validate(file, None);
        Assert.Contains(errors, e => e.Contains("Banana"));
    }

    [Fact]
    public void UnknownRelationship_Error()
    {
        var file = new SeedFile(
            [Node("Service", "a"), Node("Service", "b")],
            [Rel("LOVES", new("Service", "a"), new("Service", "b"))]);
        Assert.Contains(SeedValidator.Validate(file, None), e => e.Contains("LOVES"));
    }

    [Fact]
    public void DanglingRelationship_Error()
    {
        var file = new SeedFile(
            [Node("Service", "a")],
            [Rel("DEPENDS_ON", new("Service", "a"), new("Service", "ghost"))]);
        Assert.Contains(SeedValidator.Validate(file, None), e => e.Contains("ghost"));
    }

    [Fact]
    public void RelationshipToAlreadyLoaded_Ok()
    {
        var file = new SeedFile(
            [Node("Control", "M1")],
            [Rel("MITIGATES", new("Control", "M1"), new("Threat", "T1"))]);
        var loaded = new HashSet<NodeRef> { new("Threat", "T1") };
        Assert.Empty(SeedValidator.Validate(file, loaded));
    }

    [Fact]
    public void DuplicateNode_Error()
    {
        var file = new SeedFile([Node("Service", "a"), Node("Service", "a")], []);
        Assert.Contains(SeedValidator.Validate(file, None), e => e.Contains("Duplicate") && e.Contains("a"));
    }

    [Fact]
    public void EmptyKey_Error()
    {
        var file = new SeedFile([Node("Service", " ")], []);
        Assert.NotEmpty(SeedValidator.Validate(file, None));
    }

    [Fact]
    public void PropertiesContainKeyProperty_Error()
    {
        var props = new Dictionary<string, JsonElement> { ["id"] = JsonDocument.Parse("\"T9999\"").RootElement };
        var file = new SeedFile([new SeedNode("Threat", "T1110", props)], []);
        var error = Assert.Single(SeedValidator.Validate(file, None));
        Assert.Contains("'id'", error);
        Assert.Contains("Threat/'T1110'", error);
    }
}
