using IdentityService.Data;
using IdentityService.Models;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public static class AdminSeeder
{
    // Creates the SOC admin account if no player has this email. An existing
    // player (admin or not) is left unchanged. Registration can never create an
    // Admin, so this is the only way one comes into existence.
    public static async Task EnsureAdminAsync(IdentityDbContext db, string email, string password, CancellationToken ct)
    {
        if (await db.Players.AnyAsync(p => p.Email == email, ct))
        {
            return;
        }

        db.Players.Add(new Player
        {
            Name = "SOC",
            Surname = "Admin",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            ClubId = "pcc",
            Role = PlayerRole.Admin,
        });
        await db.SaveChangesAsync(ct);
    }
}
