namespace SocService.Models;

public class Alert
{
    public Guid AlertId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = SocConstants.Status.Open;
    public string Description { get; set; } = string.Empty;
    public string? AffectedService { get; set; }
    public string? AffectedUser { get; set; }
    public string? AffectedEntity { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
    public string DedupKey { get; set; } = string.Empty;
    public List<AlertEvent> AlertEvents { get; set; } = new();
}
