using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocService.Data;
using SocService.Queries;

namespace SocService.Controllers;

[ApiController]
[Route("api/soc/events")]
[Authorize(Roles = "Admin")]
public class SocEventsController : ControllerBase
{
    private readonly SocDbContext _db;
    public SocEventsController(SocDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(string? type, string? user, DateTimeOffset? from, DateTimeOffset? to,
        string? excludeType, Guid? alertId,
        int page = 1, int pageSize = EventsQuery.DefaultPageSize, CancellationToken ct = default) =>
        Ok(await EventsQuery.RunAsync(_db, type, user, from, to, page, pageSize, ct, excludeType, alertId));
}
