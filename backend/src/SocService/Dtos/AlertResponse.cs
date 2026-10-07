using SocService.Models;

namespace SocService.Dtos;

public record AlertResponse(
    Guid AlertId,
    DateTimeOffset Timestamp,
    string RuleName,
    string Severity,
    string Status,
    string Description,
    IReadOnlyList<Guid> RelatedEvents,
    string? AffectedService,
    string? AffectedUser,
    string? AffectedEntity,
    string RecommendedAction)
{
    public static AlertResponse From(Alert a) =>
        From(a, new Dictionary<Guid, DateTimeOffset>());

    /// <summary>RelatedEvents ordered by the linked events' timestamps (unknown timestamps sort last).</summary>
    public static AlertResponse From(Alert a, IReadOnlyDictionary<Guid, DateTimeOffset> eventTimes)
    {
        var related = a.AlertEvents
            .Select(ae => ae.EventId)
            .OrderBy(id => eventTimes.TryGetValue(id, out var t) ? t : DateTimeOffset.MaxValue)
            .ThenBy(id => id)
            .ToList();

        return new AlertResponse(a.AlertId, a.Timestamp, a.RuleName, a.Severity, a.Status, a.Description,
            related, a.AffectedService, a.AffectedUser, a.AffectedEntity, a.RecommendedAction);
    }
}
