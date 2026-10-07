namespace PaymentService.Models.Dtos;

public record HoldFundsRequest(Guid BookingId, Guid PayerId, Guid PayeeId, decimal AmountZAR);

public record WalletResponse(decimal BalanceZAR);

// Backs the "Add Funds" flow on the Dashboard. This is a demo top-up, not a
// real payment processor integration - no card details are sent here or
// validated server-side (see PaymentsController.Deposit), so this just
// credits the caller's own balance directly.
public record DepositRequest(decimal AmountZAR);
