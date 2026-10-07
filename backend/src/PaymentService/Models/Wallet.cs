namespace PaymentService.Models;

// One row per player - PlayerId doubles as the primary key since a player only
// ever has one wallet. Created lazily the first time a player's balance is
// touched (checked, topped up, debited on hold, or credited on release/refund)
// rather than at registration time, since IdentityService and PaymentService
// don't share a database and PaymentService has no "player created" event to
// react to.
public class Wallet
{
    public Guid PlayerId { get; set; }
    public decimal BalanceZAR { get; set; }
}
