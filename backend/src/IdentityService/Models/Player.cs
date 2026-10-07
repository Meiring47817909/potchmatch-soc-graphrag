namespace IdentityService.Models;

public enum PlayerRole
{
    Player,
    Coach,
    Admin
}

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string ClubId { get; set; } = string.Empty;
    public string Rank { get; set; } = "Unranked"; // mocked SportyHQ rank
    public PlayerRole Role { get; set; } = PlayerRole.Player;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Coach-only profile fields (null for regular Players). Filled in later via
    // the account/profile edit page - registering as a coach doesn't require
    // filling these in immediately.
    public string? CoachBio { get; set; }
    public string? CoachingLevel { get; set; }
    public decimal? HourlyRateZAR { get; set; }
}
