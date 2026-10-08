using IdentityService.Data;
using IdentityService.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

public record SyncPlayerResponse(Guid Id, string Email, string Name, string Surname, string Role);

[ApiController]
[Route("api/internal/sync")]
[AllowAnonymous]
public class InternalSyncController : ControllerBase
{
    private readonly IdentityDbContext _db;
    private readonly IConfiguration _config;

    public InternalSyncController(IdentityDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    private bool Authorised()
    {
        var expected = _config["GraphSync:Key"] ?? string.Empty;
        var provided = Request.Headers["X-Graph-Sync-Key"].FirstOrDefault();
        return SyncKeyValidator.IsValid(provided, expected);
    }

    // All players including the seeded Admin (the knowledge graph needs the admin user).
    [HttpGet("players")]
    public async Task<IActionResult> Players(CancellationToken ct)
    {
        if (!Authorised()) return Unauthorized();
        var rows = await _db.Players.AsNoTracking()
            .Select(p => new { p.Id, p.Email, p.Name, p.Surname, p.Role })
            .ToListAsync(ct);
        var result = rows
            .OrderBy(p => p.Email, StringComparer.Ordinal)
            .Select(p => new SyncPlayerResponse(p.Id, p.Email, p.Name, p.Surname, p.Role.ToString()))
            .ToList();
        return Ok(result);
    }
}
