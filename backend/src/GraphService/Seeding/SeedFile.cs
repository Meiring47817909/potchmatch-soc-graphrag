using System.Text.Json;

namespace GraphService.Seeding;

/// <summary>A reference to a node by label and key value.</summary>
public record NodeRef(string Label, string Key);

public record SeedNode(string Label, string Key, IReadOnlyDictionary<string, JsonElement>? Properties);

public record SeedRelationship(string Type, NodeRef From, NodeRef To);

/// <summary>Seed file format (spec 5.3): camelCase JSON with nodes and relationships.</summary>
public record SeedFile(IReadOnlyList<SeedNode> Nodes, IReadOnlyList<SeedRelationship> Relationships)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static SeedFile Parse(string json) =>
        JsonSerializer.Deserialize<SeedFile>(json, Options) ?? throw new JsonException("Seed file is empty");
}

public class SeedValidationException(string path, IReadOnlyList<string> errors)
    : Exception($"Seed file '{path}' is invalid: {string.Join("; ", errors)}")
{
    public string Path { get; } = path;
    public IReadOnlyList<string> Errors { get; } = errors;
}
