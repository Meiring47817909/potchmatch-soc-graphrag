namespace IdentityService.Logging;

// Small structured-logging helper used by this service's controllers/middleware
// to emit consistent, machine-parseable security/business events. The field
// names deliberately mirror the "SOC Event Collection Layer" schema from the
// Phase 2 (Knowledge Graph / RAG) spec - eventType, severity, userId, sourceIp,
// endpoint, httpMethod, statusCode, affectedEntity - so these logs can feed
// that layer directly later without changing what's logged, only where the
// resulting JSON lines get shipped to.
//
// Kept as a plain per-project duplicate (BookingService and PaymentService each
// have their own identical copy) rather than pulled into a shared library -
// consistent with how this codebase already duplicates small cross-cutting
// pieces like the Consul registration code in each service's Program.cs.
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
        logger.Log(
            level,
            "{EventType} [{Severity}] {Message} (UserId={UserId}, SourceIp={SourceIp}, Endpoint={Endpoint}, HttpMethod={HttpMethod}, StatusCode={StatusCode}, AffectedEntity={AffectedEntity})",
            eventType, severity, message, userId, sourceIp, endpoint, httpMethod, statusCode, affectedEntity);
    }
}
