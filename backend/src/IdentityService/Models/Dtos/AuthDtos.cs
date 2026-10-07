namespace IdentityService.Models.Dtos
{
    public record RegisterRequest(string Name, string Surname, string Email, string Password, string ClubId, bool IsCoach = false);
    public record LoginRequest(string Email, string Password);

    // ClubId is included here (not just on PlayerResponse) because the frontend needs
    // it immediately after login/register to decide which club the player is allowed
    // to host challenges/coaching sessions at, without a second round trip.
    public record AuthResponse(string Token, Guid PlayerId, string Name, string Surname, string Role, string ClubId);

    public record PlayerResponse(
        Guid Id,
        string Name,
        string Surname,
        string Email,
        string ClubId,
        string Rank,
        string Role,
        string? CoachBio,
        string? CoachingLevel,
        decimal? HourlyRateZAR);

    public record UpdatePlayerRequest(
        string Name,
        string Surname,
        string Email,
        string ClubId,
        string Rank,
        string? CoachBio,
        string? CoachingLevel,
        decimal? HourlyRateZAR);

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
