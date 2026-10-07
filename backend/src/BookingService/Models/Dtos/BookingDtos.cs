namespace BookingService.Models.Dtos;

public record CreateBookingRequest(string ClubId, decimal FeeZAR, DateTime ScheduledAt, string? ProposedLocation, bool IsCoachingSession = false);
/*
Yes, this is a duplicate of the one already in PaymentService — that's intentional, not sloppy.
Each service defines its own copy of the contract shapes it needs; there's no shared library between them.
That's a deliberate microservices trade-off:
A little duplication in exchange for the services never needing to be built/versioned/deployed together.
*/
public record HoldFundsRequest(Guid BookingId, Guid PayerId, Guid PayeeId, decimal AmountZAR);

// Mirrors just the field we need from PaymentService's EscrowTransaction response.
// Same principle as HoldFundsRequest above - we don't reference PaymentService's
// actual EscrowTransaction type directly.
public record EscrowTransactionResponse(Guid Id);
