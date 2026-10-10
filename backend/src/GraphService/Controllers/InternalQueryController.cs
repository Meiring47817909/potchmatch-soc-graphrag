using System.Text.Json;
using GraphService.Queries;
using GraphService.Rag;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;

namespace GraphService.Controllers;

/// <summary>Checks the internal key as an authorization filter, so it runs before body binding (no key and a bad body is 401, not 400).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class InternalQueryKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["GraphQuery:Key"];
        var provided = context.HttpContext.Request.Headers[InternalQueryController.KeyHeader].FirstOrDefault();
        if (!QueryKeyValidator.IsValid(provided, expected)) context.Result = new UnauthorizedResult();
    }
}

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
[InternalQueryKey]
public class InternalQueryController(GuardedQueryRunner runner, GraphSchemaDescriber describer, IConfiguration config) : ControllerBase
{
    public const string KeyHeader = "X-Graph-Query-Key";

    private bool Authorised() =>
        QueryKeyValidator.IsValid(Request.Headers[KeyHeader].FirstOrDefault(), config["GraphQuery:Key"]);

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] InternalQueryRequest? request, CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();

        var guard = CypherGuard.Check(request?.Cypher); // null/missing body is rejected by the guard
        if (!guard.Allowed) return BadRequest(new { error = "rejected", reason = guard.Reason });

        var parameters = ParameterConverter.Convert(request?.Parameters);
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
        // Connectivity, authentication and database failures: generic body, no driver message.
        catch (Neo4jException)
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
        catch (Neo4jException)
        {
            return StatusCode(503, new { error = "graph_unavailable" });
        }
    }
}
