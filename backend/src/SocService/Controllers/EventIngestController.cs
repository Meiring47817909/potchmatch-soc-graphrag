using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocService.Ingestion;

namespace SocService.Controllers;

[ApiController]
[Route("api/events")]
public class EventIngestController : ControllerBase
{
    private readonly EventIngestionService _ingestion;
    private readonly IConfiguration _config;

    public EventIngestController(EventIngestionService ingestion, IConfiguration config)
    {
        _ingestion = ingestion;
        _config = config;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Post([FromBody] List<SerilogEnvelope> batch, CancellationToken ct)
    {
        var expected = _config["SocIngest:Key"] ?? string.Empty;
        var provided = Request.Headers["X-Soc-Ingest-Key"].FirstOrDefault();
        if (!IngestKeyValidator.IsValid(provided, expected))
            return Unauthorized();

        var result = await _ingestion.IngestAsync(batch, ct);
        return Ok(result);
    }
}
