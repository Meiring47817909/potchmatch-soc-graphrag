using System.Security.Claims;
using PaymentService.Data;
using PaymentService.Logging;
using PaymentService.Models;
using PaymentService.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private const decimal CommissionRate = 0.10m; // Platform takes 10% commission on each payment

    private readonly PaymentDbContext _db;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(PaymentDbContext db, ILogger<PaymentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EscrowTransaction>> GetById(Guid id)
    {
        var tx = await _db.EscrowTransactions.FindAsync(id);
        return tx is null ? NotFound() : Ok(tx);
    }

    [HttpGet("by-booking/{bookingId:guid}")]
    public async Task<ActionResult<EscrowTransaction>> GetByBooking(Guid bookingId)
    {
        var tx = await _db.EscrowTransactions.SingleOrDefaultAsync(t => t.BookingId == bookingId);
        return tx is null ? NotFound() : Ok(tx);
    }

    // Returns the calling player's own wallet balance - a wallet that's never
    // been touched yet simply reads as R0 rather than 404ing, since every
    // player implicitly has one.
    [HttpGet("wallet")]
    public async Task<ActionResult<WalletResponse>> GetWallet()
    {
        var wallet = await _db.Wallets.FindAsync(GetCurrentPlayerId());
        return Ok(new WalletResponse(wallet?.BalanceZAR ?? 0));
    }

    // Backs the Dashboard's "Add Funds" button. This is a demo top-up, not a
    // real payment processor integration - the frontend's card-entry form
    // never sends card details here (or anywhere), so there's nothing to
    // validate or charge. It just credits the caller's own balance directly,
    // which is enough to demonstrate the escrow -> wallet flow end to end.
    [HttpPost("wallet/deposit")]
    public async Task<ActionResult<WalletResponse>> Deposit(DepositRequest request)
    {
        if (request.AmountZAR <= 0) return BadRequest("Amount must be greater than zero.");

        var playerId = GetCurrentPlayerId();
        var wallet = await GetOrCreateWalletAsync(playerId);
        wallet.BalanceZAR += request.AmountZAR;
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "WALLET_DEPOSIT", "LOW",
            $"Player {playerId} deposited R{request.AmountZAR} - new balance R{wallet.BalanceZAR}",
            userId: playerId.ToString(), endpoint: "/api/payments/wallet/deposit", httpMethod: "POST",
            affectedEntity: $"wallet:{playerId}");

        return Ok(new WalletResponse(wallet.BalanceZAR));
    }

    [HttpPost("hold")]
    public async Task<ActionResult<EscrowTransaction>> Hold(HoldFundsRequest request)
    {
        var existing = await _db.EscrowTransactions.SingleOrDefaultAsync(t => t.BookingId == request.BookingId);
        if (existing is not null)
        {
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "DUPLICATE_ESCROW_HOLD_ATTEMPT", "MEDIUM",
                $"Funds already held for booking {request.BookingId} - rejecting duplicate hold request",
                userId: request.PayerId.ToString(), endpoint: "/api/payments/hold", httpMethod: "POST",
                statusCode: 409, affectedEntity: $"booking:{request.BookingId}");
            return Conflict("Funds are already held for this booking.");
        }

        // The payer's wallet is debited right away, at hold time - not just
        // recorded as a pending amount elsewhere - so their spendable balance
        // reflects it immediately. Refund() credits it straight back if this
        // booking turns out to have already been accepted by someone else.
        var payerWallet = await GetOrCreateWalletAsync(request.PayerId);
        if (payerWallet.BalanceZAR < request.AmountZAR)
        {
            SecurityEventLog.Emit(_logger, LogLevel.Information, "ESCROW_HOLD_BLOCKED_INSUFFICIENT_FUNDS", "LOW",
                $"Player {request.PayerId} has insufficient funds to hold R{request.AmountZAR} for booking {request.BookingId}",
                userId: request.PayerId.ToString(), endpoint: "/api/payments/hold", httpMethod: "POST",
                statusCode: 402, affectedEntity: $"booking:{request.BookingId}");
            return StatusCode(402, "Insufficient funds - please add funds to your account before accepting.");
        }

        payerWallet.BalanceZAR -= request.AmountZAR;

        var tx = new EscrowTransaction
        {
            BookingId = request.BookingId,
            PayerId = request.PayerId,
            PayeeId = request.PayeeId,
            AmountZAR = request.AmountZAR,
            CommissionZAR = Math.Round(request.AmountZAR * CommissionRate, 2),
            Status = EscrowStatus.Held
        };

        _db.EscrowTransactions.Add(tx);
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "ESCROW_HELD", "LOW",
            $"Held R{tx.AmountZAR} from player {tx.PayerId} for booking {tx.BookingId}",
            userId: tx.PayerId.ToString(), affectedEntity: $"booking:{tx.BookingId}");

        return CreatedAtAction(nameof(GetById), new { id = tx.Id }, tx);
    }

    // Release/Refund don't check who's calling -
    // Any authenticated user can release or refund any transaction
    // In real system change: Only an admin, or both players confirming, can trigger release.

    [HttpPut("{id:guid}/release")]
    public async Task<ActionResult<EscrowTransaction>> Release(Guid id)
    {
        var tx = await _db.EscrowTransactions.FindAsync(id);
        if (tx is null) return NotFound();
        if (tx.Status! != EscrowStatus.Held) return Conflict("Funds are not currently held.");

        // Payout to the payee = AmountZAR - CommissionZAR; the commission stays with the platform.
        var payeeWallet = await GetOrCreateWalletAsync(tx.PayeeId);
        payeeWallet.BalanceZAR += tx.AmountZAR - tx.CommissionZAR;

        tx.Status = EscrowStatus.Released;
        tx.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "ESCROW_RELEASED", "LOW",
            $"Released R{tx.AmountZAR - tx.CommissionZAR} to player {tx.PayeeId} for booking {tx.BookingId}",
            userId: tx.PayeeId.ToString(), affectedEntity: $"booking:{tx.BookingId}");

        return Ok(tx);
    }

    [HttpPut("{id:guid}/refund")]
    public async Task<ActionResult<EscrowTransaction>> Refund(Guid id)
    {
        var tx = await _db.EscrowTransactions.FindAsync(id);
        if (tx is null) return NotFound();
        if (tx.Status != EscrowStatus.Held) return Conflict("Funds are not currently held.");

        // Undo the debit Hold() applied to the payer's wallet - the full
        // AmountZAR, since nothing was ever paid out to the payee for a
        // transaction that never left the Held state.
        var payerWallet = await GetOrCreateWalletAsync(tx.PayerId);
        payerWallet.BalanceZAR += tx.AmountZAR;

        tx.Status = EscrowStatus.Refunded;
        tx.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "ESCROW_REFUNDED", "LOW",
            $"Refunded R{tx.AmountZAR} to player {tx.PayerId} for booking {tx.BookingId}",
            userId: tx.PayerId.ToString(), affectedEntity: $"booking:{tx.BookingId}");

        return Ok(tx);
    }

    private async Task<Wallet> GetOrCreateWalletAsync(Guid playerId)
    {
        var wallet = await _db.Wallets.FindAsync(playerId);
        if (wallet is null)
        {
            wallet = new Wallet { PlayerId = playerId, BalanceZAR = 0 };
            _db.Wallets.Add(wallet);
        }

        return wallet;
    }

    private Guid GetCurrentPlayerId()
    {
        // Matches BookingService's BookingController.GetCurrentPlayerId - the JWT
        // only sets JwtRegisteredClaimNames.Sub (not ClaimTypes.NameIdentifier
        // explicitly), so fall back to the raw "sub" claim name too.
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.Parse(sub!);
    }
}
