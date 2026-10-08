namespace GraphService.Sync;

public record SyncCursor(DateTimeOffset At, Guid Id);

public record SyncEvent(
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
    string? AffectedEntity);

public record SyncEventEnvelope(SyncEvent Event, DateTimeOffset ReceivedAt);

public record SyncAlert(
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
    IReadOnlyList<SyncEvent> RelatedEvents);

public record SyncPlayer(Guid Id, string Email, string Name, string Surname, string Role);

public class GraphSyncOptions
{
    public string Key { get; set; } = "";
    public string SocUrl { get; set; } = "http://localhost:8080";
    public string IdentityUrl { get; set; } = "http://localhost:8080";
    public int IntervalSeconds { get; set; } = 15;
    public int PlayersIntervalSeconds { get; set; } = 60;
    public int AlertPageSize { get; set; } = 200;
    public int EventPageSize { get; set; } = 500;
}
