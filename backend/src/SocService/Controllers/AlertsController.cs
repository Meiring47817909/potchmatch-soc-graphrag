using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SocService.Data;
using SocService.Dtos;
using SocService.Models;

namespace SocService.Controllers;

[ApiController]
[Route("api/soc/alerts")]
[Authorize(Roles = "Admin")]
public class AlertsController : ControllerBase
{
    private readonly SocDbContext _db;
    public AlertsController(SocDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(string? status, string? severity, CancellationToken ct)
    {
        var q = _db.Alerts.AsNoTracking().Include(a => a.AlertEvents).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(severity)) q = q.Where(a => a.Severity == severity);

        var alerts = await q.OrderByDescending(a => a.Timestamp).ToListAsync(ct);
        var times = await EventTimesAsync(alerts, ct);
        return Ok(alerts.Select(a => AlertResponse.From(a, times)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var alert = await _db.Alerts.AsNoTracking().Include(a => a.AlertEvents)
            .FirstOrDefaultAsync(a => a.AlertId == id, ct);
        if (alert is null) return NotFound();
        return Ok(AlertResponse.From(alert, await EventTimesAsync(new[] { alert }, ct)));
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] AlertStatusUpdateRequest body, CancellationToken ct)
    {
        if (body is null || body.Status is null || !AlertStatusUpdate.IsValidTarget(body.Status))
            return BadRequest(new { error = "status must be ACKNOWLEDGED or RESOLVED" });

        var alert = await _db.Alerts.Include(a => a.AlertEvents).FirstOrDefaultAsync(a => a.AlertId == id, ct);
        if (alert is null) return NotFound();

        alert.Status = body.Status;
        alert.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(AlertResponse.From(alert, await EventTimesAsync(new[] { alert }, ct)));
    }

    private async Task<Dictionary<Guid, DateTimeOffset>> EventTimesAsync(IEnumerable<Alert> alerts, CancellationToken ct)
    {
        var ids = alerts.SelectMany(a => a.AlertEvents).Select(ae => ae.EventId).Distinct().ToList();
        if (ids.Count == 0) return new();
        return await _db.Events.AsNoTracking().Where(e => ids.Contains(e.EventId))
            .ToDictionaryAsync(e => e.EventId, e => e.Timestamp, ct);
    }
}
