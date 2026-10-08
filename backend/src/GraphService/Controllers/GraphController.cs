using GraphService.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;

namespace GraphService.Controllers;

[ApiController]
[Route("api/graph")]
[Authorize(Roles = "Admin")]
public class GraphController(GraphQueryRunner runner) : ControllerBase
{
    [HttpGet("services/{name}/alerts")]
    public Task<IActionResult> ServiceAlerts(string name, CancellationToken ct) =>
        Guarded(async () =>
        {
            var p = Params("name", name);
            if (!await runner.ExistsAsync(GraphQueries.ServiceExists, p, ct)) return NotFound();
            return Ok(await runner.RunAsync(GraphQueries.AlertsAffectingServiceName, GraphQueries.AlertsAffectingService, p, ct));
        });

    [HttpGet("users/high-severity")]
    public Task<IActionResult> UsersHighSeverity(CancellationToken ct) =>
        Guarded(async () => Ok(await runner.RunAsync(GraphQueries.UsersHighSeverityName, GraphQueries.UsersHighSeverity,
            new Dictionary<string, object?>(), ct)));

    [HttpGet("services/{name}/threats")]
    public Task<IActionResult> ServiceThreats(string name, CancellationToken ct) =>
        Guarded(async () =>
        {
            var p = Params("name", name);
            if (!await runner.ExistsAsync(GraphQueries.ServiceExists, p, ct)) return NotFound();
            return Ok(await runner.RunAsync(GraphQueries.ThreatsForServiceName, GraphQueries.ThreatsForService, p, ct));
        });

    [HttpGet("alerts/{id}/controls")]
    public Task<IActionResult> AlertControls(string id, CancellationToken ct) =>
        Guarded(async () =>
        {
            var p = Params("alertId", id);
            if (!await runner.ExistsAsync(GraphQueries.AlertExists, p, ct)) return NotFound();
            return Ok(await runner.RunAsync(GraphQueries.ControlsForAlertName, GraphQueries.ControlsForAlert, p, ct));
        });

    [HttpGet("services/{name}/impact")]
    public Task<IActionResult> ServiceImpact(string name, CancellationToken ct) =>
        Guarded(async () =>
        {
            var p = Params("name", name);
            if (!await runner.ExistsAsync(GraphQueries.ServiceExists, p, ct)) return NotFound();
            return Ok(await runner.RunAsync(GraphQueries.DependencyImpactName, GraphQueries.DependencyImpact, p, ct));
        });

    [HttpGet("incidents")]
    public Task<IActionResult> Incidents([FromQuery] string? status, CancellationToken ct) =>
        Guarded(async () =>
        {
            string? normalised = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                normalised = status.Trim().ToUpperInvariant();
                if (normalised is not ("OPEN" or "RESOLVED"))
                    return BadRequest(new { error = "status must be OPEN or RESOLVED" });
            }
            return Ok(await runner.RunAsync(GraphQueries.IncidentsName, GraphQueries.Incidents, Params("status", normalised), ct));
        });

    [HttpGet("incidents/{id}")]
    public Task<IActionResult> IncidentDetail(string id, CancellationToken ct) =>
        Guarded(async () =>
        {
            var p = Params("incidentId", id);
            if (!await runner.ExistsAsync(GraphQueries.IncidentExists, p, ct)) return NotFound();
            return Ok(await runner.RunAsync(GraphQueries.IncidentDetailName, GraphQueries.IncidentDetail, p, ct));
        });

    private static Dictionary<string, object?> Params(string key, object? value) => new() { [key] = value };

    /// <summary>Maps Neo4j connectivity failures to 503.</summary>
    private async Task<IActionResult> Guarded(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is ServiceUnavailableException or SessionExpiredException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Graph database unavailable" });
        }
    }
}
