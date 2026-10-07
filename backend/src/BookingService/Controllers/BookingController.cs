using System.Security.Claims;
using BookingService.Data;
using BookingService.Logging;
using BookingService.Models;
using BookingService.Models.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json; // for Http client
using Polly.CircuitBreaker;

namespace BookingService.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingController : ControllerBase
{
    private readonly BookingDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BookingController> _logger;

    public BookingController(BookingDbContext db, IHttpClientFactory httpClientFactory, ILogger<BookingController> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Booking>>> GetAll([FromQuery] string? clubId)
    {
        var query = _db.Bookings.AsQueryable();
        if (!string.IsNullOrEmpty(clubId))
        {
            query = query.Where(b => b.ClubId == clubId);
        }
        return Ok(await query.ToListAsync());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Booking>> GetById(Guid id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpPost]
    public async Task<ActionResult<Booking>> Create(CreateBookingRequest request)
    {
        // IsCoachingSession is an explicit choice made by the host, not something
        // inferred from their account - but it's only a coach's choice to make.
        // A non-coach can't retag a plain challenge as a paid coaching session
        // just by setting this flag on the request.
        if (request.IsCoachingSession && !User.IsInRole("Coach"))
        {
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "UNAUTHORISED_ACCESS", "MEDIUM",
                $"Player {GetCurrentPlayerId()} (non-coach) attempted to create a coaching session",
                userId: GetCurrentPlayerId().ToString(), endpoint: "/api/bookings", httpMethod: "POST",
                statusCode: 403, affectedEntity: "coaching-session-creation");
            return Forbid();
        }

        if (request.ScheduledAt < DateTime.UtcNow)
        {
            return BadRequest("Scheduled date must be in the future.");
        }

        var booking = new Booking
        {
            ClubId = request.ClubId,
            HostPlayerId = GetCurrentPlayerId(),
            FeeZAR = request.FeeZAR,
            ProposedLocation = request.ProposedLocation,
            IsCoachingSession = request.IsCoachingSession,
            ScheduledAt = request.ScheduledAt,
        };

        _db.Bookings.Add(booking);
        await _db.SaveChangesAsync();

        SecurityEventLog.Emit(_logger, LogLevel.Information, "BOOKING_CREATED", "LOW",
            $"{(booking.IsCoachingSession ? "Coaching session" : "Challenge")} created for R{booking.FeeZAR} at club {booking.ClubId}",
            userId: booking.HostPlayerId.ToString(), endpoint: "/api/bookings", httpMethod: "POST",
            affectedEntity: $"booking:{booking.Id}");

        return CreatedAtAction(nameof(GetById), new { id = booking.Id }, booking);
    }

    [HttpPut("{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var booking = await _db.Bookings.FindAsync(id);
        if (booking is null) return NotFound();
        if (booking.Status != BookingStatus.Open) return Conflict("Booking is not open.");

        var opponentId = GetCurrentPlayerId();
        if (opponentId == booking.HostPlayerId)
        {
            // Not a real authorisation gap (they'd only be paying themselves), but a
            // pointless/automated-looking call worth surfacing as a "suspicious
            // application event" (spec 9) rather than silently 400ing.
            SecurityEventLog.Emit(_logger, LogLevel.Warning, "SUSPICIOUS_ACTIVITY", "LOW",
                $"Player {opponentId} attempted to accept their own booking",
                userId: opponentId.ToString(), endpoint: $"/api/bookings/{id}/accept", httpMethod: "PUT",
                statusCode: 400, affectedEntity: $"booking:{id}");
            return BadRequest("You can't accept your own booking.");
        }

        // Payment client:
        var paymentClient = _httpClientFactory.CreateClient("PaymentService");

        // Forward the caller's own token - PaymentService's [Authorize] endpoints
        // Need a valid JWT too, and it's the same token, just passed along.
        var incomingToken = Request.Headers["Authorization"].ToString();
        paymentClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", incomingToken);

        var holdRequest = new HoldFundsRequest(booking.Id, opponentId, booking.HostPlayerId, booking.FeeZAR);

        // Explicit "inter-service calls" event (spec 6.2) - the
        // CorrelationPropagatingHandler on this HttpClient carries this
        // request's own CorrelationId onto the call, so this line and
        // whatever PaymentService logs for the same call share one id.
        SecurityEventLog.Emit(_logger, LogLevel.Information, "INTER_SERVICE_CALL", "LOW",
            $"Calling payment-service POST /api/payments/hold for booking {booking.Id}",
            userId: opponentId.ToString(), affectedEntity: "payment-service");

        EscrowTransactionResponse? heldTransaction;

        try
        {
            var paymentResponse = await paymentClient.PostAsJsonAsync("/api/payments/hold", holdRequest);

            if ((int)paymentResponse.StatusCode == 402)
            {
                // Insufficient funds - forward PaymentService's message as-is so the
                // frontend can point the player at the Dashboard's Add Funds button.
                var insufficientFundsMessage = await paymentResponse.Content.ReadAsStringAsync();
                SecurityEventLog.Emit(_logger, LogLevel.Information, "BOOKING_ACCEPT_BLOCKED_INSUFFICIENT_FUNDS", "LOW",
                    $"Player {opponentId} could not accept booking {booking.Id} - insufficient funds",
                    userId: opponentId.ToString(), statusCode: 402, affectedEntity: $"booking:{booking.Id}");
                return StatusCode(402, string.IsNullOrWhiteSpace(insufficientFundsMessage)
                    ? "Insufficient funds."
                    : insufficientFundsMessage);
            }

            if (!paymentResponse.IsSuccessStatusCode)
            {
                SecurityEventLog.Emit(_logger, LogLevel.Error, "INTER_SERVICE_CALL_FAILED", "HIGH",
                    $"payment-service responded {(int)paymentResponse.StatusCode} to /api/payments/hold",
                    affectedEntity: "payment-service", statusCode: (int)paymentResponse.StatusCode);
                return StatusCode(502, "Could not hold funds in escrow - booking was not accepted.");
            }

            // Capture the escrow transaction's own Id so we can refund it later if we
            // end up losing the race for this booking below.
            heldTransaction = await paymentResponse.Content.ReadFromJsonAsync<EscrowTransactionResponse>();
        }
        catch (BrokenCircuitException)
        {
            // The circuit is open - PaymentService has been failing repeatedly, so we're
            // not even attempting the call right now. Fail fast instead of making the
            // caller wait through a pointless retry sequence. This is exactly the
            // "Dependency failure between services" / "Repeated service failures"
            // example event from spec section 9.
            SecurityEventLog.Emit(_logger, LogLevel.Error, "INTER_SERVICE_CALL_FAILED", "HIGH",
                "payment-service circuit breaker is open - failing fast without attempting the call",
                affectedEntity: "payment-service");
            return StatusCode(503, "Payment service is currently unavailable. Please try again shortly.");
        }
        catch (HttpRequestException)
        {
            // All retry attempts were exhausted and PaymentService is still unreachable.
            SecurityEventLog.Emit(_logger, LogLevel.Error, "INTER_SERVICE_CALL_FAILED", "HIGH",
                "payment-service unreachable after all retry attempts",
                affectedEntity: "payment-service");
            return StatusCode(502, "Could not reach payment service after multiple attempts - booking was not accepted.");
        }

        // Funds are now held. From here on, this atomic conditional update is the real
        // consistency guard - it guarantees only one caller can win the WHERE Status = Open
        // condition even if two opponents accept at nearly the same moment.
        var rowsAffected = await _db.Bookings
            .Where(b => b.Id == id && b.Status == BookingStatus.Open)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(b => b.OpponentPlayerId, opponentId)
                .SetProperty(b => b.Status, BookingStatus.Accepted));

        if (rowsAffected == 0)
        {
            // We lost the race - someone else accepted this booking between our
            // initial check and this final commit. We're already holding their funds
            // in escrow for nothing, so we compensate by refunding this specific hold.
            // This is the saga's compensating transaction: BookingService is the
            // orchestrator, and it explicitly undoes PaymentService's step when its
            // own step can no longer go ahead.
            SecurityEventLog.Emit(_logger, LogLevel.Information, "BOOKING_ACCEPT_LOST_RACE", "LOW",
                $"Player {opponentId} lost the race to accept booking {booking.Id} - refunding held escrow",
                userId: opponentId.ToString(), affectedEntity: $"booking:{booking.Id}");

            if (heldTransaction is not null)
            {
                try
                {
                    await paymentClient.PutAsync($"/api/payments/{heldTransaction.Id}/refund", null);
                }
                catch (Exception)
                {
                    // Best-effort compensation - in a production system this would go to
                    // a durable outbox/retry queue instead of being attempted once and
                    // dropped, so it can be retried until it actually succeeds.
                }
            }

            return Conflict("This booking was already accepted by someone else.");
        }

        // ExecuteUpdateAsync writes straight to the database and bypasses the change
        // tracker, so update this in-memory copy to match what was actually committed.
        booking.OpponentPlayerId = opponentId;
        booking.Status = BookingStatus.Accepted;

        SecurityEventLog.Emit(_logger, LogLevel.Information, "BOOKING_ACCEPTED", "LOW",
            $"Player {opponentId} accepted booking {booking.Id} hosted by {booking.HostPlayerId}",
            userId: opponentId.ToString(), affectedEntity: $"booking:{booking.Id}");

        // This booking is now committed to the winning acceptor - release the
        // escrow so the host's wallet actually receives the fee (minus commission).
        // Best-effort, same as the refund compensation above: a production system
        // would retry this via a durable outbox rather than dropping it on failure,
        // but the accept itself has already succeeded either way, so this doesn't
        // block the response.
        if (heldTransaction is not null)
        {
            try
            {
                await paymentClient.PutAsync($"/api/payments/{heldTransaction.Id}/release", null);
            }
            catch (Exception)
            {
            }
        }

        return Ok(booking);
    }

    private Guid GetCurrentPlayerId()
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.Parse(sub!);
    }
}
