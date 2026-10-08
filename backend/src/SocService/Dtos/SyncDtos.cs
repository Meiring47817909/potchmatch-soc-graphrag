namespace SocService.Dtos;

public record SyncAlertResponse(
    Guid AlertId,
    DateTimeOffset Timestamp,
    DateTimeOffset UpdatedAt,
    string RuleName,
    string Severity,
    string Status,
    string Description,
    string? AffectedService,
    string? AffectedUser,
    string? AffectedEntity,
    string RecommendedAction,
    IReadOnlyList<SocEventResponse> RelatedEvents);

public record SyncEventResponse(SocEventResponse Event, DateTimeOffset ReceivedAt);
