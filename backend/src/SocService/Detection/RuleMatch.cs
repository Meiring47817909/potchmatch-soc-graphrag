namespace SocService.Detection;

public record RuleMatch(string DedupKey, string Description, string? AffectedService,
    string? AffectedUser, string? AffectedEntity, IReadOnlyList<Guid> EventIds);
