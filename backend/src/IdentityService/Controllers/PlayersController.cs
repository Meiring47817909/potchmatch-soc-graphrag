using System.Security.Claims;
using IdentityService.Data;
using IdentityService.Logging;
using IdentityService.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/players")]
[Authorize]
public class PlayersController : ControllerBase
{
    private readonly IdentityDbContext _db;
    private readonly ILogger<PlayersController> _logger;

    public PlayersController(IdentityDbContext db, ILogger<PlayersController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlayerResponse>>> GetAll()
    {
        var players = await _db.Players
            .Select(p => new PlayerResponse(p.Id, p.Name, p.Surname, p.Email, p.ClubId, p.Rank, p.Role.ToString(), p.CoachBio, p.CoachingLevel, p.HourlyRateZAR))
            .ToListAsync();

        return Ok(players);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlayerResponse>> GetById(Guid id)
    {
        var player = await _db.Players.FindAsync(id);
        if (player is null) return NotFound();

        return Ok(new PlayerResponse(player.Id, player.Name, player.Surname, player.Email, player.ClubId, player.Rank, player.Role.ToString(), player.CoachBio, player.CoachingLevel, player.HourlyRateZAR));
    }

    // Players can only edit their own account - the Gateway already requires a
    // valid JWT for every /api/players/* route, but that alone would let any
    // logged-in player edit anyone else's profile by id, so this checks the
    // caller's own id (the "sub" claim) against the id in the URL as well.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PlayerResponse>> Update(Guid id, UpdatePlayerRequest request)
    {
        if (id != GetCurrentPlayerId())
        {
            // "Unauthorised access to protected endpoint" (spec 9's example SOC
            // event) - a logged-in player has a valid token, so the Gateway and
            // this service's own JWT check both let the request through; it's
            // only this same-account check that catches them trying to edit
            // someone else's profile.
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                $"Player {GetCurrentPlayerId()} attempted to edit another player's profile",
                userId: GetCurrentPlayerId().ToString(), endpoint: $"/api/players/{id}", httpMethod: "PUT",
                statusCode: 403, affectedEntity: $"player:{id}");
            return Forbid();
        }

        var player = await _db.Players.FindAsync(id);
        if (player is null) return NotFound();

        if (await _db.Players.AnyAsync(p => p.Id != id && p.Email == request.Email))
        {
            return Conflict("A player with this email already exists");
        }

        player.Name = request.Name;
        player.Surname = request.Surname;
        player.Email = request.Email;
        player.ClubId = request.ClubId;
        player.Rank = request.Rank;
        player.CoachBio = request.CoachBio;
        player.CoachingLevel = request.CoachingLevel;
        player.HourlyRateZAR = request.HourlyRateZAR;

        await _db.SaveChangesAsync();

        return Ok(new PlayerResponse(player.Id, player.Name, player.Surname, player.Email, player.ClubId, player.Rank, player.Role.ToString(), player.CoachBio, player.CoachingLevel, player.HourlyRateZAR));
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(Guid id, ChangePasswordRequest request)
    {
        if (id != GetCurrentPlayerId())
        {
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                $"Player {GetCurrentPlayerId()} attempted to change another player's password",
                userId: GetCurrentPlayerId().ToString(), endpoint: $"/api/players/{id}/password", httpMethod: "PUT",
                statusCode: 403, affectedEntity: $"player:{id}");
            return Forbid();
        }

        var player = await _db.Players.FindAsync(id);
        if (player is null) return NotFound();

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, player.PasswordHash))
        {
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "FAILED_PASSWORD_CHANGE", "MEDIUM",
                "Current password did not match on change-password attempt",
                userId: id.ToString(), endpoint: $"/api/players/{id}/password", httpMethod: "PUT", statusCode: 401);
            return Unauthorized("Current password is incorrect");
        }

        player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetCurrentPlayerId()
    {
        // Matches BookingService's BookingController.GetCurrentPlayerId - the JWT
        // only sets JwtRegisteredClaimNames.Sub (not ClaimTypes.NameIdentifier
        // explicitly), so fall back to the raw "sub" claim name too.
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.Parse(sub!);
    }
}
