using Serilog.Context;

namespace PaymentService.Logging;

// See IdentityService's identical copy of this class for the full rationale.
// Field names mirror the Phase 2 SOC event schema (eventType, severity,
// userId, sourceIp, endpoint, httpMethod, statusCode, affectedEntity).
public static class SecurityEventLog
{
    public static void Emit(
        ILogger logger,
        LogLevel level,
        string eventType,
        string severity,
        string message,
        string? userId = null,
        string? sourceIp = null,
        string? endpoint = null,
        string? httpMethod = null,
        int? statusCode = null,
        string? affectedEntity = null)
    {
        // EventId + SecurityEvent let the SOC sink (see Program.cs) pick out exactly
        // these events and give each a stable id for idempotent ingestion.
        using (LogContext.PushProperty("EventId", Guid.NewGuid().ToString()))
        using (LogContext.PushProperty("SecurityEvent", true))
        {
            logger.Log(
                level,
                "{EventType} [{Severity}] {Message} (UserId={UserId}, SourceIp={SourceIp}, Endpoint={Endpoint}, HttpMethod={HttpMethod}, StatusCode={StatusCode}, AffectedEntity={AffectedEntity})",
                eventType, severity, message, userId, sourceIp, endpoint, httpMethod, statusCode, affectedEntity);
        }
    }
}
