using System.Text.Json;
using GraphService.Graph;

namespace GraphService.Seeding;

public static class SeedValidator
{
    /// <summary>Returns one error per problem; empty means the file is safe to load.</summary>
    public static IReadOnlyList<string> Validate(SeedFile file, IReadOnlySet<NodeRef> alreadyLoaded)
    {
        var errors = new List<string>();
        var declared = new HashSet<NodeRef>();

        foreach (var node in file.Nodes ?? [])
        {
            if (node is null) { errors.Add("Null node entry"); continue; }
            var name = $"node {node.Label}/'{node.Key}'";
            if (!GraphSchema.IsKnownLabel(node.Label ?? ""))
            {
                errors.Add($"Unknown label '{node.Label}' on {name}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(node.Key))
            {
                errors.Add($"Empty key on a {node.Label} node");
                continue;
            }
            if (!declared.Add(new NodeRef(node.Label!, node.Key)))
                errors.Add($"Duplicate {name}");
            foreach (var (prop, value) in node.Properties ?? new Dictionary<string, JsonElement>())
                if (value.ValueKind == JsonValueKind.Object)
                    errors.Add($"Property '{prop}' on {name} is a nested object, which Neo4j cannot store");
        }

        foreach (var rel in file.Relationships ?? [])
        {
            if (rel is null || rel.From is null || rel.To is null) { errors.Add("Relationship with missing endpoint"); continue; }
            var name = $"relationship {rel.Type} ({rel.From.Label}/'{rel.From.Key}' -> {rel.To.Label}/'{rel.To.Key}')";
            if (!GraphSchema.IsKnownRelationship(rel.Type ?? ""))
                errors.Add($"Unknown relationship type '{rel.Type}' in {name}");
            foreach (var end in new[] { rel.From, rel.To })
                if (!declared.Contains(end) && !alreadyLoaded.Contains(end))
                    errors.Add($"Dangling endpoint {end.Label}/'{end.Key}' in {name}");
        }

        return errors;
    }
}
