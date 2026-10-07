using System.Security.Cryptography.X509Certificates;

namespace BookingService.Models;

public enum BookingStatus
{
    Open,
    Accepted,
    Completed,
    Cancelled
}

public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ClubId { get; set; } = string.Empty;
    public Guid HostPlayerId { get; set; }
    public Guid? OpponentPlayerId { get; set; }
    public decimal FeeZAR { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Open;
    public string? ProposedLocation { get; set; }
    // Explicit type flag, set by the host at creation time - NOT derived from
    // whether the host is a Coach. A coach can host either kind (a plain
    // Challenge against another player, or a paid CoachingSession), so this
    // can't be inferred from the host player's role alone. Only settable to
    // true by a caller who actually holds the Coach role - see
    // BookingController.Create.
    public bool IsCoachingSession { get; set; }
    // When the challenge/coaching session is actually due to be played - chosen
    // by the host at creation time (see CreateBookingRequest.ScheduledAt), not
    // to be confused with CreatedAt below, which is just when the row was
    // inserted.
    public DateTime ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
