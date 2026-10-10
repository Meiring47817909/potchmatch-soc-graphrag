using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RagService.Clients;
using RagService.Options;
using RagService.Pipeline;

namespace RagService.Tests;

/// <summary>Scripted Gemini: each call pops the next response (a string, or an exception to throw).</summary>
public sealed class FakeGemini(params object[] script) : IGeminiClient
{
    private readonly Queue<object> _script = new(script);
    public List<(string System, string User)> Calls { get; } = [];

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        Calls.Add((systemPrompt, userPrompt));
        if (_script.Count == 0) throw new InvalidOperationException("Gemini called more often than scripted");
        return _script.Dequeue() switch
        {
            Exception ex => Task.FromException<string>(ex),
            string s => Task.FromResult(s),
            var o => throw new InvalidOperationException($"bad script item {o}"),
        };
    }

    public static string Cypher(string cypher, object? parameters = null, string explanation = "Follows the alert to its threat.") =>
        JsonSerializer.Serialize(new { cypher, parameters = parameters ?? new { }, explanation });

    public static string Answer(string answer) => JsonSerializer.Serialize(new { answer });
}

/// <summary>Scripted graph: each query pops the next outcome (GraphQueryOutcome or exception).</summary>
public sealed class FakeGraph(params object[] script) : IGraphQueryClient
{
    private readonly Queue<object> _script = new(script);
    public int SchemaCalls { get; private set; }
    public Exception? SchemaError { get; set; }
    public List<(string Cypher, IReadOnlyDictionary<string, object?> Parameters)> Queries { get; } = [];

    public Task<GraphSchemaDto> GetSchemaAsync(CancellationToken ct)
    {
        SchemaCalls++;
        if (SchemaError is not null) throw SchemaError;
        return Task.FromResult(Evidence.Schema);
    }

    public Task<GraphQueryOutcome> QueryAsync(string cypher, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        Queries.Add((cypher, parameters));
        if (_script.Count == 0) throw new InvalidOperationException("Graph queried more often than scripted");
        return _script.Dequeue() switch
        {
            Exception ex => Task.FromException<GraphQueryOutcome>(ex),
            GraphQueryOutcome o => Task.FromResult(o),
            var o => throw new InvalidOperationException($"bad script item {o}"),
        };
    }
}

public sealed class ListLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
        if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
            foreach (var kv in kvs) Messages.Add($"{kv.Key}={kv.Value}");
    }
}

public static class Evidence
{
    public static Dictionary<string, JsonElement> Props(object o) =>
        JsonSerializer.SerializeToElement(o).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    public static EvidenceNode Node(string id, string label, object props) => new(id, label, Props(props));

    public static readonly GraphSchemaDto Schema = new(
        [new SchemaLabel("Alert", "alertId", ["alertId", "ruleName", "severity"], 4),
         new SchemaLabel("Threat", "id", ["id", "name"], 10)],
        [new SchemaRelType("INDICATES", ["Alert"], ["Threat"], 3)],
        new SchemaSamples(["MULTIPLE_FAILED_LOGINS"], ["T1110"], ["HIGH"], ["OPEN"], ["OPEN"], ["identity-service"], ["T1110: Brute Force"]),
        [new SchemaExample("controls-for-alert", "Which controls relate to the alert with id $alertId?",
            "MATCH p = (a:Alert {alertId: $alertId})-[:INDICATES]->(t:Threat)<-[:MITIGATES]-(c:Control) RETURN p")]);

    public static IReadOnlyList<EvidenceNode> Nodes() =>
    [
        Node("4:x:1", "Alert", new { alertId = "a1", ruleName = "MULTIPLE_FAILED_LOGINS", severity = "HIGH" }),
        Node("4:x:2", "Threat", new { id = "T1110", name = "Brute Force" }),
        Node("4:x:3", "Control", new { id = "M1032", name = "Multi-factor Authentication" }),
        Node("4:x:4", "Document", new { id = "DOC-PASSWORD-RESET", title = "Password Reset Procedure" }),
        Node("4:x:5", "Document", new { id = "DOC-T1110", name = "MITRE ATT&CK T1110 Brute Force", url = "https://attack.mitre.org/techniques/T1110/" }),
    ];

    public static IReadOnlyList<EvidenceRel> Rels() =>
    [
        new("5:x:1", "INDICATES", "4:x:1", "4:x:2"),
        new("5:x:2", "MITIGATES", "4:x:3", "4:x:2"),
        new("5:x:3", "SUPPORTS_RESPONSE_TO", "4:x:4", "4:x:2"),
        new("5:x:4", "SUPPORTS_RESPONSE_TO", "4:x:5", "4:x:2"),
    ];

    public static List<Dictionary<string, JsonElement>> Rows(int n) =>
        Enumerable.Range(1, n).Select(i => Props(new { threatId = "T1110", controlId = "M1032", row = i })).ToList();

    public static GraphQueryOutcome Ok(string cypher = "MATCH p = (a:Alert) RETURN p LIMIT 200", int rows = 2, bool truncated = false,
        IReadOnlyList<EvidenceNode>? nodes = null, IReadOnlyList<EvidenceRel>? rels = null) =>
        new(true, new GraphEvidence(cypher, [], nodes ?? Nodes(), rels ?? Rels(), Rows(rows), truncated), null, null);

    public static GraphQueryOutcome Empty() => new(true, new GraphEvidence("MATCH (n) RETURN n LIMIT 200", [], [], [], [], false), null, null);

    public static GraphQueryOutcome Fail(string error, string reason) => new(false, null, error, reason);

    public static RagPipeline Pipeline(FakeGemini gemini, FakeGraph graph, GeminiOptions? o = null, ListLogger<RagPipeline>? log = null) =>
        new(gemini, graph, new MemoryCache(new MemoryCacheOptions()),
            Microsoft.Extensions.Options.Options.Create(o ?? new GeminiOptions { ApiKey = "k", Model = "gemini-2.5-flash" }),
            log ?? new ListLogger<RagPipeline>());
}
