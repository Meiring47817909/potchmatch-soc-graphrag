using Microsoft.AspNetCore.Http;

namespace BookingService.Http;

// Forwards this request's own X-Correlation-Id onto every outgoing call this
// service makes to PaymentService (see the "PaymentService" named HttpClient
// in Program.cs), so PaymentService's own logs use the SAME correlation id
// instead of minting a new one - letting a single booking's accept-flow be
// traced end-to-end across both services' logs, and giving a concrete example
// of the "inter-service calls" line item from the Centralised Logging spec.
public class CorrelationPropagatingHandler : DelegatingHandler
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationPropagatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Request.Headers[CorrelationIdHeader].ToString();
        if (!string.IsNullOrEmpty(correlationId))
        {
            request.Headers.Remove(CorrelationIdHeader);
            request.Headers.Add(CorrelationIdHeader, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
