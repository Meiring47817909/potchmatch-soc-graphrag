using IdentityService.Data;
using IdentityService.Logging;
using IdentityService.Models;
using IdentityService.Models.Dtos;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IdentityDbContext _db;
    private readonly JwtTokenService _jwtTokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IdentityDbContext db, JwtTokenService jwtTokenService, ILogger<AuthController> logger)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        if (await _db.Players.AnyAsync(p => p.Email == request.Email))
        {
            return Conflict("A player with this email already exists");
        }

        var player = new Player
        {
            Name = request.Name,
            Surname = request.Surname,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            ClubId = request.ClubId,
            Role = request.IsCoach ? PlayerRole.Coach : PlayerRole.Player,
        };

        _db.Players.Add(player);
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "PLAYER_REGISTERED", "LOW",
            $"New {player.Role} account registered: {player.Email}",
            userId: player.Id.ToString(), endpoint: "/api/auth/register", httpMethod: "POST",
            affectedEntity: $"player:{player.Id}");

        var token = _jwtTokenService.GenerateToken(player);
        return Ok(new AuthResponse(token, player.Id, player.Name, player.Surname, player.Role.ToString(), player.ClubId));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var player = await _db.Players.SingleOrDefaultAsync(p => p.Email == request.Email);

        if (player is null || !BCrypt.Net.BCrypt.Verify(request.Password, player.PasswordHash))
        {
            // "failed authentication attempts" (spec 6.2) / "failed login events"
            // (spec 9) - the exact input Phase 2's Rule 1 ("5 failed logins for the
            // same user/source IP within 5 minutes -> HIGH alert") needs to exist.
            // The attempted email is logged as UserId even though no account may
            // actually match it - that's still the identifier the attempt targeted.
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "FAILED_LOGIN", "MEDIUM",
                $"Invalid email or password for login attempt as {request.Email}",
                userId: request.Email,
                sourceIp: HttpContext.Connection.RemoteIpAddress?.ToString(),
                endpoint: "/api/auth/login", httpMethod: "POST", statusCode: 401,
                affectedEntity: "login-endpoint");

            return Unauthorized("Invalid email or password");
        }

        SecurityEventLog.Emit(_logger, LogLevel.Information, "LOGIN_SUCCESS", "LOW",
            $"{player.Email} logged in",
            userId: player.Id.ToString(), endpoint: "/api/auth/login", httpMethod: "POST");

        var token = _jwtTokenService.GenerateToken(player);
        return Ok(new AuthResponse(token, player.Id, player.Name, player.Surname, player.Role.ToString(), player.ClubId));
    }
}
