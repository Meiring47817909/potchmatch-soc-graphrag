using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RagService.Clients;

namespace RagService.Pipeline;

/// <summary>Pure helpers that turn graph evidence into prompt text, sources and citation checks.</summary>
public static partial class EvidenceFormatter
{
    private static readonly string[] DisplayKeys = ["name", "title", "id", "key", "alertId", "incidentId", "eventId", "userId"];
    private static readonly string[] CitationKeys = ["id", "alertId", "incidentId", "eventId", "userId", "key", "name", "title"];
    private const int MaxPropertyChars = 240;

    /// <summary>Human-readable node name: first non-empty of name, title, id, key, alertId, incidentId, eventId, userId, else the label.</summary>
    public static string DisplayName(EvidenceNode node) => FirstString(node, DisplayKeys) ?? node.Label;

    /// <summary>Identifier the answer should cite: the node's stable key (id, alertId, …), else name/title, else the element id.</summary>
    public static string CitationId(EvidenceNode node) => FirstString(node, CitationKeys) ?? node.Id;

    /// <summary>Readable triples "{start} —{TYPE}→ {end}"; unknown endpoints fall back to their element id.</summary>
    public static IReadOnlyList<string> Triples(IReadOnlyList<EvidenceNode> nodes, IReadOnlyList<EvidenceRel> rels)
    {
        var byId = new Dictionary<string, EvidenceNode>();
        foreach (var n in nodes) byId.TryAdd(n.Id, n);
        string Name(string id) => byId.TryGetValue(id, out var n) ? DisplayName(n) : id;
        return rels.Select(r => $"{Name(r.Start)} —{r.Type}→ {Name(r.End)}").ToList();
    }

    /// <summary>One line per node for the answer prompt: "[citeId] Label: key=value; …" with long values trimmed.</summary>
    public static IReadOnlyList<string> NodeLines(IReadOnlyList<EvidenceNode> nodes) =>
        nodes.Select(n =>
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(CitationId(n)).Append("] ").Append(n.Label).Append(':');
            var first = true;
            foreach (var (key, value) in n.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var text = value.ValueKind == JsonValueKind.String
                    ? $"\"{Trim(value.GetString()!)}\""
                    : Trim(JsonSerializer.Serialize(value, PromptJson.Options));
                sb.Append(first ? " " : "; ").Append(key).Append('=').Append(text);
                first = false;
            }
            return sb.ToString();
        }).ToList();

    /// <summary>Distinct bracketed tokens in order of appearance; "[A, B]" yields A and B.</summary>
    public static IReadOnlyList<string> Citations(string answer)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (Match m in BracketRegex().Matches(answer ?? ""))
            foreach (var part in m.Groups[1].Value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (seen.Add(part)) result.Add(part);
        return result;
    }

    /// <summary>Cited tokens that match no identifier (key properties, name, title or element id) of any evidence node; case-insensitive.</summary>
    public static IReadOnlyList<string> UnverifiedCitations(string answer, IReadOnlyList<EvidenceNode> nodes)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
        {
            known.Add(n.Id);
            foreach (var k in CitationKeys)
                if (n.Properties.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                    known.Add(s.Trim());
        }
        return Citations(answer).Where(c => !known.Contains(c)).ToList();
    }

    /// <summary>Every Document node in the evidence: id property, name (or title), url.</summary>
    public static IReadOnlyList<SourceRef> Sources(IReadOnlyList<EvidenceNode> nodes) =>
        nodes.Where(n => n.Label == "Document")
            .Select(n => new SourceRef(FirstString(n, ["id"]) ?? n.Id, FirstString(n, ["name", "title"]), FirstString(n, ["url"])))
            .DistinctBy(s => s.Id)
            .ToList();

    private static string? FirstString(EvidenceNode node, IEnumerable<string> keys)
    {
        foreach (var k in keys)
        {
            if (!node.Properties.TryGetValue(k, out var v)) continue;
            var s = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
        }
        return null;
    }

    private static string Trim(string s) => s.Length <= MaxPropertyChars ? s : s[..MaxPropertyChars] + "…";

    [GeneratedRegex(@"\[([^\[\]\r\n]{1,120})\]")]
    private static partial Regex BracketRegex();
}
