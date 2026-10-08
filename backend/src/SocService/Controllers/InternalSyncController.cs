using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocService.Ingestion;
using SocService.Queries;

namespace SocService.Controllers;

[ApiController]
[Route("api/internal/sync")]
[AllowAnonymous]
public class InternalSyncController : ControllerBase
{
    private readonly SyncQuery _query;
    private readonly IConfiguration _config;

    public InternalSyncController(SyncQuery query, IConfiguration config)
    {
        _query = query;
        _config = config;
    }

    private bool Authorised()
    {
        var expected = _config["GraphSync:Key"] ?? string.Empty;
        var provided = Request.Headers["X-Graph-Sync-Key"].FirstOrDefault();
        return IngestKeyValidator.IsValid(provided, expected);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts(DateTimeOffset? updatedSince, Guid? afterId, int? limit, CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();
        return Ok(await _query.GetAlertsAsync(updatedSince, afterId, limit ?? 200, ct));
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events(DateTimeOffset? receivedAfter, Guid? afterId, int? limit, CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();
        return Ok(await _query.GetEventsAsync(receivedAfter, afterId, limit ?? 500, ct));
    }
}
