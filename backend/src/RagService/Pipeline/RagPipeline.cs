using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using RagService.Clients;
using RagService.Options;
using Serilog.Context;

namespace RagService.Pipeline;

/// <summary>
/// Text2Cypher GraphRAG (spec §5.1): schema → Gemini writes read-only Cypher → GraphService guard executes it
/// (one repair attempt on rejection) → Gemini answers from the evidence only → answer + full evidence.
/// </summary>
public sealed class RagPipeline(
    IGeminiClient gemini,
    IGraphQueryClient graph,
    IMemoryCache cache,
    IOptions<GeminiOptions> options,
    ILogger<RagPipeline> logger)
{
    public const int MaxPromptRows = 50;
    public const int MaxPromptNodes = 80;
    public const int MaxPromptTriples = 150;
    public const string CouldNotQueryAnswer = "I could not build a valid graph query for this question.";
    public const string NoEvidenceAnswer = "The knowledge graph has no evidence for this question.";

    private const string SchemaCacheKey = "rag:graph-schema";
    private static readonly TimeSpan SchemaTtl = TimeSpan.FromMinutes(10);

    public async Task<RagAnswer> AskAsync(AskRequest request, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled) throw new RagDisabledException();

        var sw = Stopwatch.StartNew();
        var attempts = 0;
        var outcome = "error";
        RagAnswer? result = null;
        try
        {
            // 1. Schema (cached).
            var schema = await GetSchemaAsync(ct);
            var system = PromptBuilder.CypherSystem(schema);

            // 2. Generate Cypher.
            var draft = await GenerateCypherAsync(system, PromptBuilder.CypherUser(request), ct);
            var parameters = MergeParameters(draft.Parameters, request);

            // 3. Execute, with exactly one repair attempt on rejected / cypher_error.
            attempts = 1;
            var query = await graph.QueryAsync(draft.Cypher!, parameters, ct);
            if (!query.Ok)
            {
                var reason = $"{query.Error}: {query.Reason}";
                draft = await GenerateCypherAsync(system, PromptBuilder.RepairUser(request, draft.Cypher!, reason), ct);
                parameters = MergeParameters(draft.Parameters, request);
                attempts = 2;
                query = await graph.QueryAsync(draft.Cypher!, parameters, ct);
                if (!query.Ok)
                {
                    outcome = "query_failed";
                    return result = Failed(request, o, draft, parameters, query, attempts);
                }
            }

            var ev = query.Evidence!;

            // 4. No evidence: do not ask the model to answer from nothing.
            if (ev.Rows.Count == 0 && ev.Nodes.Count == 0)
            {
                outcome = "no_evidence";
                return result = Build(request, o, NoEvidenceAnswer, answered: false, draft, parameters, ev, attempts, ev.Truncated, []);
            }

            // 5. Grounded answer from at most 50 rows (+ nodes and triples).
            var promptRows = ev.Rows.Take(MaxPromptRows).ToList();
            var promptNodes = ev.Nodes.Take(MaxPromptNodes).ToList();
            var triples = EvidenceFormatter.Triples(ev.Nodes, ev.Relationships).Take(MaxPromptTriples).ToList();
            var truncated = ev.Truncated
                || ev.Rows.Count > MaxPromptRows || ev.Nodes.Count > MaxPromptNodes || ev.Relationships.Count > MaxPromptTriples;

            var answerText = await gemini.GenerateAsync(
                PromptBuilder.AnswerSystem(),
                PromptBuilder.AnswerUser(request.Question, promptRows, triples, truncated, EvidenceFormatter.NodeLines(promptNodes)),
                ct);
            var answer = LlmJson.Parse<AnswerDraft>(answerText)?.Answer?.Trim();
            if (string.IsNullOrEmpty(answer)) throw new LlmUnavailableException(RagMessages.LlmBusy);

            // 6. Respond, with the citation check made visible.
            outcome = "answered";
            return result = Build(request, o, answer, answered: true, draft, parameters, ev, attempts, truncated,
                EvidenceFormatter.UnverifiedCitations(answer, ev.Nodes));
        }
        catch (RagDisabledException) { outcome = "rag_disabled"; throw; }
        catch (LlmUnavailableException) { outcome = "llm_unavailable"; throw; }
        catch (GraphUnavailableException) { outcome = "graph_unavailable"; throw; }
        finally
        {
            LogQuery(request, outcome, attempts, result, sw.ElapsedMilliseconds);
        }
    }

    private async Task<GraphSchemaDto> GetSchemaAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(SchemaCacheKey, out GraphSchemaDto? cached) && cached is not null) return cached;
        var schema = await graph.GetSchemaAsync(ct);
        cache.Set(SchemaCacheKey, schema, SchemaTtl);
        return schema;
    }

    private async Task<CypherDraft> GenerateCypherAsync(string system, string user, CancellationToken ct)
    {
        var text = await gemini.GenerateAsync(system, user, ct);
        var draft = LlmJson.Parse<CypherDraft>(text);
        if (draft is null || string.IsNullOrWhiteSpace(draft.Cypher)) throw new LlmUnavailableException(RagMessages.LlmBusy);
        return draft with { Cypher = draft.Cypher.Trim() };
    }

    /// <summary>Model parameters first, then the request's context ids, so the model cannot override $alertId / $incidentId.</summary>
    private static Dictionary<string, object?> MergeParameters(Dictionary<string, JsonElement>? modelParameters, AskRequest request)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (modelParameters is not null)
            foreach (var (k, v) in modelParameters) merged[k] = v;
        if (!string.IsNullOrWhiteSpace(request.AlertId)) merged["alertId"] = request.AlertId.Trim();
        if (!string.IsNullOrWhiteSpace(request.IncidentId)) merged["incidentId"] = request.IncidentId.Trim();
        return merged;
    }

    private static RagAnswer Failed(AskRequest request, GeminiOptions o, CypherDraft draft, Dictionary<string, object?> parameters,
        GraphQueryOutcome query, int attempts)
    {
        // The failing query's error and reason are returned as the single evidence row (rowCount = rows.Count = 1)
        // so the admin can see why.
        var errorRow = new Dictionary<string, JsonElement>
        {
            ["error"] = JsonSerializer.SerializeToElement(query.Error ?? "rejected"),
            ["reason"] = JsonSerializer.SerializeToElement(query.Reason ?? ""),
        };
        return new RagAnswer(request.Question, false, CouldNotQueryAnswer, RagAnswer.ModeText2Cypher,
            new RetrievalInfo(RagAnswer.MethodText2Cypher, o.Model, attempts, draft.Explanation, 1, false, []),
            draft.Cypher, parameters, [], [], [errorRow], []);
    }

    private static RagAnswer Build(AskRequest request, GeminiOptions o, string answer, bool answered, CypherDraft draft,
        Dictionary<string, object?> parameters, GraphEvidence ev, int attempts, bool truncated, IReadOnlyList<string> unverified) =>
        new(request.Question, answered, answer, RagAnswer.ModeText2Cypher,
            new RetrievalInfo(RagAnswer.MethodText2Cypher, o.Model, attempts, draft.Explanation, ev.Rows.Count, truncated, unverified),
            string.IsNullOrWhiteSpace(ev.Cypher) ? draft.Cypher : ev.Cypher,
            parameters, ev.Nodes, ev.Relationships, ev.Rows, EvidenceFormatter.Sources(ev.Nodes));

    // Shipped to the SOC pipeline (SecurityEvent) as RAG_QUERY / LOW. Never the question text, prompts or key.
    private void LogQuery(AskRequest request, string outcome, int attempts, RagAnswer? result, long latencyMs)
    {
        using (LogContext.PushProperty("EventId", Guid.NewGuid().ToString()))
        using (LogContext.PushProperty("SecurityEvent", true))
        {
            logger.LogInformation(
                "{EventType} [{Severity}] RAG question processed: Outcome={Outcome}, Mode={Mode}, QuestionLength={QuestionLength}, Attempts={Attempts}, RowCount={RowCount}, Answered={Answered}, LatencyMs={LatencyMs} (Endpoint={Endpoint}, HttpMethod={HttpMethod})",
                "RAG_QUERY", "LOW", outcome, RagAnswer.ModeText2Cypher, request.Question?.Length ?? 0, attempts,
                result?.Retrieval.RowCount ?? 0, result?.Answered ?? false, latencyMs, "/api/rag/ask", "POST");
        }
    }
}
