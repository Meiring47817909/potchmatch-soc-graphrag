using SocService.Models;

namespace SocService.Dtos;

public record AlertStatusUpdateRequest(string Status);

public static class AlertStatusUpdate
{
    /// <summary>Only ACKNOWLEDGED and RESOLVED (exact, case-sensitive) are valid PATCH targets.</summary>
    public static bool IsValidTarget(string status) =>
        status == SocConstants.Status.Acknowledged || status == SocConstants.Status.Resolved;
}
