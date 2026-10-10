using System.Text.Json;
using GraphService.Queries;
using GraphService.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;

namespace GraphService.Controllers;

public sealed record InternalQueryRequest(string? Cypher, Dictionary<string, JsonElement>? Parameters);

public sealed record InternalQueryResponse(
    string Cypher,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphRelDto> Relationships,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated);

/// <summary>Service-to-service endpoints for RagService: guarded read-only Cypher and graph schema. Never routed via the gateway.</summary>
[ApiController]
[Route("api/internal")]
[AllowAnonymous]
public class InternalQueryController(GuardedQueryRunner runner, GraphSchemaDescriber describer, IConfiguration config) : ControllerBase
{
    public const string KeyHeader = "X-Graph-Query-Key";

    private bool Authorised() =>
        QueryKeyValidator.IsValid(Request.Headers[KeyHeader].FirstOrDefault(), config["GraphQuery:Key"]);

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] InternalQueryRequest request, CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();

        var guard = CypherGuard.Check(request?.Cypher);
        if (!guard.Allowed) return BadRequest(new { error = "rejected", reason = guard.Reason });

        var parameters = ParameterConverter.Convert(request!.Parameters);
        try
        {
            var r = await runner.RunAsync(guard.Cypher, parameters, ct);
            return Ok(new InternalQueryResponse(guard.Cypher, parameters, r.Nodes, r.Relationships, r.Rows, r.Truncated));
        }
        // Syntax errors, missing parameters, writes in a read transaction and 10 s timeouts all surface as client/transient errors.
        catch (Exception ex) when (ex is ClientException or TransientException)
        {
            return BadRequest(new { error = "cypher_error", reason = ex.Message });
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or SessionExpiredException)
        {
            return StatusCode(503, new { error = "graph_unavailable" });
        }
    }

    [HttpGet("schema")]
    public async Task<IActionResult> Schema(CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();
        try
        {
            return Ok(await describer.DescribeAsync(ct));
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or SessionExpiredException)
        {
            return StatusCode(503, new { error = "graph_unavailable" });
        }
    }
}
