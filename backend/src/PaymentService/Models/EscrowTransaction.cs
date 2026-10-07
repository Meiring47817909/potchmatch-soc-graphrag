namespace PaymentService.Models;

public enum EscrowStatus
{
    Held,
    Released,
    Refunded
}

public class EscrowTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid PayerId { get; set; }
    public Guid PayeeId { get; set; }
    public decimal AmountZAR { get; set; }
    public decimal CommissionZAR { get; set; }
    public EscrowStatus Status { get; set; } = EscrowStatus.Held;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt {  get; set; }
}
