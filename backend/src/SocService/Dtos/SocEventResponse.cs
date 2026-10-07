using SocService.Models;

namespace SocService.Dtos;

public record SocEventResponse(
    Guid EventId,
    DateTimeOffset Timestamp,
    string ServiceName,
    string EventType,
    string Severity,
    string? UserId,
    string? SourceIp,
    string? Endpoint,
    string? HttpMethod,
    int? StatusCode,
    string Message,
    string? CorrelationId,
    string? AffectedEntity)
{
    public static SocEventResponse From(SocEvent e) => new(e.EventId, e.Timestamp, e.ServiceName, e.EventType,
        e.Severity, e.UserId, e.SourceIp, e.Endpoint, e.HttpMethod, e.StatusCode, e.Message,
        e.CorrelationId, e.AffectedEntity);
}
