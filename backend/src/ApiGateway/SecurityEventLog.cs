namespace ApiGateway;

// See IdentityService's identical copy of this class (backend/src/IdentityService/Logging/SecurityEventLog.cs)
// for the full rationale. Field names mirror the Phase 2 SOC event schema
// (eventType, severity, userId, sourceIp, endpoint, httpMethod, statusCode,
// affectedEntity). Lives at the project root here (not under a Logging/
// folder) purely so it sits next to AddressPreferringConsulServiceBuilder.cs,
// the only other hand-written class in this project.
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
